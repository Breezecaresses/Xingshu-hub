package app.xingshu.installer;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.io.RandomAccessFile;
import java.nio.channels.FileLock;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.TimeUnit;
import java.util.regex.Pattern;

/** Multi-channel privileged installer engine running under ADB (UID 2000) or Shizuku. */
final class InstallerEngine {
    private static final String CATALOG_REL = "com.unity.addressables/catalog_3.3.0.json";

    String scanPackages() {
        try {
            String pmOut = command("/system/bin/pm", "list", "packages", "-3");
            List<String> candidates = GamePackageCatalog.parseThirdPartyPackages(pmOut);
            String psOut = "";
            try { psOut = command("/system/bin/ps", "-A"); } catch (Exception ignored) { }

            List<Map<String, Object>> items = new ArrayList<>();
            int limit = Math.min(candidates.size(), 32);
            for (int i = 0; i < limit; i++) {
                String pkg = candidates.get(i);
                String dump;
                try {
                    dump = command("/system/bin/dumpsys", "package", pkg);
                } catch (Exception ignored) {
                    continue;
                }
                String ver = GamePackageCatalog.extractVersionName(dump);
                if (ver.isEmpty()) continue;

                GamePackageCatalog.ChannelMeta meta = GamePackageCatalog.classifyPackage(pkg);
                File filesRoot = GamePackageCatalog.filesRoot(pkg);
                File catalog = new File(filesRoot, CATALOG_REL);
                boolean hasCatalog = catalog.isFile();
                boolean isRunning = Pattern.compile("(?m)\\s" + Pattern.quote(pkg) + "(?::[^\\s]+)?\\s*$")
                    .matcher(psOut).find();
                String installState = "version_mismatch";
                String resolvedRel = "";

                if ("3.3.0".equals(ver)) {
                    if (!hasCatalog) {
                        installState = "no_catalog";
                    } else {
                        try {
                            resolvedRel = AddressablesCatalog.resolveManagedRuntimeRelativePath(catalog);
                            File target = new File(filesRoot, resolvedRel);
                            if (!target.isFile()) {
                                installState = "no_bundle";
                            } else {
                                String hash = hash(target);
                                List<File> companions = CacheConflict.findCompanions(filesRoot, target);
                                if (hash.equals(PayloadSpec.PATCH) && companions.isEmpty()) {
                                    installState = "installed";
                                } else if (hash.equals(PayloadSpec.VANILLA) && companions.isEmpty()) {
                                    installState = "vanilla";
                                } else if (known(hash) && companions.isEmpty()) {
                                    installState = "upgrade";
                                } else if (CacheConflict.replaceableTarget(target, catalog, PayloadSpec.CATALOG, resolvedRel, PayloadSpec.RELATIVE)) {
                                    installState = "conflict";
                                } else {
                                    installState = "incompatible";
                                }
                            }
                        } catch (Exception ignored) {
                            installState = "bad_catalog";
                        }
                    }
                }

                Map<String, Object> item = new LinkedHashMap<>();
                item.put("packageName", pkg);
                item.put("label", meta.label);
                item.put("platform", meta.platform);
                item.put("versionName", ver);
                item.put("hasCatalog", hasCatalog);
                item.put("running", isRunning);
                item.put("installState", installState);
                item.put("resolvedRel", resolvedRel);
                items.add(item);
            }
            return MiniJson.stringify(Collections.singletonMap("packages", items));
        } catch (Exception e) {
            return MiniJson.stringify(Collections.singletonMap("error", friendly(e)));
        }
    }

