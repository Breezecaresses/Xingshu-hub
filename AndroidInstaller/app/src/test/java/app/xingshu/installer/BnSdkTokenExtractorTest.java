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

    @Test
    public void extractsTokenWhenAppIdAppearsLaterInStatisticsPeriodHelperAndEvCrashOs() {
        String oldTok = "0bsOldCachedToken1234567890abcdefghijklmnopqrstuvwxyzABCDEFGH";
        String liveTok = "01PLiveTapToken1234567890abcdefghijklmnopqrstuvwxyzABCDEFGHIJ";
        String withEvCrashOs = ""
            + "D/Ev_CRASH_OS( 5303): [JavaCrashCatching]:javaCrashFile /storage/emulated/0/Android/data/com.feimo.astralparty/files/java_110001937.log not exits\n"
            + "D/BN_DEBUG( 5303): [okhttp] post onSuccess: https://m-sdk.feimogames.com/account/authorize {\"ret\":\"1\",\"msg\":\"success\",\"content\":{\"authorize_code\":\"ac_123456\",\"user_name\":\"c:u27902200154kz\",\"user_id\":\"T5GBEuBGjDw81seB3MJ1Uko=\",\"reg_login\":0,\"data\":{\"accessToken\":\"" + liveTok + "\"}}}\n"
            + "D/BN_DEBUG( 5303): [Memory-LoginData] userSqlEntity: {\"id\":\"null\",\"username\":\"c:u27902200154kz\",\"phone\":\"13800138000\",\"type\":\"1\",\"token\":\"" + oldTok + "\",\"time\":\"1792583994079\",\"md5password\":\"*null*\",\"user_id\":\"uc:u27902200154kz\",\"authorize_code\":\"ac_123456\"openid\":\"null\"}\n"
            + "D/BN_DEBUG( 5303): [LoginSDK] the token is: " + liveTok + "\n"
            + "D/BN_UNITY( 5303): [BnUnitySdk] start onLoginRoleInfo , RoleInfo : {\"uid\":\"uc:u27902200154kz\",\"rid\":\"1556514\",\"rn\":\"\\u53e4\\u660e\\u5730\\u732b\\u8033\",\"rl\":34,\"vl\":0,\"sid\":\"6688\",\"rcoin\":0,\"rctime\":-1}\n"
            + "D/BN_DEBUG( 5303): [StatisticsPeriodHelper] save information, appid:110001937  uid:uc:u27902200154kz\n";

        List<BnSdkTokenExtractor.ChannelToken> tokens = BnSdkTokenExtractor.extractFromLogcat(withEvCrashOs);
        assertEquals(1, tokens.size());
        assertEquals("TAPTAP", tokens.get(0).platform);
        assertEquals("110001937", tokens.get(0).appId);
        assertEquals(liveTok, tokens.get(0).sdkToken);
        assertTrue(tokens.get(0).nativeSupported);
        assertTrue(tokens.get(0).userId.contains("古明地猫耳"));
        assertTrue(tokens.get(0).userId.contains("1556514"));

        // Even when Ev_CRASH_OS is absent and appid:110001937 appears only AFTER accessToken, pass 1 resolves PID 5303.
        String postAppIdOnly = ""
            + "D/BN_DEBUG( 5303): [okhttp] post onSuccess: https://m-sdk.feimogames.com/account/authorize {\"ret\":\"1\",\"data\":{\"accessToken\":\"" + liveTok + "\"}}\n"
            + "D/BN_UNITY( 5303): [BnUnitySdk] start onLoginRoleInfo , RoleInfo : {\"uid\":\"uc:u27902200154kz\",\"rid\":\"1556514\",\"rn\":\"\\u53e4\\u660e\\u5730\\u732b\\u8033\"}\n"
            + "D/BN_DEBUG( 5303): [StatisticsPeriodHelper] save information, appid:110001937  uid:uc:u27902200154kz\n";
        List<BnSdkTokenExtractor.ChannelToken> postResolved = BnSdkTokenExtractor.extractFromLogcat(postAppIdOnly);
        assertEquals(1, postResolved.size());
        assertEquals("TAPTAP", postResolved.get(0).platform);
        assertEquals("110001937", postResolved.get(0).appId);
        assertEquals(liveTok, postResolved.get(0).sdkToken);
    }

    @Test
    public void extractsTokenFromUserSqlEntityOrLoginSdkWhenOkhttpLineEvicted() {
        String sqlTok = "01PSqlEntityToken1234567890abcdefghijklmnopqrstuvwxyzABCDEF";
        String loginSdkTok = "01PLoginSdkToken1234567890abcdefghijklmnopqrstuvwxyzABCDEFG";

        String sqlOnlyLog = ""
            + "D/BN_DEBUG( 6100): [LoginSDK] now, Memory login data is: {\"id\":\"null\",\"username\":\"c:xm1\",\"type\":\"1\",\"token\":\"" + sqlTok + "\",\"user_id\":\"uc:xm1\",\"authorize_code\":\"ac_999\"}\n"
            + "D/BN_DEBUG( 6100): [StatisticsPeriodHelper] save information, appid:110001934  uid:uc:xm1\n";
        List<BnSdkTokenExtractor.ChannelToken> sqlTokens = BnSdkTokenExtractor.extractFromLogcat(sqlOnlyLog);
        assertEquals(1, sqlTokens.size());
        assertEquals("XIAOMI", sqlTokens.get(0).platform);
        assertEquals("110001934", sqlTokens.get(0).appId);
        assertEquals(sqlTok, sqlTokens.get(0).sdkToken);
        assertTrue(sqlTokens.get(0).nativeSupported);

        String sdkOnlyLog = ""
            + "D/XINGSHU_PS( 7200): package=com.feimo.astralparty.bilibili\n"
            + "D/BN_DEBUG( 7200): [LoginSDK] the token is: " + loginSdkTok + "\n";
        List<BnSdkTokenExtractor.ChannelToken> sdkTokens = BnSdkTokenExtractor.extractFromLogcat(sdkOnlyLog);
        assertEquals(1, sdkTokens.size());
        assertEquals("BILIBILI", sdkTokens.get(0).platform);
        assertEquals("110001957", sdkTokens.get(0).appId);
        assertEquals(loginSdkTok, sdkTokens.get(0).sdkToken);
        assertTrue(sdkTokens.get(0).nativeSupported);
    }

    @Test
    public void infersXiaomiAndBilibiliFromPackageOrFallbackAppId() {
        String xmTok = "xmPackageHintToken1234567890abcdefghijklmnopqrstuvwxyzABCDE";
        String biliTok = "blDiskHintToken1234567890abcdefghijklmnopqrstuvwxyzABCDEFGH";

        String xmLog = ""
            + "11-16 01:40:00.123  8100  8120 D XINGSHU_PS: package=com.feimo.jxpd.mi\n"
            + "11-16 01:40:01.456  8100  8120 D BN_DEBUG: {\"content\":{\"data\":{\"accessToken\":\"" + xmTok + "\"}}}\n";
        List<BnSdkTokenExtractor.ChannelToken> xmList = BnSdkTokenExtractor.extractFromLogcat(xmLog);
        assertEquals(1, xmList.size());
        assertEquals("XIAOMI", xmList.get(0).platform);
        assertEquals("110001934", xmList.get(0).appId);
        assertEquals(xmTok, xmList.get(0).sdkToken);

        String noAppIdLog = "D/BN_DEBUG( 9001): [okhttp] post onSuccess: {\"content\":{\"data\":{\"accessToken\":\"" + biliTok + "\"}}}\n";
        List<BnSdkTokenExtractor.ChannelToken> fallbackList = BnSdkTokenExtractor.extractFromLogcat(noAppIdLog, "110001957");
        assertEquals(1, fallbackList.size());
        assertEquals("BILIBILI", fallbackList.get(0).platform);
        assertEquals("110001957", fallbackList.get(0).appId);
        assertEquals(biliTok, fallbackList.get(0).sdkToken);
    }
}
