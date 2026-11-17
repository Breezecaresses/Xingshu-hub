package app.xingshu.installer;

import java.io.IOException;
import java.net.URI;

/** Only a short-lived server handoff can enter the QQ quick-login flow. */
final class AssistantLoginLink {
    static String parse(String raw, String extra) throws IOException {
        try {
            URI uri = new URI(raw == null ? "" : raw);
            if (!"xingshu-installer".equals(uri.getScheme()) || !"qq-login".equals(uri.getHost())
                    || uri.getUserInfo() != null || uri.getPort() != -1 || uri.getRawQuery() != null
                    || !(uri.getPath().isEmpty() || "/".equals(uri.getPath()))) throw new IOException();
            String fragment = uri.getRawFragment();
            String token = extra == null || extra.isEmpty() ? fragment : extra;
            if (fragment != null && !fragment.isEmpty() && !fragment.equals(token)) throw new IOException();
            if (token == null || !token.matches("[A-Za-z0-9_-]{43}")) throw new IOException();
            return token;
        } catch (Exception ignored) {
            throw new IOException("登录链接不完整，请回到 QQ 重新点击「趴登录」链接。");
        }
    }

    private AssistantLoginLink() { }
}
