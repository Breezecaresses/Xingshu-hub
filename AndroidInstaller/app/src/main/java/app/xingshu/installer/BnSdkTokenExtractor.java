package app.xingshu.installer;

import java.io.IOException;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Extracts BnSdk native tokens from Android BN_DEBUG logcat output and validates QQ /login URLs. */
final class BnSdkTokenExtractor {
    private static final String PUBLIC_ORIGIN = "https://124.221.176.134";
    private static final Pattern APP_ID_PATTERN = Pattern.compile(
        "(?i)(?:\\\\*\"?(?:appid|app_id)\\\\*\"?\\s*[:=]\\s*\\\\*\"?([0-9]{6,16})|(?:period|bnsdk|java|native)[/_-](1100[0-9]{5}))(?!\\d)"
    );
    private static final Pattern USER_ID_PATTERN = Pattern.compile(
        "(?i)(?:\\\\*\"?(?:userid|user_id|uid)\\\\*\"?\\s*[:=]\\s*\\\\*\"?)([A-Za-z0-9_:=-]{1,64})"
    );
    private static final Pattern ROLE_ID_PATTERN = Pattern.compile(
        "(?i)\\\\*\"rid\\\\*\"\\s*:\\s*\\\\*\"?([0-9]{1,20})(?!\\d)"
    );
    private static final Pattern ROLE_NAME_PATTERN = Pattern.compile(
        "(?i)\\\\*\"rn\\\\*\"\\s*:\\s*\\\\*\"([^\"\\r\\n]{1,128})\""
    );
    private static final Pattern ACCESS_TOKEN_PATTERN = Pattern.compile(
        "(?i)(?:\\\\*\"?access_?token\\\\*\"?\\s*:\\s*\\\\*\"([A-Za-z0-9_.=-]{40,4096})\\\\*\"|\\baccess_?token=([A-Za-z0-9_.=-]{40,4096})(?=[\\s&'\"}]|$))"
    );
    private static final Pattern SQL_ENTITY_TOKEN_PATTERN = Pattern.compile(
        "(?i)\\\\*\"token\\\\*\"\\s*:\\s*\\\\*\"([A-Za-z0-9_.=-]{40,4096})\\\\*\""
    );
    private static final Pattern LOGIN_SDK_TOKEN_PATTERN = Pattern.compile(
        "\\[LoginSDK\\]\\s*the token is:\\s*([A-Za-z0-9_.=-]{40,4096})(?=[\\s\\r\\n]|$)"
    );
    private static final Pattern BRIEF_PID_PATTERN = Pattern.compile(
        "^[A-Z]/[^(]{1,64}\\(\\s*([0-9]{1,7})\\):"
    );
    private static final Pattern THREADTIME_PID_PATTERN = Pattern.compile(
        "^\\d{2}-\\d{2}\\s+\\d{2}:\\d{2}:\\d{2}\\.\\d+\\s+([0-9]{1,7})\\s+[0-9]{1,7}\\s+[A-Z]\\s+"
    );
    private static final Pattern LOGIN_FRAGMENT_PATTERN = Pattern.compile(
        "^([A-Za-z0-9_-]{8,128})\\.([A-Za-z0-9_-]{16,256})$"
    );

    static final class ChannelToken {
        final String platform;
        final String appId;
        final String label;
        final String userId;
        final String sdkToken;
        final boolean nativeSupported;

        ChannelToken(String platform, String appId, String label, String userId, String sdkToken, boolean nativeSupported) {
            this.platform = platform;
            this.appId = appId;
            this.label = label;
            this.userId = userId == null ? "" : userId;
            this.sdkToken = sdkToken;
            this.nativeSupported = nativeSupported;
        }

        String maskedToken() {
            if (sdkToken == null || sdkToken.length() < 12) return "***";
            return sdkToken.substring(0, 4) + "..." + sdkToken.substring(sdkToken.length() - 4);
        }

