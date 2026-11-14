package app.xingshu.installer;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Base64;
import java.util.Deque;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Resolves the active AstralParty.Runtime.dll Unity Addressables bundle from any channel's
 * catalog_3.3.0.json so the installer works across packages without hardcoding a single bundle hash.
 */
final class AddressablesCatalog {
    private static final int MAX_CATALOG_BYTES = 32 * 1024 * 1024;
    private static final int MAX_TABLE_COUNT = 200000;
    private static final int MAX_VISITED = 256;
    private static final Set<String> WANTED_KEYS = new HashSet<>(Arrays.asList(
        "m_KeyDataString", "m_BucketDataString", "m_EntryDataString", "m_InternalIds"
    ));
    private static final Pattern BUNDLE_LOC_PATTERN = Pattern.compile(
        "\\{App\\.WebServerConfig\\.Path\\}[\\\\/]([0-9a-f]{32})\\.bundle"
    );

    private static final class Bucket {
        final String key;
        final int[] indexes;

        Bucket(String key, int[] indexes) {
            this.key = key;
            this.indexes = indexes;
        }
    }

    private AddressablesCatalog() { }

    static String resolveManagedRuntimeRelativePath(File catalogFile) throws IOException {
        return relativeBundlePath(managedBundleId(catalogFile));
    }

    static String relativeBundlePath(String bundleId) throws IOException {
        if (bundleId == null || !bundleId.matches("[0-9a-f]{32}")) {
            throw new IOException("非法的资源包标识");
        }
        return "com.unity.addressables/AssetBundles/" + bundleId.substring(0, 2) + "/" + bundleId + "/__data";
    }

    static String managedBundleId(File catalogFile) throws IOException {
        if (catalogFile == null || !catalogFile.isFile()) {
            throw new IOException("缺少游戏资源清单 catalog_3.3.0.json");
        }
        if (catalogFile.length() > MAX_CATALOG_BYTES) {
            throw new IOException("资源清单体积异常，超过安全限制");
        }
        ByteArrayOutputStream out = new ByteArrayOutputStream((int) Math.min(catalogFile.length(), 1024 * 1024));
        try (FileInputStream in = new FileInputStream(catalogFile)) {
            byte[] buf = new byte[65536];
            int total = 0;
            int n;
            while ((n = in.read(buf)) != -1) {
                total += n;
                if (total > MAX_CATALOG_BYTES) {
                    throw new IOException("资源清单体积异常，超过安全限制");
                }
                out.write(buf, 0, n);
            }
        }
        String text = new String(out.toByteArray(), StandardCharsets.UTF_8);
        return managedBundleId(MiniJson.parseObjectFields(text, WANTED_KEYS));
    }

