package app.xingshu.installer;

import java.io.*;
import java.security.MessageDigest;
import java.util.Properties;

/** Preserve a conflicting cache without touching unrelated mods or directories. */
final class CacheConflict {
    static boolean replaceable(File target, File catalog, String expectedCatalog) throws Exception {
        if (expectedCatalog == null || !expectedCatalog.matches("[a-f0-9]{64}")
                || !catalog.isFile() || !hash(catalog).equals(expectedCatalog)
                || target.length() < 128 || target.length() > 64L * 1024 * 1024) return false;
        byte[] header = new byte[8];
        try (DataInputStream in = new DataInputStream(new FileInputStream(target))) { in.readFully(header); }
        return java.util.Arrays.equals(header, new byte[]{'U','n','i','t','y','F','S',0});
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

    private static String hash(File file) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream in = new FileInputStream(file)) {
            byte[] bytes = new byte[65536]; int count;
            while ((count = in.read(bytes)) != -1) digest.update(bytes, 0, count);
        }
        StringBuilder value = new StringBuilder();
        for (byte b : digest.digest()) value.append(String.format(java.util.Locale.ROOT, "%02x", b & 255));
        return value.toString();
    }
}
