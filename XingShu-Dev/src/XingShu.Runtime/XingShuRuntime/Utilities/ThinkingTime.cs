using XingShuRuntime.Core;
using GameLogic;
using Tools;
using UnityEngine;

namespace XingShuRuntime.Utilities
{
    public static class ThinkingTime
    {
        private const string Key = "XINGSHU_THINKING_TIME_X10";
        private static readonly DecisionProgressOwner Progress = new DecisionProgressOwner();
        public static bool Enabled => XingShuConfigService.GetBool(Key, true);

        public static void Install(GameObject host)
        {
            AssistBall.RegisterAction("xingshu.thinking_time", "十倍思考时间", Toggle, 39,
                "选人、自己的操作回合及筹码选择使用十倍本地计时；服务端强制截止仍有效", "clock", () => Enabled);
        }

        private static void Toggle()
        {
            XingShuConfigService.SetBool(Key, !Enabled);
            XingShuConfigService.Save();
        }

        public static int ScaleSeconds(int seconds)
        {
            return (int)ScaleSeconds((float)seconds);
        }

        public static float ScaleSeconds(float seconds)
        {
            if (seconds <= 0 || !UseRealClock()) return seconds;
            return seconds * 10f;
        }

        public static bool UseRealClock()
        {
            if (!Enabled) return false;
            var logic = SimpleSingletonProvider<GameLogicManager>.inst;
            return logic?.watch?.PlayerIsWatcher() != true && logic?.replay?.Session?.IsReplay != true;
        }

        public static void BeginProgress(object owner, float duration)
        {
            if (UseRealClock()) Progress.Begin(owner, duration); else Progress.Clear();
        }

        public static void ClearProgress() { Progress.Clear(); }

        public static bool AllowRemoteProgress()
        {
            if (!UseRealClock()) { Progress.Clear(); return true; }
            return !Progress.Active;
        }

        public static void UpdateProgress(global::UI.OperateTimeWindow window, int total, float elapsed,
            string head, bool warning, object owner)
        {
            if (window == null) return;
            if (UseRealClock() && !Progress.Update(owner, total, elapsed, out total, out elapsed)) return;
            window.UpdateOperationProgress(total, elapsed, head, warning);
        }
    }
}
