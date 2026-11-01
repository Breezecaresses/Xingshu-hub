using System;
using System.Collections.Generic;
using System.Text;
using Core.Scene;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;
using XingShu.Cards;
using XingShu.Truth;
using XingShuRuntime.Core;
using XingShuRuntime.UI;

namespace XingShuRuntime.Utilities
{
    // An additional presentation of the existing hand store. It neither replaces
    // the movable dock nor issues a different kind of hand-data request.
    public sealed class HandCardRoster : MonoBehaviour
    {
        private sealed class Row
        {
            internal GComponent Root, Grid, Toggle;
            internal GGraph Badge;
            internal GTextField Caption;
            internal Vector2 DragLast;
            internal int DragPointer = int.MinValue;
            internal UIBattleInfo_Button_PlayerInfo Head;
            internal TrackedPlayerHand Hand;
            internal HandRosterPlan Plan;
            internal long Player;
            internal string Signature, DataSignature;
            internal bool Logged, LastExpanded;
            internal float LastCell, RenderedCell, RenderedUnit, RenderedOffset, RenderedStep;
            internal int LastCount, RenderedCount = -1;
            internal int Generation;
            internal Rect Anchor;
            internal readonly List<TrackedCard> Entries = new List<TrackedCard>();
            internal readonly List<GObject> Tiles = new List<GObject>();
            internal int Unknown, Count;
        }

        private static HandCardRoster _instance;
        private readonly HandRosterState _state = new HandRosterState();
        private readonly Row[] _rows = new Row[4];
        private float _nextData, _nextError;
        private bool _active, _chipHidden;
        private readonly Dictionary<long, TrackedPlayerHand> _hands = new Dictionary<long, TrackedPlayerHand>();

        internal static bool Enabled
        {
            get => XingShuConfigService.GetBool(XingShuConfigKeys.HandRosterEnabled, true);
            set
            {
                XingShuConfigService.SetBool(XingShuConfigKeys.HandRosterEnabled, value);
                XingShuConfigService.Save();
                if (value) HandCardDisplayManager.IsEnabled = true;
            }
        }
        internal static float Scale
        {
            get => XingShuConfigService.GetFloat(XingShuConfigKeys.HandRosterScale, .8f, .6f, 1.5f);
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                XingShuConfigService.SetFloat(XingShuConfigKeys.HandRosterScale, value, .6f, 1.5f);
                XingShuConfigService.Save();
            }
        }
        internal static bool IsExpanded(long player) => _instance != null && _instance._active && _instance._state.IsExpanded(player);
        internal static bool HasExpanded
        {
            get
            {
                if (_instance == null || !_instance._active) return false;
                foreach (var row in _instance._rows) if (row != null && row.Root != null && !row.Root.isDisposed && row.Root.visible && row.Root.onStage && _instance._state.IsExpanded(row.Player)) return true;
                return false;
            }
        }
        internal static void Ensure(GameObject host)
        {
            if (host != null && host.GetComponent<HandCardRoster>() == null) host.AddComponent<HandCardRoster>();
        }
        private void Awake()
        {
            _instance = this;
            if (!XingShuConfigService.GetBool(XingShuConfigKeys.HandRosterCompactDefaultApplied, false))
            {
                float previous = XingShuConfigService.GetFloat(XingShuConfigKeys.HandRosterScale, .8f);
                if (Mathf.Abs(previous - 1f) < .04f) XingShuConfigService.SetFloat(XingShuConfigKeys.HandRosterScale, .8f);
                XingShuConfigService.SetBool(XingShuConfigKeys.HandRosterCompactDefaultApplied, true);
                XingShuConfigService.Save();
            }
            XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[HandRoster] native avatar layer ready; movable dock preserved");
        }

