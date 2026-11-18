using System;
using System.Linq;
using Mono.Cecil;

internal static class CheckWindowObserverIl
{
    public static void Main(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("Pass the compiled runtime DLLs");
        foreach (var path in args)
        using (var module = ModuleDefinition.ReadModule(path))
        {
            int checkedMethods = 0;
            foreach (string name in new[] { "DefensePointHint", "DailyStarCoinShop" })
            {
                var type = module.Types.Single(t => t.Namespace == "XingShuRuntime.Utilities" && t.Name == name);
                var tick = type.Methods.Single(m => m.Name == "Tick" && m.HasBody);
                if (!tick.Body.Instructions.Any(i => i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).DeclaringType.Name == "ShownNativeWindows"))
                    throw new Exception(name + " must use the shown-window observer");
                checkedMethods++;
            }
            int scannedMethods = 0;
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    scannedMethods++;
                    foreach (var instruction in method.Body.Instructions)
                    {
                        var call = instruction.Operand as MethodReference;
                        if (call == null || call.DeclaringType.FullName != "UI.UIManager" || !call.Name.StartsWith("get_"))
                            continue;
                        string returnType = call.ReturnType.FullName;
                        if (returnType == "UI.IBasePanel" || returnType == "System.Boolean")
                            continue;
                        bool allowedToast = call.Name == "get_systemTips" && (
                            (type.FullName == "XingShuRuntime.Social.XingShuAutoPraise" && method.Name == "ShowHint") ||
                            (type.FullName == "XingShuRuntime.Utilities.ReplayPlayerGUI" && method.Name == "ShowFailTip") ||
                            (type.DeclaringType != null && type.DeclaringType.FullName == "XingShuRuntime.Utilities.AccountInfoCopyHelper"));
                        if (!allowedToast)
                            throw new Exception(type.FullName + "." + method.Name + " must not call lazy UIManager getter " + call.Name);
                    }
                }
            }
            Console.WriteLine("PASS: " + module.Name + ": " + checkedMethods + " observers + " + scannedMethods + " methods verified; no lazy UIManager window factories");
        }
    }
}
