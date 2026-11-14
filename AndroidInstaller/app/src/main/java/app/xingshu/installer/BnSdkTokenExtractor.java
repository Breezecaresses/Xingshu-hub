package app.xingshu.installer;

import java.io.IOException;
import java.util.ArrayList;
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
        "(?i)(?:\\\\*\"?(?:appid|app_id)\\\\*\"?\\s*[:=]\\s*\\\\*\"?)([0-9]{6,16})"
    );
    private static final Pattern USER_ID_PATTERN = Pattern.compile(
        "(?i)(?:\\\\*\"?(?:userid|user_id|uid)\\\\*\"?\\s*[:=]\\s*\\\\*\"?)([A-Za-z0-9_.-]{1,64})"
    );
    private static final Pattern TOKEN_FIELD_PATTERN = Pattern.compile(
        "(?i)(?:accesstoken|access_token)\\\\*\"?\\s*:\\s*\\\\*\"([A-Za-z0-9_.=-]{40,4096})\\\\*\""
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
            if (!userId.isEmpty()) sb.append(" · 渠道号 ").append(userId);
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

    static List<ChannelToken> extractFromLogcat(String logcatText) {
        List<ChannelToken> ordered = new ArrayList<>();
        if (logcatText == null || logcatText.isEmpty()) return ordered;

        Map<String, ChannelToken> dedupByChannel = new LinkedHashMap<>();
        String recentAppId = "";
        String recentUserId = "";
        for (String line : logcatText.split("[\\r\\n]+")) {
            Matcher appMatcher = APP_ID_PATTERN.matcher(line);
            String lineAppId = "";
            while (appMatcher.find()) {
                lineAppId = appMatcher.group(1);
            }
            if (!lineAppId.isEmpty()) recentAppId = lineAppId;

            Matcher userMatcher = USER_ID_PATTERN.matcher(line);
            String lineUserId = "";
            while (userMatcher.find()) {
                lineUserId = userMatcher.group(1);
            }
            if (!lineUserId.isEmpty()) recentUserId = lineUserId;

            Matcher tokMatcher = TOKEN_FIELD_PATTERN.matcher(line);
            while (tokMatcher.find()) {
                String tok = tokMatcher.group(1);
                if (tok.endsWith("_sh")) continue;
                String effectiveAppId = !lineAppId.isEmpty() ? lineAppId : recentAppId;
                String effectiveUserId = !lineUserId.isEmpty() ? lineUserId : recentUserId;
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

    static LoginLinkParams parseLoginLink(String raw) throws IOException {
        String trimmed = raw == null ? "" : raw.trim();
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
