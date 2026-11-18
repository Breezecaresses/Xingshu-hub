package app.xingshu.installer;

import android.content.SharedPreferences;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.Base64;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import javax.net.ssl.HttpsURLConnection;

/**
 * Mobile auth & account hub client talking exclusively to https://124.221.176.134.
 * Supports: (1) binding a scanned BnSdk channel token directly to a QQ Bot /login link,
 * (2) PKCE channel or Phone/Steam web authorization into the Android Hub, and
 * (3) inspecting synced snapshot summaries without storing raw game credentials.
 */
final class MobileAuthHub {
    static final String ORIGIN = "https://124.221.176.134";
    private static final String PREF_DEVICE_ID = "mobile_hub_device_id";
    private static final String PREF_SESSION_TOKEN = "mobile_hub_token";
    private static final String PREF_UID = "mobile_hub_uid";
    private static final String PREF_NICK = "mobile_hub_nick";
    private static final String PREF_PLATFORM = "mobile_hub_platform";
    private static final String PREF_PENDING_WEB_REQUEST_ID = "mobile_hub_pending_web_request_id";
    private static final String PREF_PENDING_WEB_VERIFIER = "mobile_hub_pending_web_verifier";
    private static final String PREF_PENDING_WEB_DEADLINE_MS = "mobile_hub_pending_web_deadline_ms";

    static final class PendingWebLogin {
        final String requestId;
        final String verifier;
        final long deadlineEpochMs;

        PendingWebLogin(String requestId, String verifier, long deadlineEpochMs) {
            this.requestId = requestId;
            this.verifier = verifier;
            this.deadlineEpochMs = deadlineEpochMs;
        }
    }

    static final class BoundAccount {
        final String token;
        final String uid;
        final String nick;
        final String platform;

        BoundAccount(String token, String uid, String nick, String platform) {
            this.token = token == null ? "" : token;
            this.uid = uid == null ? "" : uid;
            this.nick = nick == null ? "" : nick;
            this.platform = platform == null ? "" : platform;
        }
    }

    static final class DeviceStart {
        final String requestId;
        final String verifier;
        final String loginUrl;

        DeviceStart(String requestId, String verifier, String loginUrl) {
            this.requestId = requestId;
            this.verifier = verifier;
            this.loginUrl = loginUrl;
        }
    }

    static final class PollOutcome {
        final String status;
        final BoundAccount account;
        final String error;

        PollOutcome(String status, BoundAccount account, String error) {
            this.status = status;
            this.account = account;
            this.error = error;
        }
    }

    private static final class HttpReply {
        final int code;
        final Map<String, Object> body;
        final String xingshuCookie;

        HttpReply(int code, Map<String, Object> body, String xingshuCookie) {
            this.code = code;
            this.body = body;
            this.xingshuCookie = xingshuCookie;
        }
    }

    private MobileAuthHub() { }

    static String getOrCreateDeviceId(SharedPreferences prefs) {
        String existing = prefs.getString(PREF_DEVICE_ID, "");
        if (existing != null && existing.matches("[A-Za-z0-9_-]{16,64}")) return existing;
        String created = UUID.randomUUID().toString().replace("-", "");
        prefs.edit().putString(PREF_DEVICE_ID, created).apply();
        return created;
    }

    static BoundAccount loadSession(SharedPreferences prefs) {
        String tok = prefs.getString(PREF_SESSION_TOKEN, "");
        String uid = prefs.getString(PREF_UID, "");
        if (tok == null || !tok.matches("[A-Za-z0-9_-]{32,128}") || uid == null || uid.isEmpty()) return null;
        return new BoundAccount(tok, uid, prefs.getString(PREF_NICK, ""), prefs.getString(PREF_PLATFORM, ""));
    }

    static void saveSession(SharedPreferences prefs, BoundAccount account) {
        prefs.edit()
            .putString(PREF_SESSION_TOKEN, account.token)
            .putString(PREF_UID, account.uid)
            .putString(PREF_NICK, account.nick)
            .putString(PREF_PLATFORM, account.platform)
            .apply();
    }

    static void clearSession(SharedPreferences prefs) {
        prefs.edit()
            .remove(PREF_SESSION_TOKEN)
            .remove(PREF_UID)
            .remove(PREF_NICK)
            .remove(PREF_PLATFORM)
            .apply();
    }

