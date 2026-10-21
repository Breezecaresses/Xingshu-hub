using System;
using System.Linq;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

// Only the three local decision timers are redirected. The AOT timer library,
// network deadlines, replay timers and other players' timers are untouched.
class ThinkingClockPatcher
{
    static void Main(string[] args)
    {
        try { Patch(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); Console.Error.WriteLine(ex.StackTrace); Environment.Exit(1); }
    }

    static void Patch(string[] args)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (string dir in new[] { "updated_20261019_verified", "unity", "framework", "thirdparty" })
            resolver.AddSearchDirectory(Path.Combine(args[3], "XingShu-Dev", "references", dir));
        var reader = new ReaderParameters { AssemblyResolver = resolver };
        using (var game = AssemblyDefinition.ReadAssembly(args[0], reader))
        using (var hooks = AssemblyDefinition.ReadAssembly(args[1], reader))
        {
            var module = game.MainModule;
            var clock = hooks.MainModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.ThinkingClock");
            var enabled = hooks.MainModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.ThinkingTime")
                .Methods.Single(m => m.Name == "UseRealClock");
            var ctor = clock.Methods.Single(m => m.IsConstructor && m.IsPublic);
            var attach = clock.Methods.Single(m => m.Name == "AttachStop");
            var cancelField = clock.Fields.Single(f => f.Name == "Cancelled");
            var tickField = clock.Fields.Single(f => f.Name == "Tick");
            var owner = module.Types.FirstOrDefault(t => t.Name == "XingShuDecisionTimer");
            if (owner == null) { owner = new TypeDefinition("", "XingShuDecisionTimer", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object); module.Types.Add(owner); }
            int count = 0;
            foreach (var type in module.Types.ToArray())
            foreach (var method in type.Methods.ToArray())
            {
                bool operation = type.FullName == "GameLogic.OperationTimer" && method.Name == "ActionDownTime";
                bool selection = type.FullName == "UI.RoomHeroPanel" && (method.Name == "Refresh" || method.Name == "RefreshSelectSkin");
                if ((!operation && !selection) || !method.HasBody) continue;
                if (method.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == owner.Name)) { count++; continue; }
                var call = method.Body.Instructions.Single(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "UnityTimer.Timer" && ((MethodReference)i.Operand).Name == "Register");
                var register = (MethodReference)call.Operand;
                if (register.Parameters.Count != 13 || register.Parameters[2].ParameterType.FullName != "System.Action" || register.Parameters[4].ParameterType.FullName != "System.Action" || register.Parameters[7].ParameterType.FullName != "System.Action`1<System.Single>") throw new Exception("Unexpected decision timer signature");
                var wrapper = new MethodDefinition("Register" + count, MethodAttributes.Static | MethodAttributes.Assembly, module.ImportReference(register.ReturnType));
                foreach (var p in register.Parameters) wrapper.Parameters.Add(new ParameterDefinition(p.Name, ParameterAttributes.None, module.ImportReference(p.ParameterType)));
                owner.Methods.Add(wrapper);
                wrapper.Body.InitLocals = true;
                var state = new VariableDefinition(module.ImportReference(clock)); wrapper.Body.Variables.Add(state);
                var timer = new VariableDefinition(module.ImportReference(register.ReturnType)); wrapper.Body.Variables.Add(timer);
                var il = wrapper.Body.GetILProcessor();
                var original = il.Create(OpCodes.Nop);
                il.Emit(OpCodes.Call, module.ImportReference(enabled)); il.Emit(OpCodes.Brfalse, original);
                foreach (int i in new [] { 0, 1, 2, 4, 7 }) il.Emit(OpCodes.Ldarg, wrapper.Parameters[i]);
                il.Emit(OpCodes.Newobj, module.ImportReference(ctor)); il.Emit(OpCodes.Stloc, state);
                for (int i = 0; i < 13; i++)
                {
                    if (i == 1) il.Emit(OpCodes.Ldc_R4, float.MaxValue);
                    else if (i == 2) il.Emit(OpCodes.Ldnull);
                    else if (i == 4 || i == 7) { il.Emit(OpCodes.Ldloc, state); il.Emit(OpCodes.Ldfld, module.ImportReference(i == 4 ? cancelField : tickField)); }
                    else il.Emit(OpCodes.Ldarg, wrapper.Parameters[i]);
                }
                il.Emit(OpCodes.Call, register); il.Emit(OpCodes.Stloc, timer);
                var cancel = new MethodReference("Cancel", module.TypeSystem.Void, module.ImportReference(register.ReturnType)) { HasThis = true };
                var actionCtor = new MethodReference(".ctor", module.TypeSystem.Void, module.ImportReference(register.Parameters[2].ParameterType)) { HasThis = true };
                actionCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object)); actionCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
                il.Emit(OpCodes.Ldloc, state); il.Emit(OpCodes.Ldloc, timer); il.Emit(OpCodes.Ldftn, cancel); il.Emit(OpCodes.Newobj, actionCtor); il.Emit(OpCodes.Callvirt, module.ImportReference(attach));
                il.Emit(OpCodes.Ldloc, timer); il.Emit(OpCodes.Ret);
                il.Append(original);
                foreach (var p in wrapper.Parameters) il.Emit(OpCodes.Ldarg, p);
                il.Emit(OpCodes.Call, register); il.Emit(OpCodes.Ret);
                call.Operand = wrapper; count++;
            }
            if (count != 3) throw new Exception("Expected three local decision timer registrations, found " + count);
            var stats = hooks.MainModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.BattleStatsOverlay");
            PatchObserver(module, stats, "Core.Unit.BattleProperty", "OnGoldChanged", "ObserveGold", false);
            PatchObserver(module, stats, "GameLogic.BattleResultLogic", "OnGameFinishS2CServerCallBack", "ObserveSettlement", true);
            PatchProgress(module, enabled.DeclaringType);
            game.Write(args[2]);
            Console.WriteLine("Real-clock decision timers: " + count);
        }
    }

    static void PatchProgress(ModuleDefinition module, TypeDefinition hooks)
    {
        var operation = module.Types.Single(t => t.FullName == "GameLogic.OperationTimer");
        var start = operation.Methods.Single(m => m.Name == "ActionDownTime");
        var closure = operation.NestedTypes.Single(t => t.Methods.Any(m => m.Name == "<ActionDownTime>b__2"));
        var begin = module.ImportReference(hooks.Methods.Single(m => m.Name == "BeginProgress"));
        if (!Calls(start, "ThinkingTime::BeginProgress"))
        {
            start.Body.SimplifyMacros();
            var anchor = start.Body.Instructions.Single(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "TryShowTimerToPlayer");
            for (int i = 0; i < 4; i++) anchor = anchor.Previous;
            if (anchor.OpCode.Code != Code.Ldloc) throw new Exception("Unexpected local progress registration");
            var il = start.Body.GetILProcessor();
            var first = il.Create(OpCodes.Ldloc, start.Body.Variables.Single(v => v.VariableType.FullName == closure.FullName));
            il.InsertBefore(anchor, first);
            il.InsertBefore(anchor, il.Create(OpCodes.Ldsfld, operation.Fields.Single(f => f.Name == "operationTime")));
            il.InsertBefore(anchor, il.Create(OpCodes.Call, begin));
            foreach (var instruction in start.Body.Instructions)
                if (ReferenceEquals(instruction.Operand, anchor)) instruction.Operand = first;
            start.Body.OptimizeMacros();
        }
        foreach (var method in closure.Methods.Where(m => m.HasBody && m.Name.StartsWith("<ActionDownTime>b__")))
        {
            if (Calls(method, "ThinkingTime::UpdateProgress")) continue;
            var call = method.Body.Instructions.Single(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "UpdateOperationProgress");
            method.Body.SimplifyMacros();
            var il = method.Body.GetILProcessor(); var owner = il.Create(OpCodes.Ldarg_0); il.InsertBefore(call, owner);
            foreach (var instruction in method.Body.Instructions)
                if (ReferenceEquals(instruction.Operand, call)) instruction.Operand = owner;
            call.OpCode = OpCodes.Call; call.Operand = module.ImportReference(hooks.Methods.Single(m => m.Name == "UpdateProgress"));
            method.Body.OptimizeMacros();
        }
        var remote = module.Types.Single(t => t.FullName == "UI.OperateTimeWindow").Methods.Single(m => m.Name == "UpdateShowProgress");
        if (!Calls(remote, "ThinkingTime::AllowRemoteProgress"))
        {
            remote.Body.SimplifyMacros(); var il = remote.Body.GetILProcessor(); var first = remote.Body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Call, module.ImportReference(hooks.Methods.Single(m => m.Name == "AllowRemoteProgress"))));
            il.InsertBefore(first, il.Create(OpCodes.Brtrue, first)); il.InsertBefore(first, il.Create(OpCodes.Ret));
            remote.Body.OptimizeMacros();
        }
        var dispose = operation.Methods.Single(m => m.Name == "Dispose");
        if (!Calls(dispose, "ThinkingTime::ClearProgress"))
            dispose.Body.GetILProcessor().InsertBefore(dispose.Body.Instructions[0], Instruction.Create(OpCodes.Call, module.ImportReference(hooks.Methods.Single(m => m.Name == "ClearProgress"))));
    }

    static bool Calls(MethodDefinition method, string name)
    {
        return method.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains(name));
    }

    static void PatchObserver(ModuleDefinition module, TypeDefinition hooks, string owner, string methodName, string callback, bool checkError)
    {
        var method = module.Types.Single(t => t.FullName == owner).Methods.Single(m => m.Name == methodName);
        if (method.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains("BattleStatsOverlay::" + callback))) return;
        var hook = module.ImportReference(hooks.Methods.Single(m => m.Name == callback));
        var il = method.Body.GetILProcessor(); var first = method.Body.Instructions[0];
        if (checkError) { il.InsertBefore(first, il.Create(OpCodes.Ldarg, method.Parameters[1])); il.InsertBefore(first, il.Create(OpCodes.Brtrue, first)); }
        il.InsertBefore(first, il.Create(OpCodes.Ldarg, method.Parameters[0]));
        il.InsertBefore(first, il.Create(OpCodes.Call, hook));
    }
}
