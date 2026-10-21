using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

public class TapTapRuntimePatcher
{
    public const string EXPECTED_VANILLA_SHA256 = "04C33269C541B021556EA38D7F301EF69AF73AB3544C812237740EA186229D45";

    public static string ComputeSHA256(string path)
    {
        using (var sha = SHA256.Create())
        using (var fs = File.OpenRead(path))
        {
            return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToUpperInvariant();
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            string inputPath = args.Length > 0 ? args[0] : @"XingShu-Dev\references\taptap\AstralParty.Runtime.dll";
            string bootstrapPath = args.Length > 1 ? args[1] : @"XingShu-Dev\scratch\bootstrap_builder\bin\Bootstrap.dll";
            string outputPath = args.Length > 2 ? args[2] : @"HS4-P1.5\candidate\AstralParty.Runtime.patched.dll";
            string expectedHash = args.Length > 3 ? args[3] : EXPECTED_VANILLA_SHA256;
            string legacyBootstrap = args.Length > 4 ? args[4] : "";

            Console.WriteLine("================================================================================");
            Console.WriteLine("PROJECT XINGSHU — TAPTAP RUNTIME BOOTSTRAP PATCHER");
            Console.WriteLine("================================================================================");
            Console.WriteLine("Input Path:     " + Path.GetFullPath(inputPath));
            Console.WriteLine("Bootstrap Path: " + Path.GetFullPath(bootstrapPath));
            Console.WriteLine("Output Path:    " + Path.GetFullPath(outputPath));

            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine("ERROR: Input file not found: " + inputPath);
                return 1;
            }
            if (!File.Exists(bootstrapPath))
            {
                Console.Error.WriteLine("ERROR: Bootstrap file not found: " + bootstrapPath);
                return 1;
            }

            string inHash = ComputeSHA256(inputPath);
            Console.WriteLine("Input SHA256:   " + inHash);
            if (!string.Equals(inHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Runtime input differs from the explicitly selected baseline");

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(@"XingShu-Dev\references\unity");
            resolver.AddSearchDirectory(@"XingShu-Dev\references\framework");
            resolver.AddSearchDirectory(@"XingShu-Dev\references\taptap");
            resolver.AddSearchDirectory(@"XingShu-Dev\references\thirdparty");
            resolver.AddSearchDirectory(@"XingShu-Dev\baseline\updated_20261015\host_6cc68f01\extracted");
            resolver.AddSearchDirectory(Path.GetDirectoryName(inputPath));

            var readerParams = new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false };
            using (var runtimeAssembly = AssemblyDefinition.ReadAssembly(inputPath, readerParams))
            using (var bootstrapAssembly = AssemblyDefinition.ReadAssembly(bootstrapPath, readerParams))
            {
                var runtimeModule = runtimeAssembly.MainModule;

                // 1. Idempotency Check
                var existingBoot = runtimeModule.Types.FirstOrDefault(t => t.FullName == "XingShu.XingShuUpdatedBootstrap" || t.FullName == legacyBootstrap);
                var gmType = runtimeModule.Types.FirstOrDefault(t => t.FullName == "Core.GameManager");
                if (gmType == null)
                {
                    Console.Error.WriteLine("ERROR: Core.GameManager not found in assembly!");
                    return 2;
                }
                var gmGameStart = gmType.Methods.FirstOrDefault(m => m.Name == "GameStart");
                if (gmGameStart == null)
                {
                    Console.Error.WriteLine("ERROR: Core.GameManager.GameStart not found in assembly!");
                    return 2;
                }

                if (existingBoot != null)
                {
                    // Refresh an earlier bootstrap as well as the payload. Otherwise
                    // upgrades keep looking for the old assembly filename.
                    foreach (var instruction in gmGameStart.Body.Instructions.ToArray())
                    {
                        var reference = instruction.Operand as MethodReference;
                        if (instruction.OpCode == OpCodes.Call && reference != null &&
                            reference.DeclaringType.FullName == existingBoot.FullName && reference.Name == "InstallOnce")
                        {
                            instruction.OpCode = OpCodes.Nop;
                            instruction.Operand = null;
                        }
                    }
                    runtimeModule.Types.Remove(existingBoot);
                    Console.WriteLine("Refreshing the previous bootstrap for the current payload.");
                }

                int originalInstructionCount = gmGameStart.Body.Instructions.Count;

                var bootstrapType = bootstrapAssembly.MainModule.Types.First(t => t.FullName == "XingShu.XingShuUpdatedBootstrap");
                Console.WriteLine("Cloning bootstrap type: " + bootstrapType.FullName);

                // Create XingShu.XingShuUpdatedBootstrap type in runtimeModule
                var newBootstrapType = new TypeDefinition(
                    bootstrapType.Namespace,
                    bootstrapType.Name,
                    bootstrapType.Attributes,
                    runtimeModule.TypeSystem.Object);
                runtimeModule.Types.Add(newBootstrapType);

                // Clone fields
                var fieldMap = new Dictionary<FieldDefinition, FieldDefinition>();
                foreach (var srcField in bootstrapType.Fields)
                {
                    var newField = new FieldDefinition(
                        srcField.Name,
                        srcField.Attributes,
                        runtimeModule.ImportReference(srcField.FieldType));
                    newBootstrapType.Fields.Add(newField);
                    fieldMap[srcField] = newField;
                }

                // Create method signatures
                var methodMap = new Dictionary<MethodDefinition, MethodDefinition>();
                foreach (var srcMethod in bootstrapType.Methods)
                {
                    var newMethod = new MethodDefinition(
                        srcMethod.Name,
                        srcMethod.Attributes,
                        runtimeModule.ImportReference(srcMethod.ReturnType));

                    foreach (var p in srcMethod.Parameters)
                    {
                        newMethod.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, runtimeModule.ImportReference(p.ParameterType)));
                    }

                    newBootstrapType.Methods.Add(newMethod);
                    methodMap[srcMethod] = newMethod;
                }

                // Clone method bodies
                foreach (var srcMethod in bootstrapType.Methods)
                {
                    if (srcMethod.Body == null) continue;
                    var newMethod = methodMap[srcMethod];

                    var varMap = srcMethod.Body.Variables.ToDictionary(
                        v => v,
                        v => new VariableDefinition(runtimeModule.ImportReference(v.VariableType)));

                    foreach (var v in varMap.Values)
                    {
                        newMethod.Body.Variables.Add(v);
                    }
                    newMethod.Body.InitLocals = srcMethod.Body.InitLocals;

                    var il = newMethod.Body.GetILProcessor();
                    var instrMap = srcMethod.Body.Instructions.ToDictionary(
                        i => i,
                        i => CloneInstruction(i, runtimeModule, fieldMap, methodMap));

                    foreach (var srcInstr in srcMethod.Body.Instructions)
                    {
                        var targetInstr = instrMap[srcInstr];
                        var sourceVariable = srcInstr.Operand as VariableDefinition;
                        var sourceParameter = srcInstr.Operand as ParameterDefinition;
                        if (sourceVariable != null) targetInstr.Operand = varMap[sourceVariable];
                        if (sourceParameter != null) targetInstr.Operand = newMethod.Parameters[sourceParameter.Index];
                        var branchTarget = srcInstr.Operand as Instruction;
                        var branchTargets = srcInstr.Operand as Instruction[];
                        if (branchTarget != null && instrMap.ContainsKey(branchTarget))
                        {
                            targetInstr.Operand = instrMap[branchTarget];
                        }
                        else if (branchTargets != null)
                        {
                            targetInstr.Operand = branchTargets.Select(t => instrMap[t]).ToArray();
                        }
                        il.Append(targetInstr);
                    }

                    foreach (var eh in srcMethod.Body.ExceptionHandlers)
                    {
                        var newEh = new ExceptionHandler(eh.HandlerType);
                        newEh.CatchType = eh.CatchType != null ? runtimeModule.ImportReference(eh.CatchType) : null;
                        newEh.TryStart = instrMap[eh.TryStart];
                        newEh.TryEnd = eh.TryEnd != null ? (instrMap.ContainsKey(eh.TryEnd) ? instrMap[eh.TryEnd] : null) : null;
                        newEh.HandlerStart = instrMap[eh.HandlerStart];
                        newEh.HandlerEnd = eh.HandlerEnd != null ? (instrMap.ContainsKey(eh.HandlerEnd) ? instrMap[eh.HandlerEnd] : null) : null;
                        newMethod.Body.ExceptionHandlers.Add(newEh);
                    }
                }

                // Inject call at entry of Core.GameManager.GameStart
                var gmIl = gmGameStart.Body.GetILProcessor();
                var firstInstr = gmGameStart.Body.Instructions[0];
                var installOnceMethod = methodMap.Values.First(m => m.Name == "InstallOnce");
                var callInstruction = gmIl.Create(OpCodes.Call, installOnceMethod);
                gmIl.InsertBefore(firstInstr, callInstruction);
                if (gmGameStart.Body.Instructions.Count != originalInstructionCount + 1)
                    throw new InvalidOperationException("Bootstrap must add exactly one entry call");

                Console.WriteLine("Injected call to InstallOnce at entry of Core.GameManager.GameStart.");

                // Write patched assembly
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                runtimeAssembly.Write(outputPath);
                Console.WriteLine("Wrote patched assembly to: " + outputPath);
            }