        private void Build()
        {
            for (int i = 0; i < _rows.Length; i++) _rows[i] = new Row();
        }
        private void EnsureRow(Row row)
        {
            if (row.Root != null && !row.Root.isDisposed && row.Root.parent == row.Head) return;
            if (row.Root != null && !row.Root.isDisposed) row.Root.Dispose();
            row.Root = new GComponent { name = "__xingshu_roster_hand", sortingOrder = 0 };
            row.Head.AddChild(row.Root); // Native avatar owns drawing order and lifecycle.
            XingShuOverlayInput.Protect(row.Root);
            row.Toggle = new GComponent { name = "ExpandPlayerHand" };
            XingShuOverlayInput.Protect(row.Toggle); row.Root.AddChild(row.Toggle);
            row.Badge = new GGraph { touchable = false }; row.Toggle.AddChild(row.Badge);
            row.Caption = Label(row.Toggle, "手牌", 0, 0, 48, 40, 12, Color.black);
            row.Toggle.onClick.Add((EventCallback0)(() => Toggle(row)));
            row.Grid = new GComponent { name = "IndividualCards" };
            XingShuOverlayInput.Protect(row.Grid); row.Root.AddChild(row.Grid);
            row.Grid.onTouchBegin.Add((EventCallback1)(context =>
            {
                var input = context.inputEvent; if (input == null) return;
                row.DragLast = input.position; row.DragPointer = input.touchId; context.CaptureTouch();
            }));
            row.Grid.onTouchMove.Add((EventCallback1)(context =>
            {
                var input = context.inputEvent;
                if (input == null || row.DragPointer != input.touchId) return;
                Scroll(row, row.DragLast.x - input.position.x); row.DragLast = input.position;
            }));
            row.Grid.onTouchEnd.Add((EventCallback1)(context => row.DragPointer = int.MinValue));
            row.Grid.AddEventListener("onMouseWheel", (EventCallback1)(context =>
            {
                if (context.inputEvent != null) Scroll(row, -context.inputEvent.mouseWheelDelta * row.Plan.Cell);
            }));
            row.Signature = null; row.RenderedCount = -1;
        }
        private static GTextField Label(GComponent parent, string text, float x, float y, float width, float height, int fontSize, Color color)
        {
            var label = new GTextField { autoSize = AutoSizeType.None, touchable = false, verticalAlign = VertAlignType.Middle };
            label.SetXY(x, y); label.SetSize(width, height);
            var format = label.textFormat; format.size = fontSize; format.color = color; format.align = AlignType.Center; format.bold = true;
            label.textFormat = format; label.text = text; parent.AddChild(label); return label;
        }

        private void LateUpdate()
        {
            try
            {
                var logic = SimpleSingletonProvider<GameLogicManager>.inst;
                long room = HandSessionLifecycle.ActiveRoomId();
                bool supported = HandCardDisplayManager.IsPVEMode() || XingShu.Spectator.SpectatorStateStore.IsInSpectatorSession();
                bool battle = SimpleSingletonProvider<SceneManager>.inst?.currentType?.Value == SceneType.Battle;
                string session = room > 0 && battle && supported ? room + "/" + logic?.room?.curRoomInfo?.info?.StartTime : null;
                if (_state.BeginSession(session))
                {
                    _hands.Clear(); _nextData = 0;
                    if (_rows[0] != null) foreach (var row in _rows) Reset(row);
                }
                bool enabled = Enabled && HandCardDisplayManager.IsEnabled;
                if (session == null || !enabled)
                {
                    Hide();
                    if (!enabled) _state.BeginSession(null);
                    return;
                }
                if (XingShuUICenter.IsOpen) { Hide(); return; }
                bool chipHidden = HandCardVisibility.ShouldHide;
                if (_chipHidden != chipHidden)
                {
                    _chipHidden = chipHidden;
                    XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[HandRoster] nativeModalHidden=" + chipHidden + "; window=" + HandCardVisibility.BlockingWindow);
                }
                if (chipHidden) { Hide(); return; }
                var container = logic?.battle?.battleInfo?.UIObject?.com_BattlePlayer?.com_Container;
                if (container == null || !Visible(container)) { Hide(); return; }
                if (_rows[0] == null) Build();
                _active = true;
                _rows[0].Head = container.com_Player_1; _rows[1].Head = container.com_Player_2;
                _rows[2].Head = container.com_Player_3; _rows[3].Head = container.com_Player_4;
                bool refreshData = Time.unscaledTime >= _nextData;
                if (refreshData)
                {
                    _nextData = Time.unscaledTime + .25f;
                    _hands.Clear();
                    var roster = HandTruthSource.GetActiveRoomPlayerIds();
                    foreach (var pair in HandCardDisplayManager.PlayerHands)
                        if (pair.Value != null && (roster == null || roster.Count == 0 || roster.Contains(pair.Key))) _hands[pair.Key] = pair.Value;
                }
                Rect safe = XingShuMobileUIHelper.SafeScreenRect;
                safe.y = Screen.height - safe.yMax;
                bool mobile = XingShuMobileUIHelper.IsMobile;
                float size = Scale;
                foreach (var row in _rows) row.Anchor = Anchor(row.Head);
                foreach (var row in _rows)
                {
                    var player = row.Head?.PlayerData?.player;
                    TrackedPlayerHand hand;
                    if (row.Anchor.width <= 0 || player == null || player.Slot < 0 || player.Slot > 3 ||
                        !_hands.TryGetValue(player.Id, out hand) || hand.PlayerId != player.Id)
                    { Reset(row); continue; }
                    if (row.Player != player.Id) { Reset(row); row.Player = player.Id; }
                    row.Hand = hand;
                    if (refreshData || row.DataSignature == null) { ReadEntries(row); row.DataSignature = CardSignature(row); }
                    float lane = float.PositiveInfinity;
                    foreach (var other in _rows)
                        if (other != row && other.Anchor.width > 0)
                        { float gap = Mathf.Abs(other.Anchor.center.y - row.Anchor.center.y); if (gap > 1) lane = Mathf.Min(lane, gap); }
                    if (float.IsInfinity(lane)) lane = row.Anchor.height;
                    bool expanded = _state.IsExpanded(row.Player);
                    row.Plan = HandRosterLayout.Build(row.Anchor, safe, lane, row.Entries.Count, size, expanded, mobile);
                    if (!row.Plan.Valid) { Reset(row); continue; }
                    EnsureRow(row); row.Root.visible = true;
                    ApplyRow(row, expanded);
                }
            }
            catch (Exception ex)
            {
                Hide();
                if (Time.unscaledTime >= _nextError)
                { _nextError = Time.unscaledTime + 10; Debug.LogWarning("[XingShu HandRoster] " + ex.Message); }
            }
        }

