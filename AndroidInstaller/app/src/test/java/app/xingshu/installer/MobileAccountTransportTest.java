package app.xingshu.installer;

import static org.junit.Assert.*;
import android.content.SharedPreferences;
import java.io.*;
import java.net.*;
import java.security.Principal;
import java.security.cert.Certificate;
import java.util.*;
import javax.net.ssl.HttpsURLConnection;
import org.junit.Before;
import org.junit.BeforeClass;
import org.junit.Test;

public class MobileAccountTransportTest {
    private static FakeConnection latest;
    private static String reply;
    private static int failNextAttempts;
    @BeforeClass public static void mockOfficialNode() {
        URL.setURLStreamHandlerFactory(protocol -> !"https".equals(protocol) ? null : new URLStreamHandler() {
            @Override protected URLConnection openConnection(URL url) {
                latest = new FakeConnection(url);
                return latest;
            }
        });
    }
    @Before public void resetMock() {
        failNextAttempts = 0;
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
    @Test public void pollRetriesTransientConnectFailureReadsMessageAndUsesConnectionClose() throws Exception {
        String rid="R".repeat(32), verifier="V".repeat(43);
        failNextAttempts=1;
        reply="{\"status\":\"failed\",\"message\":\"渠道授权未完成，请重试\"}";
        MobileAuthHub.PollOutcome outcome=MobileAuthHub.pollDeviceLogin(rid,verifier);
        assertEquals("failed",outcome.status);
        assertEquals("渠道授权未完成，请重试",outcome.error);
        assertEquals("close",latest.getRequestProperty("Connection"));
    }
    @Test public void pendingWebLoginSurvivesUntilExpiryAndClears() {
        InMemoryPrefs prefs=new InMemoryPrefs();
        String rid="R".repeat(32), verifier="V".repeat(43);
        MobileAuthHub.DeviceStart start=new MobileAuthHub.DeviceStart(rid,verifier,MobileAuthHub.ORIGIN+"/login?token="+"T".repeat(43));
        MobileAuthHub.savePendingWebLogin(prefs,start,2000L);
        MobileAuthHub.PendingWebLogin loaded=MobileAuthHub.loadPendingWebLogin(prefs,1000L);
        assertNotNull(loaded);
        assertEquals(rid,loaded.requestId);
        assertEquals(verifier,loaded.verifier);
        assertNull(MobileAuthHub.loadPendingWebLogin(prefs,2000L));
        MobileAuthHub.savePendingWebLogin(prefs,start,5000L);
        MobileAuthHub.clearPendingWebLogin(prefs);
        assertNull(MobileAuthHub.loadPendingWebLogin(prefs,1000L));
    }
    private static class FakeConnection extends HttpsURLConnection {
        final ByteArrayOutputStream sent=new ByteArrayOutputStream();
        FakeConnection(URL url) { super(url); }
        @Override public void disconnect() { }
        @Override public boolean usingProxy() { return false; }
        @Override public void connect() { }
        @Override public int getResponseCode() throws IOException {
            if (failNextAttempts > 0) {
                failNextAttempts--;
                throw new ConnectException("Failed to connect to /124.221.176.134:443");
            }
            return 200;
        }
        @Override public InputStream getInputStream() throws IOException { return new ByteArrayInputStream(reply.getBytes("UTF-8")); }
        @Override public OutputStream getOutputStream() { return sent; }
        @Override public Map<String,List<String>> getHeaderFields() { return Collections.emptyMap(); }
        @Override public String getCipherSuite() { return "ISOLATED-TEST"; }
        @Override public Certificate[] getLocalCertificates() { return new Certificate[0]; }
        @Override public Certificate[] getServerCertificates() { return new Certificate[0]; }
        @Override public Principal getPeerPrincipal() { return null; }
        @Override public Principal getLocalPrincipal() { return null; }
    }
    private static class InMemoryPrefs implements SharedPreferences {
        final Map<String,Object> data=new HashMap<>();
        @Override public Map<String,?> getAll() { return new HashMap<>(data); }
        @Override public String getString(String key,String defValue) { Object v=data.get(key); return v instanceof String?(String)v:defValue; }
        @Override @SuppressWarnings("unchecked") public Set<String> getStringSet(String key,Set<String> defValues) { Object v=data.get(key); return v instanceof Set?(Set<String>)v:defValues; }
        @Override public int getInt(String key,int defValue) { Object v=data.get(key); return v instanceof Integer?(Integer)v:defValue; }
        @Override public long getLong(String key,long defValue) { Object v=data.get(key); return v instanceof Long?(Long)v:defValue; }
        @Override public float getFloat(String key,float defValue) { Object v=data.get(key); return v instanceof Float?(Float)v:defValue; }
        @Override public boolean getBoolean(String key,boolean defValue) { Object v=data.get(key); return v instanceof Boolean?(Boolean)v:defValue; }
        @Override public boolean contains(String key) { return data.containsKey(key); }
        @Override public Editor edit() {
            return new Editor() {
                final Map<String,Object> updates=new HashMap<>();
                final Set<String> removals=new HashSet<>();
                boolean clear;
                @Override public Editor putString(String k,String v) { updates.put(k,v); return this; }
                @Override public Editor putStringSet(String k,Set<String> v) { updates.put(k,v); return this; }
                @Override public Editor putInt(String k,int v) { updates.put(k,v); return this; }
                @Override public Editor putLong(String k,long v) { updates.put(k,v); return this; }
                @Override public Editor putFloat(String k,float v) { updates.put(k,v); return this; }
                @Override public Editor putBoolean(String k,boolean v) { updates.put(k,v); return this; }
                @Override public Editor remove(String k) { removals.add(k); return this; }
                @Override public Editor clear() { clear=true; return this; }
                @Override public boolean commit() { apply(); return true; }
                @Override public void apply() {
                    if (clear) data.clear();
                    for (String r : removals) data.remove(r);
                    data.putAll(updates);
                }
            };
        }
        @Override public void registerOnSharedPreferenceChangeListener(OnSharedPreferenceChangeListener l) { }
        @Override public void unregisterOnSharedPreferenceChangeListener(OnSharedPreferenceChangeListener l) { }
    }
}
