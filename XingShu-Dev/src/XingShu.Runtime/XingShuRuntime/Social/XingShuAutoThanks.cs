using System;
using System.Reflection;
using XingShuRuntime.Core;
using Core;
using Core.Scene;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;

namespace XingShuRuntime.Social
{
    /// <summary>
    /// XingShu 自动感谢 (AutoThanks) 增强组件 (Phase 2D-5B)
    /// 100% 复用游戏原生 UI.ExpressionWindow._btn_QuickReply 点击与网络协议
    /// 严格遵循多层守卫：场景/非观战/非回放/非Bot/单次提示单次触发/0.15s动画等待
    /// 绝无外部网络请求，绝无构造非法 ChatID
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XingShuAutoThanks : MonoBehaviour
    {
        private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo ReplyField = typeof(ExpressionWindow).GetField("_btn_QuickReply", Fields);
        private static readonly FieldInfo PromptField = typeof(ExpressionWindow).GetField("quickReplyCts", Fields);
        private static readonly FieldInfo AnimationField = typeof(ExpressionWindow).GetField("_showQuickReply", Fields);
        private static readonly FieldInfo BridgeField = typeof(EventListener).GetField("_bridge", Fields);
        private static readonly FieldInfo LockField = typeof(EventBridge).GetField("_isLocking", Fields);

        private static XingShuAutoThanks _instance;

        private bool _attempted = true;
        private float _scanAt;
        private float _seenAt;
        private string _session = "";
        private ExpressionWindow _owner;
        private CtsInfo _prompt;
        private int _triggerCount = 0;
        private string _status = "等待对局中快捷回复触发";

        public static XingShuAutoThanks Instance => _instance;

        public static bool IsEnabled
        {
            get => XingShuConfigService.GetBool(XingShuConfigKeys.AutoThanksEnabled, XingShuConfigKeys.AutoThanksEnabledDefault);
            set => XingShuConfigService.SetBool(XingShuConfigKeys.AutoThanksEnabled, value);
        }

        public static int TriggerCount => _instance != null ? _instance._triggerCount : 0;
        public static string StatusText => _instance != null ? _instance._status : "未初始化";

        public static void Install(GameObject host)
        {
            if (host != null && host.GetComponent<XingShuAutoThanks>() == null)
            {
                host.AddComponent<XingShuAutoThanks>();
                Debug.Log("[XingShu Social] 自动感谢 (XingShuAutoThanks) 已成功初始化");
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

        private bool SyncContext()
        {
            if (!SimpleSingletonProvider<GameLogicManager>.hasInstance ||
                !SimpleSingletonProvider<SceneManager>.hasInstance ||
                !SimpleSingletonProvider<UIManager>.hasInstance)
            {
                return false;
            }

            GameLogicManager logic = SimpleSingletonProvider<GameLogicManager>.inst;
            if (SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle ||
                logic.watch == null || logic.watch.PlayerIsWatcher() ||
                (logic.replay != null && logic.replay.Session != null && logic.replay.Session.IsReplay) ||
                logic.room == null || logic.room.roomController == null ||
                logic.room.roomController.roomStateType != RoomStateType.RUNNING ||
                logic.battle == null || !logic.battle.clientFinishReady || logic.account == null)
            {
                return false;
            }

            RoomInfo curRoomInfo = logic.room.curRoomInfo;
            BattlePlayerData selfPlayer = logic.battle.GetSelfPlayerData();
            if (curRoomInfo == null || curRoomInfo.info == null || curRoomInfo.info.Id <= 0 ||
                selfPlayer == null || selfPlayer.player == null || selfPlayer.player.Id <= 0 ||
                selfPlayer.player.IsBot || !logic.account.IsSelf(selfPlayer.player.Id))
            {
                return false;
            }

            ExpressionWindow expression = SimpleSingletonProvider<UIManager>.inst.expression;
            if (expression == null || !expression.isShowing || expression.contentPane == null || !Visible(expression.contentPane))
            {
                return false;
            }

            string currentSession = curRoomInfo.info.Id + "/" + curRoomInfo.info.StartTime + "/" + selfPlayer.player.Id;
            if (expression != _owner || currentSession != _session)
            {
                _owner = expression;
                _session = currentSession;
                // The first prompt can be what made the expression window visible.
                // Treat it as pending instead of silently consuming it on reattachment.
                _prompt = null;
                _attempted = false;
            }

            return true;
        }

        private static bool Allowed(UIExpression_Button_QuickReply button)
        {
            if (button == null || string.IsNullOrEmpty(button.title))
            {
                return false;
            }

            // 严格匹配官方 3 个预设感谢 ChatID
            string t = button.title;
            return t == 50000.GetLocal(UIStringType.Chat) ||
                   t == 50001.GetLocal(UIStringType.Chat) ||
                   t == 50002.GetLocal(UIStringType.Chat);
        }

        private static bool Ready(UIExpression_Button_QuickReply button, Transition animation)
        {
            if (button == null || !Visible(button) || button.grayed ||
                button.onClick.isEmpty || button.onClick.isDispatching ||
                (animation != null && animation.playing))
            {
                return false;
            }

            // Parent touchability controls pointer hit testing, not programmatic
            // EventListener.Call. Only the native listener's retain lock matters.
            object bridge = BridgeField?.GetValue(button.onClick);
            if (bridge != null)
            {
                return LockField == null || !(bool)LockField.GetValue(bridge);
            }
            return true;
        }

        private static bool Visible(GObject obj)
        {
            for (GObject cur = obj; cur != null; cur = cur.parent)
            {
                if (!cur.visible)
                {
                    return false;
                }
            }
            return true;
        }

        private static T Read<T>(FieldInfo field, object target) where T : class
        {
            if (field == null || target == null) return null;
            try
            {
                return field.GetValue(target) as T;
            }
            catch
            {
                return null;
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
                _scanAt = Time.unscaledTime + 0.05f;

                if (!IsEnabled)
                {
                    _status = "功能已在设置中关闭";
                    return;
                }

                if (!SyncContext())
                {
                    _owner = null;
                    _prompt = null;
                    _attempted = true;
                    _status = "等待对局中（观战/回放/非战斗不触发）";
                    return;
                }

                CtsInfo ctsInfo = Read<CtsInfo>(PromptField, _owner);
                if (ctsInfo != _prompt)
                {
                    _prompt = ctsInfo;
                    _attempted = false;
                    _seenAt = Time.unscaledTime;
                }

                // 等待 0.15s 展开动画稳定后再执行原生点击
                if (!_attempted && ctsInfo != null && !ctsInfo.IsCancellationRequested && (Time.unscaledTime - _seenAt >= 0.15f))
                {
                    UIExpression_Button_QuickReply replyBtn = Read<UIExpression_Button_QuickReply>(ReplyField, _owner);
                    Transition anim = Read<Transition>(AnimationField, _owner);

                    if (Allowed(replyBtn) && Ready(replyBtn, anim))
                    {
                        _attempted = true;
                        _triggerCount++;
                        replyBtn.onClick.Call();
                        _status = "已触发原生感谢回复 (" + replyBtn.title + ")";
                    }
                }
            }
            catch (Exception ex)
            {
                _attempted = true;
                _status = "自动感谢异常: " + ex.GetType().Name;
            }
        }
    }
}
