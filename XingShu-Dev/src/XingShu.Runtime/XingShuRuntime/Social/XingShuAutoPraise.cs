using System;
using System.Collections.Generic;
using System.Reflection;
using Core.Scene;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;
using XingShuRuntime.Core;
using XingShuRuntime.UI;
using XingShuRuntime.Utilities;

namespace XingShuRuntime.Social
{
    /// <summary>Match-local avatar choices, delivered once through native settlement buttons.</summary>
    [DisallowMultipleComponent]
    public sealed class XingShuAutoPraise : MonoBehaviour
    {
        private sealed class Avatar
        {
            internal UIBattleInfo_Button_PlayerInfo Head;
            internal GComponent Button;
            internal GImage Image;
            internal GGraph Mask;
            internal long Player, PressedPlayer;
            internal string PressedSession;
        }
        private static readonly FieldInfo SettlementField = typeof(UIBattleSettlement_Com_Item).GetField("_PlayerSettlement", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo BridgeField = typeof(EventListener).GetField("_bridge", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo LockField = typeof(EventBridge).GetField("_isLocking", BindingFlags.Instance | BindingFlags.NonPublic);
        private static XingShuAutoPraise _instance;
        private readonly PraiseSelection _selection = new PraiseSelection();
        private readonly Avatar[] _avatars = { new Avatar(), new Avatar(), new Avatar(), new Avatar() };
        private readonly List<UIBattleSettlement_Com_Item> _items = new List<UIBattleSettlement_Com_Item>();
        private readonly Dictionary<UIBattleSettlement_Com_Item, float> _seenAt = new Dictionary<UIBattleSettlement_Com_Item, float>();
        private BattleLogic _battle;
        private RoomInfo _room;
        private long _self;
        private bool _settlement, _selfEasterEggLit;
        private float _scanAt, _sendAt;
        private string _status = "已默认开启本局全队点赞，点击彩色图标可取消对应玩家";

        public static XingShuAutoPraise Instance { get { return _instance; } }
        public static bool IsEnabled
        {
            get { return XingShuConfigService.GetBool(XingShuConfigKeys.AutoPraiseEnabled, XingShuConfigKeys.AutoPraiseEnabledDefault); }
            set { XingShuConfigService.SetBool(XingShuConfigKeys.AutoPraiseEnabled, value); }
        }
        public static bool DefaultAllSelected
        {
            get { return XingShuConfigService.GetBool(XingShuConfigKeys.AutoPraiseDefault, XingShuConfigKeys.AutoPraiseDefaultDefault); }
        }
        public static float PraiseScale
        {
            get { return XingShuConfigService.GetFloat(XingShuConfigKeys.AutoPraiseScale, XingShuConfigKeys.AutoPraiseScaleDefault, XingShuConfigKeys.AutoPraiseScaleMin, XingShuConfigKeys.AutoPraiseScaleMax); }
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                float clamped = Mathf.Clamp(value, XingShuConfigKeys.AutoPraiseScaleMin, XingShuConfigKeys.AutoPraiseScaleMax);
                if (Mathf.Abs(PraiseScale - clamped) < .001f) return;
                XingShuConfigService.SetFloat(XingShuConfigKeys.AutoPraiseScale, clamped);
                XingShuConfigService.Save();
                if (_instance != null) _instance.UpdateAvatars();
            }
        }
        public static float OffsetX
        {
            get { return XingShuConfigService.GetFloat(XingShuConfigKeys.AutoPraiseOffsetX, XingShuConfigKeys.AutoPraiseOffsetXDefault, XingShuConfigKeys.AutoPraiseOffsetMin, XingShuConfigKeys.AutoPraiseOffsetMax); }
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                float clamped = Mathf.Clamp(value, XingShuConfigKeys.AutoPraiseOffsetMin, XingShuConfigKeys.AutoPraiseOffsetMax);
                if (Mathf.Abs(OffsetX - clamped) < .01f) return;
                XingShuConfigService.SetFloat(XingShuConfigKeys.AutoPraiseOffsetX, clamped);
                XingShuConfigService.Save();
                if (_instance != null) _instance.UpdateAvatars();
            }
        }
        public static float OffsetY
        {
            get { return XingShuConfigService.GetFloat(XingShuConfigKeys.AutoPraiseOffsetY, XingShuConfigKeys.AutoPraiseOffsetYDefault, XingShuConfigKeys.AutoPraiseOffsetMin, XingShuConfigKeys.AutoPraiseOffsetMax); }
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                float clamped = Mathf.Clamp(value, XingShuConfigKeys.AutoPraiseOffsetMin, XingShuConfigKeys.AutoPraiseOffsetMax);
                if (Mathf.Abs(OffsetY - clamped) < .01f) return;
                XingShuConfigService.SetFloat(XingShuConfigKeys.AutoPraiseOffsetY, clamped);
                XingShuConfigService.Save();
                if (_instance != null) _instance.UpdateAvatars();
            }
        }
        public static void ResetPosition()
        {
            XingShuConfigService.SetFloat(XingShuConfigKeys.AutoPraiseOffsetX, XingShuConfigKeys.AutoPraiseOffsetXDefault);
            XingShuConfigService.SetFloat(XingShuConfigKeys.AutoPraiseOffsetY, XingShuConfigKeys.AutoPraiseOffsetYDefault);
            XingShuConfigService.Save();
            if (_instance != null) _instance.UpdateAvatars();
        }
        public static int PraiseCount { get { return _instance != null ? _instance._selection.SubmittedCount : 0; } }
        public static string StatusText { get { return _instance != null ? _instance._status : "未初始化"; } }
        public static void Install(GameObject host)
        { if (host != null && host.GetComponent<XingShuAutoPraise>() == null) host.AddComponent<XingShuAutoPraise>(); }
        private void Awake()
        { if (_instance == null) _instance = this; else if (_instance != this) Destroy(this); }
        private void OnDestroy()
        { ClearAvatars(); if (_instance == this) { _instance = null; NativePraiseAssets.Reset(); } }