            // Post-patch validation
            string outHash = ComputeSHA256(outputPath);
            Console.WriteLine("Patched Assembly SHA256: " + outHash);

            using (var verifyAsm = AssemblyDefinition.ReadAssembly(outputPath, readerParams))
            {
                var boot = verifyAsm.MainModule.Types.FirstOrDefault(t => t.FullName == "XingShu.XingShuUpdatedBootstrap");
                if (boot == null) throw new Exception("Post-verification failed: XingShuUpdatedBootstrap missing!");
                var gm = verifyAsm.MainModule.Types.First(t => t.FullName == "Core.GameManager");
                var gs = gm.Methods.First(m => m.Name == "GameStart");
                if (gs.Body.Instructions[0].OpCode != OpCodes.Call)
                    throw new Exception("Post-verification failed: GameStart entry instruction is not call!");
                if (!gs.Body.Instructions[0].Operand.ToString().Contains("InstallOnce"))
                    throw new Exception("Post-verification failed: GameStart does not call InstallOnce!");
                if (gs.Body.Instructions.Count(i => i.Operand != null && i.Operand.ToString().Contains("XingShuUpdatedBootstrap::InstallOnce")) != 1)
                    throw new Exception("Post-verification failed: duplicate bootstrap call");
                Console.WriteLine("Post-verification SUCCESSFUL: Clean single-instruction injection verified.");
            }