    String inspect(String packageName) {
        try {
            String pkg = GamePackageCatalog.requireValidPackageName(packageName);
            GamePackageCatalog.ChannelMeta meta = GamePackageCatalog.classifyPackage(pkg);
            File filesRoot = GamePackageCatalog.filesRoot(pkg);
            File catalog = new File(filesRoot, CATALOG_REL);

            checkGame(pkg, meta);
            if (!catalog.isFile()) {
                throw new IOException("未找到 " + meta.label + "（" + pkg + "）的 catalog_3.3.0.json。\n请先打开该渠道游戏更新资源并进入大厅一次，再正常退出后检查。");
            }
            String resolvedRel = AddressablesCatalog.resolveManagedRuntimeRelativePath(catalog);
            File target = new File(filesRoot, resolvedRel);
            String hash = hash(target);
            probe(target);

            List<File> companions = CacheConflict.findCompanions(filesRoot, target);
            boolean targetConflict = !known(hash);
            String state;
            if (targetConflict || !companions.isEmpty()) {
                boolean replaceable = CacheConflict.replaceableTarget(
                    target, catalog, PayloadSpec.CATALOG, resolvedRel, PayloadSpec.RELATIVE);
                if (!replaceable) {
                    String catHash = catalog.isFile() ? CacheConflict.hash(catalog) : "缺失";
                    state = "未知版本，禁止替换；资源清单或容器与本版不匹配（" + meta.label
                        + "，catalog SHA256=" + catHash + "，target SHA256=" + hash
                        + "，bundle=" + resolvedRel + "），请先用官方资源修复。";
                } else {
                    String pluginName = CacheConflict.identifyPlugin(target, companions);
                    StringBuilder detail = new StringBuilder();
                    detail.append("同位置覆盖冲突（").append(pluginName).append("），可备份整理并删除替换\n");
                    detail.append("冲突文件SHA256：").append(hash);
                    if (!companions.isEmpty()) {
                        detail.append("\n检测到 ").append(companions.size()).append(" 个关联冲突文件：");
                        String prefix = filesRoot.getPath() + "/";
                        for (int i = 0; i < Math.min(companions.size(), 5); i++) {
                            String p = companions.get(i).getPath();
                            detail.append("\n  - ").append(p.startsWith(prefix) ? p.substring(prefix.length()) : companions.get(i).getName());
                        }
                    }
                    detail.append("\n清理并替换后原插件将停止生效，其他目录的 MOD 不会移动。");
                    state = detail.toString();
                }
            } else {
                state = hash.equals(PayloadSpec.PATCH) ? "本版星枢"
                    : hash.equals(PayloadSpec.VANILLA) ? "官方原版"
                    : "可升级的旧版星枢";
            }
            return "游戏版本匹配，目录可读取并写入。\n目标渠道：" + meta.label + "（" + pkg + "）\n定位资源：" + resolvedRel
                + "\n当前缓存：" + state + "\n"
                + (running(pkg) ? "游戏仍在运行，请正常退出后安装。" : "游戏已退出，可以安装或还原。");
        } catch (Exception e) {
            return "检查未通过：" + friendly(e);
        }
    }

    synchronized String apply(String packageName, boolean restore, String conflictHash,
            InputStream candidate, InputStream vanilla) {
        try {
            String pkg = GamePackageCatalog.requireValidPackageName(packageName);
            File backups = GamePackageCatalog.backupRoot(pkg);
            if (!backups.isDirectory() && !backups.mkdirs()) {
                throw new IOException("无法创建备份目录");
            }
            try (RandomAccessFile guard = new RandomAccessFile(new File(backups, "transaction.lock"), "rw");
                 FileLock lock = guard.getChannel().tryLock()) {
                if (lock == null) throw new IOException("另一个安装操作正在进行，请稍后重试");
                return applyLocked(pkg, backups, restore, conflictHash, candidate, vanilla);
            }
        } catch (Exception e) {
            return "未完成：" + friendly(e);
        }
    }

