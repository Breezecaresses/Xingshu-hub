package app.xingshu.installer;

import android.content.Context;
import android.os.Binder;
import android.os.ParcelFileDescriptor;
import java.io.*;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.concurrent.TimeUnit;
import java.util.regex.*;

/** Only this fixed, hash-pinned game cache is writable through the Binder API. */
public final class InstallerService extends IInstaller.Stub {
    private final int ownerUid;
    private final File target = new File("/sdcard/Android/data/com.feimo.astralparty/files/"+PayloadSpec.RELATIVE);
    private final File backups = new File("/data/local/tmp/xingshu-installer/"+PayloadSpec.VANILLA);
    public InstallerService(Context context) { ownerUid=context.getApplicationInfo().uid; }
    private void caller() { if (Binder.getCallingUid()!=ownerUid) throw new SecurityException("调用方不匹配"); }
    @Override public void destroy() {
        int uid=Binder.getCallingUid();
        if(uid!=ownerUid && uid!=2000 && uid!=0) throw new SecurityException("调用方不匹配");
        System.exit(0);
    }
    @Override public synchronized String inspect() {
        caller();
        try {
            checkGame(); String hash=hash(target); probe();
            return "游戏版本匹配，目录可读取并写入。\n当前缓存："+(hash.equals(PayloadSpec.PATCH)?"本版星枢":hash.equals(PayloadSpec.VANILLA)?"官方原版":known(hash)?"可升级的旧版星枢":"未知版本，禁止替换")
                +"\n"+(running()?"游戏仍在运行，请正常退出后安装。":"游戏已退出，可以安装或还原。");
        } catch(Exception e) { return "检查未通过："+friendly(e); }
    }
    @Override public synchronized String apply(boolean restore, ParcelFileDescriptor candidate, ParcelFileDescriptor vanilla) {
        caller();
        File staged=null, original=null, snapshot=null;
        try {
            checkGame();
            if (running()) throw new IOException("游戏仍在运行，请正常退出后重试，不会中断对局");
            String before=hash(target);
            if (!known(before)) throw new IOException("目标不是已验证的原版或星枢缓存。请使用官方资源修复，再进大厅更新后退出；未修改文件");
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
            snapshot=new File(backups,"before.__data"); copy(target,snapshot);
            if (!hash(snapshot).equals(before)) throw new IOException("备份期间缓存发生变化，未安装");
            if (running() || !hash(target).equals(before)) throw new IOException("游戏已启动或缓存已变化，请退出后重试");
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
            return (restore?"已还原官方原版":"已安装 / 更新星枢 "+PayloadSpec.VERSION)+"，SHA256 校验通过。\n备份保存在 /data/local/tmp/xingshu-installer。现在可以正常启动游戏。";
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
    private static void receive(ParcelFileDescriptor fd,File target,String sha) throws Exception {
        if(fd==null)throw new IOException("缺少安装文件");
        try(InputStream in=new ParcelFileDescriptor.AutoCloseInputStream(fd); FileOutputStream out=new FileOutputStream(target)) {
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
            return "系统仍拒绝目录读写。请确认 Shizuku 已授权；小米可检查开发者选项的“USB 调试（安全设置）”。仍受限时请使用电脑 ADB 安装，未要求关闭系统防护或清除游戏数据。\n"+message;
        return message;
    }
}
