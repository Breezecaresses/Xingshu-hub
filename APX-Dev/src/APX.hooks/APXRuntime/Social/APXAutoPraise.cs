using System;
using System.Collections.Generic;
using System.Reflection;
using APXRuntime.Core;
using Core.Scene;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;

namespace APXRuntime.Social
{
    /// <summary>
    /// APX 自动点赞 (AutoPraise) 增强组件 (Phase 2D-5B)
    /// 在局末结算界面扫描真实队友结算卡片，模拟点击原生 btn_Praise
    /// 严格排除自身、排除 Bot、每局每人仅赞一次、0.2s 频控防刷
    /// 100% 走原生结算上报，绝不构造独立网络包
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class APXAutoPraise : MonoBehaviour
    {
        private static readonly FieldInfo SettlementField = typeof(UIBattleSettlement_Com_Item).GetField("_PlayerSettlement", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo BridgeField = typeof(EventListener).GetField("_bridge", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo LockField = typeof(EventBridge).GetField("_isLocking", BindingFlags.Instance | BindingFlags.NonPublic);

        private static APXAutoPraise _instance;

        private readonly HashSet<long> _praisedPlayerIds = new HashSet<long>();
        private readonly List<UIBattleSettlement_Com_Item> _settlementItems = new List<UIBattleSettlement_Com_Item>();
        private readonly Dictionary<UIBattleSettlement_Com_Item, float> _itemSeenAt = new Dictionary<UIBattleSettlement_Com_Item, float>();

        private float _scanAt;
        private float _sendAt;
        private string _session = "";
        private int _praiseCount = 0;
        private string _status = "等待结算界面";

        public static APXAutoPraise Instance => _instance;

        public static bool IsEnabled
        {
            get => APXConfigService.GetBool(APXConfigKeys.AutoPraiseEnabled, APXConfigKeys.AutoPraiseEnabledDefault);
            set => APXConfigService.SetBool(APXConfigKeys.AutoPraiseEnabled, value);
        }

        public static int PraiseCount => _instance != null ? _instance._praiseCount : 0;
        public static string StatusText => _instance != null ? _instance._status : "未初始化";

        public static void Install(GameObject host)
        {
            if (host != null && host.GetComponent<APXAutoPraise>() == null)
            {
                host.AddComponent<APXAutoPraise>();
                Debug.Log("[APX Social] 自动点赞 (APXAutoPraise) 已成功初始化");
            }
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private bool SyncContext(out long selfId)
        {
            selfId = 0L;
            if (!SimpleSingletonProvider<GameLogicManager>.hasInstance ||
                !SimpleSingletonProvider<SceneManager>.hasInstance)
            {
                return false;
            }

            if (SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle)
            {
                return false;
            }

            GameLogicManager logic = SimpleSingletonProvider<GameLogicManager>.inst;
            if (logic.watch != null && logic.watch.PlayerIsWatcher())
            {
                return false;
            }

            if (logic.replay != null && logic.replay.Session != null && logic.replay.Session.IsReplay)
            {
                return false;
            }

            RoomInfo roomInfo = logic.room != null ? logic.room.curRoomInfo : null;
            if (roomInfo == null || roomInfo.info == null || roomInfo.info.Id <= 0 ||
                logic.battle == null || logic.room.roomController == null)
            {
                return false;
            }

            RoomStateType roomState = logic.room.roomController.roomStateType;
            if (roomState != RoomStateType.SETTLEMENT)
            {
                return false;
            }

            BattlePlayerData selfData = logic.battle.GetSelfPlayerData();
            if (selfData != null && selfData.player != null)
            {
                selfId = selfData.player.Id;
            }

            string currentSession = roomInfo.info.Id + "/" + roomInfo.info.StartTime;
            if (currentSession != _session)
            {
                _session = currentSession;
                _praisedPlayerIds.Clear();
                _itemSeenAt.Clear();
                _praiseCount = 0;
            }

            return true;
        }

        private static BattlePlayerData GetPlayerData(UIBattleSettlement_Com_Item item)
        {
            if (SettlementField == null || item == null) return null;
            try
            {
                var settlement = SettlementField.GetValue(item) as PlayerSettlement;
                return settlement != null ? settlement.PlayerData : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsItemReady(UIBattleSettlement_Com_Item item)
        {
            if (item == null || item.isDisposed || !item.onStage)
            {
                return false;
            }

            if (item.btn_Praise == null || item.btn_Praise.isDisposed || !item.btn_Praise.onStage)
            {
                return false;
            }

            UIBattleSettlement_Button_OperatePlayer praiseBtn = item.btn_Praise;
            if (praiseBtn.grayed ||
                (praiseBtn.isFinish != null && praiseBtn.isFinish.selectedIndex == 1) ||
                praiseBtn.onClick.isEmpty || praiseBtn.onClick.isDispatching)
            {
                return false;
            }

            for (GObject obj = praiseBtn; obj != null; obj = obj.parent)
            {
                if (obj.isDisposed || !obj.visible || !obj.touchable || obj.alpha <= 0.01f)
                {
                    return false;
                }
            }

            if (BridgeField != null && LockField != null)
            {
                object bridge = BridgeField.GetValue(praiseBtn.onClick);
                if (bridge != null)
                {
                    return !(bool)LockField.GetValue(bridge);
                }
            }

            return true;
        }

        private void WalkGRoot(GComponent comp, int depth)
        {
            if (comp == null || comp.isDisposed || depth > 10) return;
            int count = comp.numChildren;
            for (int i = 0; i < count; i++)
            {
                GObject child = comp.GetChildAt(i);
                if (child is UIBattleSettlement_Com_Item item)
                {
                    _settlementItems.Add(item);
                }
                else if (child is GComponent subComp)
                {
                    WalkGRoot(subComp, depth + 1);
                }
            }
        }

        private void Update()
        {
            try
            {
                if (Time.unscaledTime < _scanAt)
                {
                    return;
                }
                _scanAt = Time.unscaledTime + 0.1f;

                if (!IsEnabled)
                {
                    _status = "自动点赞已在设置中关闭";
                    return;
                }

                if (!SyncContext(out long selfId))
                {
                    _itemSeenAt.Clear();
                    _status = "等待结算界面（对战/结算中触发）";
                    return;
                }

                _settlementItems.Clear();
                if (GRoot.inst != null)
                {
                    WalkGRoot(GRoot.inst, 0);
                }

                foreach (UIBattleSettlement_Com_Item item in _settlementItems)
                {
                    if (item == null || item.isDisposed) continue;

                    if (!_itemSeenAt.ContainsKey(item))
                    {
                        _itemSeenAt[item] = Time.unscaledTime;
                    }

                    // 出现后等待 0.15s 动画稳定，并且满足 0.2s 频控
                    if (Time.unscaledTime < _sendAt || Time.unscaledTime - _itemSeenAt[item] < 0.15f)
                    {
                        continue;
                    }

                    BattlePlayerData playerData = GetPlayerData(item);
                    if (playerData == null || playerData.player == null) continue;

                    long targetId = playerData.player.Id;
                    bool isBot = playerData.player.IsBot;

                    // 严格过滤：非自己、非 Bot、当前局尚未点赞
                    if (targetId > 0 && targetId != selfId && !isBot && !_praisedPlayerIds.Contains(targetId))
                    {
                        if (IsItemReady(item))
                        {
                            _sendAt = Time.unscaledTime + 0.2f;
                            _praisedPlayerIds.Add(targetId);
                            _praiseCount++;
                            item.btn_Praise.onClick.Call();
                            _status = "已向玩家 [Slot " + playerData.player.Slot + "] 触发原生点赞";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _status = "自动点赞异常: " + ex.GetType().Name;
            }
        }
    }
}
