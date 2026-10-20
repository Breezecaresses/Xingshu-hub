import os
import glob

assets_dir = r'assets/vanilla-ui-r3'
pngs = sorted(glob.glob(os.path.join(assets_dir, '*.png')))

print(f"Found {len(pngs)} PNG files in {assets_dir}")
assert len(pngs) == 24, f"Expected 24 PNGs, got {len(pngs)}"

lines = []
lines.append("using System;")
lines.append("using System.Collections.Generic;")
lines.append("")
lines.append("namespace APXRuntime.UI")
lines.append("{")
lines.append("    /// <summary>")
lines.append("    /// APX 生产级原子 UI 资产确定性编译内嵌载荷表 (Self-Contained Payload Contract)")
lines.append("    /// 规避 Unity IL2CPP / HybridCLR 对动态程序集 GetManifestResourceStream 的反射截断限制，")
lines.append("    /// 确保在任何引擎平台、任何加载器环境下核心原子视觉切片 100% 必定命中。")
lines.append("    /// </summary>")
lines.append("    internal static class APXUIAssetPayloads")
lines.append("    {")
lines.append("        private static readonly Dictionary<string, byte[]> _payloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);")
lines.append("")
lines.append("        static APXUIAssetPayloads()")
lines.append("        {")

for p in pngs:
    fname = os.path.basename(p)
    with open(p, 'rb') as fp:
        data = fp.read()
    # Check PNG magic
    assert data[:8] == b'\x89PNG\r\n\x1a\n', f"Invalid PNG magic in {fname}"
    b64 = data.hex()
    lines.append(f'            RegisterPayload("{fname}", "APX.{fname}", "{b64}");')

lines.append("        }")
lines.append("")
lines.append("        private static void RegisterPayload(string logicalName, string resourceName, string hex)")
lines.append("        {")
lines.append("            byte[] bytes = HexToBytes(hex);")
lines.append("            _payloads[logicalName] = bytes;")
lines.append("            _payloads[resourceName] = bytes;")
lines.append("            if (logicalName.EndsWith(\".png\", StringComparison.OrdinalIgnoreCase))")
lines.append("            {")
lines.append("                _payloads[logicalName.Substring(0, logicalName.Length - 4)] = bytes;")
lines.append("            }")
lines.append("        }")
lines.append("")
lines.append("        internal static byte[] GetPayload(string key)")
lines.append("        {")
lines.append("            if (string.IsNullOrEmpty(key)) return null;")
lines.append("            byte[] bytes;")
lines.append("            if (_payloads.TryGetValue(key, out bytes))")
lines.append("            {")
lines.append("                return bytes;")
lines.append("            }")
lines.append("            return null;")
lines.append("        }")
lines.append("")
lines.append("        internal static int Count => _payloads.Count;")
lines.append("")
lines.append("        private static byte[] HexToBytes(string hex)")
lines.append("        {")
lines.append("            int len = hex.Length;")
lines.append("            byte[] bytes = new byte[len / 2];")
lines.append("            for (int i = 0; i < len; i += 2)")
lines.append("            {")
lines.append("                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);")
lines.append("            }")
lines.append("            return bytes;")
lines.append("        }")
lines.append("    }")
lines.append("}")

target_file = r'src/APX.hooks/APXRuntime/UI/APXUIAssetPayloads.cs'
with open(target_file, 'w', encoding='utf-8') as f:
    f.write('\n'.join(lines) + '\n')

print(f"Generated {target_file} successfully! Size: {os.path.getsize(target_file)} bytes")
