package app.xingshu.installer;

import java.io.*;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.concurrent.TimeUnit;
import java.util.regex.*;

/** Shared hash-pinned cache transaction for local ADB and Shizuku. */
final class InstallerEngine {
    private final File target = new File("/sdcard/Android/data/com.feimo.astralparty/files/"+PayloadSpec.RELATIVE);
    private final File backups = new File("/data/local/tmp/xingshu-installer/"+PayloadSpec.VANILLA);
    private final File catalog = new File("/sdcard/Android/data/com.feimo.astralparty/files/com.unity.addressables/catalog_3.3.0.json");
    public synchronized String inspect() {
        try {
            checkGame(); String hash=hash(target); probe();
            String state=hash.equals(PayloadSpec.PATCH)?"本版星枢":hash.equals(PayloadSpec.VANILLA)?"官方原版":known(hash)?"可升级的旧版星枢":
                CacheConflict.replaceable(target,catalog,PayloadSpec.CATALOG)?"同位置覆盖冲突，可备份整理\n冲突文件SHA256："+hash+"\n可能是其他插件或 MOD；整理后这些修改不再生效，其他位置的模组不会移动。":"未知版本，禁止替换；资源清单与本版不匹配，请先用官方资源修复。";
            return "游戏版本匹配，目录可读取并写入。\n当前缓存："+state
                +"\n"+(running()?"游戏仍在运行，请正常退出后安装。":"游戏已退出，可以安装或还原。");
        } catch(Exception e) { return "检查未通过："+friendly(e); }
    }
    public synchronized String apply(boolean restore, String conflictHash, InputStream candidate, InputStream vanilla) {
        try {
            if (!backups.isDirectory() && !backups.mkdirs()) throw new IOException("无法创建备份目录");
            try (RandomAccessFile guard = new RandomAccessFile(new File(backups,"transaction.lock"),"rw");
                 java.nio.channels.FileLock lock = guard.getChannel().tryLock()) {
                if (lock == null) throw new IOException("另一个安装操作正在进行，请稍后重试");
                return applyLocked(restore,conflictHash,candidate,vanilla);
            }
        } catch (Exception e) { return "未完成："+friendly(e); }
    }
    private String applyLocked(boolean restore, String conflictHash, InputStream candidate, InputStream vanilla) {
        File staged=null, original=null, snapshot=null;
        try {
            checkGame();
            if (running()) throw new IOException("游戏仍在运行，请正常退出后重试，不会中断对局");
            String before=hash(target);
            if (conflictHash!=null && !conflictHash.equals(before)) throw new IOException("冲突文件在确认后发生变化，请重新检查，未修改游戏");
            boolean conflict=!known(before);
            if (conflict && (restore || conflictHash==null || !CacheConflict.replaceable(target,catalog,PayloadSpec.CATALOG)))
                throw new IOException("目标是其他覆盖文件或未知资源。请先检查冲突；仅资源清单匹配且确认备份后才能整理安装，未修改文件");
            probe();
            if (!backups.isDirectory() && !backups.mkdirs()) throw new IOException("无法创建备份目录");
            original=new File(backups,"vanilla.staging");
            receive(vanilla,original,PayloadSpec.VANILLA);
            staged=new File(backups,"candidate.staging");
            if(!restore) receive(candidate,staged,PayloadSpec.PATCH);
            // Preserve both the verified vanilla recovery and the exact pre-install bytes.
            File backup=new File(backups,"official.__data");
            if (!backup.exists()) copy(original,backup);
            if (!hash(backup).equals(PayloadSpec.VANILLA)) throw new IOException("原版备份校验失败，已停止");
            snapshot=CacheConflict.archive(target,backups,before);
            if (!hash(snapshot).equals(before)) throw new IOException("备份期间缓存发生变化，未安装");
            if (running() || !hash(target).equals(before)) throw new IOException("游戏已启动或缓存已变化，请退出后重试");
            if (!CacheConflict.replaceable(target,catalog,PayloadSpec.CATALOG)) throw new IOException("官方资源清单已变化，请重新检查，未修改游戏");
            File source=restore?original:staged;
            String expected=restore?PayloadSpec.VANILLA:PayloadSpec.PATCH;
            try {
                // Overwrite the existing inode to preserve the game's ownership and mode.
                copy(source,target);
                if (!hash(target).equals(expected)) throw new IOException("安装后 SHA256 校验失败");
            } catch(Exception writeError) {
                copy(snapshot,target);
                if (!hash(target).equals(before)) throw new IOException("写入和自动恢复均未通过校验，请保留备份并使用官方资源修复");
                throw new IOException("写入失败，已恢复安装前缓存："+writeError.getMessage());
            }
            return (restore?"已还原官方原版":"已安装 / 更新星枢 "+PayloadSpec.VERSION)+"，SHA256 校验通过。\n"+
                (conflict?"同位置覆盖文件已整理备份；原有修改不再生效，其他模组未移动。\n":"")+
                "安装前文件与记录："+snapshot.getParent()+"\n现在可以正常启动游戏。";
        } catch(Exception e) { return "未完成："+friendly(e); }
        finally { if(staged!=null)staged.delete(); if(original!=null)original.delete(); }
    }
    private boolean known(String hash) {
        return hash.equals(PayloadSpec.VANILLA)||hash.equals(PayloadSpec.PATCH)||Arrays.asList(PayloadSpec.PREVIOUS).contains(hash);
    }
    private void checkGame() throws Exception {
        String dump=command("/system/bin/dumpsys","package","com.feimo.astralparty");
        Matcher v=Pattern.compile("(?m)^\\s*versionName=(\\S+)\\s*$").matcher(dump);
        if (!v.find() || !v.group(1).equals("3.3.0")) throw new IOException("只适配官方游戏 3.3.0，请勿使用于其他版本或分身目录");
        if (!target.isFile()) throw new IOException("未找到此版游戏资源。请在官方游戏完成更新并进入大厅一次，再退出安装");
        if (!CacheConflict.replaceable(target,catalog,PayloadSpec.CATALOG)) throw new IOException("官方资源清单或容器与本版不匹配，请完成官方资源修复并使用对应版本的助手");
        File files=new File("/sdcard/Android/data/com.feimo.astralparty/files");
        if (!target.getCanonicalPath().startsWith(files.getCanonicalPath()+"/com.unity.addressables/AssetBundles/"))
                throw new IOException("缓存路径校验失败");
    }
    private void probe() throws IOException {
        // Some Android builds allow overwriting an existing cache but deny
        // creating a sibling. Opening without truncation tests the needed right.
        try (RandomAccessFile probe=new RandomAccessFile(target,"rw")) { probe.getFD().sync(); }
    }
    private boolean running() throws Exception {
        String ps=command("/system/bin/ps","-A");
        return Pattern.compile("(?m)\\scom\\.feimo\\.astralparty(?::[^\\s]+)?\\s*$").matcher(ps).find();
    }
    private static String command(String...args) throws Exception {
        Process p=new ProcessBuilder(args).redirectErrorStream(true).start();
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();
        Thread reader=new Thread(() -> { try { byte[] b=new byte[8192]; int n; while((n=p.getInputStream().read(b))!=-1)bytes.write(b,0,n); } catch(IOException ignored){} });
        reader.start();
        if(!p.waitFor(15,TimeUnit.SECONDS)) { p.destroyForcibly(); throw new IOException("系统检查超时"); }
        reader.join(2000);
        if(p.exitValue()!=0) throw new IOException("系统拒绝检查游戏权限");
        return bytes.toString("UTF-8");
    }
    private static void receive(InputStream fd,File target,String sha) throws Exception {
        if(fd==null)throw new IOException("缺少安装文件");
        try(InputStream in=fd; FileOutputStream out=new FileOutputStream(target)) {
            transfer(in,out); out.getFD().sync();
        }
        if(!hash(target).equals(sha))throw new IOException("安装包损坏，SHA256 不匹配，未修改游戏");
    }
    private static void copy(File source,File destination) throws IOException {
        try(InputStream in=new FileInputStream(source); FileOutputStream out=new FileOutputStream(destination)) {
            transfer(in,out); out.getFD().sync();
        }
    }
    private static void transfer(InputStream in,OutputStream out) throws IOException {
        byte[] b=new byte[65536]; int n; while((n=in.read(b))!=-1)out.write(b,0,n);
    }
    private static String hash(File file) throws Exception {
        MessageDigest digest=MessageDigest.getInstance("SHA-256");
        try(InputStream in=new FileInputStream(file)) { byte[] b=new byte[65536]; int n; while((n=in.read(b))!=-1)digest.update(b,0,n); }
        StringBuilder hex=new StringBuilder(); for(byte b:digest.digest())hex.append(String.format(java.util.Locale.ROOT,"%02x",b&255)); return hex.toString();
    }
    private static String friendly(Exception error) {
        String message=String.valueOf(error.getMessage());
        if(message.contains("Permission denied")||message.contains("EACCES")||message.contains("Operation not permitted"))
            return "系统仍拒绝目录读写。请检查无线调试授权；小米可检查开发者选项的“USB 调试（安全设置）”。未修改游戏，请保留检查结果反馈。\n"+message;
        return message;
    }
}
