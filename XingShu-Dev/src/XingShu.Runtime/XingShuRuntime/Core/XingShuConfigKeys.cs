using UnityEngine;

namespace XingShuRuntime.Core;

/// <summary>
/// XingShu 配置键名、默认值与边界集中常量表 (Phase 1B.3D Wave 3)
/// 
/// 契约说明:
/// 1. 集中定义键名与单一真相源默认值常量，成对出现；数值型配置提供明确上下界 (Min/Max)。
/// 2. Wave 3 包含既有配置以及 HandCardDisplayManager 与 AssistBallGUI 迁移配置项。
/// 3. HandCard 与 AssistBall 解耦出牌按钮尺寸 (XINGSHU_HAND_CARD_BTN_SIZE)，确立 HandCard 为单一所有者。
/// </summary>
internal static class XingShuConfigKeys
{
	// Wave 1: 自动重连与对局统计
	internal const string AutoReconnectEnabled = "XINGSHU_CONFIG_AUTO_RECONNECT_ENABLED";

	internal const bool AutoReconnectEnabledDefault = true;

	internal const string MatchObserveEnabled = "XINGSHU_MATCH_OBSERVE_ENABLED";

	internal const bool MatchObserveEnabledDefault = true;

	// Wave 2: 游戏全局变速控制器 (GameSpeedControllerGUI)
	internal const string SpeedEnabled = "XINGSHU_CONFIG_SPEED_ENABLED";

	internal const bool SpeedEnabledDefault = false;

	internal const string TimeScale = "XINGSHU_CONFIG_TIMESCALE";

	internal const float TimeScaleDefault = 1.0f;

	internal const float TimeScaleMin = 0.0f;

	internal const float TimeScaleMax = 10.0f;

	internal const string SpeedHotkey = "XINGSHU_CONFIG_SPEED_HOTKEY";

	internal const KeyCode SpeedHotkeyDefault = KeyCode.F7;

	// Wave 4: 双变速引擎与原生加速 (Phase 2D-2B2)
	internal const string GameSpeedEngineMode = "XINGSHU_GAME_SPEED_ENGINE_MODE";

	internal const int GameSpeedEngineModeDefault = 0;

	internal const string NativeSpeedEnabled = "XINGSHU_NATIVE_SPEED_ENABLED";

	internal const bool NativeSpeedEnabledDefault = false;

	internal const string NativeSpeedValue = "XINGSHU_NATIVE_SPEED_VALUE";

	internal const float NativeSpeedValueDefault = 2.0f;

	internal const float NativeSpeedValueMin = 0.25f;

	internal const float NativeSpeedValueMax = 10.0f;

	internal const string NativeSmartPauseEnabled = "XINGSHU_NATIVE_SMART_PAUSE_ENABLED";

	internal const bool NativeSmartPauseEnabledDefault = false;

	// Wave 2: 地图自由镜头 (MapCameraGUI)
	internal const string MapFreeCameraEnabled = "XINGSHU_MAP_FREE_CAMERA_ENABLED";

	internal const bool MapFreeCameraEnabledDefault = false;

	internal const string MapScrollSpeed = "XINGSHU_MAP_SCROLL_SPEED";

	internal const float MapScrollSpeedDefault = 0.14f;

	internal const float MapScrollSpeedMin = 0.02f;

	internal const float MapScrollSpeedMax = 0.5f;

	// Wave 2: 移动端/通用 UI 缩放 (XingShuMobileUIHelper)
	internal const string MobileUserScale = "XINGSHU_UI_USER_SCALE";

	// Phone feature-center typography is independent of map widgets and the ball.
	internal const string MobileTextScale = "XINGSHU_MOBILE_TEXT_SCALE";
	internal const float MobileTextScaleDefault = .80f;
	internal const float MobileTextScaleMin = .80f;
	internal const float MobileTextScaleMax = 1.35f;
	internal const string MobilePanelScale = "XINGSHU_MOBILE_PANEL_SCALE";
	internal const float MobilePanelScaleMin = .70f;
	internal const float MobilePanelScaleMax = 1.30f;
	internal const string MobilePanelX = "XINGSHU_MOBILE_PANEL_X";
	internal const string MobilePanelY = "XINGSHU_MOBILE_PANEL_Y";

	internal const float MobileUserScaleDefault = 1.0f;

	internal const float MobileUserScaleMin = 1.0f;

	internal const float MobileUserScaleMax = 10.0f;

	// Track C (Phase 2D-7D): 桌面现代功能中心安全显示比例
	internal const float FunctionCenterDesktopScaleMin = 1.00f;

	internal const float FunctionCenterDesktopScaleDefault = 1.00f;

	internal const float FunctionCenterDesktopScaleMax = 1.25f;

	// Wave 3: 手牌监视管理器 (HandCardDisplayManager)
	internal const string HandCardEnabled = "XINGSHU_HAND_CARD_ENABLED";

