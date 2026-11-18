package app.xingshu.installer;

import java.io.BufferedReader;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.io.RandomAccessFile;
import java.nio.channels.FileLock;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Deque;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.TimeUnit;
import java.util.regex.Matcher;
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
                            resolvedRel = AddressablesCatalog.resolveManagedRuntimeRelativePath(filesRoot, catalog);
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
            String resolvedRel = AddressablesCatalog.resolveManagedRuntimeRelativePath(filesRoot, catalog);
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
            String resolvedRel = AddressablesCatalog.resolveManagedRuntimeRelativePath(filesRoot, catalog);
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

            String recheckedRel = AddressablesCatalog.resolveManagedRuntimeRelativePath(filesRoot, catalog);
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

    private static final Pattern PS_LINE_PATTERN = Pattern.compile(
        "^\\S+\\s+([0-9]{1,7})\\s+.*\\s(\\S+)\\s*$"
    );
    private static final Pattern APPID_IN_NAME_PATTERN = Pattern.compile("^(?:java|native)_(1100[0-9]{5})\\.log$");

    String readChannelTokensJson() {
        try {
            StringBuilder combined = new StringBuilder();

            // Source 1: query full logcat ring buffer for BnSdk tags without -t 4000 / Unity:V noise.
            String tagLogs = commandBestEffort(1500 * 1024,
                "/system/bin/logcat", "-d", "-b", "main", "-s",
                "BN_DEBUG:V", "BN_UNITY:V", "BN_WARN:V", "Ev_CRASH_OS:V");
            if (!hasSdkSignalLine(tagLogs)) {
                String defaultBufLogs = commandBestEffort(1500 * 1024,
                    "/system/bin/logcat", "-d", "-s",
                    "BN_DEBUG:V", "BN_UNITY:V", "BN_WARN:V", "Ev_CRASH_OS:V");
                if (!defaultBufLogs.isEmpty()) {
                    combined.append(defaultBufLogs).append('\n');
                }
            } else {
                combined.append(tagLogs).append('\n');
            }

            // Source 2: scan running candidate game PIDs with --pid filtering (fallback for ROMs with tag quirks).
            String psOut = commandBestEffort(512 * 1024, "/system/bin/ps", "-A");
            if (psOut.trim().isEmpty()) {
                psOut = commandBestEffort(512 * 1024, "/system/bin/ps");
            }
            Map<String, String> runningPidToPkg = findRunningCandidatePids(psOut);
            Set<String> runningAppIds = new LinkedHashSet<>();
            for (Map.Entry<String, String> entry : runningPidToPkg.entrySet()) {
                String pid = entry.getKey();
                String pkg = entry.getValue();
                String appId = BnSdkTokenExtractor.appIdForPackage(pkg);
                if (!appId.isEmpty()) runningAppIds.add(appId);
                combined.append("D/XINGSHU_PS(").append(pid).append("): package=").append(pkg).append('\n');
                String pidLogs = readPidRelevantLogcat(pid);
                if (!pidLogs.isEmpty()) {
                    combined.append(pidLogs).append('\n');
                }
            }

            // Source 3: installed package list + on-disk BnSdk/ibingniao hints & logs.
            String pmOut = commandBestEffort(128 * 1024, "/system/bin/pm", "list", "packages", "-3");
            List<String> installedPkgs = GamePackageCatalog.parseThirdPartyPackages(pmOut);
            Set<String> installedNativeAppIds = new LinkedHashSet<>();
            for (String pkg : installedPkgs) {
                String appId = BnSdkTokenExtractor.appIdForPackage(pkg);
                if (!appId.isEmpty()) installedNativeAppIds.add(appId);
                appendOnDiskBnSdkEvidence(pkg, combined);
            }
            appendIbingniaoPeriodHints(combined);

            String fallbackAppId = "";
            if (runningAppIds.size() == 1) {
                fallbackAppId = runningAppIds.iterator().next();
            } else if (installedNativeAppIds.size() == 1) {
                fallbackAppId = installedNativeAppIds.iterator().next();
            }

            List<BnSdkTokenExtractor.ChannelToken> tokens =
                BnSdkTokenExtractor.extractFromLogcat(combined.toString(), fallbackAppId);
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

    private static boolean hasSdkSignalLine(String text) {
        if (text == null || text.isEmpty()) return false;
        return text.contains("BN_DEBUG")
            || text.contains("BN_UNITY")
            || text.contains("BN_WARN")
            || text.contains("accessToken")
            || text.contains("userSqlEntity");
    }

    private static boolean isRelevantSdkLogLine(String line) {
        if (line == null || line.isEmpty()) return false;
        return line.contains("BN_")
            || line.contains("accessToken")
            || line.contains("access_token")
            || line.contains("userSqlEntity")
            || line.contains("[LoginSDK]")
            || line.contains("RoleInfo")
            || line.contains("11000")
            || line.contains("feimogames")
            || line.contains("com.feimo.");
    }

    private static Map<String, String> findRunningCandidatePids(String psOut) {
        Map<String, String> result = new LinkedHashMap<>();
        if (psOut == null || psOut.isEmpty()) return result;
        for (String rawLine : psOut.split("[\\r\\n]+")) {
            Matcher m = PS_LINE_PATTERN.matcher(rawLine.trim());
            if (!m.matches()) continue;
            String pid = m.group(1);
            String procName = m.group(2);
            int colon = procName.indexOf(':');
            String basePkg = colon > 0 ? procName.substring(0, colon) : procName;
            if (GamePackageCatalog.isCandidatePackageName(basePkg) && colon < 0) {
                result.put(pid, basePkg);
            }
        }
        return result;
    }

    private static String readPidRelevantLogcat(String pid) {
        if (pid == null || !pid.matches("[0-9]{1,7}")) return "";
        StringBuilder out = new StringBuilder();
        try {
            Process p = new ProcessBuilder("/system/bin/logcat", "-d", "--pid=" + pid)
                .redirectErrorStream(true)
                .start();
            final int maxChars = 256 * 1024;
            Thread reader = new Thread(() -> {
                try (BufferedReader br = new BufferedReader(
                        new InputStreamReader(p.getInputStream(), StandardCharsets.UTF_8))) {
                    String line;
                    while ((line = br.readLine()) != null) {
                        if (isRelevantSdkLogLine(line) && out.length() < maxChars) {
                            out.append(line).append('\n');
                        }
                    }
                } catch (IOException ignored) { }
            });
            reader.start();
            if (!p.waitFor(10, TimeUnit.SECONDS)) {
                p.destroyForcibly();
            }
            reader.join(2000);
        } catch (Exception ignored) { }
        return out.toString();
    }

    private static void appendOnDiskBnSdkEvidence(String pkg, StringBuilder combined) {
        try {
            File filesRoot = GamePackageCatalog.filesRoot(pkg);
            if (!filesRoot.isDirectory()) return;
            File[] rootEntries = filesRoot.listFiles();
            if (rootEntries != null) {
                for (File f : rootEntries) {
                    if (!f.isFile()) continue;
                    Matcher m = APPID_IN_NAME_PATTERN.matcher(f.getName());
                    if (m.matches()) {
                        combined.append("D/XINGSHU_DISK: ")
                            .append(f.getPath())
                            .append(" package=").append(pkg)
                            .append('\n');
                    }
                }
            }
            File bnSdkDir = new File(filesRoot, "BnSdk");
            if (!bnSdkDir.isDirectory()) return;
            List<File> logFiles = new ArrayList<>();
            collectBnSdkLogs(bnSdkDir, 0, logFiles);
            logFiles.sort((a, b) -> Long.compare(b.lastModified(), a.lastModified()));
            int scanned = 0;
            for (File logFile : logFiles) {
                if (scanned >= 8) break;
                if (logFile.length() <= 0 || logFile.length() > 512 * 1024) continue;
                scanned++;
                appendFilteredLogFile(logFile, combined);
            }
        } catch (Exception ignored) { }
    }

    private static void collectBnSdkLogs(File dir, int depth, List<File> out) {
        if (dir == null || depth > 3 || out.size() >= 32) return;
        File[] children = dir.listFiles();
        if (children == null) return;
        for (File child : children) {
            if (child.isDirectory()) {
                collectBnSdkLogs(child, depth + 1, out);
            } else if (child.isFile() && child.getName().toLowerCase(Locale.ROOT).endsWith(".log")) {
                out.add(child);
                if (out.size() >= 32) return;
            }
        }
    }

    private static void appendFilteredLogFile(File file, StringBuilder combined) {
        combined.append("D/XINGSHU_DISK_FILE: ").append(file.getPath()).append('\n');
        try (BufferedReader br = new BufferedReader(
                new InputStreamReader(new FileInputStream(file), StandardCharsets.UTF_8))) {
            String line;
            int budget = 128 * 1024;
            while ((line = br.readLine()) != null && budget > 0) {
                if (isRelevantSdkLogLine(line)) {
                    combined.append(line).append('\n');
                    budget -= line.length() + 1;
                }
            }
        } catch (IOException ignored) { }
    }

    private static void appendIbingniaoPeriodHints(StringBuilder combined) {
        try {
            File periodRoot = new File("/sdcard/ibingniao/period");
            if (!periodRoot.isDirectory()) return;
            File[] appDirs = periodRoot.listFiles();
            if (appDirs == null) return;
            for (File appDir : appDirs) {
                if (!appDir.isDirectory() || !appDir.getName().matches("1100[0-9]{5}")) continue;
                File[] uidEntries = appDir.listFiles();
                if (uidEntries != null && uidEntries.length > 0) {
                    combined.append("D/XINGSHU_DISK: /period/")
                        .append(appDir.getName()).append('/').append(uidEntries[0].getName()).append('\n');
                }
            }
        } catch (Exception ignored) { }
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

    String writeGameHandoff(String packageName, String payloadJson) {
        try {
            String pkg = GamePackageCatalog.requireValidPackageName(packageName);
            if (payloadJson == null || payloadJson.isEmpty() || payloadJson.length() > 256 || payloadJson.indexOf('\0') >= 0) {
                throw new IllegalArgumentException("同步凭据无效");
            }
            File filesRoot = GamePackageCatalog.filesRoot(pkg);
            if (!filesRoot.isDirectory()) {
                return "SKIP:NO_DIR";
            }
            File tmp = new File(filesRoot, "xingshu_handoff.json.tmp");
            File target = new File(filesRoot, "xingshu_handoff.json");
            try (FileOutputStream out = new FileOutputStream(tmp)) {
                out.write(payloadJson.getBytes(StandardCharsets.UTF_8));
                out.getFD().sync();
            }
            if (!tmp.renameTo(target)) {
                copy(tmp, target);
                tmp.delete();
            }
            return "OK";
        } catch (Exception e) {
            return "ERR:" + friendly(e);
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

    private static String commandBestEffort(int maxTailBytes, String... args) {
        try {
            Process p = new ProcessBuilder(args).redirectErrorStream(true).start();
            Deque<byte[]> chunks = new ArrayDeque<>();
            final int[] total = { 0 };
            Thread reader = new Thread(() -> {
                try {
                    byte[] b = new byte[8192];
                    int n;
                    while ((n = p.getInputStream().read(b)) != -1) {
                        chunks.addLast(Arrays.copyOf(b, n));
                        total[0] += n;
                        while (total[0] > maxTailBytes && chunks.size() > 1) {
                            total[0] -= chunks.removeFirst().length;
                        }
                    }
                } catch (IOException ignored) { }
            });
            reader.start();
            if (!p.waitFor(12, TimeUnit.SECONDS)) {
                p.destroyForcibly();
            }
            reader.join(2000);
            if (reader.isAlive()) return "";
            ByteArrayOutputStream out = new ByteArrayOutputStream(Math.max(256, total[0]));
            for (byte[] chunk : chunks) {
                out.write(chunk, 0, chunk.length);
            }
            return out.toString("UTF-8");
        } catch (Exception ignored) {
            return "";
        }
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