        String displaySummary() {
            StringBuilder sb = new StringBuilder();
            sb.append(label).append(" [").append(platform);
            if (!appId.isEmpty()) sb.append(":").append(appId);
            sb.append("]");
            if (!userId.isEmpty()) {
                sb.append(userId.contains("UID ") ? " · " : " · 渠道号 ").append(userId);
            }
            sb.append(" · 票据 ").append(maskedToken());
            if (!nativeSupported) sb.append("（需进游戏用 趴绑定）");
            return sb.toString();
        }
    }

    static final class LoginLinkParams {
        final String requestId;
        final String pageToken;

        LoginLinkParams(String requestId, String pageToken) {
            this.requestId = requestId;
            this.pageToken = pageToken;
        }
    }

    private BnSdkTokenExtractor() { }

    static ChannelToken classifyApp(String appId, String userId, String sdkToken) {
        String id = appId == null ? "" : appId.trim();
        switch (id) {
            case "110001934":
                return new ChannelToken("XIAOMI", id, "小米渠道", userId, sdkToken, true);
            case "110001937":
                return new ChannelToken("TAPTAP", id, "TapTap 安卓", userId, sdkToken, true);
            case "110001958":
                return new ChannelToken("TAPTAP", id, "TapTap", userId, sdkToken, true);
            case "110001957":
                return new ChannelToken("BILIBILI", id, "哔哩哔哩 (B站)", userId, sdkToken, true);
            case "110001949":
                return new ChannelToken("WEGAME", id, "WeGame", userId, sdkToken, true);
            case "110001933":
                return new ChannelToken("STEAM_CN", id, "Steam / 官服", userId, sdkToken, true);
            case "110001959":
                return new ChannelToken("STEAM_CN", id, "Steam 国服", userId, sdkToken, true);
            default:
                return new ChannelToken("ANDROID_CN", id, id.isEmpty() ? "未知渠道" : "渠道 " + id, userId, sdkToken, false);
        }
    }

    static String inferAppIdFromPackage(String text) {
        if (text == null || text.isEmpty()) return "";
        if (text.contains("com.feimo.jxpd.mi")) return "110001934";
        if (text.contains("com.feimo.astralparty.bilibili") || text.contains("com.feimo.jxpd.bili")) return "110001957";
        if (text.contains("com.feimo.astralparty")) return "110001937";
        return "";
    }

    static String appIdForPackage(String packageName) {
        String pkg = packageName == null ? "" : packageName.trim();
        if (pkg.isEmpty()) return "";
        GamePackageCatalog.ChannelMeta meta = GamePackageCatalog.classifyPackage(pkg);
        switch (meta.platform) {
            case "XIAOMI":
                return "110001934";
            case "BILIBILI":
                return "110001957";
            case "TAPTAP":
                return "110001937";
            default:
                return "";
        }
    }

    static List<ChannelToken> extractFromLogcat(String logcatText) {
        return extractFromLogcat(logcatText, "");
    }

