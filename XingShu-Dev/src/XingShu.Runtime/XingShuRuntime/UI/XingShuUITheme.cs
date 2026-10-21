using System;
using UnityEngine;

namespace XingShuRuntime.UI
{
    /// <summary>游戏设置页视觉：深色框架、浅色卡片、粉色页签与黄色标题。</summary>
    internal static class XingShuUITheme
    {
        internal const float RadiusWindow = 20f;
        internal const float RadiusLargeSurface = 16f;
        internal const float RadiusCard = 12f;
        internal const float RadiusButton = 10f;
        internal const float RadiusTab = 10f;
        internal const float RadiusBadge = 8f;
        internal const float RadiusPill = 999f;
        internal const float AlphaMainWindow = 0.78f;
        internal const float AlphaHeader = 0.82f;
        internal const float AlphaSidebar = 0.93f;
        internal const float AlphaCard = 0.93f;
        internal const float AlphaBackdrop = 0.20f;
        internal static readonly Color MainBackground = new Color(0.145f, 0.145f, 0.180f, AlphaMainWindow);
        internal static readonly Color PanelBase = MainBackground;
        internal static readonly Color PanelElevated = new Color(0.980f, 0.976f, 0.965f, 1f);
        internal static readonly Color Card = new Color(0.980f, 0.976f, 0.965f, 1f);
        internal static readonly Color CardHover = new Color(1f, 0.92f, 0.96f, 1f);
        internal static readonly Color Sidebar = new Color(0.980f, 0.976f, 0.965f, 1f);
        internal static readonly Color GlassOuterBorder = new Color(0.12f, 0.10f, 0.16f, 1f);
        internal static readonly Color GlassInnerBorder = new Color(0.85f, 0.82f, 0.88f, 1f);
        internal static readonly Color GlassTopHighlight = new Color(1f, 1f, 1f, 0.5f);
        internal static readonly Color DividerColor = new Color(1f, 0.75f, 0f, 1f);
        internal static readonly Color PrimaryText = new Color(0.13f, 0.12f, 0.17f, 1f);
        internal static readonly Color SecondaryText = new Color(0.35f, 0.32f, 0.40f, 1f);
        internal static readonly Color MutedText = new Color(0.46f, 0.43f, 0.51f, 1f);
        internal static readonly Color NumericAccent = new Color(0.87f, 0.04f, 0.43f, 1f);
        internal static readonly Color LimeAccent = new Color(0.58f, 0.34f, 0f, 1f);
        internal static readonly Color ToggleActiveLime = new Color(1f, 0.15f, 0.58f, 1f);
        internal static readonly Color PresetSelectedLime = new Color(1f, 0.75f, 0f, 1f);
        internal static readonly Color CyanAccent = new Color(1f, 0.15f, 0.58f, 1f);
        internal static readonly Color Positive = new Color(0.02f, 0.44f, 0.32f, 1f);
        internal static readonly Color Danger = new Color(0.80f, 0.04f, 0.26f, 1f);
        internal static readonly Color DangerCoralHover = new Color(1f, 0.30f, 0.65f, 1f);
        internal static readonly Color CloseGlyph = new Color(1f, 1f, 1f, 1f);
        internal static readonly Color CloseGlyphHover = new Color(1.000f, 1.000f, 1.000f, 1f);
        internal static readonly Color Border = new Color(0.16f, 0.14f, 0.20f, 1f);
        internal static readonly Color Header = new Color(0.145f, 0.145f, 0.180f, AlphaHeader);
        internal static readonly Color HeaderBase = Header;
        internal static readonly Color CardFeatureBase = new Color(0.980f, 0.976f, 0.965f, 1f);
        internal static readonly Color TabSelectedBase = new Color(1f, 0.75f, 0f, 1f);
        internal static readonly Color SidebarBase = new Color(0.980f, 0.976f, 0.965f, 1f);
        internal static readonly Color ButtonSecondaryBase = new Color(0.94f, 0.93f, 0.95f, 1f);
        internal static readonly Color ButtonSelectedBase = new Color(1f, 0.75f, 0f, 1f);
        internal static readonly Color CardDark = Card;
        internal static readonly Color ItemHover = CardHover;
        internal static readonly Color ItemSelected = new Color(1f, 0.75f, 0f, 1f);
        internal static readonly Color AccentPrimary = NumericAccent;
        internal static readonly Color AccentCyan = CyanAccent;
        internal static readonly Color TextPrimary = PrimaryText;
        internal static readonly Color TextSecondary = SecondaryText;
        internal static readonly Color TextDisabled = MutedText;
        internal static readonly Color TextDarkSelected = PrimaryText;
        internal static readonly Color Pink = new Color(1f, 0.153f, 0.58f, 1f);
        internal static readonly Color Yellow = new Color(1f, 0.75f, 0f, 1f);
        internal static readonly Color OnDark = Color.white;
        internal static readonly Color BorderLight = GlassOuterBorder;
        internal static readonly Color BorderCyan = CyanAccent;

        // Bright text for the dark status badges; regular text tokens target white cards.
        internal static readonly Color BadgeMuted = new Color(0.82f, 0.80f, 0.87f, 1f);
        internal static readonly Color BadgeActive = new Color(0.42f, 1f, 0.72f, 1f);

        // ==================== 字体管理 ====================
        private static Font _cachedFont;
        private static Font _fallbackFont;
        private static bool _usingGameFont;
        private static float _nextFontProbe;

        internal static Font GetMainFont()
        {
            // Settings_fui and UIConfig use JingNanBoBoHei. Reuse the game's Font;
            // do not redistribute its font binary or modify the game's font registry.
            if (!_usingGameFont && Time.unscaledTime >= _nextFontProbe)
            {
                _nextFontProbe = Time.unscaledTime + 5f;
                try
                {
                    FairyGUI.DynamicFont gameFont = FairyGUI.FontManager.GetFont(FairyGUI.UIConfig.defaultFont) as FairyGUI.DynamicFont;
                    if (gameFont != null && gameFont.nativeFont != null)
                    {
                        _cachedFont = gameFont.nativeFont;
                        _usingGameFont = true;
                        Debug.Log("[XingShu UITheme] 使用游戏设置字体: " + _cachedFont.name);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[XingShu UITheme] 游戏字体暂未就绪: " + ex.Message);
                }
            }
            if (_cachedFont != null)
            {
                return _cachedFont;
            }
            _cachedFont = GetFallbackFont();
            return _cachedFont;
        }

        internal static Font GetFontForText(string content)
        {
            Font font = GetMainFont();
            if (_usingGameFont && !string.IsNullOrEmpty(content))
            {
                foreach (char ch in content)
                {
                    if (!char.IsControl(ch) && !char.IsWhiteSpace(ch) && !font.HasCharacter(ch))
                        return GetFallbackFont();
                }
            }
            return font;
        }

        internal static void RefreshFonts(GameObject root)
        {
            if (root == null) return;
            foreach (UnityEngine.UI.Text text in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                text.font = GetFontForText(text.text);
        }

        private static Font GetFallbackFont()
        {
            if (_fallbackFont != null) return _fallbackFont;

            try
            {
                _fallbackFont = Font.CreateDynamicFontFromOSFont(new string[]
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
                Debug.LogWarning("[XingShu UITheme] OS 字体回退失败: " + ex.Message);
            }

            if (_fallbackFont == null)
            {
                _fallbackFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return _fallbackFont;
        }
    }
}
