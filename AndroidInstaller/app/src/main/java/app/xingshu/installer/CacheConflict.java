package app.xingshu.installer;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import java.util.Properties;

/** Preserve a conflicting cache and clean same-role plugin companions without touching unrelated mods. */
final class CacheConflict {
    private static final String[] APX_COMPANION_PATHS = {
        "runtime_dependencies.json",
        "apx_modern_assist_ui.bundle",
        "dependence/apx_modern_assist_ui.bundle",
        "Plugins/x86_64/APXLive2DBridge.dll",
        "com.unity.addressables/AssetBundles/be8a6f7fa87357b26096b5a0ada25e0b/b7a6b1b538aa75bef22b9d95f0ff93bf/__data"
    };
    private static final String APX_EXTRA_INFO = "com.unity.addressables/AssetBundles/be8a6f7fa87357b26096b5a0ada25e0b/b7a6b1b538aa75bef22b9d95f0ff93bf/__info";
    private static final String APX_LEGACY_DATA = "com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/3c7700aa58877b365d45231b1225fcbb/__data";
    private static final String APX_LEGACY_INFO = "com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/3c7700aa58877b365d45231b1225fcbb/__info";
    private static final String APX_CUBISM = "Plugins/x86_64/Live2DCubismCore.dll";

    static boolean replaceable(File target, File catalog, String expectedCatalog) throws Exception {
        if (expectedCatalog == null || !expectedCatalog.matches("[a-f0-9]{64}")
                || !catalog.isFile() || !hash(catalog).equals(expectedCatalog)
                || target.length() < 128 || target.length() > 64L * 1024 * 1024) return false;
        byte[] header = new byte[8];
        try (DataInputStream in = new DataInputStream(new FileInputStream(target))) { in.readFully(header); }
        return Arrays.equals(header, new byte[]{'U','n','i','t','y','F','S',0});
    }

    static List<File> findCompanions(File filesRoot, File target) {
        List<File> found = new ArrayList<>();
        if (filesRoot == null || !filesRoot.isDirectory()) return found;
        String targetPath = safeCanonical(target);
        for (String rel : APX_COMPANION_PATHS) {
            File f = new File(filesRoot, rel);
            if (f.isFile() && !safeCanonical(f).equals(targetPath)) found.add(f);
        }
        File extraData = new File(filesRoot, APX_COMPANION_PATHS[APX_COMPANION_PATHS.length - 1]);
        if (extraData.isFile()) {
            File info = new File(filesRoot, APX_EXTRA_INFO);
            if (info.isFile()) found.add(info);
        }
        File legacyData = new File(filesRoot, APX_LEGACY_DATA);
        if (legacyData.isFile() && !safeCanonical(legacyData).equals(targetPath)) {
            found.add(legacyData);
            File legacyInfo = new File(filesRoot, APX_LEGACY_INFO);
            if (legacyInfo.isFile()) found.add(legacyInfo);
        }
        if (!found.isEmpty()) {
            File cubism = new File(filesRoot, APX_CUBISM);
            if (cubism.isFile()) found.add(cubism);
        }
        return found;
    }

    static String identifyPlugin(File target, List<File> companions) {
        if (companions != null && !companions.isEmpty()) return "APX 插件";
        try {
            if (target != null && target.isFile() && target.length() <= 32L * 1024 * 1024) {
                ByteArrayOutputStream raw = new ByteArrayOutputStream();
                try (InputStream in = new FileInputStream(target)) {
                    byte[] buf = new byte[65536]; int n;
                    while ((n = in.read(buf)) != -1) raw.write(buf, 0, n);
                }
                String text = new String(raw.toByteArray(), StandardCharsets.US_ASCII).toLowerCase(Locale.ROOT);
                if (text.contains("apxlive2d") || text.contains("apx_modern_assist_ui") || text.contains("apx.runtime"))
                    return "APX 插件";
            }
        } catch (Exception ignored) { }
        return "同定位覆盖插件 / MOD";
    }