	internal const bool HandCardEnabledDefault = true;

	// Phase HS4-P1.6: 实时手牌悬浮窗进入观战自动开启偏好
	internal const string HandCardSpectatorAutoOpen = "XINGSHU_HAND_CARD_SPECTATOR_AUTO_OPEN";

	internal const bool HandCardSpectatorAutoOpenDefault = true;

	// Phase HS4-P1.6: 实时手牌悬浮窗整体 UI 缩放偏好
	internal const string HandCardUIScale = "XINGSHU_HAND_CARD_UI_SCALE";

	internal const float HandCardUIScaleDefault = 1.0f;

	internal const float HandCardUIScaleMin = 0.7f;

	internal const float HandCardUIScaleMax = 1.5f;

	internal const string HandCardScale = "XINGSHU_HAND_CARD_SCALE";

	internal const float HandCardScaleDesktopDefault = 1.0f;

	internal const float HandCardScaleMobileDefault = 1.35f;

	internal const float HandCardScaleMin = 0.6f;

	internal const float HandCardScaleMax = 3.0f;

	internal const string HandCardBtnSize = "XINGSHU_HAND_CARD_BTN_SIZE";

	internal const float HandCardBtnSizeDesktopDefault = 24.0f;

	internal const float HandCardBtnSizeMobileDefault = 38.0f;

	internal const float HandCardBtnSizeMin = 16.0f;

	internal const float HandCardBtnSizeMax = 200.0f;

	// Wave 3: 悬浮球设置与样式 (AssistBallGUI)
	internal const string AssistAutoHide = "XINGSHU_ASSIST_AUTOHIDE";

	internal const bool AssistAutoHideDefault = false;

	internal const string AssistSize = "XINGSHU_ASSIST_SIZE";

	internal const float AssistSizeDesktopDefault = 64.0f;

	internal const float AssistSizeMobileDefault = 64.0f;

	internal const float AssistSizeMobileMin = 48.0f;

	internal const string AssistMobileSizeV2Applied = "XINGSHU_ASSIST_MOBILE_SIZE_V2_APPLIED";

	internal const string AssistMobileCompactDefaultApplied = "XINGSHU_ASSIST_MOBILE_COMPACT_DEFAULT_APPLIED";

	internal const float AssistSizeMin = 36.0f;

	internal const float AssistSizeMax = 200.0f;

	internal const string AssistAlpha = "XINGSHU_ASSIST_ALPHA";

	internal const float AssistAlphaDefault = 0.9f;

	internal const float AssistAlphaMin = 0.2f;

	internal const float AssistAlphaMax = 1.0f;

	internal const string AssistX = "XINGSHU_ASSIST_X";

	internal const float AssistXDefault = -40.0f;

	internal const string AssistY = "XINGSHU_ASSIST_Y";

	internal const float AssistYDefault = -180.0f;

	internal const string AssistXImgui = "XINGSHU_ASSIST_X_IMGUI";

	internal const string AssistYImgui = "XINGSHU_ASSIST_Y_IMGUI";

	internal const string AssistHotkey = "XINGSHU_ASSIST_HOTKEY";

	internal const KeyCode AssistHotkeyDefault = KeyCode.BackQuote;

	internal const string AssistDesktopScale = "XINGSHU_ASSIST_DESKTOP_SCALE";

	internal const float AssistDesktopScaleDefault = 1.0f;

	internal const float AssistDesktopScaleMin = 1.0f;

	internal const float AssistDesktopScaleMax = 3.0f;

	// Wave 3: 对局增强 · 击飞概率 (MonsterKillProbability)
	internal const string MatchKillEnabled = "XINGSHU_MATCH_KILL_ENABLED_V1";

	internal const bool MatchKillEnabledDefault = true;

	internal const string MonsterTargetEnabled = "XINGSHU_MONSTER_TARGET_ENABLED_V1";

	internal const bool MonsterTargetEnabledDefault = true;

	internal const string MatchKillFightSize = "XINGSHU_MATCH_KILL_FIGHT_SIZE";

	internal const int MatchKillFightSizeDefault = 26;

	internal const int MatchKillFightSizeMin = 12;

	internal const int MatchKillFightSizeMax = 72;

	internal const string MatchKillFightX = "XINGSHU_MATCH_KILL_FIGHT_X";

	internal const float MatchKillFightXDefault = 50.0f;

	internal const float MatchKillFightXMin = -1000.0f;

	internal const float MatchKillFightXMax = 1000.0f;

	internal const string MatchKillFightY = "XINGSHU_MATCH_KILL_FIGHT_Y";

	internal const float MatchKillFightYDefault = -32.0f;

	internal const float MatchKillFightYMin = -1000.0f;

	internal const float MatchKillFightYMax = 1000.0f;

