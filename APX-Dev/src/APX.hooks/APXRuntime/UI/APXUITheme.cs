using System;
using UnityEngine;

namespace APXRuntime.UI
{
    /// <summary>
    /// APX 原生风格视觉主题与设计规范 Tokens (Phase 2B-R4 生产整合版)
    /// 严格对齐 R3.2 视觉设计系统与原子资产规范
    /// </summary>
    internal static class APXUITheme
    {
        // ==================== 统一圆角 Radius Tokens (Phase 2B-R4-R3 单一来源) ====================
        internal const float RadiusWindow = 20f;       // 主窗体外轮廓与标题栏顶部
        internal const float RadiusLargeSurface = 16f; // 大面积背景与主要容器
        internal const float RadiusCard = 12f;         // 标准内容卡片、特色卡片
        internal const float RadiusButton = 10f;       // 预设按钮、次级操作按钮
        internal const float RadiusTab = 10f;          // 侧边栏导航 Tab
        internal const float RadiusBadge = 8f;         // 快捷键徽章、辅助标签
        internal const float RadiusPill = 999f;        // 完整胶囊 (Toggle、Slider Track/Fill)

        // ==================== 统一拟态磨砂玻璃 Alpha Tokens (Frosted Glass V1) ====================
        internal const float AlphaMainWindow = 0.90f; // 0.88 ~ 0.92
        internal const float AlphaHeader = 0.86f;     // 0.84 ~ 0.90
        internal const float AlphaSidebar = 0.80f;    // 0.78 ~ 0.84
        internal const float AlphaCard = 0.76f;       // 0.72 ~ 0.82
        internal const float AlphaBackdrop = 0.40f;   // 0.35 ~ 0.45

        // ==================== R4-R3 核心色彩与半透磨砂玻璃 Tokens ====================
        // 主背景 (深蓝黑半透玻璃基底)
        internal static readonly Color MainBackground = new Color(0.082f, 0.098f, 0.133f, AlphaMainWindow);
        // 面板基底 (#181D27)
        internal static readonly Color PanelBase = new Color(0.094f, 0.114f, 0.153f, AlphaMainWindow);
        // 浮层/卡片容器底色 (#1E2430)
        internal static readonly Color PanelElevated = new Color(0.118f, 0.141f, 0.188f, AlphaSidebar);
        // 标准内容卡片底色 (#202633)
        internal static readonly Color Card = new Color(0.125f, 0.149f, 0.200f, AlphaCard);
        // 卡片悬停底色 (#222B3A)
        internal static readonly Color CardHover = new Color(0.141f, 0.180f, 0.251f, 0.82f);
        // 侧边栏导航底色 (#181D27)
        internal static readonly Color Sidebar = new Color(0.094f, 0.114f, 0.153f, AlphaSidebar);

        // 玻璃质感边框与高光 (Glass Border & Top Highlight)
        internal static readonly Color GlassOuterBorder = new Color(0.40f, 0.55f, 0.75f, 0.14f); // 窗体外轮廓 0.12~0.16
        internal static readonly Color GlassInnerBorder = new Color(0.40f, 0.55f, 0.75f, 0.10f); // 卡片/内框 0.06~0.10
        internal static readonly Color GlassTopHighlight = new Color(1f, 1f, 1f, 0.08f);          // 顶部 1px 细微高光
        internal static readonly Color DividerColor = new Color(0.25f, 0.32f, 0.44f, 0.35f);     // 侧边栏细腻分割线

        // 文本色阶 (WCAG AAA / AA 极高可读性)
        internal static readonly Color PrimaryText = new Color(0.957f, 0.969f, 0.980f, 1f);   // #F4F7FA 纯白 (优先级 2: 标题)
        internal static readonly Color SecondaryText = new Color(0.725f, 0.761f, 0.812f, 1f); // #B9C2CF 浅蓝灰
        internal static readonly Color MutedText = new Color(0.498f, 0.537f, 0.596f, 1f);     // #7F8998 弱化灰

