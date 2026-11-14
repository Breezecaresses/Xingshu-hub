package app.xingshu.installer;

import org.junit.Test;
import java.io.IOException;
import java.util.List;
import static org.junit.Assert.*;

public class BnSdkTokenExtractorTest {
    @Test
    public void extractsXiaomiAndTapTapTokensSkipsShAndKeepsNewest() {
        String oldTok = "eyOldTapToken1234567890abcdefghijklmnopqrstuvwxyzABCD";
        String newTapTok = "eyNewTapToken1234567890abcdefghijklmnopqrstuvwxyzABCD";
        String miTok = "miLiveToken1234567890abcdefghijklmnopqrstuvwxyzABCDEFG";
        String shTok = "tempChannelToken1234567890abcdefghijklmnopqrstuvwxy_sh";
        String logcat = ""
            + "11-14 01:00:00.100 I/BN_DEBUG(1001): {\"appId\":\"110001958\",\"accessToken\":\"" + oldTok + "\"}\n"
            + "11-14 01:01:00.200 I/BN_DEBUG(1002): Init app_id=110001934 userId=998877\n"
            + "11-14 01:01:01.200 I/BN_DEBUG(1002): {\"msg\":\"{\\\"accessToken\\\":\\\"" + shTok + "\\\"}\"}\n"
            + "11-14 01:01:02.200 I/BN_DEBUG(1002): {\"msg\":\"{\\\"accessToken\\\":\\\"" + miTok + "\\\"}\"}\n"
            + "11-14 01:02:00.300 I/BN_DEBUG(1001): {\"app_id\":110001958,\"access_token\":\"" + newTapTok + "\"}\n";

        List<BnSdkTokenExtractor.ChannelToken> tokens = BnSdkTokenExtractor.extractFromLogcat(logcat);
        assertEquals(2, tokens.size());
        assertEquals("TAPTAP", tokens.get(0).platform);
        assertEquals("110001958", tokens.get(0).appId);
        assertEquals(newTapTok, tokens.get(0).sdkToken);
        assertTrue(tokens.get(0).nativeSupported);

        assertEquals("XIAOMI", tokens.get(1).platform);
        assertEquals("110001934", tokens.get(1).appId);
        assertEquals("998877", tokens.get(1).userId);
        assertEquals(miTok, tokens.get(1).sdkToken);
        assertTrue(tokens.get(1).nativeSupported);
        assertFalse(tokens.get(1).displaySummary().contains(miTok));
    }

    @Test
    public void marksUnsupportedChannelTokensForInGameBind() {
        String huaweiTok = "hwLiveToken1234567890abcdefghijklmnopqrstuvwxyzABCDEF";
        String logcat = "11-14 02:00:00 I/BN_DEBUG: {\"appId\":\"110099999\",\"accessToken\":\"" + huaweiTok + "\"}\n";
        List<BnSdkTokenExtractor.ChannelToken> tokens = BnSdkTokenExtractor.extractFromLogcat(logcat);
        assertEquals(1, tokens.size());
        assertFalse(tokens.get(0).nativeSupported);
        assertTrue(tokens.get(0).displaySummary().contains("趴绑定"));
    }

    @Test
    public void parsesOfficialLoginUrlsAndRejectsForeignOrigins() throws Exception {
        BnSdkTokenExtractor.LoginLinkParams p = BnSdkTokenExtractor.parseLoginLink(
            "https://124.221.176.134/login#req_12345678901234567890123456.pageTok_1234567890123456"
        );
        assertEquals("req_12345678901234567890123456", p.requestId);
        assertEquals("pageTok_1234567890123456", p.pageToken);

        try {
            BnSdkTokenExtractor.parseLoginLink("https://evil.example/login#req_1234567890.tok_1234567890123456");
            fail("Expected foreign origin rejection");
        } catch (IOException expected) {
            assertTrue(expected.getMessage().contains("官方授权链接"));
        }
    }
}
