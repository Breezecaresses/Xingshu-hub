using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace APXRuntime.UI
{
    /// <summary>
    /// APX 统一原版 UI 资源加载与生命周期服务 (Phase 2B-R4 生产整合版)
    /// 1. 优先从 Assembly Embedded Resources 读取原子切片，确保 Candidate 100% 自包含；
    /// 2. 建立确定性资源名称映射 (Exact Resource Lookup) 与 9-Slice Border 映射；
    /// 3. Stream 循环读满保底 (Read Stream Fully)；
    /// 4. LoadImage 结果诊断与 DECODE_FAILED 探测；
    /// 5. 格式化输出至 persistentDataPath/apx_ui_runtime.log；
    /// 6. 严禁无着色 WhiteSprite 产生大面积白光。
    /// </summary>
    internal static class APXUIAssetService
    {
        private static readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _loggedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _logLock = new object();

        // R3.2 24 项生产资产确定性资源名称映射 (严禁 brand_mark)
        private static readonly Dictionary<string, string> _exactResourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "window_frame.png",       "APX.window_frame.png" },
            { "header_bar.png",         "APX.header_bar.png" },
            { "sidebar_tab_normal.png", "APX.sidebar_tab_normal.png" },
            { "sidebar_tab_hover.png",  "APX.sidebar_tab_hover.png" },
            { "sidebar_tab_selected.png","APX.sidebar_tab_selected.png" },
            { "card_base.png",          "APX.card_base.png" },
            { "card_feature_header.png","APX.card_feature_header.png" },
            { "button_primary.png",     "APX.button_primary.png" },
            { "button_secondary.png",   "APX.button_secondary.png" },
            { "button_selected.png",    "APX.button_selected.png" },
            { "hotkey_badge.png",       "APX.hotkey_badge.png" },
            { "toggle_track_off.png",   "APX.toggle_track_off.png" },
            { "toggle_track_on.png",    "APX.toggle_track_on.png" },
            { "toggle_thumb.png",       "APX.toggle_thumb.png" },
            { "slider_track.png",       "APX.slider_track.png" },
            { "slider_fill.png",        "APX.slider_fill.png" },
            { "slider_handle.png",      "APX.slider_handle.png" },
            { "divider.png",            "APX.divider.png" },
            { "close_normal.png",       "APX.close_normal.png" },
            { "close_hover.png",        "APX.close_hover.png" },
            { "floating_entry_idle.png","APX.floating_entry_idle.png" },
            { "floating_entry_hover.png","APX.floating_entry_hover.png" },
            { "floating_entry_active.png","APX.floating_entry_active.png" },
            { "floating_entry_notify.png","APX.floating_entry_notify.png" }
        };

        // R3.2 24 项生产资产 9-Slice Border 映射表 [L, B, R, T]
        private static readonly Dictionary<string, Vector4> _sliceBorderMap = new Dictionary<string, Vector4>(StringComparer.OrdinalIgnoreCase)
        {
            { "window_frame.png",       new Vector4(36, 36, 36, 36) },
            { "header_bar.png",         new Vector4(32, 24, 32, 32) },
            { "sidebar_tab_normal.png", new Vector4(60, 30, 30, 30) },
            { "sidebar_tab_hover.png",  new Vector4(60, 30, 30, 30) },
            { "sidebar_tab_selected.png",new Vector4(60, 30, 30, 30) },
            { "card_base.png",          new Vector4(20, 20, 20, 20) },
            { "card_feature_header.png",new Vector4(24, 24, 24, 24) },
            { "button_primary.png",     new Vector4(16, 16, 16, 16) },
            { "button_secondary.png",   new Vector4(16, 16, 16, 16) },
            { "button_selected.png",    new Vector4(16, 16, 16, 16) },
            { "hotkey_badge.png",       new Vector4(10, 10, 10, 10) },
            { "toggle_track_off.png",   new Vector4(18, 18, 18, 18) },
            { "toggle_track_on.png",    new Vector4(18, 18, 18, 18) },
            { "toggle_thumb.png",       Vector4.zero },
            { "slider_track.png",       new Vector4(16, 16, 16, 16) },
            { "slider_fill.png",        new Vector4(32, 16, 32, 16) },
            { "slider_handle.png",      Vector4.zero },
            { "divider.png",            new Vector4(32, 2, 32, 2) },
            { "close_normal.png",       Vector4.zero },
            { "close_hover.png",        Vector4.zero },
            { "floating_entry_idle.png",Vector4.zero },
            { "floating_entry_hover.png",Vector4.zero },
            { "floating_entry_active.png",Vector4.zero },
            { "floating_entry_notify.png",Vector4.zero }
        };

        internal static void LogUIRuntime(string message)
        {
            try
            {
                lock (_logLock)
                {
                    string logDir = Application.persistentDataPath;
                    string logPath = Path.Combine(logDir, "apx_ui_runtime.log");
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    File.AppendAllText(logPath, $"[{timestamp}] {message}\r\n");
                }
            }
            catch { }
            Debug.Log(message);
        }

        private static void LogAssetEntry(string logicalName, string resourceName, string source, int bytes, string decodedSize, string result)
        {
            if (_loggedAssets.Add(logicalName))
            {
                LogUIRuntime($"[ASSET]\r\nLogicalName={logicalName}\r\nResourceName={resourceName}\r\nSource={source}\r\nBytes={bytes}\r\nDecodedSize={decodedSize}\r\nResult={result}");
            }
        }

        private static Sprite _whiteSprite;
        private static readonly Dictionary<string, Sprite> _roundedFallbackSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _loggedFallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Fallback 圆角 Token 映射 (与 R3.2 / R4-R3 规范严格一致)
        private static readonly Dictionary<string, int> _assetFallbackRadiusMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "window_frame.png",       20 },
            { "header_bar.png",         20 },
            { "card_base.png",          12 },
            { "card_feature_header.png",12 },
            { "sidebar_tab_normal.png", 10 },
            { "sidebar_tab_hover.png",  10 },
            { "sidebar_tab_selected.png",10 },
            { "button_primary.png",     10 },
            { "button_secondary.png",   10 },
            { "button_selected.png",    10 },
            { "hotkey_badge.png",       8 },
            { "close_normal.png",       10 },
            { "close_hover.png",        10 }
        };

        private static void LogFallbackOnce(string logicalName, int radius, string tintDesc, string reason)
        {
            if (_loggedFallbacks.Add(logicalName))
            {
                LogUIRuntime($"[UI_ASSET_FALLBACK]\r\nLogicalName={logicalName}\r\nFallbackType=ProceduralRounded\r\nRadius={radius}\r\nTint={tintDesc}\r\nReason={reason}");
            }
        }

        /// <summary>
        /// 获取程序化抗锯齿圆角保底 Sprite (运行时通过 Texture2D 动态生成 64x64 Alpha 遮罩，9-Slice 切片，杜绝直角与白板)
        /// </summary>
        internal static Sprite GetRoundedFallbackSprite(int radius, bool topOnly = false)
        {
            if (radius <= 0) return WhiteSprite;
            string key = $"{radius}_{(topOnly ? "top" : "all")}";
            if (_roundedFallbackSprites.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            int w = 64;
            int h = 64;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = "APX_FallbackTex_" + key;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[w * h];
            float r = radius;

            for (int y = 0; y < h; y++)
            {
                float py = y + 0.5f;
                float dBottom = py;
                float dTop = h - py;
                bool isTopHalf = py >= h * 0.5f;

                for (int x = 0; x < w; x++)
                {
                    float px = x + 0.5f;
                    float dx = Mathf.Min(px, w - px);
                    float dy;
                    float effectiveR;

                    if (topOnly)
                    {
                        if (isTopHalf)
                        {
                            dy = dTop;
                            effectiveR = r;
                        }
                        else
                        {
                            dy = dBottom;
                            effectiveR = 0f;
                        }
                    }
                    else
                    {
                        dy = Mathf.Min(dBottom, dTop);
                        effectiveR = r;
                    }

                    float alpha;
                    if (effectiveR > 0f && dx < effectiveR && dy < effectiveR)
                    {
                        float vx = effectiveR - dx;
                        float vy = effectiveR - dy;
                        float dist = Mathf.Sqrt(vx * vx + vy * vy);
                        if (dist > effectiveR + 0.5f)
                            alpha = 0f;
                        else if (dist < effectiveR - 0.5f)
                            alpha = 1f;
                        else
                            alpha = Mathf.Clamp01(effectiveR + 0.5f - dist);
                    }
                    else
                    {
                        alpha = 1f;
                    }

                    pixels[y * w + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            Vector4 border = topOnly ? new Vector4(radius, 0f, radius, radius) : new Vector4(radius, radius, radius, radius);
            Sprite sp = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sp.name = "APX_FallbackSprite_" + key;
            _roundedFallbackSprites[key] = sp;
            return sp;
        }

        /// <summary>
        /// 判定指定 Sprite 是否属于保底 Sprite (用于触发 Theme Tint 着色契约，杜绝白板)
        /// </summary>
        internal static bool IsFallbackSprite(Sprite sprite)
        {
            if (sprite == null || sprite == _whiteSprite) return true;
            foreach (var kv in _roundedFallbackSprites)
            {
                if (kv.Value == sprite) return true;
            }
            return false;
        }

        /// <summary>
        /// 获取纯白单像素基础 Sprite (用于底层几何渲染或着色蒙版)
        /// </summary>
        internal static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    Color[] pixels = new Color[4] { Color.white, Color.white, Color.white, Color.white };
                    tex.SetPixels(pixels);
                    tex.Apply();
                    _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, Vector4.one);
                    _whiteSprite.name = "APX_White_Fallback";
                }
                return _whiteSprite;
            }
        }

        /// <summary>
        /// 获取或加载指定的 UI Sprite，优先使用 manifest 预设 9-Slice Border
        /// </summary>
        internal static Sprite GetSprite(string assetName, Vector4 border = default)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                return WhiteSprite;
            }

            string cleanName = Path.GetFileName(assetName);
            if (!cleanName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                cleanName += ".png";
            }

            // 若未显式传入 border，尝试从 manifest border 映射中取
            if (border == default && _sliceBorderMap.TryGetValue(cleanName, out Vector4 defaultBorder))
            {
                border = defaultBorder;
            }

            string spriteKey = cleanName + "_" + border.ToString();
            if (_sprites.TryGetValue(spriteKey, out Sprite cachedSprite) && cachedSprite != null)
            {
                return cachedSprite;
            }

            Texture2D tex = GetTexture(cleanName);
            if (tex == null)
            {
                int fbRadius;
                bool topOnly = cleanName.Equals("header_bar.png", StringComparison.OrdinalIgnoreCase);
                if (!_assetFallbackRadiusMap.TryGetValue(cleanName, out fbRadius))
                {
                    fbRadius = (int)APXUITheme.RadiusCard;
                }
                LogFallbackOnce(cleanName, fbRadius, "ThemeTint", "TextureDecodeOrPayloadUnavailable");
                return GetRoundedFallbackSprite(fbRadius, topOnly);
            }

            try
            {
                Sprite newSprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    border
                );
                newSprite.name = "APX_Sprite_" + cleanName;
                _sprites[spriteKey] = newSprite;
                return newSprite;
            }
            catch (Exception ex)
            {
                LogUIRuntime($"[APX UIAssetService ERROR] 创建 Sprite 失败 ('{cleanName}'): {ex.Message}");
                int fbRadius;
                bool topOnly = cleanName.Equals("header_bar.png", StringComparison.OrdinalIgnoreCase);
                if (!_assetFallbackRadiusMap.TryGetValue(cleanName, out fbRadius))
                {
                    fbRadius = (int)APXUITheme.RadiusCard;
                }
                LogFallbackOnce(cleanName, fbRadius, "ThemeTint", "SpriteCreateException: " + ex.Message);
                return GetRoundedFallbackSprite(fbRadius, topOnly);
            }
        }

        /// <summary>
        /// 获取或加载底层 Texture2D 资源
        /// </summary>
        internal static Texture2D GetTexture(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                return null;
            }

            string cleanName = Path.GetFileName(assetName);
            if (!cleanName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                cleanName += ".png";
            }

            if (_textures.TryGetValue(cleanName, out Texture2D cachedTex) && cachedTex != null)
            {
                return cachedTex;
            }

            string source;
            string resName;
            byte[] bytes = LoadRawAssetBytes(cleanName, out resName, out source);
            if (bytes == null || bytes.Length == 0)
            {
                LogUIRuntime($"[APX UIAssetService WARNING] 未找到素材原始字节: '{cleanName}'");
                LogAssetEntry(cleanName, resName ?? "none", "WhiteSpriteFallback", 0, "0x0", "NOT_FOUND");
                return null;
            }

            try
            {
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool success = ImageConversion.LoadImage(tex, bytes);
                if (!success)
                {
                    bool isPng = bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
                    LogUIRuntime($"[APX UIAssetService DECODE_FAILED] Asset='{cleanName}', BytesLen={bytes.Length}, PngMagicValid={isPng}");
                    LogAssetEntry(cleanName, resName, source, bytes.Length, "0x0", "DECODE_FAILED");
                    return null;
                }

                tex.name = "APX_Tex_" + cleanName;
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                _textures[cleanName] = tex;
                LogAssetEntry(cleanName, resName, source, bytes.Length, $"{tex.width}x{tex.height}", "OK");
                return tex;
            }
            catch (Exception ex)
            {
                LogUIRuntime($"[APX UIAssetService ERROR] 解码 Texture2D 异常 ('{cleanName}'): {ex.Message}");
                LogAssetEntry(cleanName, resName, source, bytes.Length, "0x0", "EXCEPTION: " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// 核心生产级资产运行时自检 (在 UI 安装或首次打开时调用一次)
        /// 验证 24 项生产资产是否均能成功载入并正确解码为真实分辨率 Texture
        /// </summary>
        internal static bool ValidateCoreAssets()
        {
            int pass = 0;
            int fail = 0;
            int total = _exactResourceMap.Count;

            foreach (var kv in _exactResourceMap)
            {
                string logicalName = kv.Key;
                Texture2D tex = GetTexture(logicalName);
                if (tex != null && tex.width > 2 && tex.height > 2)
                {
                    pass++;
                }
                else
                {
                    fail++;
                    LogUIRuntime($"[UI_ASSET_SELFTEST FAIL] 资产 '{logicalName}' 校验失败");
                }
            }

            LogUIRuntime($"[UI_ASSET_SELFTEST] Total={total} Pass={pass} Fail={fail}");
            if (fail > 0)
            {
                LogUIRuntime($"[APX UIAssetService WARNING] VISUAL_ASSET_DEGRADED: {fail} 个核心资产自检失败，降级使用 Dark Fail-Safe 几何保底");
            }
            return fail == 0;
        }

        private static byte[] LoadRawAssetBytes(string cleanName, out string resourceName, out string source)
        {
            source = "WhiteSpriteFallback";
            resourceName = "none";

            string exactResourceName;
            if (!_exactResourceMap.TryGetValue(cleanName, out exactResourceName))
            {
                exactResourceName = "APX." + cleanName;
            }
            resourceName = exactResourceName;

            // 1. 优先尝试从 Assembly Manifest Resource Stream 读取
            try
            {
                Assembly asm = typeof(APXUIAssetService).Assembly;
                MethodInfo mi = typeof(Assembly).GetMethod("GetManifestResourceStream", new Type[] { typeof(string) });
                Stream stream = (mi != null) ? (Stream)mi.Invoke(asm, new object[] { exactResourceName }) : null;

                if (stream != null)
                {
                    using (stream)
                    {
                        int length = (int)stream.Length;
                        byte[] buffer = new byte[length];
                        int totalRead = 0;

                        // 循环读取确保完整流消费 (Read Stream Fully)
                        while (totalRead < length)
                        {
                            int read = stream.Read(buffer, totalRead, length - totalRead);
                            if (read <= 0) break;
                            totalRead += read;
                        }

                        if (totalRead == length)
                        {
                            source = "EmbeddedResource";
                            return buffer;
                        }
                        else
                        {
                            LogUIRuntime($"[APX UIAssetService ERROR] 流读取不完整 '{cleanName}': 标称={length}, 实际={totalRead}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogUIRuntime($"[APX UIAssetService ERROR] 读取内嵌资源异常 ('{cleanName}'): {ex.Message}");
            }

            // 2. 编译自包含保底载荷表 (Self-Contained Payload Contract, 100% 免疫 IL2CPP 运行时动态加载截断)
            try
            {
                byte[] payload = APXUIAssetPayloads.GetPayload(cleanName);
                if (payload != null && payload.Length > 0)
                {
                    source = "CompiledPayload";
                    return payload;
                }
            }
            catch (Exception ex)
            {
                LogUIRuntime($"[APX UIAssetService ERROR] 读取载荷表异常 ('{cleanName}'): {ex.Message}");
            }

            // 3. 本地外部目录开发回退 (纠正路径解析，杜绝重复目录)
            try
            {
                string gameRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string[] candidateDirs = new string[]
                {
                    Path.Combine(gameRoot, "assets", "vanilla-ui-r3"),
                    Path.Combine(Application.persistentDataPath, "assets", "vanilla-ui-r3"),
                    Path.Combine(Environment.CurrentDirectory, "assets", "vanilla-ui-r3"),
                    Path.Combine(gameRoot, "outimg")
                };

                foreach (string dir in candidateDirs)
                {
                    if (!Directory.Exists(dir)) continue;

                    string directFile = Path.Combine(dir, cleanName);
                    if (File.Exists(directFile))
                    {
                        source = "DevFallback";
                        resourceName = directFile;
                        LogUIRuntime($"[APX UIAssetService INFO] 本地命中外部资源: '{directFile}'");
                        return File.ReadAllBytes(directFile);
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 清理 APX 自身创建的纹理与 Sprite 缓存，释放内存
        /// </summary>
        internal static void Cleanup()
        {
            foreach (var kv in _sprites)
            {
                if (kv.Value != null && kv.Value != _whiteSprite)
                {
                    UnityEngine.Object.Destroy(kv.Value);
                }
            }
            _sprites.Clear();

            foreach (var kv in _textures)
            {
                if (kv.Value != null)
                {
                    UnityEngine.Object.Destroy(kv.Value);
                }
            }
            _textures.Clear();

            if (_whiteSprite != null)
            {
                UnityEngine.Object.Destroy(_whiteSprite);
                _whiteSprite = null;
            }

            foreach (var kv in _roundedFallbackSprites)
            {
                if (kv.Value != null)
                {
                    if (kv.Value.texture != null)
                    {
                        UnityEngine.Object.Destroy(kv.Value.texture);
                    }
                    UnityEngine.Object.Destroy(kv.Value);
                }
            }
            _roundedFallbackSprites.Clear();
            _loggedFallbacks.Clear();
        }
    }
}
