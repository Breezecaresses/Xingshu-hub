using System;
using System.Collections.Generic;
using XingShuRuntime.Utilities;
using UnityEngine;

internal static class HandRosterTests
{
    private static int _checks, _failed;
    private static void Check(bool pass, string message)
    { _checks++; if (!pass) { _failed++; Console.WriteLine("FAIL " + message); } }
    private static bool Near(float a, float b) => Math.Abs(a - b) < .01f;

    private static void CheckLayout(int width, int height, bool mobile, float size, int count)
    {
        float ratio = Math.Min(width / 1600f, height / 900f);
        var safe = new Rect(mobile ? 28 * ratio : 0, 0, width - (mobile ? 56 * ratio : 0), height);
        var plans = new HandRosterPlan[4];
        for (int i = 0; i < 4; i++)
        {
            var head = new Rect(safe.x + 4 * ratio, (90 + i * 112) * ratio, 250 * ratio, 106 * ratio);
            var p = HandRosterLayout.Build(head, safe, 112 * ratio, count, size, true, mobile); plans[i] = p;
            Check(p.Valid, "supported view must produce a usable grid");
            Check(p.Bounds.xMin >= head.xMax, "grid stays beside native HP/coins");
            Check(p.Bounds.xMax <= safe.x + safe.width * .64f + .01f, "grid leaves right-hand action controls clear");
            Check(p.Bounds.yMin >= safe.yMin && p.Bounds.yMax <= safe.yMax, "grid stays inside safe area");
            Check(p.Step >= p.Cell * .72f && p.Step <= p.Cell + p.Gap, "only gentle overlap; never squeeze cards into unreadable strips");
            Check(p.Bounds.height <= 112 * ratio, "many cards cannot cover the next player row");
            Check(p.Cell >= 24 * p.Unit - .01f && p.Cell <= 87 * p.Unit + .01f, "cell stays square and readable (subpixel rounding tolerance)");
            var visited = new HashSet<int>();
            for (int index = 0; index < count; index++)
            {
                Check(visited.Add(index), "no repeated hand entry in the strip");
                float offset = HandRosterLayout.Clamp(index * p.Step, 0, p.MaxOffset);
                float x = HandRosterLayout.CardX(p, index, offset);
                Check(x >= p.Gap - .01f && x + p.Cell <= p.GridWidth - p.Gap + .01f, "every card can be fully exposed within the invisible viewport");
                Check(p.GridY + p.Gap + p.Cell <= p.Bounds.height + .01f, "single-card row vertically contained");
            }
            Check(visited.Count == count, "every card is reachable without pagination");
            if (count >= 7) Check(p.Step < p.Cell, "many cards overlap slightly instead of creating a page footer");
            var closed = HandRosterLayout.Build(head, safe, 112 * ratio, count, size, false, mobile);
            Check(Near(closed.Bounds.width, p.ToggleWidth), "closed row only captures its small toggle");
            Check(Near(closed.Bounds.height, p.ToggleHeight), "closed row does not capture invisible cards");
        }
        for (int i = 0; i < 3; i++) Check(plans[i].Bounds.yMax < plans[i + 1].Bounds.yMin, "expanded player rows do not overlap");
    }