    private String applyLocked(String pkg, File backups, boolean restore, String conflictHash,
            InputStream candidate, InputStream vanilla) {
        File staged = null, original = null, snapshot = null;
        try {
            GamePackageCatalog.ChannelMeta meta = GamePackageCatalog.classifyPackage(pkg);
            File filesRoot = GamePackageCatalog.filesRoot(pkg);
            File catalog = new File(filesRoot, CATALOG_REL);

            checkGame(pkg, meta);
            if (running(pkg)) throw new IOException("游戏仍在运行，请正常退出后重试，不会中断对局");
            String resolvedRel = AddressablesCatalog.resolveManagedRuntimeRelativePath(catalog);
            File target = new File(filesRoot, resolvedRel);

            String before = hash(target);
            if (conflictHash != null && !conflictHash.equals(before)) {
                throw new IOException("冲突文件在确认后发生变化，请重新检查，未修改游戏");
            }
            List<File> companions = restore ? Collections.emptyList() : CacheConflict.findCompanions(filesRoot, target);
            boolean conflict = !known(before) || !companions.isEmpty();
            boolean replaceable = CacheConflict.replaceableTarget(
                target, catalog, PayloadSpec.CATALOG, resolvedRel, PayloadSpec.RELATIVE);
            if (conflict && (restore || conflictHash == null || !replaceable)) {
                throw new IOException("目标是其他覆盖文件、残留冲突插件或未知资源。请先检查冲突；仅资源清单匹配且确认备份删除冲突后才能安装，未修改文件");
            }
            probe(target);
            if (!backups.isDirectory() && !backups.mkdirs()) throw new IOException("无法创建备份目录");

            original = new File(backups, "vanilla.staging");
            receive(vanilla, original, PayloadSpec.VANILLA);
            staged = new File(backups, "candidate.staging");
            if (!restore) receive(candidate, staged, PayloadSpec.PATCH);

            File backup = new File(backups, "official.__data");
            if (!backup.exists()) copy(original, backup);
            if (!hash(backup).equals(PayloadSpec.VANILLA)) throw new IOException("原版备份校验失败，已停止");

            snapshot = CacheConflict.archive(target, backups, before);
            if (!hash(snapshot).equals(before)) throw new IOException("备份期间缓存发生变化，未安装");
            if (!companions.isEmpty()) CacheConflict.archiveAndDeleteCompanions(filesRoot, companions, snapshot.getParentFile());
            if (running(pkg) || !hash(target).equals(before)) throw new IOException("游戏已启动或缓存已变化，请退出后重试");

            String recheckedRel = AddressablesCatalog.resolveManagedRuntimeRelativePath(catalog);
            if (!recheckedRel.equals(resolvedRel) || !CacheConflict.replaceableTarget(
                    target, catalog, PayloadSpec.CATALOG, recheckedRel, PayloadSpec.RELATIVE)) {
                throw new IOException("官方资源清单已变化，请重新检查，未修改游戏");
            }

            File source = restore ? original : staged;
            String expected = restore ? PayloadSpec.VANILLA : PayloadSpec.PATCH;
            if (!before.equals(expected)) {
                try {
                    // Overwrite existing inode to preserve game UID ownership and SELinux context.
                    copy(source, target);
                    if (!hash(target).equals(expected)) throw new IOException("安装后 SHA256 校验失败");
                } catch (Exception writeError) {
                    copy(snapshot, target);
                    if (!hash(target).equals(before)) {
                        throw new IOException("写入和自动恢复均未通过校验，请保留备份并使用官方资源修复");
                    }
                    throw new IOException("写入失败，已恢复安装前缓存：" + writeError.getMessage());
                }
            }
            return (restore ? "已还原官方原版" : "已安装 / 更新星枢 " + PayloadSpec.VERSION)
                + "（" + meta.label + "），SHA256 校验通过。\n"
                + (conflict ? "同位置冲突插件及关联文件（共 " + companions.size() + " 个附加项）已备份删除并替换；其他模组未移动。\n" : "")
                + "安装前文件与记录：" + snapshot.getParent() + "\n现在可以从该渠道入口启动游戏。";
        } catch (Exception e) {
            return "未完成：" + friendly(e);
        } finally {
            if (staged != null) staged.delete();
            if (original != null) original.delete();
        }
    }

    String readChannelTokensJson() {
        try {
            String raw;
            try {
                raw = command("/system/bin/logcat", "-d", "-t", "4000", "-s", "BN_DEBUG:V", "Unity:V");
            } catch (Exception ignored) {
                raw = command("/system/bin/logcat", "-d", "-t", "4000", "-s", "BN_DEBUG:V");
            }
            List<BnSdkTokenExtractor.ChannelToken> tokens = BnSdkTokenExtractor.extractFromLogcat(raw);
            List<Map<String, Object>> out = new ArrayList<>(tokens.size());
            for (BnSdkTokenExtractor.ChannelToken tok : tokens) {
                Map<String, Object> item = new LinkedHashMap<>();
                item.put("platform", tok.platform);
                item.put("appId", tok.appId);
                item.put("label", tok.label);
                item.put("userId", tok.userId);
                item.put("sdkToken", tok.sdkToken);
                item.put("nativeSupported", tok.nativeSupported);
                out.add(item);
            }
            return MiniJson.stringify(Collections.singletonMap("tokens", out));
        } catch (Exception e) {
            return MiniJson.stringify(Collections.singletonMap("error", friendly(e)));
        }
    }

