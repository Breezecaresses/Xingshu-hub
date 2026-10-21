package app.xingshu.installer;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.*;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.*;
import android.view.View;
import android.widget.*;
import java.io.*;
import java.util.concurrent.*;
import rikka.shizuku.Shizuku;

public final class MainActivity extends Activity {
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private TextView status, log;
    private Button install, restore, connect, inspect;
    private IInstaller remote;
    private boolean binding, busy, destroyed;
    private String lastResult="";
    private final Shizuku.UserServiceArgs serviceArgs = new Shizuku.UserServiceArgs(
        new ComponentName(BuildConfig.APPLICATION_ID, InstallerService.class.getName()))
        .daemon(false).processNameSuffix("installer").debuggable(false).version(BuildConfig.VERSION_CODE);
    private final Shizuku.OnBinderReceivedListener received = () -> runOnUiThread(this::refresh);
    private final Shizuku.OnBinderDeadListener dead = () -> runOnUiThread(() -> { remote = null; binding = false; refresh(); });
    private final Shizuku.OnRequestPermissionResultListener permission = (code, result) -> runOnUiThread(() -> {
        if (result == PackageManager.PERMISSION_GRANTED) connect(); else write("未授权。请在 Shizuku 的应用管理中允许星枢安装助手。");
    });
    private final ServiceConnection connection = new ServiceConnection() {
        public void onServiceConnected(ComponentName name, IBinder binder) {
            remote = IInstaller.Stub.asInterface(binder); binding = false; refresh(); inspect();
        }
        public void onServiceDisconnected(ComponentName name) { remote = null; binding = false; refresh(); }
    };

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(Color.rgb(255,35,143));
        getWindow().setNavigationBarColor(Color.rgb(255,246,222));
        ScrollView scroll = new ScrollView(this); scroll.setFillViewport(true);
        LinearLayout page = new LinearLayout(this); page.setOrientation(1); page.setPadding(dp(22),dp(24),dp(22),dp(24));
        page.setBackgroundColor(Color.rgb(255,246,222));
        page.setOnApplyWindowInsetsListener((view,insets) -> {
            view.setPadding(dp(22)+insets.getSystemWindowInsetLeft(),dp(24)+insets.getSystemWindowInsetTop(),
                dp(22)+insets.getSystemWindowInsetRight(),dp(24)+insets.getSystemWindowInsetBottom());
            return insets;
        });
        scroll.addView(page); setContentView(scroll);
        TextView title = text("星枢 · 手机安装助手",25); title.setTypeface(null,Typeface.BOLD); page.addView(title);
        page.addView(text("安卓预览更新  ·  " + BuildConfig.VERSION_NAME,12));
        page.addView(text("先完成官方游戏资源更新并进过大厅，再正常退出游戏。助手会核对版本、备份缓存并校验写入。",15));
        status = text("检查 Shizuku 状态…",16); page.addView(status);
        connect = button(page,"① 连接 / 授权 Shizuku",false, v -> connect());
        inspect = button(page,"② 检查游戏与目录权限",true, v -> inspect());
        install = button(page,"安装 / 更新星枢",false, v -> execute(false));
        restore = button(page,"还原官方原版",true, v -> new AlertDialog.Builder(this)
            .setMessage("将恢复此版本的官方程序集缓存，保留账号与设置。请先正常退出游戏。")
            .setNegativeButton("取消",null).setPositiveButton("还原",(d,w) -> execute(true)).show());
        button(page,"打开 Shizuku",true, v -> {
            Intent launch = getPackageManager().getLaunchIntentForPackage("moe.shizuku.privileged.api");
            if (launch != null) startActivity(launch); else open("https://shizuku.rikka.app/download/");
        });
        button(page,"查看手机授权步骤",true, v -> open("https://shizuku.rikka.app/guide/setup/"));
        page.addView(text("首次需要在 Shizuku 中通过无线调试启动服务并授权本助手。手机重启后通常需要重新启动 Shizuku。系统仍拒绝写入时，使用电脑 ADB 安装入口。",14));
        log = text("适配：吉星派对 3.3.0\n不修改游戏 APK，不清除游戏数据。\n",13); log.setTextIsSelectable(true); page.addView(log);
        Shizuku.addBinderReceivedListenerSticky(received); Shizuku.addBinderDeadListener(dead);
        Shizuku.addRequestPermissionResultListener(permission); refresh();
    }
    private int dp(int v) { return Math.round(v * getResources().getDisplayMetrics().density); }
    private TextView text(String value,int size) {
        TextView t = new TextView(this); t.setText(value); t.setTextSize(size); t.setTextColor(Color.rgb(47,41,48));
        t.setPadding(0,dp(5),0,dp(10)); return t;
    }
    private Button button(LinearLayout page,String label,boolean yellow,View.OnClickListener action) {
        Button b = new Button(this); b.setText(label); b.setTextSize(16); b.setAllCaps(false); b.setTextColor(yellow?Color.BLACK:Color.WHITE);
        GradientDrawable bg = new GradientDrawable(); bg.setColor(yellow?0xffffc600:0xffff238f); bg.setCornerRadius(dp(22)); b.setBackground(bg);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1,dp(52)); p.setMargins(0,dp(5),0,dp(5)); page.addView(b,p); b.setOnClickListener(action); return b;
    }
    private void open(String url) { try { startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse(url))); } catch(Exception e) { write(url); } }
    private void write(String s) { if (!destroyed) log.append(s+"\n"); }
    private void refresh() {
        if (destroyed) return;
        boolean alive = Shizuku.pingBinder();
        status.setText(busy ? "正在处理，请保持助手在前台…" : (remote!=null ? "已连接授权服务" : alive ? "Shizuku 已运行，点击连接并授权" : "Shizuku 尚未运行，请先打开并启动")+(lastResult.isEmpty()?"":"\n"+lastResult));
        install.setEnabled(!busy && remote!=null); restore.setEnabled(!busy && remote!=null); inspect.setEnabled(!busy && remote!=null);
        connect.setEnabled(!busy && !binding);
        for(Button b:new Button[]{install,restore,inspect,connect}) b.setAlpha(b.isEnabled()?1f:.45f);
    }
    private void connect() {
        try {
            if (!Shizuku.pingBinder()) { write("请先打开 Shizuku，按指南完成无线调试配对并启动服务。"); refresh(); return; }
            if (Shizuku.getVersion()<13) throw new Exception("请更新 Shizuku 至支持 API 13 的版本");
            if (Shizuku.checkSelfPermission()!=PackageManager.PERMISSION_GRANTED) {
                if (Shizuku.shouldShowRequestPermissionRationale()) write("请在 Shizuku 的应用管理中允许星枢安装助手，然后返回连接。");
                else Shizuku.requestPermission(1);
                return;
            }
            if (remote==null && !binding) {
                binding = true; Shizuku.bindUserService(serviceArgs,connection);
                new Handler(Looper.getMainLooper()).postDelayed(() -> { if (!destroyed && remote==null) { binding=false; write("连接超时。请检查 Shizuku 后台运行状态，再点击连接。"); refresh(); } },15000);
            }
            refresh();
        } catch(Exception e) { binding=false; write("连接失败："+e.getMessage()); refresh(); }
    }
    private void inspect() { run(false,false); }
    private void execute(boolean undo) { run(true,undo); }
    private void run(boolean apply,boolean undo) {
        if (busy || remote==null) return;
        final IInstaller service=remote; busy=true; refresh(); write(apply?"正在核验并写入，请保持助手在前台…":"正在检查游戏与文件权限…");
        worker.execute(() -> {
            String result;
            try {
                if (!apply) result=service.inspect();
                else {
                    File patch=undo?null:stage("candidate.__data"), vanilla=stage("vanilla.__data");
                    try (ParcelFileDescriptor p=undo?null:ParcelFileDescriptor.open(patch,ParcelFileDescriptor.MODE_READ_ONLY);
                         ParcelFileDescriptor v=ParcelFileDescriptor.open(vanilla,ParcelFileDescriptor.MODE_READ_ONLY)) {
                        result=service.apply(undo,p,v);
                    }
                }
            } catch(Exception e) { result="未完成："+e.getMessage(); }
            final String output=result;
            runOnUiThread(() -> { if (!destroyed) { busy=false; lastResult=output; write(output); refresh(); } });
        });
    }
    private File stage(String asset) throws IOException {
        File f=new File(getCacheDir(),asset);
        try (InputStream in=getAssets().open(asset); FileOutputStream out=new FileOutputStream(f)) {
            byte[] b=new byte[65536]; int n; while ((n=in.read(b))!=-1) out.write(b,0,n); out.getFD().sync();
        }
        return f;
    }
    @Override protected void onResume() { super.onResume(); if (status!=null) refresh(); }
    @Override protected void onDestroy() {
        destroyed=true; Shizuku.removeBinderReceivedListener(received); Shizuku.removeBinderDeadListener(dead);
        Shizuku.removeRequestPermissionResultListener(permission);
        // A foreground install must finish its rollback even if Android recreates the Activity.
        worker.execute(() -> { try { if (Shizuku.pingBinder()) Shizuku.unbindUserService(serviceArgs,connection,true); } catch(Exception ignored) {} });
        worker.shutdown(); super.onDestroy();
    }
}