    public static int Main()
    {
        foreach (var screen in new[] { new[] { 1280, 720 }, new[] { 1366, 768 }, new[] { 1600, 900 }, new[] { 1920, 1080 }, new[] { 2400, 1080 }, new[] { 2560, 1440 } })
            foreach (bool mobile in new[] { false, true })
                foreach (float size in new[] { .6f, .8f, 1f, 1.5f })
                    foreach (int count in new[] { 0, 1, 4, 7, 12, 30 }) CheckLayout(screen[0], screen[1], mobile, size, count);

        var offset = new Rect(36, 48, 1460, 780);
        var normalHead = new Rect(40, 100, 240, 100);
        var dense = HandRosterLayout.Build(normalHead, offset, 220, 30, 1, true, false);
        Check(dense.Count == 30 && dense.MaxOffset > 0 && dense.Bounds.height < 100, "many cards stay in one frameless strip with overflow drag");
        Check(dense.Bounds.y >= offset.yMin && dense.Bounds.yMax <= offset.yMax, "nonzero safe-area origin is respected");
        Check(!HandRosterLayout.Build(new Rect(1000, 100, 220, 100), offset, 110, 10, 1, true, false).Valid, "no room beside native panel must not cover it");
        Check(!HandRosterLayout.Build(new Rect(float.NaN, 1, 240, 100), offset, 110, 10, 1, true, false).Valid, "invalid native transform is hidden");
        Check(!HandRosterLayout.Build(new Rect(0, 0, .002f, .002f), offset, 110, 10, 1, true, false).Valid, "native transition shrinking to zero cannot flash controls at screen origin");
        Check(!HandRosterLayout.Build(normalHead, new Rect(0, 0, 0, 0), 110, 10, 1, true, false).Valid, "unavailable screen cannot build offscreen UI");
        Check(HandRosterLayout.Build(normalHead, offset, float.NaN, 10, float.NaN, true, false).Valid, "bad preferences fall back without corrupting the view");

        var state = new HandRosterState();
        Check(state.BeginSession("room/match1"), "first match establishes state");
        Check(!state.IsExpanded(11) && !state.IsExpanded(33), "new match starts folded");
        Check(state.Toggle(11) && state.Toggle(33), "multiple players can expand together");
        Check(state.IsExpanded(11) && state.IsExpanded(33), "expanded identity does not depend on current ranking");
        Check(!state.BeginSession("room/match1") && state.IsExpanded(11), "returning from a battle does not collapse this match");
        state.Scroll(11, 80, 100); Check(Near(state.Offset(11, 100), 80), "horizontal drag");
        state.Scroll(11, -200, 100); Check(Near(state.Offset(11, 100), 0), "drag cannot exceed the first card");
        state.Scroll(11, 200, 100); Check(Near(state.Offset(11, 100), 100), "drag cannot exceed the last card");
        Check(Near(state.Offset(11, 0), 0), "discarded cards clamp vanished overflow");
        state.Scroll(11, float.NaN, 100); Check(Near(state.Offset(11, 100), 0), "invalid gesture does not corrupt offsets");
        state.Scroll(11, 80, 100); state.Toggle(11);
        Check(!state.IsExpanded(11) && state.IsExpanded(33), "closing one player leaves the other expanded");
        state.Toggle(11); Check(Near(state.Offset(11, 100), 0), "reopening begins at the first card");
        Check(!state.Toggle(0) && !state.IsExpanded(0), "missing player cannot expand");
        Check(state.BeginSession("room/match2") && !state.IsExpanded(11) && !state.IsExpanded(33), "same-room rematch cannot retain old player display");
        state.Toggle(11); state.BeginSession(null);
        Check(!state.IsExpanded(11) && Near(state.Offset(11, 100), 0), "leaving battle clears hand presentation state");

        Tools.SimpleSingletonProvider<UI.UIManager>.Value = null;
        Tools.SimpleSingletonProvider<UI.UIManager>.Reads = 0;
        Check(!HandCardVisibility.ShouldHide, "no UI manager means no native modal");
        Check(Tools.SimpleSingletonProvider<UI.UIManager>.Reads == 0, "visibility check must not instantiate the native UI manager");
        var ui = new UI.UIManager(); Tools.SimpleSingletonProvider<UI.UIManager>.Value = ui;
        Check(!HandCardVisibility.ShouldHide && ui.Created == 0, "checking hidden chooser must not trigger lazy window creation");
        state.BeginSession("chip-test"); state.Toggle(11); state.Scroll(11, 40, 100);
        var stale = new UI.ChooseRoundCardWindow { isShowing = true }; ui.SetChooser(stale);
        Check(!HandCardVisibility.ShouldHide, "stale cached window is not the visible instance");
        foreach (var window in new FairyGUI.Window[] { new UI.ChooseRoundCardWindow(), new UI.BattleSelectMonsterWindow(), new UI.LandShopWindow(), new UI.UpgradeWindow() })
        {
            FairyGUI.GRoot.inst.Add(window);
            Check(HandCardVisibility.ShouldHide && HandCardVisibility.BlockingWindow == window.GetType().Name, "actual foreground instance hides hands even when absent from UIManager cache");
            state.BeginSession("chip-test");
            Check(state.IsExpanded(11) && Near(state.Offset(11, 100), 40), "temporary native modal preserves expanded player and scroll position");
            window.visible = false; Check(!HandCardVisibility.ShouldHide, "hidden foreground instance does not keep hands suppressed");
            window.visible = true; window.isDisposed = true; Check(!HandCardVisibility.ShouldHide, "disposed native window is ignored");
            FairyGUI.GRoot.inst.Remove(window);
            Check(!HandCardVisibility.ShouldHide, "closing native window restores hand eligibility");
        }
        var timer = new UI.OperateTimeWindow(); FairyGUI.GRoot.inst.Add(timer);
        Check(!HandCardVisibility.ShouldHide, "ordinary native HUD timer does not hide cards"); FairyGUI.GRoot.inst.Remove(timer);
        Check(ui.Created == 0, "visibility checks never call UIManager's lazy chooser getter");
        Console.WriteLine("Hand roster layout/state: " + (_checks - _failed) + "/" + _checks + " passed");
        return _failed == 0 ? 0 : 1;
    }
}

