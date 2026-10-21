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
    private Button install, restore, connect, inspect, pair, shizuku;
    private EditText pairingPort, pairingCode, connectionPort;
    private volatile LocalAdbInstaller local;
    private boolean useShizuku;
    private IInstaller remote;
    private boolean binding, busy, destroyed;
    private String lastResult="";
    private final Shizuku.UserServiceArgs serviceArgs = new Shizuku.UserServiceArgs(
        new ComponentName(BuildConfig.APPLICATION_ID, InstallerService.class.getName()))
        .daemon(false).processNameSuffix("installer").debuggable(false).version(BuildConfig.VERSION_CODE);
    private final Shizuku.OnBinderReceivedListener received = () -> runOnUiThread(this::refresh);
    private final Shizuku.OnBinderDeadListener dead = () -> runOnUiThread(() -> { remote = null; binding = false; refresh(); });
    private final Shizuku.OnRequestPermissionResultListener permission = (code, result) -> runOnUiThread(() -> {
        if (result == PackageManager.PERMISSION_GRANTED) connectShizuku(); else write("未授权。请在 Shizuku 的应用管理中允许星枢安装助手。");
    });
    private final ServiceConnection connection = new ServiceConnection() {
        public void onServiceConnected(ComponentName name, IBinder binder) {
            remote = IInstaller.Stub.asInterface(binder); useShizuku=true; binding = false; refresh(); inspect();
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
        page.addView(text("只用手机即可安装：Android 11 及以上，通过本机无线调试授权。无需电脑、无需 Shizuku 通知。",15));
        status = text("尚未连接本机无线调试",16); page.addView(status);
        button(page,"打开开发者选项",true, v -> {
            try { startActivity(new Intent(android.provider.Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS)); }
            catch(Exception e) { write("请手动打开系统设置 → 开发者选项 → 无线调试。"); }
        });
        page.addView(text("① 首次配对\n先把本助手与系统设置分屏。在设置 → 开发者选项 → 无线调试中，打开“使用配对码配对设备”，保持弹窗可见。把弹窗里的端口和六位配对码填在下方。",14));
        pairingPort=input(page,"配对端口或完整地址，如 192.168.31.2:45678",false);
        pairingCode=input(page,"六位配对码",true);
        pair=button(page,"① 配对这部手机",false, v -> pairPhone());
        page.addView(text("② 连接本机\n配对成功后关闭配对码弹窗，在“无线调试”主页面找到“IP 地址和端口”。可填冒号后面的连接端口，也可粘贴完整地址；始终只连接本机。两个端口通常不同。",14));
        connectionPort=input(page,"连接端口或完整地址（无线调试主页面）",false);
        connectionPort.setText(getPreferences(0).getString("connectionPort",""));
        connect=button(page,"② 连接本机无线调试",false, v -> connectPhone());
        inspect=button(page,"③ 检查游戏与目录权限",true, v -> inspect());
        install=button(page,"安装 / 更新星枢",false, v -> execute(false));
        restore=button(page,"还原官方原版",true, v -> new AlertDialog.Builder(this)
            .setMessage("将恢复此版本的官方程序集缓存，保留账号与设置。请先正常退出游戏。")
            .setNegativeButton("取消",null).setPositiveButton("还原",(d,w) -> execute(true)).show());
        page.addView(text("配对身份仅保存在本助手内，不保存配对码。切换 Wi-Fi 或重启手机后，可能需要重新填写连接端口。安装完成后可关闭无线调试。",14));
        shizuku=button(page,"其他方式：连接已运行的 Shizuku",true, v -> connectShizuku());
        button(page,"打开 Shizuku（可选）",true, v -> {
            Intent launch = getPackageManager().getLaunchIntentForPackage("moe.shizuku.privileged.api");
            if (launch != null) startActivity(launch); else open("https://shizuku.rikka.app/download/");
        });
        button(page,"开源组件与许可",true, v -> {
            try(InputStream in=getAssets().open("THIRD_PARTY_NOTICES.txt")) {
                ByteArrayOutputStream out=new ByteArrayOutputStream(); byte[] b=new byte[4096]; int n;
                while((n=in.read(b))!=-1)out.write(b,0,n);
                new AlertDialog.Builder(this).setTitle("开源组件").setMessage(out.toString("UTF-8")).setPositiveButton("关闭",null).show();
            } catch(Exception e) { write(e.getMessage()); }
        });
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
    private EditText input(LinearLayout page,String hint,boolean secret) {
        EditText field=new EditText(this); field.setHint(hint); field.setTextSize(16); field.setSingleLine(true);
        field.setInputType(secret?android.text.InputType.TYPE_CLASS_NUMBER|android.text.InputType.TYPE_NUMBER_VARIATION_PASSWORD:
            android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_URI);
        field.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(secret?6:32)});
        field.setSaveEnabled(false); field.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);
        page.addView(field,new LinearLayout.LayoutParams(-1,dp(54))); return field;
    }
    private int port(EditText field) {
        return LocalPort.parse(field.getText().toString());
    }
    private LocalAdbInstaller local() throws Exception {
        if(local==null)local=new LocalAdbInstaller(this); return local;
    }
    private interface Work { String run() throws Exception; }
    private void submit(String message,Work work) {
        if(busy)return; busy=true; lastResult=""; write(message); refresh();
        worker.execute(() -> {
            String result;
            try { result=work.run(); } catch(Throwable e) {
                // Pairing libraries can raise native linkage errors; report without crashing the UI.
                result="未完成："+e.getClass().getSimpleName()+" "+String.valueOf(e.getMessage())+
                    "\n配对失败请重新打开配对码弹窗；连接失败请核对主页面连接端口，再重试。";
            }
            final String output=result;
            runOnUiThread(() -> { if(!destroyed) {
                busy=false; lastResult=output; write(output); refresh();
                if(!isFinishing())new AlertDialog.Builder(this).setTitle("处理结果").setMessage(output).setPositiveButton("知道了",null).show();
            } });
        });
    }
    private void pairPhone() {
        try {
            final int port=port(pairingPort); final String code=pairingCode.getText().toString();
            if(!code.matches("[0-9]{6}"))throw new IllegalArgumentException("请输入六位配对码");
            pairingCode.setText("");
            submit("正在本机配对，请保持设置中的配对码弹窗可见…",() -> {
                local().pair(port,code); useShizuku=false;
                return "配对成功。请关闭配对码弹窗，填写无线调试主页面的连接端口，再点击连接。";
            });
        } catch(Exception e) { write(e.getMessage()); }
    }
    private void connectPhone() {
        try {
            final int port=port(connectionPort);
            getPreferences(0).edit().putString("connectionPort",String.valueOf(port)).apply();
            submit("正在连接本机无线调试…",() -> {
                local().connect(port); useShizuku=false; return "已连接本机无线调试。\n"+local().inspect();
            });
        } catch(Exception e) { write(e.getMessage()); }
    }
    private void refresh() {
        if(destroyed)return;
        boolean ready=useShizuku?remote!=null:local!=null&&local.connected();
        status.setText(busy?"正在处理，请保持助手在前台…":(ready?(useShizuku?"已连接 Shizuku":"已连接本机无线调试"):"尚未连接，请先配对并填写连接端口")+(lastResult.isEmpty()?"":"\n"+lastResult));
        install.setEnabled(!busy&&ready); restore.setEnabled(!busy&&ready); inspect.setEnabled(!busy&&ready);
        connect.setEnabled(!busy&&!binding); pair.setEnabled(!busy&&!binding); shizuku.setEnabled(!busy&&!binding);
        pairingPort.setEnabled(!busy); pairingCode.setEnabled(!busy); connectionPort.setEnabled(!busy);
        for(Button b:new Button[]{install,restore,inspect,connect,pair,shizuku})b.setAlpha(b.isEnabled()?1f:.45f);
    }
    private void connectShizuku() {
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
        if(busy)return;
        if(!useShizuku) {
            submit(apply?"正在核验、传送并写入，请保持助手在前台…":"正在检查游戏与文件权限…",
                () -> apply?local().apply(undo):local().inspect());
            return;
        }
        if(remote==null)return;
        final IInstaller service=remote;
        submit(apply?"正在核验并写入，请保持助手在前台…":"正在检查游戏与文件权限…",() -> {
            if(!apply)return service.inspect();
            File patch=undo?null:stage("candidate.__data"), vanilla=stage("vanilla.__data");
            try(ParcelFileDescriptor p=undo?null:ParcelFileDescriptor.open(patch,ParcelFileDescriptor.MODE_READ_ONLY);
                ParcelFileDescriptor v=ParcelFileDescriptor.open(vanilla,ParcelFileDescriptor.MODE_READ_ONLY)) {
                return service.apply(undo,p,v);
            }
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
        worker.execute(() -> { try { if(local!=null)local.close(); } catch(Exception ignored) {} try { if (Shizuku.pingBinder()) Shizuku.unbindUserService(serviceArgs,connection,true); } catch(Exception ignored) {} });
        worker.shutdown(); super.onDestroy();
    }
}
