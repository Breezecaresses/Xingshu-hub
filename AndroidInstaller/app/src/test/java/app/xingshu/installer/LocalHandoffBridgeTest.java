package app.xingshu.installer;

import org.junit.Test;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import static org.junit.Assert.*;

public final class LocalHandoffBridgeTest {
    @Test public void buildJsonAndServeSingleUseLoopbackHandoff() throws Exception {
        String token = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";
        assertEquals(43, token.length());
        String json = LocalHandoffBridge.buildHandoffJson(token, "1556514");
        Map<String, Object> parsed = MiniJson.parseObject(json);
        assertEquals(token, parsed.get("handoff"));
        assertEquals("1556514", parsed.get("uid"));

        // Invalid inputs should not crash or start serving anything.
        LocalHandoffBridge.publish("short", "1556514");
        LocalHandoffBridge.publish(token, "0");

        LocalHandoffBridge.publish(token, "1556514");
        HttpURLConnection conn = (HttpURLConnection) new URL("http://127.0.0.1:" + LocalHandoffBridge.PORT + "/handoff").openConnection();
        conn.setConnectTimeout(2000);
        conn.setReadTimeout(2000);
        assertEquals(200, conn.getResponseCode());
        try (InputStream in = conn.getInputStream()) {
            String body = new String(in.readAllBytes(), StandardCharsets.UTF_8);
            Map<String, Object> served = MiniJson.parseObject(body);
            assertEquals(token, served.get("handoff"));
            assertEquals("1556514", served.get("uid"));
        }

        // Single-use: subsequent fetch must not return the token again.
        try {
            HttpURLConnection second = (HttpURLConnection) new URL("http://127.0.0.1:" + LocalHandoffBridge.PORT + "/handoff").openConnection();
            second.setConnectTimeout(1000);
            second.setReadTimeout(1000);
            int code = second.getResponseCode();
            assertEquals(404, code);
        } catch (java.io.IOException closedAfterConsume) {
            // Also acceptable once the accept loop shuts down cleanly after single-use consumption.
        }
    }
}