        private bool SyncContext()
        {
            if (!SimpleSingletonProvider<GameLogicManager>.hasInstance || !SimpleSingletonProvider<SceneManager>.hasInstance ||
                SimpleSingletonProvider<SceneManager>.inst.currentType == null ||
                SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle) return false;
            GameLogicManager logic = SimpleSingletonProvider<GameLogicManager>.inst;
            if ((logic.watch != null && logic.watch.PlayerIsWatcher()) ||
                (logic.replay != null && logic.replay.Session != null && logic.replay.Session.IsReplay)) return false;
            _room = logic.room != null ? logic.room.curRoomInfo : null;
            _battle = logic.battle;
            if (_room == null || _room.info == null || _room.info.Id <= 0 || _battle == null || logic.room.roomController == null) return false;
            RoomStateType state = logic.room.roomController.roomStateType;
            if (state != RoomStateType.RUNNING && state != RoomStateType.SETTLEMENT) return false;
            BattlePlayerData self = _battle.GetSelfPlayerData();
            if (self == null || self.player == null || self.player.Id <= 0 || logic.account == null || !logic.account.IsSelf(self.player.Id)) return false;
            _self = self.player.Id;
            if (_selection.Begin(_room.info.Id + "/" + _room.info.StartTime, _self))
            { ClearAvatars(); _seenAt.Clear(); _sendAt = 0; _selfEasterEggLit = false; }
            _settlement = state == RoomStateType.SETTLEMENT;
            return true;
        }
        private bool Represented(BattlePlayerData data)
        {
            if (data == null || data.player == null || _battle == null || _room == null ||
                _battle.GetPlayerDataById(data.player.Id) != data) return false;
            foreach (RoomPlayer player in _room.Players) if (player != null && player.Id == data.player.Id) return true;
            return false;
        }
        private bool Eligible(BattlePlayerData data)
        { return Represented(data) && data.player.Id > 0 && data.player.Id != _self && !data.player.IsBot; }
        private static bool Visible(GObject obj)
        {
            if (obj == null || obj.isDisposed || !obj.onStage) return false;
            for (GObject node = obj; node != null; node = node.parent)
                if (node.isDisposed || !node.visible || node.alpha <= .01f) return false;
            return true;
        }
        private static void EnsureButtonAboveContainer(GComponent targetParent, GComponent container, GComponent button)
        {
            if (targetParent == null || targetParent.isDisposed || button == null || button.isDisposed || button.parent != targetParent) return;
            if (container != null && !container.isDisposed && container.parent == targetParent &&
                targetParent.GetChildIndex(button) < targetParent.GetChildIndex(container))
                targetParent.SetChildIndex(button, targetParent.numChildren - 1);
        }
        private void EnsureAvatar(Avatar avatar, GComponent targetParent, GComponent container)
        {
            if (avatar.Button != null && !avatar.Button.isDisposed && avatar.Button.parent == targetParent)
            {
                EnsureButtonAboveContainer(targetParent, container, avatar.Button);
                return;
            }
            if (avatar.Button != null && !avatar.Button.isDisposed) avatar.Button.Dispose();
            avatar.Button = new GComponent { name = "__xingshu_avatar_praise", sortingOrder = 0 };
            avatar.Image = new GImage { touchable = false };
            avatar.Mask = new GGraph { touchable = false };
            avatar.Button.AddChild(avatar.Image); avatar.Button.AddChild(avatar.Mask);
            avatar.Button.mask = avatar.Mask.displayObject;
            targetParent.AddChild(avatar.Button);
            EnsureButtonAboveContainer(targetParent, container, avatar.Button);
            XingShuOverlayInput.Protect(avatar.Button);
            avatar.Button.onTouchBegin.Add((EventCallback1)(context =>
            {
                context.StopPropagation();
                context.CaptureTouch();
                avatar.PressedPlayer = avatar.Player; avatar.PressedSession = _selection.Session;
            }));
            avatar.Button.onClick.Add((EventCallback1)(context =>
            {
                context.StopPropagation();
                string pressedSession = avatar.PressedSession; long pressedPlayer = avatar.PressedPlayer;
                avatar.PressedPlayer = 0; avatar.PressedSession = null;
                if (!IsEnabled || !SyncContext() || pressedPlayer != avatar.Player || pressedSession != _selection.Session) return;
                BattlePlayerData data = avatar.Head != null ? avatar.Head.PlayerData : null;
                if (data == null || data.player == null || data.player.Id != avatar.Player || !Represented(data)) return;
                if (avatar.Player == _self)
                {
                    _selfEasterEggLit = !_selfEasterEggLit;
                    ShowHint(_selfEasterEggLit ? "宝宝你是自恋吗，爱你哦" : "已收起自恋小彩蛋");
                    UpdateAvatars();
                    return;
                }
                if (!Eligible(data))
                { ShowHint("人机不参与原版点赞；真人玩家可在这里选择或取消"); return; }
                if (_selection.IsSubmitted(avatar.Player)) { ShowHint("点赞已提交，原版不支持撤回"); return; }
                if (!_selection.Toggle(avatar.Player, true)) return;
                UpdateAvatars(); UpdateStatus();
            }));
        }
        private void UpdateAvatars()
        {
            var logic = SimpleSingletonProvider<GameLogicManager>.hasInstance ? SimpleSingletonProvider<GameLogicManager>.inst : null;
            var fight = logic != null && logic.fight != null ? logic.fight.battleFightData : null;
            var uiMgr = SimpleSingletonProvider<UIManager>.hasInstance ? SimpleSingletonProvider<UIManager>.inst : null;
            bool combat = (fight != null && !fight.isEnd) || ShownNativeWindows.Find<FightWindow>(uiMgr) != null;
            if (XingShuUICenter.IsOpen || StreamPrivacyHands.Enabled || combat) { HideAvatars(); return; }
            var ui = _battle != null && _battle.battleInfo != null ? _battle.battleInfo.UIObject : null;
            var battlePlayer = ui != null ? ui.com_BattlePlayer : null;
            var container = battlePlayer != null ? battlePlayer.com_Container : null;
            if (container == null || !Visible(container)) { HideAvatars(); return; }
            // Mount on com_BattlePlayer (above com_Container) so upper-border buttons between rows are never clipped by individual heads.
            GComponent targetParent = battlePlayer != null && container.parent == battlePlayer ? (GComponent)battlePlayer : container;
            bool modalOpen = HandCardVisibility.BlockingWindow != null;
            if (_self <= 0) { HideAvatars(); return; }
            Rect safe = XingShuMobileUIHelper.SafeScreenRect;
            float safeTop = Screen.height - safe.yMax;
            _avatars[0].Head = container.com_Player_1; _avatars[1].Head = container.com_Player_2;
            _avatars[2].Head = container.com_Player_3; _avatars[3].Head = container.com_Player_4;
            foreach (Avatar avatar in _avatars)
            {
                BattlePlayerData data = avatar.Head != null ? avatar.Head.PlayerData : null;
                bool isSelf = data != null && data.player != null && data.player.Id == _self;
                if (avatar.Head == null || avatar.Head.parent != container || !Visible(avatar.Head) || !Represented(data) || data.player.Id <= 0)
                {
                    avatar.Player = 0;
                    if (avatar.Button != null && !avatar.Button.isDisposed)
                    {
                        avatar.Button.visible = false;
                        avatar.Button.touchable = false;
                    }
                    continue;
                }
                avatar.Player = data.player.Id;
                bool eligible = Eligible(data);
                if (eligible) _selection.EnsureDefaultSelected(avatar.Player, true, DefaultAllSelected);
                EnsureAvatar(avatar, targetParent, container);
                GObject portrait = avatar.Head.btn_Head;
                if (portrait == null || portrait.width <= 0 || portrait.height <= 0) portrait = avatar.Head.com_player;
                if (portrait == null || portrait.width <= 0 || portrait.height <= 0) { avatar.Button.visible = false; continue; }
                PraiseIconLayout layout = PraiseIconLayout.Calculate(portrait.width, portrait.height, XingShuMobileUIHelper.IsMobile, PraiseScale, OffsetX, OffsetY);
                if (layout.Size <= 0) { avatar.Button.visible = false; continue; }
                Vector2 origin = targetParent.GlobalToLocal(portrait.LocalToGlobal(Vector2.zero));
                Vector2 extent = targetParent.GlobalToLocal(portrait.LocalToGlobal(new Vector2(portrait.width, portrait.height)));
                float scaleX = (extent.x - origin.x) / portrait.width;
                float scaleY = (extent.y - origin.y) / portrait.height;
                if (float.IsNaN(scaleX) || float.IsNaN(scaleY) || scaleX <= .01f || scaleY <= .01f) { avatar.Button.visible = false; continue; }
                Vector2 globalPos = portrait.LocalToGlobal(new Vector2(layout.X, layout.Y));
                float maxX = Mathf.Max(safe.xMin + 2f, safe.xMax - layout.Size * scaleX - 2f);
                float maxY = Mathf.Max(safeTop + 2f, safeTop + safe.height - layout.Size * scaleY - 2f);
                globalPos.x = Mathf.Clamp(globalPos.x, safe.xMin + 2f, maxX);
                globalPos.y = Mathf.Clamp(globalPos.y, safeTop + 2f, maxY);
                Vector2 localPos = targetParent.GlobalToLocal(globalPos);
                float size = layout.Size;
                avatar.Button.SetSize(size, size);
                avatar.Button.SetScale(scaleX, scaleY);
                avatar.Button.SetXY(localPos.x, localPos.y);
                avatar.Image.SetSize(size, size);
                if (Mathf.Abs(avatar.Mask.width - size) > .1f) avatar.Mask.DrawEllipse(size, size, Color.white);
                bool lit = isSelf ? _selfEasterEggLit : _selection.IsSelected(avatar.Player);
                avatar.Image.texture = NativePraiseAssets.Get(lit);
                avatar.Button.visible = true;
                avatar.Button.touchable = !modalOpen;
                avatar.Button.alpha = (eligible || isSelf) ? 1f : .8f;
                avatar.Button.tooltips = isSelf ? "点击给自己点赞（小彩蛋，不会在结算时生效）" :
                    !eligible ? "人机不参与点赞" :
                    _selection.IsConfirmed(avatar.Player) ? "原版结算已确认点赞，无法撤回" :
                    _selection.IsSubmitted(avatar.Player) ? "已尝试提交点赞，请以原版结算为准" :
                    _selection.IsSelected(avatar.Player) ? "已开启点赞：结算时点赞，点击彩色图标可取消" : "已取消点赞：点击蓝色图标可重新开启";
            }
        }
        private void ShowHint(string text)
        {
            _status = text;
            if (!SimpleSingletonProvider<UIManager>.hasInstance) return;
            UIManager manager = SimpleSingletonProvider<UIManager>.inst;
            if (manager != null && manager.systemTips != null) manager.systemTips.ShowTips(text);
        }
        private void HideAvatars()
        { foreach (Avatar avatar in _avatars) if (avatar.Button != null && !avatar.Button.isDisposed) avatar.Button.visible = false; }
        private void ClearAvatars()
        {
            foreach (Avatar avatar in _avatars)
            {
                if (avatar.Button != null && !avatar.Button.isDisposed) avatar.Button.Dispose();
                avatar.Button = null; avatar.Image = null; avatar.Mask = null; avatar.Player = avatar.PressedPlayer = 0; avatar.PressedSession = null;
            }
        }
        private static BattlePlayerData PlayerData(UIBattleSettlement_Com_Item item)
        {
            try { var value = SettlementField != null ? SettlementField.GetValue(item) as PlayerSettlement : null; return value != null ? value.PlayerData : null; }
            catch { return null; }
        }
        private static bool Ready(UIBattleSettlement_Com_Item item)
        {
            if (!Visible(item) || !Visible(item.btn_Praise)) return false;
            var button = item.btn_Praise;
            if (button.grayed || (button.isFinish != null && button.isFinish.selectedIndex == 1) || button.onClick.isEmpty || button.onClick.isDispatching) return false;
            for (GObject node = button; node != null; node = node.parent) if (!node.touchable) return false;
            if (BridgeField != null && LockField != null)
            {
                object bridge = BridgeField.GetValue(button.onClick);
                if (bridge != null && (bool)LockField.GetValue(bridge)) return false;
            }
            return true;
        }
        private void FindSettlement(GComponent node, int depth, ref int budget)
        {
            if (node == null || node.isDisposed || depth > 16 || budget-- <= 0) return;
            for (int i = 0; i < node.numChildren && budget > 0; i++)
            {
                GObject child = node.GetChildAt(i);
                if (child is UIBattleSettlement_Com_Item item) _items.Add(item);
                else if (child is GComponent component) FindSettlement(component, depth + 1, ref budget);
            }
        }
        private void SubmitSelected()
        {
            _items.Clear(); int budget = 4000; FindSettlement(GRoot.inst, 0, ref budget);
            foreach (UIBattleSettlement_Com_Item item in _items)
            {
                BattlePlayerData data = PlayerData(item);
                if (!Eligible(data)) continue;
                long id = data.player.Id;
                if (item.btn_Praise != null && item.btn_Praise.isFinish != null && item.btn_Praise.isFinish.selectedIndex == 1)
                { _selection.ObserveNative(id, true); continue; }
                if (!_seenAt.ContainsKey(item)) _seenAt[item] = Time.unscaledTime;
                if (Time.unscaledTime < _sendAt || Time.unscaledTime - _seenAt[item] < .15f || !Ready(item)) continue;
                if (!_selection.Reserve(id, Eligible(data))) continue;
                _sendAt = Time.unscaledTime + .2f;
                item.btn_Praise.onClick.Call();
            }
        }
        private void EnsureDefaultSelections()
        {
            if (!DefaultAllSelected || _battle == null || _battle.PlayerDatas == null) return;
            foreach (BattlePlayerData data in _battle.PlayerDatas)
            {
                if (Eligible(data)) _selection.EnsureDefaultSelected(data.player.Id, true, true);
            }
        }
        private void UpdateStatus()
        {
            bool defaultAll = DefaultAllSelected;
            _status = _selection.SubmittedCount > 0 ? "本局已尝试提交 " + _selection.SubmittedCount + " 个点赞，以原版结算为准" :
                _selection.PendingCount > 0
                    ? (defaultAll
                        ? "已默认开启本局全队点赞，点击彩色图标可取消对应玩家（当前 " + _selection.PendingCount + " 位）"
                        : "已选择 " + _selection.PendingCount + " 位玩家，结算前可再次点击取消")
                    : (defaultAll
                        ? "已取消本局全部默认点赞，点击蓝色图标可重新开启"
                        : "点击头像旁蓝色图标选择点赞，本局默认不点赞");
        }
        private void Update()
        {
            if (Time.unscaledTime < _scanAt) return;
            _scanAt = Time.unscaledTime + .1f;
            try
            {
                if (!IsEnabled) { _selection.ClearChoices(); HideAvatars(); _status = "头像点赞已关闭"; return; }
                if (!SyncContext()) { _self = 0; ClearAvatars(); _selection.Begin(null, 0); _seenAt.Clear(); _status = "等待真人对局（观战和回放不点赞）"; return; }
                EnsureDefaultSelections();
                UpdateAvatars();
                if (_settlement) SubmitSelected();
                UpdateStatus();
            }
            catch (Exception ex) { HideAvatars(); _status = "头像点赞暂不可用：" + ex.GetType().Name; }
        }
    }
}
