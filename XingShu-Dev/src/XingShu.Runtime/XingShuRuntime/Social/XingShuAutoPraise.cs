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
        private bool _settlement;
        private float _scanAt, _sendAt;
        private string _status = "局内点击头像旁的蓝色图标选择点赞";

        public static XingShuAutoPraise Instance { get { return _instance; } }
        public static bool IsEnabled
        {
            get { return XingShuConfigService.GetBool(XingShuConfigKeys.AutoPraiseEnabled, XingShuConfigKeys.AutoPraiseEnabledDefault); }
            set { XingShuConfigService.SetBool(XingShuConfigKeys.AutoPraiseEnabled, value); }
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
            { ClearAvatars(); _seenAt.Clear(); _sendAt = 0; }
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
        private void EnsureAvatar(Avatar avatar)
        {
            if (avatar.Button != null && !avatar.Button.isDisposed && avatar.Button.parent == avatar.Head) return;
            if (avatar.Button != null && !avatar.Button.isDisposed) avatar.Button.Dispose();
            avatar.Button = new GComponent { name = "__xingshu_avatar_praise", sortingOrder = 3 };
            avatar.Image = new GImage { touchable = false };
            avatar.Mask = new GGraph { touchable = false };
            avatar.Button.AddChild(avatar.Image); avatar.Button.AddChild(avatar.Mask);
            avatar.Button.mask = avatar.Mask.displayObject;
            avatar.Head.AddChild(avatar.Button);
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
                if (!Eligible(data))
                { ShowHint(avatar.Player == _self ? "宝宝你是自恋吗，爱你哦" : "人机不参与原版点赞；真人玩家可在这里选择或取消"); return; }
                if (_selection.IsSubmitted(avatar.Player)) { ShowHint("点赞已提交，原版不支持撤回"); return; }
                if (!_selection.Toggle(avatar.Player, true)) return;
                UpdateAvatars(); UpdateStatus();
            }));
        }
        private void UpdateAvatars()
        {
            if (XingShuUICenter.IsOpen || HandCardVisibility.ShouldHide) { HideAvatars(); return; }
            var ui = _battle != null && _battle.battleInfo != null ? _battle.battleInfo.UIObject : null;
            var container = ui != null && ui.com_BattlePlayer != null ? ui.com_BattlePlayer.com_Container : null;
            if (container == null || !Visible(container)) { HideAvatars(); return; }
            _avatars[0].Head = container.com_Player_1; _avatars[1].Head = container.com_Player_2;
            _avatars[2].Head = container.com_Player_3; _avatars[3].Head = container.com_Player_4;
            foreach (Avatar avatar in _avatars)
            {
                BattlePlayerData data = avatar.Head != null ? avatar.Head.PlayerData : null;
                if (!Visible(avatar.Head) || !Represented(data))
                { if (avatar.Button != null && !avatar.Button.isDisposed) avatar.Button.visible = false; continue; }
                avatar.Player = data.player.Id;
                EnsureAvatar(avatar);
                GObject portrait = avatar.Head.btn_Head;
                if (portrait == null || portrait.width <= 0 || portrait.height <= 0) portrait = avatar.Head.com_player;
                if (portrait == null || portrait.width <= 0 || portrait.height <= 0) { avatar.Button.visible = false; continue; }
                float portraitWidth = Mathf.Min(portrait.width, portrait.height);
                float size = Mathf.Clamp(portraitWidth * .32f, 42f, 52f);
                avatar.Button.SetSize(size, size);
                avatar.Button.SetXY(portrait.x + Mathf.Max(0, portraitWidth - size * 1.25f - 6), portrait.y + 6);
                avatar.Image.SetSize(size, size);
                if (Mathf.Abs(avatar.Mask.width - size) > .1f) avatar.Mask.DrawEllipse(size, size, Color.white);
                avatar.Image.texture = NativePraiseAssets.Get(_selection.IsSelected(avatar.Player));
                bool eligible = Eligible(data);
                avatar.Button.visible = true;
                avatar.Button.touchable = true;
                avatar.Button.alpha = eligible ? 1f : .8f;
                avatar.Button.tooltips = !eligible ? (avatar.Player == _self ? "宝宝你是自恋吗，爱你哦" : "人机不参与点赞") :
                    _selection.IsConfirmed(avatar.Player) ? "原版结算已确认点赞，无法撤回" :
                    _selection.IsSubmitted(avatar.Player) ? "已尝试提交点赞，请以原版结算为准" :
                    _selection.IsSelected(avatar.Player) ? "已选择：结算时点赞，再次点击可取消" : "点击选择：本局结束时点赞";
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
        private void UpdateStatus()
        {
            _status = _selection.SubmittedCount > 0 ? "本局已尝试提交 " + _selection.SubmittedCount + " 个点赞，以原版结算为准" :
                _selection.PendingCount > 0 ? "已选择 " + _selection.PendingCount + " 位玩家，结算前可再次点击取消" : "点击头像旁蓝色图标选择点赞，本局默认不点赞";
        }
        private void Update()
        {
            if (Time.unscaledTime < _scanAt) return;
            _scanAt = Time.unscaledTime + .1f;
            try
            {
                if (!IsEnabled) { _selection.ClearChoices(); HideAvatars(); _status = "头像点赞已关闭"; return; }
                if (!SyncContext()) { ClearAvatars(); _selection.Begin(null, 0); _seenAt.Clear(); _status = "等待真人对局（观战和回放不点赞）"; return; }
                UpdateAvatars();
                if (_settlement) SubmitSelected();
                UpdateStatus();
            }
            catch (Exception ex) { HideAvatars(); _status = "头像点赞暂不可用：" + ex.GetType().Name; }
        }
    }
}