    static void savePendingWebLogin(SharedPreferences prefs, DeviceStart start, long deadlineEpochMs) {
        if (prefs == null || start == null || start.requestId == null || start.requestId.isEmpty()
                || start.verifier == null || start.verifier.isEmpty()) {
            return;
        }
        prefs.edit()
            .putString(PREF_PENDING_WEB_REQUEST_ID, start.requestId)
            .putString(PREF_PENDING_WEB_VERIFIER, start.verifier)
            .putLong(PREF_PENDING_WEB_DEADLINE_MS, deadlineEpochMs)
            .apply();
    }

    static PendingWebLogin loadPendingWebLogin(SharedPreferences prefs, long nowEpochMs) {
        if (prefs == null) return null;
        String rid = prefs.getString(PREF_PENDING_WEB_REQUEST_ID, "");
        String verifier = prefs.getString(PREF_PENDING_WEB_VERIFIER, "");
        long deadline = prefs.getLong(PREF_PENDING_WEB_DEADLINE_MS, 0L);
        if (rid == null || !rid.matches("[A-Za-z0-9_-]{32}")
                || verifier == null || !verifier.matches("[A-Za-z0-9._~-]{43,128}")
                || deadline <= nowEpochMs) {
            clearPendingWebLogin(prefs);
            return null;
        }
        return new PendingWebLogin(rid, verifier, deadline);
    }

    static void clearPendingWebLogin(SharedPreferences prefs) {
        if (prefs == null) return;
        prefs.edit()
            .remove(PREF_PENDING_WEB_REQUEST_ID)
            .remove(PREF_PENDING_WEB_VERIFIER)
            .remove(PREF_PENDING_WEB_DEADLINE_MS)
            .apply();
    }

    static BoundAccount completeQqLinkWithChannelToken(String linkInput, BnSdkTokenExtractor.ChannelToken token)
            throws Exception {
        if (token == null || !token.nativeSupported) {
            throw new IOException("该渠道（" + (token == null ? "未知" : token.label) + "）不支持 m-sdk 离线授权；请安装插件后进游戏用「趴绑定」");
        }
        BnSdkTokenExtractor.LoginLinkParams link = BnSdkTokenExtractor.parseLoginLink(linkInput);

        Map<String, Object> claimBody = new LinkedHashMap<>();
        claimBody.put("request_id", link.requestId);
        claimBody.put("page_token", link.pageToken);
        HttpReply claim = postJson("/auth/page/claim", claimBody, ORIGIN, null, null, null, 20000);
        if (claim.code != 200) {
            throw new IOException(extractError(claim, "QQ 授权链接已过期或已被使用，请回到 QQ 重新发送 趴登录"));
        }
        String csrf = MiniJson.stringField(claim.body, "csrf");
        if (csrf.isEmpty() || claim.xingshuCookie == null || claim.xingshuCookie.isEmpty()) {
            throw new IOException("服务器未返回完整授权会话，请重试");
        }

        Map<String, Object> loginBody = new LinkedHashMap<>();
        loginBody.put("platform", token.platform);
        loginBody.put("app_id", token.appId);
        loginBody.put("sdk_token", token.sdkToken);
        HttpReply finish = postJson("/auth/page/channel", loginBody, ORIGIN,
            "xingshu_auth=" + claim.xingshuCookie, csrf, null, 65000);
        if (finish.code != 200) {
            throw new IOException(extractError(finish, "渠道票据验证未通过"));
        }
        Map<String, Object> acct = MiniJson.objectField(finish.body, "account");
        String uid = MiniJson.stringField(acct, "uid");
        String nick = MiniJson.stringField(acct, "nick");
        if (uid.isEmpty()) throw new IOException("未能识别游戏账号 UID");
        return new BoundAccount("", uid, nick, token.platform);
    }

    static BoundAccount completeQqHandoff(String handoff, BnSdkTokenExtractor.ChannelToken token) throws Exception {
        return completeQqHandoff(handoff, token, null);
    }