        // 强调与状态色 (严格对齐 Accent Priority 视觉层级)
        // 优先级 1: 关键倍率数字 (最高视觉焦点暖金 #FFC438)
        internal static readonly Color NumericAccent = new Color(1.000f, 0.769f, 0.220f, 1f);
        // 优先级 3: 侧边栏选中激活条 (#A6F23A 纯粹 Lime)
        internal static readonly Color LimeAccent = new Color(0.651f, 0.949f, 0.227f, 1f);
        // 优先级 4: 开关激活态 (略微降低 Alpha 的软化 Lime #96E432)
        internal static readonly Color ToggleActiveLime = new Color(0.588f, 0.894f, 0.196f, 0.85f);
        // 优先级 5: 预设按钮选中态 (比侧栏弱约 10% 的柔和绿玻璃 #8AD628)
        internal static readonly Color PresetSelectedLime = new Color(0.541f, 0.839f, 0.157f, 0.75f);
        // 优先级 6: 悬浮球交互环 / 次级科技青 (#20C8E8)
        internal static readonly Color CyanAccent = new Color(0.125f, 0.784f, 0.910f, 1f);

        // 辅助状态色
        internal static readonly Color Positive = new Color(0.267f, 0.843f, 0.627f, 1f);      // #44D7A0 状态正常
        internal static readonly Color Danger = new Color(1.000f, 0.376f, 0.455f, 1f);        // #FF6074 危险/珊瑚红
        internal static readonly Color DangerCoralHover = new Color(0.922f, 0.294f, 0.373f, 0.65f); // 关闭按钮悬停珊瑚色
        internal static readonly Color CloseGlyph = new Color(0.796f, 0.827f, 0.867f, 1f);    // #CBD3DD 关闭图标
        internal static readonly Color CloseGlyphHover = new Color(1.000f, 1.000f, 1.000f, 1f);// #FFFFFF
        internal static readonly Color Border = new Color(0.165f, 0.200f, 0.267f, 1f);        // #2A3344 暗色收边

        // ==================== Fail-Safe 深色保底 Tokens ====================
        internal static readonly Color Header = new Color(0.105f, 0.128f, 0.176f, AlphaHeader); // #1C2230 标题栏深底
        internal static readonly Color HeaderBase = Header;
        internal static readonly Color CardFeatureBase = new Color(0.129f, 0.157f, 0.212f, 0.80f);// 特色卡片深底
        internal static readonly Color TabSelectedBase = new Color(0.10f, 0.17f, 0.13f, 0.80f);
        internal static readonly Color SidebarBase = new Color(0.08f, 0.10f, 0.14f, AlphaSidebar);
        internal static readonly Color ButtonSecondaryBase = new Color(0.13f, 0.16f, 0.21f, 0.70f);
        internal static readonly Color ButtonSelectedBase = new Color(0.10f, 0.17f, 0.13f, 0.80f);

        // ==================== 兼容性别名 (保持既有调用稳定) ====================
        internal static readonly Color CardDark = Card;
        internal static readonly Color ItemHover = CardHover;
        internal static readonly Color ItemSelected = new Color(0.10f, 0.17f, 0.13f, 0.80f);
        internal static readonly Color AccentPrimary = NumericAccent;
        internal static readonly Color AccentCyan = CyanAccent;
        internal static readonly Color TextPrimary = PrimaryText;
        internal static readonly Color TextSecondary = SecondaryText;
        internal static readonly Color TextDisabled = MutedText;
        internal static readonly Color TextDarkSelected = PrimaryText;
        internal static readonly Color BorderLight = GlassOuterBorder;
        internal static readonly Color BorderCyan = CyanAccent;

        // ==================== 字体管理 ====================
        private static Font _cachedFont;

        internal static Font GetMainFont()
        {
            if (_cachedFont != null)
            {
                return _cachedFont;
            }

            try
            {
                _cachedFont = Font.CreateDynamicFontFromOSFont(new string[]
                {
                    "Microsoft YaHei",
                    "SimHei",
                    "Noto Sans CJK SC",
                    "Noto Sans SC",
                    "Source Han Sans CN",
                    "PingFang SC",
                    "Hiragino Sans GB",
                    "Arial"
                }, 16);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[APX UITheme] OS 字体回退失败: " + ex.Message);
            }

            if (_cachedFont == null)
            {
                _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return _cachedFont;
        }
    }
}