    static List<ChannelToken> extractFromLogcat(String logcatText, String fallbackAppId) {
        List<ChannelToken> ordered = new ArrayList<>();
        if (logcatText == null || logcatText.isEmpty()) return ordered;

        String[] lines = logcatText.split("[\\r\\n]+");
        Map<String, String> pidToAppId = new HashMap<>();
        Map<String, String> pidToUserId = new HashMap<>();
        Map<String, String> pidToRole = new HashMap<>();
        String globalLatestAppId = "";
        String globalLatestRole = "";

        // Pass 1: collect PID-scoped and global channel/account hints across the entire log.
        for (String line : lines) {
            String pid = extractPid(line);
            String lineAppId = extractLineAppId(line);
            if (!lineAppId.isEmpty()) {
                globalLatestAppId = lineAppId;
                if (!pid.isEmpty()) pidToAppId.put(pid, lineAppId);
            }
            String lineUserId = extractLineUserId(line);
            if (!lineUserId.isEmpty() && !pid.isEmpty()) {
                pidToUserId.put(pid, lineUserId);
            }
            String lineRole = extractRoleSummary(line);
            if (!lineRole.isEmpty()) {
                globalLatestRole = lineRole;
                if (!pid.isEmpty()) pidToRole.put(pid, lineRole);
            }
        }

        // Pass 2: extract tokens (okhttp accessToken, LoginSDK token, userSqlEntity token) and correlate appId.
        String normFallback = fallbackAppId == null ? "" : fallbackAppId.trim();
        Map<String, ChannelToken> dedupByChannel = new LinkedHashMap<>();
        Map<String, String> pidForwardAppId = new HashMap<>();
        Map<String, String> pidForwardUserId = new HashMap<>();
        String recentAppId = "";
        String recentUserId = "";

        for (String line : lines) {
            String pid = extractPid(line);
            String lineAppId = extractLineAppId(line);
            if (!lineAppId.isEmpty()) {
                recentAppId = lineAppId;
                if (!pid.isEmpty()) pidForwardAppId.put(pid, lineAppId);
            }
            String lineUserId = extractLineUserId(line);
            if (!lineUserId.isEmpty()) {
                recentUserId = lineUserId;
                if (!pid.isEmpty()) pidForwardUserId.put(pid, lineUserId);
            }
            String lineRole = extractRoleSummary(line);

            List<String> tokensOnLine = extractTokensFromLine(line);
            if (tokensOnLine.isEmpty()) continue;

            String effectiveAppId = firstNonEmpty(
                lineAppId,
                pid.isEmpty() ? "" : pidForwardAppId.get(pid),
                pid.isEmpty() ? "" : pidToAppId.get(pid),
                recentAppId,
                globalLatestAppId,
                normFallback,
                "110001937"
            );
            String effectiveRole = firstNonEmpty(
                lineRole,
                pid.isEmpty() ? "" : pidToRole.get(pid),
                pid.isEmpty() ? globalLatestRole : ""
            );
            String effectiveUserId = !effectiveRole.isEmpty()
                ? effectiveRole
                : firstNonEmpty(
                    lineUserId,
                    pid.isEmpty() ? "" : pidForwardUserId.get(pid),
                    pid.isEmpty() ? "" : pidToUserId.get(pid),
                    pid.isEmpty() ? recentUserId : ""
                );

            for (String tok : tokensOnLine) {
                ChannelToken parsed = classifyApp(effectiveAppId, effectiveUserId, tok);
                String key = parsed.platform + ":" + parsed.appId;
                // Remove and re-insert so newest matching line moves to the end, then reverse below.
                dedupByChannel.remove(key);
                dedupByChannel.put(key, parsed);
            }
        }

        List<ChannelToken> values = new ArrayList<>(dedupByChannel.values());
        for (int i = values.size() - 1; i >= 0; i--) {
            ordered.add(values.get(i));
        }
        return ordered;
    }

    private static List<String> extractTokensFromLine(String line) {
        List<String> tokens = new ArrayList<>();
        if (line == null || line.isEmpty()) return tokens;

        Matcher accessMatcher = ACCESS_TOKEN_PATTERN.matcher(line);
        while (accessMatcher.find()) {
            addIfValidToken(tokens, firstNonEmpty(accessMatcher.group(1), accessMatcher.group(2)));
        }
        Matcher sdkMatcher = LOGIN_SDK_TOKEN_PATTERN.matcher(line);
        while (sdkMatcher.find()) {
            addIfValidToken(tokens, sdkMatcher.group(1));
        }
        if (tokens.isEmpty() && isSqlOrMemoryTokenLine(line)) {
            Matcher sqlMatcher = SQL_ENTITY_TOKEN_PATTERN.matcher(line);
            while (sqlMatcher.find()) {
                addIfValidToken(tokens, sqlMatcher.group(1));
            }
        }
        return tokens;
    }

    private static boolean isSqlOrMemoryTokenLine(String line) {
        return line.contains("userSqlEntity")
            || line.contains("Memory login data")
            || line.contains("update from username")
            || line.contains("authorize_code");
    }

    private static void addIfValidToken(List<String> tokens, String tok) {
        if (tok == null || tok.isEmpty() || tok.endsWith("_sh")) return;
        tokens.add(tok);
    }