    static String managedBundleId(Map<String, Object> catalog) throws IOException {
        if (catalog == null) throw new IOException("资源清单为空");
        byte[] keys = decodeBase64Field(catalog, "m_KeyDataString");
        byte[] bucketsRaw = decodeBase64Field(catalog, "m_BucketDataString");
        byte[] entriesRaw = decodeBase64Field(catalog, "m_EntryDataString");
        Object rawIds = catalog.get("m_InternalIds");
        if (!(rawIds instanceof List<?>)) {
            throw new IOException("资源清单缺少内部路径列表");
        }
        List<?> idObjs = (List<?>) rawIds;
        List<String> internalIds = new ArrayList<>(idObjs.size());
        for (Object item : idObjs) {
            if (!(item instanceof String)) {
                throw new IOException("资源清单内部路径项格式无效");
            }
            internalIds.add((String) item);
        }

        int entryCount = readInt(entriesRaw, 0);
        if (entryCount <= 0 || entryCount > MAX_TABLE_COUNT || entriesRaw.length != 4 + entryCount * 28) {
            throw new IOException("资源条目表结构无效");
        }
        int bucketCount = readInt(bucketsRaw, 0);
        if (bucketCount <= 0 || bucketCount > MAX_TABLE_COUNT) {
            throw new IOException("资源桶数量无效");
        }

        List<Bucket> buckets = new ArrayList<>(bucketCount);
        Bucket runtimeBucket = null;
        int pos = 4;
        for (int b = 0; b < bucketCount; b++) {
            int keyOffset = readInt(bucketsRaw, pos);
            int count = readInt(bucketsRaw, pos + 4);
            pos += 8;
            if (count < 0 || count > MAX_TABLE_COUNT || pos + (long) count * 4L > bucketsRaw.length) {
                throw new IOException("资源桶索引范围越界");
            }
            int[] indexes = new int[count];
            for (int i = 0; i < count; i++) {
                int idx = readInt(bucketsRaw, pos + i * 4);
                if (idx < 0 || idx >= entryCount) {
                    throw new IOException("资源条目索引越界");
                }
                indexes[i] = idx;
            }
            pos += count * 4;
            if (keyOffset < 0 || keyOffset >= keys.length) {
                throw new IOException("资源键偏移越界");
            }
            String key = null;
            if ((keys[keyOffset] & 0xFF) == 0) {
                int size = readInt(keys, keyOffset + 1);
                if (size < 0 || keyOffset + 5L + size > keys.length) {
                    throw new IOException("资源键字符串越界");
                }
                key = new String(keys, keyOffset + 5, size, StandardCharsets.US_ASCII);
            }
            Bucket bucket = new Bucket(key, indexes);
            buckets.add(bucket);
            if ("AstralParty.Runtime.dll".equals(key)) {
                if (runtimeBucket != null) {
                    throw new IOException("资源清单存在重复的运行时条目");
                }
                runtimeBucket = bucket;
            }
        }
        if (pos != bucketsRaw.length) {
            throw new IOException("资源桶存在未解析尾随字节");
        }
        if (runtimeBucket == null || runtimeBucket.indexes.length != 1) {
            throw new IOException("未找到唯一的 AstralParty.Runtime.dll 资源入口");
        }

        Set<String> found = new HashSet<>();
        Set<Integer> visited = new HashSet<>();
        Deque<Integer> pending = new ArrayDeque<>();
        pending.add(runtimeBucket.indexes[0]);
        while (!pending.isEmpty()) {
            int index = pending.pop();
            if (!visited.add(index)) continue;
            if (visited.size() > MAX_VISITED) {
                throw new IOException("运行时资源依赖链过长");
            }
            int offset = 4 + index * 28;
            int internalIdIdx = readInt(entriesRaw, offset);
            int dependencyBucket = readInt(entriesRaw, offset + 8);
            if (internalIdIdx < 0 || internalIdIdx >= internalIds.size()) {
                throw new IOException("内部资源路径索引越界");
            }
            String location = internalIds.get(internalIdIdx);
            Matcher matcher = BUNDLE_LOC_PATTERN.matcher(location);
            if (matcher.matches()) {
                found.add(matcher.group(1));
            }
            if (dependencyBucket != -1) {
                if (dependencyBucket < 0 || dependencyBucket >= buckets.size()) {
                    throw new IOException("依赖资源桶索引越界");
                }
                for (int depIdx : buckets.get(dependencyBucket).indexes) {
                    pending.push(depIdx);
                }
            }
        }
        if (found.size() != 1) {
            throw new IOException("未能确定唯一的受控运行时资源包（数量=" + found.size() + "）");
        }
        return found.iterator().next();
    }

    private static byte[] decodeBase64Field(Map<String, Object> catalog, String key) throws IOException {
        Object raw = catalog.get(key);
        if (!(raw instanceof String)) {
            throw new IOException("资源清单缺少字段 " + key);
        }
        try {
            return Base64.getDecoder().decode((String) raw);
        } catch (IllegalArgumentException e) {
            throw new IOException("资源清单字段 Base64 解码失败: " + key, e);
        }
    }

    private static int readInt(byte[] blob, int offset) throws IOException {
        if (blob == null || offset < 0 || offset + 4 > blob.length) {
            throw new IOException("资源清单整型表越界");
        }
        return ByteBuffer.wrap(blob, offset, 4).order(ByteOrder.LITTLE_ENDIAN).getInt();
    }
}
