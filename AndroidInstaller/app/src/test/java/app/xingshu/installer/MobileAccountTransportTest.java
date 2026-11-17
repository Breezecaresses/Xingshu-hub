package app.xingshu.installer;

import static org.junit.Assert.*;
import java.io.*;
import java.net.*;
import java.security.Principal;
import java.security.cert.Certificate;
import java.util.*;
import javax.net.ssl.HttpsURLConnection;
import org.junit.BeforeClass;
import org.junit.Test;

public class MobileAccountTransportTest {
    private static FakeConnection latest;
    private static String reply;
    @BeforeClass public static void mockOfficialNode() {
        URL.setURLStreamHandlerFactory(protocol -> !"https".equals(protocol) ? null : new URLStreamHandler() {
            @Override protected URLConnection openConnection(URL url) {
                latest = new FakeConnection(url);
                return latest;
            }
        });
    }
    @Test public void cloudAccountUsesGetAndCurrentBearer() throws Exception {
        reply="{\"account\":{\"observedAt\":\"2026-11-17\",\"snapshot\":{\"name\":\"Test player\",\"level\":3,\"matches\":[{}]}}}";
        String summary=MobileAuthHub.fetchAccountSummary("TEST-DEVICE-SESSION");
        assertEquals("GET",latest.getRequestMethod());
        assertFalse(latest.getDoOutput());
        assertEquals("Bearer TEST-DEVICE-SESSION",latest.getRequestProperty("Authorization"));
        assertEquals("/account/data",latest.getURL().getPath());
        assertTrue(summary.contains("Test player"));
        assertTrue(summary.contains("1"));
    }
    @Test public void gameReceivesOnlyScopedOneUseCapabilityAndMatchingUid() throws Exception {
        String cap="H".repeat(43);
        reply="{\"handoff\":\""+cap+"\",\"account\":{\"uid\":\"123\"}}";
        MobileAuthHub.GameHandoff handoff=MobileAuthHub.prepareGameHandoff(new MobileAuthHub.BoundAccount("TEST-PARENT-SESSION","123","test","TAPTAP"));
        assertEquals(cap,handoff.token);
        assertEquals("123",handoff.uid);
        assertFalse(latest.sent.toString("UTF-8").contains("TEST-PARENT-SESSION"));
        assertEquals("POST",latest.getRequestMethod());
        reply="{\"handoff\":\""+cap+"\",\"account\":{\"uid\":\"456\"}}";
        try {
            MobileAuthHub.prepareGameHandoff(new MobileAuthHub.BoundAccount("TEST-PARENT-SESSION","123","test","TAPTAP"));
            fail("mismatched UID must not reach a game intent");
        } catch (IOException expected) { }
    }
    @Test public void currentSessionQqBindConfirmsAccountWithoutResendingPassword() throws Exception {
        reply="{\"account\":{\"uid\":\"123\",\"nick\":\"test\"}}";
        MobileAuthHub.BoundAccount result=MobileAuthHub.bindCurrentSessionToQq("H".repeat(43),new MobileAuthHub.BoundAccount("TEST-PARENT-SESSION","123","test","TAPTAP"));
        assertEquals("TEST-PARENT-SESSION",result.token);
        assertEquals("/auth/assistant/session",latest.getURL().getPath());
        assertTrue(latest.sent.toString("UTF-8").contains("\"confirm_account\":true"));
        assertFalse(latest.sent.toString("UTF-8").contains("password"));
    }
    @Test public void browserLoginAcceptsCopySafeLinksAndKeepsLegacyCompatibility() throws Exception {
        String rid="R".repeat(32), token="T".repeat(43);
        String url=MobileAuthHub.ORIGIN+"/login?token="+token;
        reply="{\"request_id\":\""+rid+"\",\"login_url\":\""+url+"\"}";
        assertEquals(url,MobileAuthHub.startWebDeviceLogin("TEST-DEVICE-123456").loginUrl);
        assertTrue(latest.sent.toString("UTF-8").contains("\"copy_safe\":true"));
        reply="{\"request_id\":\""+rid+"\",\"login_url\":\""+MobileAuthHub.ORIGIN+"/login#"+rid+"."+token+"\"}";
        assertTrue(MobileAuthHub.startWebDeviceLogin("TEST-DEVICE-123456").loginUrl.contains("#"));
        for (String bad : new String[]{"https://124.221.176.134.evil.test/login?token="+token,url+"&token="+token}) {
            reply="{\"request_id\":\""+rid+"\",\"login_url\":\""+bad+"\"}";
            try { MobileAuthHub.startWebDeviceLogin("TEST-DEVICE-123456"); fail("foreign or ambiguous login URL accepted"); }
            catch (IOException expected) { }
        }
    }
    @Test public void copiedQqQueryLinkCanStillUseManualTokenFallback() throws Exception {
        String token="T".repeat(43);
        BnSdkTokenExtractor.LoginLinkParams result=BnSdkTokenExtractor.parseLoginLink(MobileAuthHub.ORIGIN+"/login?token="+token);
        assertEquals("",result.requestId);
        assertEquals(token,result.pageToken);
        try { BnSdkTokenExtractor.parseLoginLink(MobileAuthHub.ORIGIN+"/login?token="+token+"&token="+token); fail("duplicate token"); }
        catch (IOException expected) { }
    }
    private static class FakeConnection extends HttpsURLConnection {
        final ByteArrayOutputStream sent=new ByteArrayOutputStream();
        FakeConnection(URL url) { super(url); }
        @Override public void disconnect() { }
        @Override public boolean usingProxy() { return false; }
        @Override public void connect() { }
        @Override public int getResponseCode() { return 200; }
        @Override public InputStream getInputStream() throws IOException { return new ByteArrayInputStream(reply.getBytes("UTF-8")); }
        @Override public OutputStream getOutputStream() { return sent; }
        @Override public Map<String,List<String>> getHeaderFields() { return Collections.emptyMap(); }
        @Override public String getCipherSuite() { return "ISOLATED-TEST"; }
        @Override public Certificate[] getLocalCertificates() { return new Certificate[0]; }
        @Override public Certificate[] getServerCertificates() { return new Certificate[0]; }
        @Override public Principal getPeerPrincipal() { return null; }
        @Override public Principal getLocalPrincipal() { return null; }
    }
}
