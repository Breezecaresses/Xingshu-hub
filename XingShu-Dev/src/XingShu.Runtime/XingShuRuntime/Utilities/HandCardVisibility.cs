using System;
using FairyGUI;
using Tools;
using UI;

namespace XingShuRuntime.Utilities
{
    // Inspect actual native window instances, not UIManager's optional lazy caches.
    internal static class HandCardVisibility
    {
        private static readonly string[] BlockingTypes = new string[]
        {
            "ChooseRoundCardWindow", "UpgradeWindow", "BattleSelectMonsterWindow", "BattlePreMonsterWindow",
            "LandShopWindow", "LandVendorWindow", "CardWindow", "SkillWindow", "LoseCardWindow", "FightWindow",
            "SettingWindow", "SettingInBattleWindow", "SinglePlayerSettingInBattleWindow", "BattlePlayerInfoWindow",
            "ExplainWindow", "LandEventWindow", "LandLotteryWindow", "LandGambleWindow", "LandBatteryWindow",
            "LandHospitalWindow", "LandDivinationWindow", "LandFillingStationWindow", "LandRollGoldWindow",
            "LandPursuitWindow", "LandPrayWindow", "LandRelicWindow", "RelicWindow", "DisplayCardWindow", "ATMWindow"
        };
        internal static bool ShouldHide => StreamPrivacyHands.Enabled || BlockingWindow != null;
        internal static string BlockingWindow
        {
            get
            {
                try
                {
                    if (!SimpleSingletonProvider<UIManager>.hasInstance) return null;
                    var root = GRoot.inst;
                    for (int i = root.numChildren - 1; i >= 0; i--)
                    {
                        var window = root.GetChildAt(i) as Window;
                        if (window != null && !window.isDisposed && window.parent != null && window.onStage && window.visible && Array.IndexOf(BlockingTypes, window.GetType().Name) >= 0)
                            return window.GetType().Name;
                    }
                    return null;
                }
                catch { return null; }
            }
        }
    }
}
