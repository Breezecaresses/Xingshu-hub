package app.xingshu.installer;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Ephemeral loopback-only (127.0.0.1:47821) handoff bridge between Mobile Assistant and the game.
 * Exposes ONLY single-use {handoff, uid} (never parent session tokens) so Android SplashScreen
 * activities that strip launch Intent extras cannot break automatic account sync.
 */
final class LocalHandoffBridge {
    static final int PORT = 47821;
    private static final long TTL_MS = 120_000L;

    private static final Object LOCK = new Object();
    private static String pendingHandoff = "";
    private static String pendingUid = "";
    private static long expiresAtMs = 0L;
    private static ServerSocket serverSocket;
    private static Thread acceptThread;

    private LocalHandoffBridge() { }

    static void publish(String handoff, String uid) {
        if (handoff == null || !handoff.matches("[A-Za-z0-9_-]{43}") || uid == null || !uid.matches("[1-9][0-9]{0,18}")) {
            return;
        }
        synchronized (LOCK) {
            pendingHandoff = handoff;
            pendingUid = uid;
            expiresAtMs = System.currentTimeMillis() + TTL_MS;
            ensureStartedLocked();
        }
    }

    static String buildHandoffJson(String handoff, String uid) {
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("handoff", handoff);
        body.put("uid", uid);
        return MiniJson.stringify(body);
    }

    private static void ensureStartedLocked() {
        if (serverSocket != null && !serverSocket.isClosed() && acceptThread != null && acceptThread.isAlive()) {
            return;
        }
        try {
            InetAddress loopback = InetAddress.getByAddress(new byte[] { 127, 0, 0, 1 });
            ServerSocket sock = new ServerSocket(PORT, 8, loopback);
            sock.setSoTimeout(5000);
            serverSocket = sock;
            Thread t = new Thread(() -> runLoop(sock), "xingshu-handoff-bridge");
            t.setDaemon(true);
            acceptThread = t;
            t.start();
        } catch (Exception ignored) { }
    }

    private static void runLoop(ServerSocket sock) {
        try {
            while (!sock.isClosed()) {
                synchronized (LOCK) {
                    if (pendingHandoff.isEmpty() || System.currentTimeMillis() >= expiresAtMs) {
                        pendingHandoff = "";
                        pendingUid = "";
                        break;
                    }
                }
                try {
                    Socket client = sock.accept();
                    handleClient(client);
                } catch (java.net.SocketTimeoutException ignored) {
                    // Recheck expiry on loop condition.
                }
            }
        } catch (Exception ignored) {
        } finally {
            synchronized (LOCK) {
                if (serverSocket == sock) serverSocket = null;
                try { sock.close(); } catch (Exception ignored) { }
            }
        }
    }

    private static void handleClient(Socket client) {
        try (Socket s = client) {
            s.setSoTimeout(2000);
            BufferedReader reader = new BufferedReader(new InputStreamReader(s.getInputStream(), StandardCharsets.US_ASCII));
            String reqLine = reader.readLine();
            if (reqLine == null || !reqLine.startsWith("GET /handoff")) {
                writeResponse(s.getOutputStream(), 404, "{\"status\":\"not_found\"}");
                return;
            }
            String json = null;
            synchronized (LOCK) {
                if (!pendingHandoff.isEmpty() && System.currentTimeMillis() < expiresAtMs) {
                    json = buildHandoffJson(pendingHandoff, pendingUid);
                    pendingHandoff = "";
                    pendingUid = "";
                    expiresAtMs = 0L;
                }
            }
            if (json == null) {
                writeResponse(s.getOutputStream(), 404, "{\"status\":\"empty\"}");
            } else {
                writeResponse(s.getOutputStream(), 200, json);
            }
        } catch (Exception ignored) { }
    }

    private static void writeResponse(OutputStream out, int code, String body) throws Exception {
        byte[] payload = body.getBytes(StandardCharsets.UTF_8);
        String reason = code == 200 ? "OK" : "Not Found";
        String headers = "HTTP/1.1 " + code + " " + reason + "\r\n"
            + "Content-Type: application/json; charset=utf-8\r\n"
            + "Content-Length: " + payload.length + "\r\n"
            + "Cache-Control: no-store\r\n"
            + "Connection: close\r\n\r\n";
        out.write(headers.getBytes(StandardCharsets.US_ASCII));
        out.write(payload);
        out.flush();
    }
}