        private static bool Visible(GObject item)
        {
            if (item == null || item.isDisposed || !item.onStage) return false;
            for (GObject node = item; node != null; node = node.parent)
                if (node.isDisposed || !node.visible || node.alpha <= .01f || node.displayObject == null || !node.displayObject.visible) return false;
            return true;
        }
        private static Rect ObjectBounds(GObject item)
        {
            if (!Visible(item) || item.width <= 0 || item.height <= 0) return default(Rect);
            var a = item.LocalToGlobal(Vector2.zero); var b = item.LocalToGlobal(new Vector2(item.width, item.height));
            if (float.IsNaN(a.x) || float.IsNaN(a.y) || float.IsNaN(b.x) || float.IsNaN(b.y) ||
                float.IsInfinity(a.x) || float.IsInfinity(a.y) || float.IsInfinity(b.x) || float.IsInfinity(b.y)) return default(Rect);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
        private static Rect Anchor(UIBattleInfo_Button_PlayerInfo head)
        {
            if (!Visible(head)) return default(Rect);
            Rect result = ObjectBounds(head.com_player), label = ObjectBounds(head.btn_Label);
            if (result.width <= 0) result = ObjectBounds(head);
            if (label.width > 0) result = Rect.MinMaxRect(Mathf.Min(result.xMin, label.xMin), Mathf.Min(result.yMin, label.yMin), Mathf.Max(result.xMax, label.xMax), Mathf.Max(result.yMax, label.yMax));
            return result;
        }
        private static void ReadEntries(Row row)
        {
            row.Entries.Clear(); row.Unknown = 0;
            foreach (var card in row.Hand.Cards)
            { if (card != null && card.CardId > 0) row.Entries.Add(card); else row.Unknown++; }
            row.Count = Math.Max(row.Hand.Cards.Count, row.Hand.MaskedHandCount);
            row.Unknown = Math.Max(row.Unknown, row.Count - row.Entries.Count);
            if (row.Unknown > 0 || row.Entries.Count == 0) row.Entries.Add(null);
        }
        private static string CardSignature(Row row)
        {
            var signature = new StringBuilder().Append(row.Player).Append(':').Append(row.Hand.SyncState).Append(':').Append(row.Hand.IsAuthoritative).Append(':').Append(row.Unknown);
            foreach (var card in row.Hand.Cards) if (card != null) signature.Append('|').Append(card.CardId).Append(':').Append(card.BattleCost).Append(':').Append(card.PurifyNum);
            try { signature.Append(':').Append(global::Core.GameSettings.angelMode); } catch { }
            return signature.ToString();
        }
        private void ApplyRow(Row row, bool expanded)
        {
            var p = row.Plan;
            // Layout uses physical pixels; cancel the native parent's UI scale.
            var origin = row.Head.GlobalToLocal(new Vector2(p.Bounds.x, p.Bounds.y));
            var pixel = row.Head.GlobalToLocal(new Vector2(p.Bounds.x + 1, p.Bounds.y + 1)) - origin;
            if (pixel.x <= 0 || pixel.y <= 0 || float.IsNaN(pixel.x) || float.IsNaN(pixel.y)) { row.Root.visible = false; return; }
            row.Root.SetXY(origin.x, origin.y); row.Root.SetScale(pixel.x, pixel.y); row.Root.SetSize(p.Bounds.width, p.Bounds.height);
            row.Toggle.SetXY(0, (p.Bounds.height - p.ToggleHeight) * .5f); row.Toggle.SetSize(p.ToggleWidth, p.ToggleHeight);
            row.Badge.DrawRoundRect(p.ToggleWidth, p.ToggleHeight, expanded ? new Color(1, .79f, 0, .96f) : new Color(1, 1, 1, .94f), new float[] { 8, 8, 8, 8 });
            row.Caption.SetSize(p.ToggleWidth, p.ToggleHeight);
            var format = row.Caption.textFormat; format.size = Mathf.Max(10, Mathf.RoundToInt((XingShuMobileUIHelper.IsMobile ? 14 : 12) * p.Unit)); row.Caption.textFormat = format;
            row.Caption.text = "手牌\n" + (row.Hand.SyncState == HandSyncState.Latest ? row.Count.ToString() : "…") + (expanded ? " ◂" : " ▸");
            row.Grid.visible = expanded;
            if (!row.Logged || row.LastExpanded != expanded || row.LastCount != p.Count || Mathf.Abs(row.LastCell - p.Cell) > .05f)
            {
                row.Logged = true; row.LastExpanded = expanded; row.LastCount = p.Count; row.LastCell = p.Cell;
                XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[HandRoster] native layout slot=" + (row.Hand.Slot + 1) + "; expanded=" + expanded + "; bounds=" + p.Bounds +
                    "; cell=" + p.Cell.ToString("F1") + "; step=" + p.Step.ToString("F1") + "; cards=" + p.Count + "; avatarParent=" + (row.Root.parent == row.Head));
            }
            if (!expanded) return;
            float offset = _state.Offset(row.Player, p.MaxOffset);
            row.Grid.SetXY(p.GridX, p.GridY); row.Grid.SetSize(p.GridWidth, p.Cell + 2 * p.Gap);
            ((Container)row.Grid.displayObject).clipRect = new Rect(0, 0, p.GridWidth, p.Cell + 2 * p.Gap);
            if (row.Signature != row.DataSignature || row.RenderedCount != p.Count || Mathf.Abs(row.RenderedStep - p.Step) > .01f ||
                Mathf.Abs(row.RenderedCell - p.Cell) > .01f || Mathf.Abs(row.RenderedUnit - p.Unit) > .01f)
            {
                row.Signature = row.DataSignature; row.RenderedCount = p.Count; row.RenderedOffset = offset; row.RenderedStep = p.Step;
                row.RenderedCell = p.Cell; row.RenderedUnit = p.Unit; Render(row, offset);
            }
            else if (Mathf.Abs(row.RenderedOffset - offset) > .01f)
            {
                row.RenderedOffset = offset;
                for (int i = 0; i < row.Tiles.Count; i++) row.Tiles[i].SetXY(HandRosterLayout.CardX(p, i, offset), p.Gap);
            }
        }