    static BoundAccount completeQqHandoff(String handoff, BnSdkTokenExtractor.ChannelToken token, String deviceId) throws Exception {
        if (handoff == null || !handoff.matches("[A-Za-z0-9_-]{43}") || token == null || !token.nativeSupported) {
            throw new IOException("请从 QQ 登录网页重新打开手机助手，或使用游戏内「趴绑定」。");
        }
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("handoff", handoff);
        body.put("platform", token.platform);
        body.put("app_id", token.appId);
        body.put("sdk_token", token.sdkToken);
        body.put("confirm_account", Boolean.TRUE);
        String[] pkce = deviceId == null ? null : generatePkce();
        if (pkce != null) {
            body.put("device_id", deviceId);
            body.put("code_challenge", pkce[1]);
        }
        HttpReply reply = postJson("/auth/page/assistant/complete", body, ORIGIN, null, null, null, 90000);
        if (reply.code != 200) throw new IOException(extractError(reply, "本次快捷登录未完成，请返回网页重试。"));
        Map<String, Object> account = MiniJson.objectField(reply.body, "account");
        String uid = MiniJson.stringField(account, "uid");
        if (uid.isEmpty()) throw new IOException("未取得游戏账号，请返回网页重新登录。");
        if (pkce != null) {
            String requestId = MiniJson.stringField(MiniJson.objectField(reply.body, "device_login"), "request_id");
            PollOutcome own = pollDeviceLogin(requestId, pkce[0]);
            if (!"succeeded".equals(own.status) || own.account == null || !uid.equals(own.account.uid)) {
                throw new IOException("QQ 已绑定，请点击登录星枢恢复助手会话");
            }
            return new BoundAccount(own.account.token, uid, own.account.nick, token.platform);
        }
        return new BoundAccount("", uid, MiniJson.stringField(account, "nick"), token.platform);
    }

    static BoundAccount bindCurrentSessionToQq(String handoff, BoundAccount account) throws Exception {
        if (account == null || account.token.isEmpty()) throw new IOException("请先登录星枢");
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("handoff", handoff);
        body.put("confirm_account", Boolean.TRUE);
        HttpReply reply = postJson("/auth/assistant/session", body, ORIGIN, null, null, account.token, 20000);
        if (reply.code == 401) throw new IOException("UNAUTHORIZED");
        if (reply.code != 200) throw new IOException(extractError(reply, "QQ 账号绑定未完成"));
        Map<String, Object> value = MiniJson.objectField(reply.body, "account");
        if (!account.uid.equals(MiniJson.stringField(value, "uid"))) throw new IOException("账号身份核对未通过");
        return new BoundAccount(account.token, account.uid, MiniJson.stringField(value, "nick"), account.platform);
    }

    static final class GameHandoff {
        final String token, uid;
        GameHandoff(String token, String uid) { this.token = token; this.uid = uid; }
    }

    static GameHandoff prepareGameHandoff(BoundAccount account) throws Exception {
        HttpReply reply = postJson("/auth/mobile/handoff", new LinkedHashMap<String, Object>(), null, null, null, account.token, 20000);
        if (reply.code == 401) throw new IOException("UNAUTHORIZED");
        if (reply.code != 200) throw new IOException(extractError(reply, "游戏账号同步暂未完成"));
        String token = MiniJson.stringField(reply.body, "handoff");
        String uid = MiniJson.stringField(MiniJson.objectField(reply.body, "account"), "uid");
        if (!token.matches("[A-Za-z0-9_-]{43}") || !account.uid.equals(uid)) throw new IOException("游戏账号同步身份不一致");
        return new GameHandoff(token, uid);
    }

    static String qqHandoffRequester(String handoff) throws Exception {
        if (handoff == null || !handoff.matches("[A-Za-z0-9_-]{43}")) throw new IOException("请回到 QQ 重新打开登录链接。");
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("handoff", handoff);
        HttpReply reply = postJson("/auth/page/assistant/info", body, ORIGIN, null, null, null, 20000);
        if (reply.code != 200) throw new IOException(extractError(reply, "快捷登录已过期，请返回网页重试。"));
        if (!"qq".equals(MiniJson.stringField(reply.body, "purpose"))) throw new IOException("请从 QQ 的趴登录页面打开助手。");
        String requester = MiniJson.stringField(reply.body, "requester");
        return requester.isEmpty() ? "本次 QQ 登录请求" : requester;
    }

    static DeviceStart startChannelHubLogin(String deviceId, BnSdkTokenExtractor.ChannelToken token) throws Exception {
        if (token == null || !token.nativeSupported) {
            throw new IOException("当前检测到的渠道暂不支持直接云同步验证，请使用游戏内「趴绑定」");
        }
        String[] pkce = generatePkce();
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("device_id", deviceId);
        body.put("code_challenge", pkce[1]);
        body.put("platform", token.platform);
        body.put("app_id", token.appId);
        body.put("game_sdk_token", token.sdkToken);
        HttpReply reply = postJson("/auth/channel/device", body, null, null, null, null, 20000);
        if (reply.code != 200) {
            throw new IOException(extractError(reply, "发起渠道授权失败"));
        }
        String requestId = MiniJson.stringField(reply.body, "request_id");
        if (requestId.isEmpty()) throw new IOException("服务器未返回授权请求编号");
        return new DeviceStart(requestId, pkce[0], "");
    }

