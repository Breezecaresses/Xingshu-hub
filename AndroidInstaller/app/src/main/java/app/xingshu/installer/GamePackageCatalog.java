package app.xingshu.installer;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Package validation and multi-channel metadata shared between privileged engine and UI. */
final class GamePackageCatalog {
    static final String DEFAULT_PACKAGE = "com.feimo.astralparty";
    private static final Pattern PKG_PATTERN = Pattern.compile("[a-zA-Z][a-zA-Z0-9_]*(\\.[a-zA-Z0-9_]+)+");
    private static final Pattern VERSION_NAME_PATTERN = Pattern.compile("(?m)^\\s*versionName=(\\S+)\\s*$");

    static final class ChannelMeta {
        final String packageName;
        final String platform;
        final String label;

        ChannelMeta(String packageName, String platform, String label) {
            this.packageName = packageName;
            this.platform = platform;
            this.label = label;
        }
    }

    private GamePackageCatalog() { }

    static boolean isValidPackageName(String pkg) {
        return pkg != null && pkg.length() >= 3 && pkg.length() <= 128 && PKG_PATTERN.matcher(pkg).matches();
    }

    static String requireValidPackageName(String pkg) throws IOException {
        String clean = pkg == null ? "" : pkg.trim();
        if (clean.isEmpty()) return DEFAULT_PACKAGE;
        if (!isValidPackageName(clean)) {
            throw new IOException("非法的游戏包名");
        }
        return clean;
    }

    static boolean isCandidatePackageName(String pkg) {
        if (!isValidPackageName(pkg)) return false;
        String lower = pkg.toLowerCase(Locale.ROOT);
        if (lower.startsWith("app.xingshu.") || lower.contains("xingshu")) return false;
        return lower.startsWith("com.feimo.")
            || lower.contains("astralparty")
            || lower.contains(".jxpd")
            || lower.endsWith(".jxpd");
    }

    static ChannelMeta classifyPackage(String pkg) {
        String clean = isValidPackageName(pkg) ? pkg : DEFAULT_PACKAGE;
        String lower = clean.toLowerCase(Locale.ROOT);
        if (lower.equals("com.feimo.astralparty")) {
            return new ChannelMeta(clean, "TAPTAP", "TapTap / 官服");
        }
        if (lower.equals("com.feimo.jxpd.mi") || lower.endsWith(".mi") || lower.contains("xiaomi")) {
            return new ChannelMeta(clean, "XIAOMI", "小米渠道");
        }
        if (lower.contains("bili")) {
            return new ChannelMeta(clean, "BILIBILI", "B站 (哔哩哔哩)");
        }
        if (lower.contains("huawei")) {
            return new ChannelMeta(clean, "HUAWEI", "华为渠道");
        }
        if (lower.contains("nearme") || lower.contains("oppo")) {
            return new ChannelMeta(clean, "OPPO", "OPPO渠道");
        }
        if (lower.contains("vivo")) {
            return new ChannelMeta(clean, "VIVO", "vivo渠道");
        }
        if (lower.contains("tmgp") || lower.contains("yyb") || lower.contains("tencent")) {
            return new ChannelMeta(clean, "YYB", "应用宝渠道");
        }
        if (lower.contains("4399")) {
            return new ChannelMeta(clean, "M4399", "4399渠道");
        }
        if (lower.contains("meizu")) {
            return new ChannelMeta(clean, "MEIZU", "魅族渠道");
        }
        return new ChannelMeta(clean, "ANDROID_CN", "安卓渠道 (" + clean + ")");
    }

    static File filesRoot(String pkg) throws IOException {
        String valid = requireValidPackageName(pkg);
        return new File("/sdcard/Android/data/" + valid + "/files");
    }

    static File backupRoot(String pkg) throws IOException {
        String valid = requireValidPackageName(pkg);
        if (DEFAULT_PACKAGE.equals(valid)) {
            return new File("/data/local/tmp/xingshu-installer/" + PayloadSpec.VANILLA);
        }
        return new File("/data/local/tmp/xingshu-installer/" + valid + "/" + PayloadSpec.VANILLA);
    }

    static List<String> parseThirdPartyPackages(String pmOutput) {
        Set<String> pkgs = new LinkedHashSet<>();
        if (pmOutput != null) {
            for (String rawLine : pmOutput.split("[\\r\\n]+")) {
                String line = rawLine.trim();
                if (line.startsWith("package:")) {
                    String pkg = line.substring("package:".length()).trim();
                    if (isCandidatePackageName(pkg)) pkgs.add(pkg);
                }
            }
        }
        return new ArrayList<>(pkgs);
    }

    static String extractVersionName(String dumpsysOutput) {
        if (dumpsysOutput == null) return "";
        Matcher m = VERSION_NAME_PATTERN.matcher(dumpsysOutput);
        return m.find() ? m.group(1).trim() : "";
    }
}