        private void Render(Row row, float offset)
        {
            row.Generation++; row.Grid.RemoveChildren(0, -1, true); row.Tiles.Clear();
            var p = row.Plan;
            for (int slot = 0; slot < row.Entries.Count; slot++)
            {
                var card = row.Entries[slot];
                var tile = new GComponent { name = card == null ? "UnavailableHandSummary" : "HandCard", touchable = false };
                tile.SetXY(HandRosterLayout.CardX(p, slot, offset), p.Gap); tile.SetSize(p.Cell, p.Cell);
                row.Grid.AddChild(tile); row.Tiles.Add(tile);
                var background = new GGraph { touchable = false }; tile.AddChild(background);
                background.DrawRect(p.Cell, p.Cell, 1, Color.black, new Color(.13f, .12f, .17f, .96f));
                if (card == null)
                {
                    Label(tile, row.Unknown > 0 ? "未同步\n×" + row.Unknown : row.Hand.SyncState == HandSyncState.Latest ? "暂无\n手牌" : "等待\n同步",
                        0, 0, p.Cell, p.Cell, Mathf.RoundToInt(12 * p.Unit), new Color(.92f, .83f, .55f));
                    continue;
                }
                var image = new GImage { touchable = false }; image.SetXY(2 * p.Unit, 2 * p.Unit); image.SetSize(p.Cell - 4 * p.Unit, p.Cell - 4 * p.Unit); tile.AddChild(image);
                bool alt; string altTitle;
                var images = HandCardDisplayManager.GetCardImageCandidates(card.Config, row.Hand, SimpleSingletonProvider<GameLogicManager>.inst.account.IsSelf(row.Player), out alt, out altTitle);
                if (images.Count == 0 && CardRegistryGenerated.TryGetCard(card.CardId, out var fallback) && !string.IsNullOrEmpty(fallback.ImageKey)) images.Add(fallback.ImageKey);
                if (images.Count > 0) HandCardDisplayManager.SafeLoadCardTexture(images, image, tile);
                float nameHeight = 17 * p.Unit;
                var plate = new GGraph { touchable = false }; plate.SetXY(0, p.Cell - nameHeight); tile.AddChild(plate);
                plate.DrawRect(p.Cell, nameHeight, 0, Color.clear, new Color(.05f, .04f, .08f, .85f));
                Label(tile, Name(card), 0, p.Cell - nameHeight, p.Cell, nameHeight, Mathf.RoundToInt(11 * p.Unit), Color.white);
                if (card.BattleCost > 0)
                {
                    var cost = Label(tile, "◆" + card.BattleCost, 2 * p.Unit, 0, p.Cell - 4 * p.Unit, 18 * p.Unit, Mathf.RoundToInt(12 * p.Unit), Color.white);
                    var format = cost.textFormat; format.align = AlignType.Left; cost.textFormat = format; cost.stroke = 1; cost.strokeColor = Color.black;
                }
            }
        }

