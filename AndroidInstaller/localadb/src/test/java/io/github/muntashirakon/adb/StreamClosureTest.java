package io.github.muntashirakon.adb;

import org.junit.Test;
import org.junit.BeforeClass;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.*;
import static org.junit.Assert.*;
import static org.mockito.Mockito.*;

public final class StreamClosureTest {
    private static AdbConnection connection;
    @BeforeClass public static void connectionFixture() throws Exception {
        connection=mock(AdbConnection.class); when(connection.getMaxData()).thenReturn(4096);
    }
    private AdbStream stream() throws Exception {
        return new AdbStream(connection,1);
    }
    @Test(timeout=3000) public void drainsBufferedAndQueuedBytesAfterPeerClose() throws Exception {
        AdbStream stream=stream(); InputStream in=stream.openInputStream();
        stream.addPayload("abc".getBytes(StandardCharsets.UTF_8));
        assertEquals('a',in.read());
        stream.addPayload("def".getBytes(StandardCharsets.UTF_8)); stream.notifyClose(true);
        byte[] result=new byte[5]; int offset=0,n;
        while(offset<result.length&&(n=in.read(result,offset,result.length-offset))!=-1)offset+=n;
        assertEquals("bcdef",new String(result,StandardCharsets.UTF_8));
        assertEquals(-1,in.read()); assertEquals(0,in.read(new byte[0]));
    }
    @Test(timeout=3000) public void closeWithNoPayloadReturnsEof() throws Exception {
        AdbStream stream=stream(); stream.notifyClose(true); assertEquals(-1,stream.openInputStream().read());
    }
    @Test(timeout=3000) public void peerCloseWakesWaitingReader() throws Exception {
        AdbStream stream=stream(); ExecutorService worker=Executors.newSingleThreadExecutor();
        try {
            Future<Integer> read=worker.submit(() -> stream.openInputStream().read());
            stream.notifyClose(true); assertEquals(Integer.valueOf(-1),read.get(1,TimeUnit.SECONDS));
        } finally { worker.shutdownNow(); }
    }
}
