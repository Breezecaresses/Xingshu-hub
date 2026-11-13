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
                foreach (var instruction in tick.Body.Instructions)
                {
                    var call = instruction.Operand as MethodReference;
                    if (call != null && call.DeclaringType.FullName == "UI.UIManager" &&
                        (call.Name == "get_Fight" || call.Name == "get_purchase"))
                        throw new Exception(name + " must not call a lazy native window factory");
                }
                if (!tick.Body.Instructions.Any(i => i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).DeclaringType.Name == "ShownNativeWindows"))
                    throw new Exception(name + " must use the shown-window observer");
                checkedMethods++;
            }
            Console.WriteLine("PASS: " + module.Name + ": " + checkedMethods + " observers use existing windows; no Fight/purchase factories");
        }
    }
}