    private static String extractPid(String line) {
        if (line == null || line.isEmpty()) return "";
        Matcher brief = BRIEF_PID_PATTERN.matcher(line);
        if (brief.find()) return brief.group(1);
        Matcher threadtime = THREADTIME_PID_PATTERN.matcher(line);
        if (threadtime.find()) return threadtime.group(1);
        return "";
    }

    private static String extractLineAppId(String line) {
        if (line == null || line.isEmpty()) return "";
        Matcher m = APP_ID_PATTERN.matcher(line);
        String found = "";
        while (m.find()) {
            String candidate = firstNonEmpty(m.group(1), m.group(2));
            if (!candidate.isEmpty()) found = candidate;
        }
        if (!found.isEmpty()) return found;
        return inferAppIdFromPackage(line);
    }

    private static String extractLineUserId(String line) {
        if (line == null || line.isEmpty()) return "";
        Matcher m = USER_ID_PATTERN.matcher(line);
        String found = "";
        while (m.find()) {
            String candidate = m.group(1);
            if (candidate != null && !candidate.isEmpty()) found = candidate;
        }
        return found;
    }

    private static String extractRoleSummary(String line) {
        if (line == null || line.isEmpty()) return "";
        if (!line.contains("RoleInfo") && !line.contains("\"rid\"")) return "";
        Matcher ridMatcher = ROLE_ID_PATTERN.matcher(line);
        String rid = ridMatcher.find() ? ridMatcher.group(1) : "";
        Matcher rnMatcher = ROLE_NAME_PATTERN.matcher(line);
        String rn = "";
        if (rnMatcher.find()) {
            String raw = rnMatcher.group(1);
            while (raw.endsWith("\\")) raw = raw.substring(0, raw.length() - 1);
            rn = unescapeUnicode(raw).trim();
        }
        if (!rn.isEmpty() && !rid.isEmpty()) return rn + " (UID " + rid + ")";
        if (!rid.isEmpty()) return "UID " + rid;
        return "";
    }

    private static String unescapeUnicode(String raw) {
        if (raw == null || !raw.contains("\\u")) return raw == null ? "" : raw;
        StringBuilder out = new StringBuilder(raw.length());
        for (int i = 0; i < raw.length(); i++) {
            char c = raw.charAt(i);
            if (c == '\\' && i + 5 < raw.length() && raw.charAt(i + 1) == 'u') {
                String hex = raw.substring(i + 2, i + 6);
                try {
                    out.append((char) Integer.parseInt(hex, 16));
                    i += 5;
                    continue;
                } catch (NumberFormatException ignored) { }
            }
            out.append(c);
        }
        return out.toString();
    }

    private static String firstNonEmpty(String... values) {
        if (values == null) return "";
        for (String v : values) {
            if (v != null && !v.isEmpty()) return v;
        }
        return "";
    }

    static LoginLinkParams parseLoginLink(String raw) throws IOException {
        String trimmed = raw == null ? "" : raw.trim();
        String compactPrefix = PUBLIC_ORIGIN + "/login?token=";
        if (trimmed.startsWith(compactPrefix)) {
            String pageToken = trimmed.substring(compactPrefix.length());
            if (!pageToken.matches("[A-Za-z0-9_-]{43}")) throw new IOException("请复制完整的星枢登录链接");
            return new LoginLinkParams("", pageToken);
        }
        String prefix = PUBLIC_ORIGIN + "/login#";
        String fragment = trimmed.startsWith(prefix) ? trimmed.substring(prefix.length()) : trimmed;
        if (trimmed.toLowerCase(Locale.ROOT).startsWith("http") && !trimmed.startsWith(prefix)) {
            throw new IOException("仅支持星枢官方授权链接（https://124.221.176.134/login#...）");
        }
        Matcher m = LOGIN_FRAGMENT_PATTERN.matcher(fragment);
        if (!m.matches()) {
            throw new IOException("请粘贴完整的 QQ Bot 登录链接（或 # 后的授权参数）");
        }
        return new LoginLinkParams(m.group(1), m.group(2));
    }
}