    static File archive(File target, File directory, String expectedHash) throws Exception {
        if (!expectedHash.matches("[a-f0-9]{64}")) throw new IOException("冲突文件校验值无效");
        File home = new File(directory, "conflicts/" + expectedHash);
        if (!home.isDirectory() && !home.mkdirs()) throw new IOException("无法创建冲突备份目录");
        File saved = new File(home, "before.__data");
        if (!saved.exists()) {
            File stage = new File(home, "before.staging");
            try (InputStream in = new FileInputStream(target); FileOutputStream out = new FileOutputStream(stage)) {
                byte[] bytes = new byte[65536]; int count;
                while ((count = in.read(bytes)) != -1) out.write(bytes, 0, count);
                out.getFD().sync();
            }
            if (!hash(stage).equals(expectedHash)) { stage.delete(); throw new IOException("冲突备份校验失败，未安装"); }
            if (!stage.renameTo(saved)) throw new IOException("无法保存冲突备份，未安装");
        }
        if (!hash(saved).equals(expectedHash)) throw new IOException("已有冲突备份损坏，已停止");
        File record = new File(home, "conflict.properties");
        if (!record.exists()) {
            Properties info = new Properties();
            info.setProperty("target", target.getCanonicalPath());
            info.setProperty("sha256", expectedHash);
            info.setProperty("detectedAt", Long.toString(System.currentTimeMillis()));
            info.setProperty("kind", "pre-install-cache-at-same-path");
            try (FileOutputStream out = new FileOutputStream(record)) { info.store(out, "XingShu conflict backup"); out.getFD().sync(); }
        }
        return saved;
    }

    static void archiveAndDeleteCompanions(File filesRoot, List<File> companions, File conflictHome) throws Exception {
        if (companions == null || companions.isEmpty()) return;
        String rootPrefix = filesRoot.getCanonicalPath() + File.separator;
        File companionRoot = new File(conflictHome, "companions");
        Properties manifest = new Properties();
        int idx = 0;
        for (File file : companions) {
            if (!file.isFile()) continue;
            String canonical = file.getCanonicalPath();
            String rel = canonical.startsWith(rootPrefix) ? canonical.substring(rootPrefix.length()) : file.getName();
            String sha = hash(file);
            File dest = new File(companionRoot, rel);
            File parent = dest.getParentFile();
            if (parent != null && !parent.isDirectory() && !parent.mkdirs())
                throw new IOException("无法创建冲突插件备份目录");
            try (InputStream in = new FileInputStream(file); FileOutputStream out = new FileOutputStream(dest)) {
                byte[] buf = new byte[65536]; int n;
                while ((n = in.read(buf)) != -1) out.write(buf, 0, n);
                out.getFD().sync();
            }
            if (!hash(dest).equals(sha)) throw new IOException("冲突插件备份校验失败：" + rel);
            manifest.setProperty("item." + idx + ".path", rel);
            manifest.setProperty("item." + idx + ".sha256", sha);
            idx++;
            if (!file.delete()) throw new IOException("无法删除已冲突插件文件：" + rel);
        }
        manifest.setProperty("count", Integer.toString(idx));
        File rec = new File(conflictHome, "companions.properties");
        try (FileOutputStream out = new FileOutputStream(rec)) { manifest.store(out, "XingShu deleted conflict companions"); out.getFD().sync(); }
    }

    private static String safeCanonical(File f) {
        try { return f.getCanonicalPath(); } catch (IOException e) { return f.getAbsolutePath(); }
    }

    static String hash(File file) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream in = new FileInputStream(file)) {
            byte[] bytes = new byte[65536]; int count;
            while ((count = in.read(bytes)) != -1) digest.update(bytes, 0, count);
        }
        StringBuilder value = new StringBuilder();
        for (byte b : digest.digest()) value.append(String.format(Locale.ROOT, "%02x", b & 255));
        return value.toString();
    }
}