    static DeviceStart startWebDeviceLogin(String deviceId) throws Exception {
        String[] pkce = generatePkce();
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("device_id", deviceId);
        body.put("code_challenge", pkce[1]);
        body.put("copy_safe", Boolean.TRUE);
        HttpReply reply = requestJsonWithRetry("POST", "/auth/device", body, null, null, null, null, 20000, 1);
        if (reply.code != 200) {
            throw new IOException(extractError(reply, "无法获取星枢官网授权页"));
        }
        String requestId = MiniJson.stringField(reply.body, "request_id");
        String loginUrl = MiniJson.stringField(reply.body, "login_url");
        boolean compact = loginUrl.matches(java.util.regex.Pattern.quote(ORIGIN) + "/login\\?token=[A-Za-z0-9_-]{43}");
        boolean legacy = loginUrl.matches(java.util.regex.Pattern.quote(ORIGIN + "/login#" + requestId + ".") + "[A-Za-z0-9_-]{43}");
        if (!requestId.matches("[A-Za-z0-9_-]{32}") || !(compact || legacy)) {
            throw new IOException("官网授权地址格式无效");
        }
        return new DeviceStart(requestId, pkce[0], loginUrl);
    }

    static PollOutcome pollDeviceLogin(String requestId, String verifier) throws Exception {
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("request_id", requestId);
        body.put("code_verifier", verifier);
        HttpReply reply = requestJsonWithRetry("POST", "/auth/device/poll", body, null, null, null, null, 20000, 1);
        if (reply.code != 200) {
            throw new IOException(extractError(reply, "查询授权状态失败"));
        }
        String status = MiniJson.stringField(reply.body, "status");
        if ("succeeded".equals(status)) {
            String accessToken = MiniJson.stringField(reply.body, "access_token");
            Map<String, Object> acct = MiniJson.objectField(reply.body, "account");
            String uid = MiniJson.stringField(acct, "uid");
            String nick = MiniJson.stringField(acct, "nick");
            String platform = MiniJson.stringField(acct, "platform");
            if (accessToken.isEmpty() || uid.isEmpty()) {
                throw new IOException("授权已确认但缺少身份令牌");
            }
            return new PollOutcome(status, new BoundAccount(accessToken, uid, nick, platform), "");
        }
        String error = MiniJson.stringField(reply.body, "error");
        if (error.isEmpty()) error = MiniJson.stringField(reply.body, "message");
        return new PollOutcome(status, null, error);
    }

    static String fetchAccountSummary(String bearerToken) throws Exception {
        HttpReply reply = requestJsonWithRetry("GET", "/account/data", null, null, null, null, bearerToken, 20000, 1);
        if (reply.code == 401) throw new IOException("UNAUTHORIZED");
        if (reply.code != 200) throw new IOException(extractError(reply, "读取云端资料失败"));
        Map<String, Object> account = MiniJson.objectField(reply.body, "account");
        Map<String, Object> snapshot = MiniJson.objectField(account, "snapshot");
        String name = MiniJson.stringField(snapshot, "name");
        long level = MiniJson.longField(snapshot, "level", 0);
        String observedAt = MiniJson.stringField(account, "observedAt");
        Object rawMatches = snapshot == null ? null : snapshot.get("matches");
        int matchesCount = rawMatches instanceof List<?> ? ((List<?>) rawMatches).size() : 0;
        StringBuilder sb = new StringBuilder();
        if (!name.isEmpty()) sb.append(name).append(" · Lv.").append(level);
        if (matchesCount > 0) sb.append(sb.length() > 0 ? " · " : "").append("已存战绩 ").append(matchesCount).append(" 场");
        if (!observedAt.isEmpty()) sb.append(sb.length() > 0 ? "\n" : "").append("最近云端同步：").append(observedAt);
        return sb.length() > 0 ? sb.toString() : "已连接云端会话（进游戏大厅或发 趴数据 可刷新）";
    }

    static void logout(String bearerToken) {
        if (bearerToken == null || bearerToken.isEmpty()) return;
        try {
            postJson("/auth/logout", new LinkedHashMap<String, Object>(), null, null, null, bearerToken, 10000);
        } catch (Exception ignored) { }
    }