	// Visual V3: 击飞预测文字颜色模式与炫彩高亮 (Phase 2D-2B4H)
	internal const string MatchKillAutoColor = "XINGSHU_MATCH_KILL_AUTO_COLOR";

	internal const bool MatchKillAutoColorDefault = true;

	internal const string MatchKillRainbow100 = "XINGSHU_MATCH_KILL_RAINBOW_100";

	internal const bool MatchKillRainbow100Default = false;

	internal const string MatchKillProbabilityColor = "XINGSHU_MATCH_KILL_PROBABILITY_COLOR";

	internal const string MatchKillProbabilityColorDefault = "#FFD85A";

	internal const string MatchKillTargetColor = "XINGSHU_MATCH_KILL_TARGET_COLOR";

	internal const string MatchKillTargetColorDefault = "#F4F7FF";

	// Wave 4: 立绘替换 (StaticPortraitGUI)
	internal const string StaticPortraitEnableLoad = "XINGSHU_STATIC_ENABLE_LOAD";

	internal const bool StaticPortraitEnableLoadDefault = true;

	internal const string StaticPortraitEnableDump = "XINGSHU_STATIC_ENABLE_DUMP";

	internal const bool StaticPortraitEnableDumpDefault = true;

	internal const string StaticPortraitBasePath = "XINGSHU_STATIC_BASE_PATH";

	internal const string StaticPortraitBasePathDefault = "";

	internal const string StaticPortraitModImg = "XINGSHU_STATIC_MODIMG";

	internal const string StaticPortraitModImgDefault = "";

	internal const string StaticPortraitModOut = "XINGSHU_STATIC_MODOUT";

	internal const string StaticPortraitModOutDefault = "";

	internal const string StaticPortraitHotkey = "XINGSHU_STATIC_HOTKEY";

	internal const KeyCode StaticPortraitHotkeyDefault = KeyCode.F8;

	// Wave 4: 统计悬浮窗 (StatsWidgetView) - PC
	internal const string StatsWidgetPcBackground = "XINGSHU_STATS_WIDGET_PC_BACKGROUND";

	internal const string StatsWidgetPcText = "XINGSHU_STATS_WIDGET_PC_TEXT";

	internal const string StatsWidgetPcScale = "XINGSHU_STATS_WIDGET_PC_SCALE";

	internal const string StatsWidgetPcX = "XINGSHU_STATS_WIDGET_PC_X";

	internal const string StatsWidgetPcY = "XINGSHU_STATS_WIDGET_PC_Y";

	internal const string StatsWidgetPcLock = "XINGSHU_STATS_WIDGET_PC_LOCK";

	// Wave 4: 统计悬浮窗 (StatsWidgetView) - Android/Mobile
	internal const string StatsWidgetAndroidBackground = "XINGSHU_STATS_WIDGET_ANDROID_BACKGROUND";

	internal const string StatsWidgetAndroidText = "XINGSHU_STATS_WIDGET_ANDROID_TEXT";

	internal const string StatsWidgetAndroidScale = "XINGSHU_STATS_WIDGET_ANDROID_SCALE";

	internal const string StatsWidgetAndroidX = "XINGSHU_STATS_WIDGET_ANDROID_X";

	internal const string StatsWidgetAndroidY = "XINGSHU_STATS_WIDGET_ANDROID_Y";

	internal const string StatsWidgetAndroidLock = "XINGSHU_STATS_WIDGET_ANDROID_LOCK";

	// Wave 4: 统计悬浮窗默认值
	internal const float StatsWidgetBackgroundDefault = 0.66f;

	internal const float StatsWidgetTextAlphaDefault = 0.96f;

	internal const float StatsWidgetScaleDefault = 1.0f;
	internal const float StatsWidgetAndroidScaleDefault = .80f;

	internal const float StatsWidgetXDefault = 1.0f;

	internal const float StatsWidgetYDefault = 0.12f;

	internal const bool StatsWidgetLockDefault = false;

	// Phase 2D-7B-R2: 统计悬浮挂件进入对局自动显示偏好 (独立于底层数据采集开关)
	internal const string StatsWidgetAutoShow = "XINGSHU_STATS_WIDGET_AUTO_SHOW";

	internal const bool StatsWidgetAutoShowDefault = true;

	// Phase 2D-5B: 原生快捷社交增强 (Track A)
	internal const string AutoThanksEnabled = "XINGSHU_AUTO_THANKS_ENABLED";

	internal const bool AutoThanksEnabledDefault = true;

	internal const string AutoPraiseEnabled = "XINGSHU_AUTO_PRAISE_ENABLED";

	internal const bool AutoPraiseEnabledDefault = true;

	internal const string AutoPraiseDefault = "XINGSHU_AUTO_PRAISE_DEFAULT";

	internal const bool AutoPraiseDefaultDefault = true;
}
