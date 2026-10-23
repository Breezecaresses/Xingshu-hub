package app.xingshu.installer;

import org.junit.Test;
import static org.junit.Assert.*;
import java.io.*;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.List;

public final class DownloadProgressTest {
    private static final class Events implements ReleaseUpdates.DownloadListener {
        final List<Long> bytes = new ArrayList<>();
        public void progress(long count,long total) { bytes.add(count); }
        public void verifying() { fail("A stream copy cannot claim APK validation"); }
    }
    @Test public void completeStreamReportsActualBytesAndKeepsIntegrityHash() throws Exception {
        byte[] data = new byte[200000];
        for(int i=0;i<data.length;i++)data[i]=(byte)(i*31);
        Events events = new Events(); ByteArrayOutputStream output = new ByteArrayOutputStream();
        String hash=ReleaseUpdates.copyDownload(new ByteArrayInputStream(data),output,data.length,events);
        assertArrayEquals(data,output.toByteArray());
        assertEquals(ReleaseUpdates.hex(MessageDigest.getInstance("SHA-256").digest(data)),hash);
        assertEquals(Long.valueOf(0),events.bytes.get(0));
        assertEquals(Long.valueOf(data.length),events.bytes.get(events.bytes.size()-1));
        for(int i=1;i<events.bytes.size();i++)assertTrue(events.bytes.get(i)>=events.bytes.get(i-1));
    }
    @Test public void truncatedResponseNeverReportsFullDownload() throws Exception {
        Events events=new Events();
        try { ReleaseUpdates.copyDownload(new ByteArrayInputStream(new byte[100]),new ByteArrayOutputStream(),1000,events); fail(); }
        catch(IOException expected) { assertTrue(expected.getMessage().contains("未收到完整")); }
        assertTrue(events.bytes.stream().noneMatch(n -> n==1000));
    }
    @Test public void oversizedResponseIsRejectedBeforeWritingExcess() throws Exception {
        ByteArrayOutputStream output=new ByteArrayOutputStream();
        try { ReleaseUpdates.copyDownload(new ByteArrayInputStream(new byte[1001]),output,1000,new Events()); fail(); }
        catch(IOException expected) { assertTrue(expected.getMessage().contains("超出声明")); }
        assertTrue(output.size()<=1000);
    }
    @Test public void networkErrorsPropagateWithoutClaimingCompletion() throws Exception {
        Events events=new Events();
        InputStream failed=new InputStream() { public int read() throws IOException { throw new IOException("network timeout"); } };
        try { ReleaseUpdates.copyDownload(failed,new ByteArrayOutputStream(),1000,events); fail(); }
        catch(IOException expected) { assertEquals("network timeout",expected.getMessage()); }
        assertEquals(1,events.bytes.size()); assertEquals(Long.valueOf(0),events.bytes.get(0));
    }
    @Test public void idleTimeAndSizeExplainSlowNetworkAndResetWhenBytesArrive() {
        DownloadProgress progress=new DownloadProgress(2*1048576,1000);
        progress.received(1048576,2000);
        assertEquals(50,progress.percent());
        assertTrue(progress.description(3000).contains("1.00 MB / 2.00 MB"));
        assertTrue(progress.description(3000).contains("512.0 KB/s"));
        assertTrue(progress.description(12000).contains("10 秒未收到新数据"));
        progress.received(1048576+4096,13000);
        assertFalse(progress.description(13000).contains("未收到"));
    }
    @Test public void updateCheckFailureExplainsTimeoutDnsAndValidationSeparately() {
        assertTrue(ReleaseUpdates.failureMessage(new java.net.SocketTimeoutException()).contains("超时"));
        assertTrue(ReleaseUpdates.failureMessage(new java.net.UnknownHostException()).contains("解析"));
        assertEquals("更新清单签名校验失败",ReleaseUpdates.failureMessage(new IOException("更新清单签名校验失败")));
    }
    @Test public void historyNetworkFailureKeepsVerifiedResultButCannotClaimLatest() throws Exception {
        ReleaseUpdates.Update verified=new ReleaseUpdates.Update(null);
        assertSame(verified,ReleaseUpdates.partialResult(verified,new java.net.SocketTimeoutException()));
        assertTrue(verified.checkWarning.contains("暂不能确认"));
        IOException error=new IOException("offline");
        try { ReleaseUpdates.partialResult(null,error); fail("No verified result must remain a failure"); }
        catch(IOException expected) { assertSame(error,expected); }
    }
}
