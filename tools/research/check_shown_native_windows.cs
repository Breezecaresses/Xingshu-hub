using System;
using System.Collections.Generic;
using XingShuRuntime.Utilities;

// Native window factories deliberately throw: a read-only observer must not call them.
namespace UI
{
    public class BaseWindow { public bool isDisposed, isShowing; public object contentPane; }
    public sealed class FightWindow : BaseWindow { }
    public sealed class PurchaseWindow : BaseWindow { }
    public sealed class UIManager
    {
        public Stack<BaseWindow> propUpWindows = new Stack<BaseWindow>();
        public FightWindow Fight { get { throw new Exception("Fight factory invoked"); } }
        public PurchaseWindow purchase { get { throw new Exception("Purchase factory invoked"); } }
    }
}
internal static class CheckShownNativeWindows
{
    static int assertions;
    static void Check(bool ok) { assertions++; if (!ok) throw new Exception("Window lifecycle regression"); }
    public static void Main()
    {
        Check(ShownNativeWindows.Find<UI.FightWindow>(null) == null);
        var ui = new UI.UIManager();
        for (int i = 0; i < 10000; i++)
        {
            Check(ShownNativeWindows.Find<UI.FightWindow>(ui) == null);
            Check(ShownNativeWindows.Find<UI.PurchaseWindow>(ui) == null);
        }
        ui.propUpWindows.Push(null);
        var fight = new UI.FightWindow(); ui.propUpWindows.Push(fight);
        Check(ShownNativeWindows.Find<UI.FightWindow>(ui) == null);
        fight.isShowing = true;
        Check(ShownNativeWindows.Find<UI.FightWindow>(ui) == null);
        fight.contentPane = new object();
        Check(ReferenceEquals(ShownNativeWindows.Find<UI.FightWindow>(ui), fight));
        var purchase = new UI.PurchaseWindow { isShowing = true, contentPane = new object() };
        ui.propUpWindows.Push(purchase);
        Check(ReferenceEquals(ShownNativeWindows.Find<UI.FightWindow>(ui), fight));
        Check(ReferenceEquals(ShownNativeWindows.Find<UI.PurchaseWindow>(ui), purchase));
        fight.isShowing = false;
        Check(ShownNativeWindows.Find<UI.FightWindow>(ui) == null);
        fight.isShowing = true; fight.isDisposed = true;
        Check(ShownNativeWindows.Find<UI.FightWindow>(ui) == null);
        ui.propUpWindows.Clear();
        Check(ShownNativeWindows.Find<UI.FightWindow>(ui) == null);
        Check(ShownNativeWindows.Find<UI.PurchaseWindow>(ui) == null);
        ui.propUpWindows = null;
        Check(ShownNativeWindows.Find<UI.FightWindow>(ui) == null);
        Console.WriteLine("PASS: " + assertions + " window lifecycle checks; no lazy factories invoked");
    }
}