    String launchGame(String packageName) {
        try {
            String pkg = GamePackageCatalog.requireValidPackageName(packageName);
            GamePackageCatalog.ChannelMeta meta = GamePackageCatalog.classifyPackage(pkg);
            try {
                String resolved = command("/system/bin/cmd", "package", "resolve-activity", "--brief", pkg);
                String component = "";
                for (String line : resolved.split("[\\r\\n]+")) {
                    String t = line.trim();
                    if (t.startsWith(pkg + "/") && t.length() <= 200 && !t.contains(" ")) {
                        component = t;
                    }
                }
                if (!component.isEmpty()) {
                    command("/system/bin/am", "start", "--user", "0", "-n", component);
                    return "已启动 " + meta.label + "（" + pkg + "）";
                }
            } catch (Exception ignored) { }
            command("/system/bin/monkey", "-p", pkg, "-c", "android.intent.category.LAUNCHER", "1");
            return "已启动 " + meta.label + "（" + pkg + "）";
        } catch (Exception e) {
            return "启动游戏失败：" + friendly(e);
        }
    }

    private boolean known(String hash) {
        return hash.equals(PayloadSpec.VANILLA) || hash.equals(PayloadSpec.PATCH)
            || Arrays.asList(PayloadSpec.PREVIOUS).contains(hash);
    }

    private void checkGame(String pkg, GamePackageCatalog.ChannelMeta meta) throws Exception {
        String dump = command("/system/bin/dumpsys", "package", pkg);
        String ver = GamePackageCatalog.extractVersionName(dump);
        if (!"3.3.0".equals(ver)) {
            throw new IOException("仅适配 3.3.0 版本（" + meta.label + " " + pkg + " 当前为 "
                + (ver.isEmpty() ? "未安装/未检测到版本" : ver) + "），请勿使用于其他版本或分身目录");
        }
    }

    private void probe(File target) throws Exception {
        try (RandomAccessFile test = new RandomAccessFile(target, "rw")) {
            test.length();
        }
    }

    private boolean running(String pkg) throws Exception {
        String ps = command("/system/bin/ps", "-A");
        return Pattern.compile("(?m)\\s" + Pattern.quote(pkg) + "(?::[^\\s]+)?\\s*$").matcher(ps).find();
    }

    private static String command(String... args) throws Exception {
        Process p = new ProcessBuilder(args).redirectErrorStream(true).start();
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        Thread reader = new Thread(() -> {
            try {
                byte[] b = new byte[8192];
                int n;
                while ((n = p.getInputStream().read(b)) != -1) bytes.write(b, 0, n);
            } catch (IOException ignored) { }
        });
        reader.start();
        if (!p.waitFor(15, TimeUnit.SECONDS)) {
            p.destroyForcibly();
            throw new IOException("系统检查超时");
        }
        reader.join(2000);
        if (p.exitValue() != 0) throw new IOException("系统拒绝检查游戏权限");
        return bytes.toString("UTF-8");
    }

    private static void receive(InputStream fd, File target, String sha) throws Exception {
        if (fd == null) throw new IOException("缺少安装文件");
        try (InputStream in = fd; FileOutputStream out = new FileOutputStream(target)) {
            transfer(in, out);
            out.getFD().sync();
        }
        if (!hash(target).equals(sha)) throw new IOException("安装包损坏，SHA256 不匹配，未修改游戏");
    }

    private static void copy(File source, File destination) throws IOException {
        try (InputStream in = new FileInputStream(source); FileOutputStream out = new FileOutputStream(destination)) {
            transfer(in, out);
            out.getFD().sync();
        }
    }

    private static void transfer(InputStream in, OutputStream out) throws IOException {
        byte[] b = new byte[65536];
        int n;
        while ((n = in.read(b)) != -1) out.write(b, 0, n);
    }

    private static String hash(File file) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream in = new FileInputStream(file)) {
            byte[] b = new byte[65536];
            int n;
            while ((n = in.read(b)) != -1) digest.update(b, 0, n);
        }
        StringBuilder hex = new StringBuilder();
        for (byte b : digest.digest()) hex.append(String.format(Locale.ROOT, "%02x", b & 255));
        return hex.toString();
    }

    private static String friendly(Exception error) {
        String message = String.valueOf(error.getMessage());
        if (message.contains("Permission denied") || message.contains("EACCES") || message.contains("Operation not permitted")) {
            return "系统仍拒绝目录读写。请检查无线调试授权；小米可检查开发者选项的“USB 调试（安全设置）”。未修改游戏，请保留检查结果反馈。\n" + message;
        }
        return message;
    }
}