            Console.WriteLine("STATUS: SUCCESS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FATAL ERROR: " + ex);
            return 99;
        }
    }

    static Instruction CloneInstruction(
        Instruction src,
        ModuleDefinition module,
        Dictionary<FieldDefinition, FieldDefinition> fieldMap,
        Dictionary<MethodDefinition, MethodDefinition> methodMap)
    {
        if (src.Operand == null)
            return Instruction.Create(src.OpCode);

        var fr = src.Operand as FieldReference;
        if (fr != null)
        {
            var resolved = fr.Resolve();
            if (resolved != null && fieldMap.ContainsKey(resolved))
            {
                return Instruction.Create(src.OpCode, fieldMap[resolved]);
            }
            return Instruction.Create(src.OpCode, module.ImportReference(fr));
        }

        var mr = src.Operand as MethodReference;
        if (mr != null)
        {
            var resolved = mr.Resolve();
            if (resolved != null && methodMap.ContainsKey(resolved))
            {
                return Instruction.Create(src.OpCode, methodMap[resolved]);
            }
            return Instruction.Create(src.OpCode, module.ImportReference(mr));
        }

        var tr = src.Operand as TypeReference;
        if (tr != null)
        {
            return Instruction.Create(src.OpCode, module.ImportReference(tr));
        }

        if (src.Operand is string) return Instruction.Create(src.OpCode, (string)src.Operand);
        if (src.Operand is int) return Instruction.Create(src.OpCode, (int)src.Operand);
        if (src.Operand is byte) return Instruction.Create(src.OpCode, (byte)src.Operand);
        if (src.Operand is sbyte) return Instruction.Create(src.OpCode, (sbyte)src.Operand);
        if (src.Operand is VariableDefinition) return Instruction.Create(src.OpCode, (VariableDefinition)src.Operand);
        if (src.Operand is ParameterDefinition) return Instruction.Create(src.OpCode, (ParameterDefinition)src.Operand);

        var inst = src.Operand as Instruction;
        if (inst != null)
        {
            return Instruction.Create(src.OpCode, inst);
        }

        var insts = src.Operand as Instruction[];
        if (insts != null)
        {
            return Instruction.Create(src.OpCode, insts);
        }

        return Instruction.Create(src.OpCode);
    }
}