    private static String[] generatePkce() throws Exception {
        byte[] entropy = new byte[32];
        new SecureRandom().nextBytes(entropy);
        Base64.Encoder enc = Base64.getUrlEncoder().withoutPadding();
        String verifier = enc.encodeToString(entropy);
        byte[] digest = MessageDigest.getInstance("SHA-256").digest(verifier.getBytes(StandardCharsets.US_ASCII));
        return new String[] { verifier, enc.encodeToString(digest) };
    }

    private static HttpReply postJson(String path, Map<String, Object> jsonBody, String reqOrigin,
            String cookie, String csrf, String bearer, int readTimeoutMs) throws Exception {
        return requestJson("POST", path, jsonBody, reqOrigin, cookie, csrf, bearer, readTimeoutMs);
    }

    private static HttpReply requestJsonWithRetry(String method, String path, Map<String, Object> jsonBody, String reqOrigin,
            String cookie, String csrf, String bearer, int readTimeoutMs, int maxRetries) throws Exception {
        IOException last = null;
        for (int attempt = 0; attempt <= maxRetries; attempt++) {
            try {
                return requestJson(method, path, jsonBody, reqOrigin, cookie, csrf, bearer, readTimeoutMs);
            } catch (IOException err) {
                last = err;
                if (attempt >= maxRetries) break;
                try {
                    Thread.sleep(250L);
                } catch (InterruptedException ie) {
                    Thread.currentThread().interrupt();
                    throw err;
                }
            }
        }
        throw last;
    }

    private static HttpReply requestJson(String method, String path, Map<String, Object> jsonBody, String reqOrigin,
            String cookie, String csrf, String bearer, int readTimeoutMs) throws Exception {
        URL url = new URL(ORIGIN + path);
        if (!"https".equals(url.getProtocol()) || !"124.221.176.134".equals(url.getHost())) {
            throw new IOException("拒绝访问非官方授权节点");
        }
        HttpsURLConnection conn = (HttpsURLConnection) url.openConnection();
        conn.setInstanceFollowRedirects(false);
        conn.setConnectTimeout(15000);
        conn.setReadTimeout(readTimeoutMs);
        conn.setRequestMethod(method);
        conn.setDoOutput("POST".equals(method));
        conn.setRequestProperty("Content-Type", "application/json; charset=utf-8");
        conn.setRequestProperty("User-Agent", "XingShu-Android-Hub/1");
        conn.setRequestProperty("Connection", "close");
        if (reqOrigin != null) conn.setRequestProperty("Origin", reqOrigin);
        if (cookie != null) conn.setRequestProperty("Cookie", cookie);
        if (csrf != null) conn.setRequestProperty("X-XingShu-CSRF", csrf);
        if (bearer != null) conn.setRequestProperty("Authorization", "Bearer " + bearer);

        try {
            if ("POST".equals(method)) {
                byte[] payload = MiniJson.stringify(jsonBody).getBytes(StandardCharsets.UTF_8);
                try (OutputStream out = conn.getOutputStream()) { out.write(payload); }
            }
            int status = conn.getResponseCode();
            InputStream stream = status >= 400 ? conn.getErrorStream() : conn.getInputStream();
            String raw = readBounded(stream, "GET".equals(method) ? 2097152 : 65536);
            Map<String, Object> parsed = null;
            if (!raw.isEmpty() && raw.trim().startsWith("{")) {
                try { parsed = MiniJson.parseObject(raw); } catch (IOException ignored) { }
            }
            String xingshuCookie = extractCookie(conn.getHeaderFields().get("Set-Cookie"), "xingshu_auth");
            return new HttpReply(status, parsed, xingshuCookie);
        } finally {
            conn.disconnect();
        }
    }

    private static String extractCookie(List<String> headers, String cookieName) {
        if (headers == null) return null;
        String prefix = cookieName + "=";
        for (String h : headers) {
            if (h == null) continue;
            for (String part : h.split(";")) {
                String p = part.trim();
                if (p.startsWith(prefix)) return p.substring(prefix.length());
            }
        }
        return null;
    }

    private static String extractError(HttpReply reply, String fallback) {
        if (reply != null && reply.body != null) {
            String msg = MiniJson.stringField(reply.body, "error");
            if (!msg.isEmpty()) return msg;
        }
        return fallback + (reply == null ? "" : " (HTTP " + reply.code + ")");
    }

    private static String readBounded(InputStream in, int maxBytes) throws IOException {
        if (in == null) return "";
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[4096];
        int total = 0;
        int n;
        while ((n = in.read(buf)) != -1) {
            total += n;
            if (total > maxBytes) break;
            out.write(buf, 0, n);
        }
        return new String(out.toByteArray(), StandardCharsets.UTF_8);
    }
}
