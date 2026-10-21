package app.xingshu.installer;

import android.content.Context;
import android.os.Build;
import io.github.muntashirakon.adb.AbsAdbConnectionManager;
import io.github.muntashirakon.adb.AdbStream;
import java.io.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.security.PrivateKey;
import java.security.cert.Certificate;
import java.util.Base64;
import java.util.concurrent.*;

/** Loopback only; never accepts remote hosts, shell commands or file paths from the UI. */
final class LocalAdbInstaller implements Closeable {
    private final Context context;
    private final Manager manager;
    private final ScheduledExecutorService timer=Executors.newSingleThreadScheduledExecutor();
    private static final class Manager extends AbsAdbConnectionManager {
        final PhoneIdentity identity;
        Manager(Context context) throws Exception {
            identity=new PhoneIdentity(context); setApi(Build.VERSION.SDK_INT);
            setHostAddress("127.0.0.1"); setTimeout(15,TimeUnit.SECONDS);
        }
        protected PrivateKey getPrivateKey() { return identity.key; }
        protected Certificate getCertificate() { return identity.certificate; }
        protected String getDeviceName() { return "XingShu-Phone"; }
    }
    LocalAdbInstaller(Context context) throws Exception { this.context=context.getApplicationContext(); manager=new Manager(context); }
    boolean connected() { return manager.isConnected(); }
    void pair(int port,String code) throws Exception {
        requireAndroid11();
        if(!manager.pair("127.0.0.1",port,code)) throw new IOException("配对未成功，请重新打开配对码弹窗");
    }
    void connect(int port) throws Exception {
        requireAndroid11(); manager.disconnect();
        try {
            if(!manager.connect("127.0.0.1",port)) throw new IOException("连接超时，请核对连接端口");
            String uid=shell("id -u",20000).trim();
            if(!uid.equals("2000")) throw new IOException("未取得本机调试权限");
        } catch(Exception e) { manager.disconnect(); throw e; }
    }
    String inspect() throws Exception { return operation("inspect"); }
    String apply(boolean restore) throws Exception {
        // Preflight before sending payloads; the engine repeats every gate before touching the target.
        String preflight=inspect();
        if(!preflight.startsWith("游戏版本匹配") || preflight.contains("游戏仍在运行") || preflight.contains("未知版本")) return preflight;
        shell("mkdir -p "+quote(LocalInstallerMain.STAGING),20000);
        try {
            push("vanilla.__data"); if(!restore) push("candidate.__data");
            return operation(restore?"restore":"install");
        } finally {
            try { shell("rm -f "+quote(LocalInstallerMain.STAGING+"/vanilla.__data")+" "+quote(LocalInstallerMain.STAGING+"/candidate.__data"),20000); } catch(Exception ignored) { }
        }
    }
    private String operation(String operation) throws Exception {
        String output=shell("CLASSPATH="+quote(context.getApplicationInfo().sourceDir)+
            " /system/bin/app_process / app.xingshu.installer.LocalInstallerMain "+operation,180000);
        String marker="XINGSHU_RESULT:";
        for(String line:output.split("[\\r\\n]+")) {
            if(line.startsWith(marker)) return new String(Base64.getDecoder().decode(line.substring(marker.length()).trim()),StandardCharsets.UTF_8);
        }
        throw new IOException("授权进程未返回结果："+output.substring(0,Math.min(output.length(),1200)));
    }
    private String shell(String command,long timeout) throws Exception {
        // Bound OPEN as well as reads. Closing the transport only occurs at the deadline.
        ScheduledFuture<?> deadline=timer.schedule(() -> { try { manager.disconnect(); } catch(Exception ignored) { } },timeout,TimeUnit.MILLISECONDS);
        try(AdbStream stream=manager.openStream("shell:"+command); InputStream in=stream.openInputStream()) {
            ByteArrayOutputStream bytes=new ByteArrayOutputStream(); byte[] b=new byte[4096]; int n;
            while((n=in.read(b))!=-1) { bytes.write(b,0,n); if(bytes.size()>1024*1024)throw new IOException("检查输出超出限制"); }
            return bytes.toString("UTF-8").replace("\r","");
        } finally { deadline.cancel(false); }
    }
    private void push(String asset) throws Exception {
        ScheduledFuture<?> deadline=timer.schedule(() -> { try { manager.disconnect(); } catch(Exception ignored) { } },90000,TimeUnit.MILLISECONDS);
        try(AdbStream stream=manager.openStream("sync:"); InputStream assetIn=context.getAssets().open(asset)) {
            OutputStream out=stream.openOutputStream(); DataInputStream reply=new DataInputStream(stream.openInputStream());
            byte[] path=(LocalInstallerMain.STAGING+"/"+asset+",33152").getBytes(StandardCharsets.UTF_8);
            packet(out,"SEND",path.length); out.write(path); out.flush();
            byte[] data=new byte[4096]; int n;
            while((n=assetIn.read(data))!=-1) { packet(out,"DATA",n); out.write(data,0,n); out.flush(); }
            packet(out,"DONE",(int)(System.currentTimeMillis()/1000)); out.flush();
            byte[] header=new byte[8]; reply.readFully(header);
            String id=new String(header,0,4,StandardCharsets.US_ASCII);
            int size=ByteBuffer.wrap(header,4,4).order(ByteOrder.LITTLE_ENDIAN).getInt();
            if(!id.equals("OKAY")) {
                if(size<0||size>8192)throw new IOException("传送文件响应无效");
                byte[] error=new byte[size]; reply.readFully(error); throw new IOException("传送文件失败："+new String(error,StandardCharsets.UTF_8));
            }
        } finally { deadline.cancel(false); }
    }
    private static void packet(OutputStream out,String id,int size) throws IOException {
        out.write(ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN).put(id.getBytes(StandardCharsets.US_ASCII)).putInt(size).array());
    }
    private static String quote(String value) { return "'"+value.replace("'","'\\''")+"'"; }
    private static void requireAndroid11() throws IOException { if(Build.VERSION.SDK_INT<30)throw new IOException("本机无线配对需要 Android 11 或更新版本"); }
    public void close() throws IOException { timer.shutdownNow(); manager.disconnect(); }
}
