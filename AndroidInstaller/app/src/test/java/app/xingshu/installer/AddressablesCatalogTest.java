package app.xingshu.installer;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.Base64;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import static org.junit.Assert.*;

public class AddressablesCatalogTest {
    @Rule public TemporaryFolder temporary = new TemporaryFolder();

    private static byte[] leInt(int v) {
        return ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt(v).array();
    }

    private static byte[] buildCatalogJson(String keyName, String bundleLoc, boolean selfCycle) throws IOException {
        ByteArrayOutputStream keys = new ByteArrayOutputStream();
        byte[] nameBytes = keyName.getBytes(StandardCharsets.US_ASCII);
        keys.write(0); // AsciiString
        keys.write(leInt(nameBytes.length));
        keys.write(nameBytes);
        int secondKeyOffset = keys.size();
        byte[] depKey = "dep".getBytes(StandardCharsets.US_ASCII);
        keys.write(0);
        keys.write(leInt(depKey.length));
        keys.write(depKey);

        ByteArrayOutputStream entries = new ByteArrayOutputStream();
        entries.write(leInt(2));
        // Entry 0: internalId=0, depBucket=1
        entries.write(leInt(0));
        entries.write(leInt(0));
        entries.write(leInt(1));
        entries.write(leInt(0));
        entries.write(leInt(0));
        entries.write(leInt(0));
        entries.write(leInt(0));
        // Entry 1: internalId=1, depBucket=0 if cycle else -1
        entries.write(leInt(1));
        entries.write(leInt(0));
        entries.write(leInt(selfCycle ? 0 : -1));
        entries.write(leInt(0));
        entries.write(leInt(0));
        entries.write(leInt(0));
        entries.write(leInt(0));

        ByteArrayOutputStream buckets = new ByteArrayOutputStream();
        buckets.write(leInt(2));
        buckets.write(leInt(0));
        buckets.write(leInt(1));
        buckets.write(leInt(0));
        buckets.write(leInt(secondKeyOffset));
        buckets.write(leInt(1));
        buckets.write(leInt(1));

        List<String> internalIds = new ArrayList<>();
        internalIds.add("AstralParty.Runtime.dll");
        internalIds.add(bundleLoc);

        Map<String, Object> root = new LinkedHashMap<>();
        root.put("m_LocatorId", "AddressablesMainContentCatalog");
        root.put("m_ExtraDataString", "ignored-large-blob");
        root.put("m_InternalIds", internalIds);
        root.put("m_KeyDataString", Base64.getEncoder().encodeToString(keys.toByteArray()));
        root.put("m_BucketDataString", Base64.getEncoder().encodeToString(buckets.toByteArray()));
        root.put("m_EntryDataString", Base64.getEncoder().encodeToString(entries.toByteArray()));
        return MiniJson.stringify(root).getBytes(StandardCharsets.UTF_8);
    }

    @Test
    public void resolvesArbitraryChannelManagedBundleRelativePath() throws Exception {
        String bundleId = "b2f76c77694452e77940f40f0889964e";
        File cat = temporary.newFile("catalog_3.3.0.json");
        Files.write(cat.toPath(), buildCatalogJson("AstralParty.Runtime.dll", "{App.WebServerConfig.Path}/" + bundleId + ".bundle", false));

        String rel = AddressablesCatalog.resolveManagedRuntimeRelativePath(cat);
        assertEquals("com.unity.addressables/AssetBundles/b2/" + bundleId + "/__data", rel);
    }

    @Test
    public void rejectsExternalUrlLocation() throws Exception {
        File cat = temporary.newFile("bad.json");
        Files.write(cat.toPath(), buildCatalogJson("AstralParty.Runtime.dll", "https://evil.example/aabbccdd00112233445566778899aabb.bundle", false));
        try {
            AddressablesCatalog.managedBundleId(cat);
            fail("Expected external URL to be rejected");
        } catch (IOException expected) {
            assertTrue(expected.getMessage().contains("受控运行时资源包"));
        }
    }

    @Test
    public void handlesCyclicDependencySafelyAndRejectsMissingBundle() throws Exception {
        File cat = temporary.newFile("cycle.json");
        Files.write(cat.toPath(), buildCatalogJson("AstralParty.Runtime.dll", "local/no-match.bundle", true));
        try {
            AddressablesCatalog.managedBundleId(cat);
            fail("Expected cycle without matching bundle to fail");
        } catch (IOException expected) {
            assertTrue(expected.getMessage().contains("数量=0"));
        }
    }

    @Test
    public void rejectsMissingRuntimeKey() throws Exception {
        String bundleId = "0123456789abcdef0123456789abcdef";
        File cat = temporary.newFile("missing-key.json");
        Files.write(cat.toPath(), buildCatalogJson("Other.dll", "{App.WebServerConfig.Path}/" + bundleId + ".bundle", false));
        try {
            AddressablesCatalog.managedBundleId(cat);
            fail("Expected missing runtime key to be rejected");
        } catch (IOException expected) {
            assertTrue(expected.getMessage().contains("AstralParty.Runtime.dll"));
        }
    }
}
