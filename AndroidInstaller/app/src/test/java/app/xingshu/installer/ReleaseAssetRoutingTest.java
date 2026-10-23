package app.xingshu.installer;

import org.junit.Test;
import static org.junit.Assert.*;
import java.io.IOException;
import java.net.SocketTimeoutException;
import java.util.ArrayList;
import java.util.List;

public final class ReleaseAssetRoutingTest {
    private static final String BROWSER="https://github.com/"+ReleaseUpdates.REPO+"/releases/download/v1/file.apk";

    @Test public void locatorUsesOnlyFixedRepositoryAndPositiveId() {
        assertEquals("https://api.github.com/repos/Breezecaresses/Xingshu-hub/releases/assets/669387880",ReleaseUpdates.assetApiAddress(669387880));
        for(long id:new long[]{0,-1,Long.MIN_VALUE}) {
            try { ReleaseUpdates.assetApiAddress(id); fail("Invalid asset ID accepted"); }
            catch(IllegalArgumentException expected) { assertTrue(expected.getMessage().contains("编号")); }
        }
    }
    @Test public void successfulApiResponseNeverUsesFallback() throws Exception {
        List<String> calls=new ArrayList<>();
        String result=ReleaseUpdates.preferAsset(123,BROWSER,address -> { calls.add(address); return "verify later"; },() -> fail("Unexpected fallback"));
        assertEquals("verify later",result);
        assertEquals(1,calls.size()); assertEquals(ReleaseUpdates.assetApiAddress(123),calls.get(0));
    }
    @Test public void networkFallbackReusesTheSameDeadline() throws Exception {
        long[] now={0}; CheckDeadline budget=new CheckDeadline(() -> now[0],1_000_000_000L);
        List<String> calls=new ArrayList<>(); int[] notices={0};
        String result=ReleaseUpdates.preferAsset(123,BROWSER,address -> {
            calls.add(address);
            if(calls.size()==1) { now[0]=900_000_000L; throw new ReleaseUpdates.NetworkFailure(new IOException("offline")); }
            assertEquals(100,budget.limitMillis(15000)); return "fallback bytes";
        },() -> notices[0]++);
        assertEquals("fallback bytes",result); assertEquals(2,calls.size());
        assertEquals(BROWSER,calls.get(1)); assertEquals(1,notices[0]);
    }
    @Test public void expiredBudgetStopsFallbackWithoutRestartingWait() throws Exception {
        long[] now={0}; CheckDeadline budget=new CheckDeadline(() -> now[0],1_000_000_000L);
        int[] calls={0};
        try {
            ReleaseUpdates.preferAsset(123,BROWSER,address -> {
                calls[0]++; budget.limitMillis(15000); now[0]=1_000_000_000L;
                throw new SocketTimeoutException("endpoint timeout");
            },() -> {});
            fail("Expired check unexpectedly succeeded");
        } catch(SocketTimeoutException expected) { assertEquals(2,calls[0]); }
    }
    @Test public void rejectedResponseCannotTriggerAnotherDownload() throws Exception {
        IOException rejection=new IOException("更新文件大小不符");
        try { ReleaseUpdates.preferAsset(123,BROWSER,address -> { throw rejection; },() -> fail("Validation failure retried")); fail(); }
        catch(IOException expected) { assertSame(rejection,expected); }
    }
    @Test public void missingLocatorUsesFixedBrowserAddressOnce() throws Exception {
        List<String> calls=new ArrayList<>();
        ReleaseUpdates.preferAsset(0,BROWSER,address -> { calls.add(address); return address; },() -> fail("No API request to fail"));
        assertEquals(1,calls.size()); assertEquals(BROWSER,calls.get(0));
    }
}