        private void Toggle(Row row)
        {
            if (!_active || row.Hand == null) return;
            bool expanded = _state.Toggle(row.Player); row.Signature = null; _nextData = 0;
            if (expanded && (HandCardDisplayManager.NeedFetchFromServer || row.Hand.SyncState != HandSyncState.Latest)) HandCardDisplayManager.TriggerFetchCards("RosterGridExpanded");
            XingShu.Runtime.XingShuRuntimeHost.LogLifecycle("[HandRoster] slot=" + (row.Hand.Slot + 1) + "; expanded=" + expanded + "; cards=" + row.Count);
        }
        private void Scroll(Row row, float delta)
        { _state.Scroll(row.Player, delta, row.Plan.MaxOffset); }
        private static string Name(TrackedCard card)
        { var config = card.Config; return HandCardDock.PlainCardText(config == null ? CardRegistryGenerated.GetCardName(card.CardId) : config.NameID.GetLocal(UIStringType.Card)); }

        private static void Reset(Row row)
        {
            if (row.Root != null && !row.Root.isDisposed) row.Root.visible = false;
            row.Player = 0; row.Hand = null;
            row.Signature = null; row.DataSignature = null; row.Logged = false; row.RenderedCount = -1; row.Generation++;
        }
        private void Hide()
        {
            _active = false;
            foreach (var row in _rows) if (row?.Root != null && !row.Root.isDisposed) row.Root.visible = false;
        }
        private void OnDisable() { Hide(); }
        private void OnDestroy() { Hide(); foreach (var row in _rows) if (row?.Root != null && !row.Root.isDisposed) row.Root.Dispose(); if (_instance == this) _instance = null; }
    }
}