namespace UI
{
    internal sealed class ChooseRoundCardWindow : FairyGUI.Window { internal bool isShowing; }
    internal sealed class BattleSelectMonsterWindow : FairyGUI.Window { }
    internal sealed class LandShopWindow : FairyGUI.Window { }
    internal sealed class UpgradeWindow : FairyGUI.Window { }
    internal sealed class OperateTimeWindow : FairyGUI.Window { }
    internal sealed class UIManager
    {
        private ChooseRoundCardWindow _cachedChooseRoundCardWindow;
        internal int Created;
        internal ChooseRoundCardWindow ChooseRoundCard { get { if (_cachedChooseRoundCardWindow == null) { Created++; _cachedChooseRoundCardWindow = new ChooseRoundCardWindow(); } return _cachedChooseRoundCardWindow; } }
        internal void SetChooser(ChooseRoundCardWindow window) { _cachedChooseRoundCardWindow = window; }
    }
}
namespace FairyGUI
{
    internal class GObject { internal bool visible = true, isDisposed; internal GRoot parent; internal bool onStage => parent != null; }
    internal class Window : GObject { }
    internal sealed class GRoot
    {
        internal static readonly GRoot inst = new GRoot();
        private readonly List<GObject> _children = new List<GObject>();
        internal int numChildren => _children.Count;
        internal GObject GetChildAt(int index) => _children[index];
        internal void Add(GObject child) { child.parent = this; _children.Add(child); }
        internal void Remove(GObject child) { child.parent = null; _children.Remove(child); }
    }
}
namespace Tools
{
    internal static class SimpleSingletonProvider<T> where T : class
    {
        internal static T Value; internal static int Reads;
        internal static bool hasInstance => Value != null;
        internal static T inst { get { Reads++; return Value; } }
    }
}

// Engine-free geometry input/output. Production layout and session state above
// are compiled directly; no game rendering or device acceptance is simulated.
namespace UnityEngine
{
    internal struct Vector2 { internal float x, y; internal Vector2(float x, float y) { this.x = x; this.y = y; } }
    internal struct Rect
    {
        internal float x, y, width, height;
        internal Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        internal float xMin => x; internal float yMin => y; internal float xMax => x + width; internal float yMax => y + height;
        internal Vector2 center => new Vector2(x + width / 2, y + height / 2);
    }
}
