package app.xingshu.installer;

import org.junit.Test;
import java.io.IOException;
import java.util.Arrays;
import java.util.List;
import static org.junit.Assert.*;

public class GamePackageCatalogTest {
    @Test
    public void discoversMultipleGameChannelsWithoutHardcodingSinglePackage() {
        String pmOut = ""
            + "package:com.android.chrome\n"
            + "package:app.xingshu.installer\n"
            + "package:com.feimo.astralparty\n"
            + "package:com.feimo.jxpd.mi\n"
            + "package:com.feimo.astralparty.bilibili\n"
            + "package:com.tencent.tmgp.astralparty\n";

        List<String> found = GamePackageCatalog.parseThirdPartyPackages(pmOut);
        assertEquals(Arrays.asList(
            "com.feimo.astralparty",
            "com.feimo.jxpd.mi",
            "com.feimo.astralparty.bilibili",
            "com.tencent.tmgp.astralparty"
        ), found);
        assertEquals("XIAOMI", GamePackageCatalog.classifyPackage("com.feimo.jxpd.mi").platform);
        assertEquals("BILIBILI", GamePackageCatalog.classifyPackage("com.feimo.astralparty.bilibili").platform);
        assertEquals("YYB", GamePackageCatalog.classifyPackage("com.tencent.tmgp.astralparty").platform);
    }

    @Test
    public void rejectsUnsafeOrTraversalPackageNames() {
        assertFalse(GamePackageCatalog.isValidPackageName("../sdcard"));
        assertFalse(GamePackageCatalog.isValidPackageName("com.feimo;id"));
        assertFalse(GamePackageCatalog.isValidPackageName("singleword"));
        try {
            GamePackageCatalog.filesRoot("../bad");
            fail("Expected traversal package name rejection");
        } catch (IOException expected) {
            assertTrue(expected.getMessage().contains("非法"));
        }
    }

    @Test
    public void extractsVersionNameAndIsolatesPerPackageBackups() throws Exception {
        String dump = "  Package [com.feimo.jxpd.mi] (12345):\n    versionCode=330\n    versionName=3.3.0\n";
        assertEquals("3.3.0", GamePackageCatalog.extractVersionName(dump));
        assertTrue(GamePackageCatalog.backupRoot("com.feimo.jxpd.mi").getPath().replace('\\', '/')
            .contains("/com.feimo.jxpd.mi/"));
        assertFalse(GamePackageCatalog.backupRoot("com.feimo.astralparty").getPath().replace('\\', '/')
            .contains("/com.feimo.astralparty/"));
    }
}
