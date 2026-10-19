using APXRuntime.Core;
using GameLogic;
using Tools;
using UnityEngine;

namespace APXRuntime.Utilities
{
    public static class ThinkingTime
    {
        private const string Key = "XINGSHU_THINKING_TIME_X10";
        public static bool Enabled => APXConfigService.GetBool(Key, true);

        public static void Install(GameObject host)
        {
            AssistBall.RegisterAction("xingshu.thinking_time", "十倍思考时间", Toggle, 39,
                "选人、自己的操作回合及筹码选择使用十倍本地计时；服务端强制截止仍有效", "clock", () => Enabled);
        }

        private static void Toggle()
        {
            APXConfigService.SetBool(Key, !Enabled);
            APXConfigService.Save();
        }

        public static int ScaleSeconds(int seconds)
        {
            return (int)ScaleSeconds((float)seconds);
        }

        public static float ScaleSeconds(float seconds)
        {
            if (seconds <= 0 || !Enabled) return seconds;
            var logic = SimpleSingletonProvider<GameLogicManager>.inst;
            if (logic?.watch?.PlayerIsWatcher() == true || logic?.replay?.Session?.IsReplay == true) return seconds;
            return seconds * 10f;
        }
    }
}
