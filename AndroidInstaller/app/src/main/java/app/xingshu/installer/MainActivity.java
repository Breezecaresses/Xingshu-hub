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
import java.util.concurrent.atomic.AtomicBoolean;
import rikka.shizuku.Shizuku;

public final class MainActivity extends Activity {
    // Activity recreation must not start another transfer against the same update cache.
    private static final AtomicBoolean UPDATE_TASK_ACTIVE = new AtomicBoolean();
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private TextView status, log;
    private Button install, restore, connect, inspect, pair, shizuku, updateCheck;
    private Switch previewUpdates;
    private TextView updateStatus;
    private TextView updateDetails;
    private ProgressBar updateProgress;
    private DownloadProgress downloadProgress;
    private final Handler progressHandler = new Handler(Looper.getMainLooper());
    private long checkStarted;
    private String checkStage;
    private final Runnable checkTick = new Runnable() {
        @Override public void run() {
            if(destroyed || checkStage==null)return;
            updateStatus.setText(checkStage+"\n已等待 "+Math.max(0,(SystemClock.elapsedRealtime()-checkStarted)/1000)+" 秒");
            progressHandler.postDelayed(this,1000);
        }
    };
    private final Runnable progressTick = new Runnable() {
        @Override public void run() {
            if(destroyed || downloadProgress==null)return;
            updateDetails.setText(downloadProgress.description(SystemClock.elapsedRealtime()));
            progressHandler.postDelayed(this,1000);
        }
    };
    private ReleaseUpdates.Update pendingUpdate;
    private boolean waitingInstallPermission;
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
        LinearLayout page = new LinearLayout(this); page.setOrientation(LinearLayout.VERTICAL); page.setPadding(dp(22),dp(24),dp(22),dp(24));
        page.setBackgroundColor(Color.rgb(255,246,222));
        page.setOnApplyWindowInsetsListener((view,insets) -> {
            view.setPadding(dp(22)+insets.getSystemWindowInsetLeft(),dp(24)+insets.getSystemWindowInsetTop(),
                dp(22)+insets.getSystemWindowInsetRight(),dp(24)+insets.getSystemWindowInsetBottom());
            return insets;
        });
        scroll.addView(page); setContentView(scroll);
        TextView title = text("星枢 · 手机安装助手",25); title.setTypeface(null,Typeface.BOLD); page.addView(title);
        page.addView(text("安卓预览更新  ·  " + BuildConfig.VERSION_NAME,12));
        updateStatus=text("助手版本："+BuildConfig.VERSION_NAME+"\n助手内置游戏插件："+PayloadSpec.VERSION,13); page.addView(updateStatus);
        updateCheck=button(page,"检查助手更新",true,v -> checkAssistantUpdate());
        updateProgress=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);
        updateProgress.setMax(100); updateProgress.setProgressTintList(android.content.res.ColorStateList.valueOf(0xffff238f));
        page.addView(updateProgress,new LinearLayout.LayoutParams(-1,dp(12))); updateProgress.setVisibility(View.GONE);
        updateDetails=text("",13); page.addView(updateDetails); updateDetails.setVisibility(View.GONE);
        previewUpdates=new Switch(this); previewUpdates.setText("包含测试版");
        previewUpdates.setChecked(getPreferences(0).getBoolean("previewUpdates",true));
        previewUpdates.setOnCheckedChangeListener((b,checked) -> getPreferences(0).edit().putBoolean("previewUpdates",checked).apply()); page.addView(previewUpdates);
        page.addView(text("助手更新会保留设置和配对身份。更新助手后，游戏内插件仍需连接授权，再点“安装 / 更新星枢”。",13));
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
        updateCheck.setEnabled(!busy&&!binding); previewUpdates.setEnabled(!busy);
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
    private void updateFinished() { busy=false; checkStage=null; progressHandler.removeCallbacks(checkTick); if(!destroyed)refresh(); }
    private void stopDownloadProgress() { progressHandler.removeCallbacks(progressTick); downloadProgress=null; }
    private boolean acquireUpdateTask() {
        if(UPDATE_TASK_ACTIVE.compareAndSet(false,true))return true;
        updateStatus.setText("上一次更新任务仍在进行，请稍后重试。");
        write("上一次更新检查或下载尚未结束，已阻止重复更新任务。");
        return false;
    }
    private void runUpdateTask(Runnable task) {
        try {
            worker.execute(() -> {
                try { task.run(); }
                finally { UPDATE_TASK_ACTIVE.set(false); }
            });
        } catch(RejectedExecutionException e) {
            UPDATE_TASK_ACTIVE.set(false); updateFinished();
            if(!destroyed) {
                updateProgress.setVisibility(View.GONE);
                updateStatus.setText("更新任务未启动，请重新打开助手后重试。");
            }
        }
    }
    private void checkAssistantUpdate() {
        if(busy||!acquireUpdateTask())return; stopDownloadProgress(); updateProgress.setVisibility(View.GONE); updateDetails.setVisibility(View.GONE);
        busy=true; refresh(); checkStarted=SystemClock.elapsedRealtime(); checkStage="正在连接官方更新服务…";
        updateProgress.setVisibility(View.VISIBLE); updateProgress.setIndeterminate(true); progressHandler.post(checkTick);
        final boolean preview=previewUpdates.isChecked();
        runUpdateTask(() -> {
            try {
                final ReleaseUpdates.Update found=ReleaseUpdates.check(preview,() -> getAssets().open("update-public-cert.txt"),label -> runOnUiThread(() -> { if(!destroyed)checkStage=label; }));
                final boolean newer=found!=null&&found.code()>BuildConfig.VERSION_CODE;
                final String warning=found==null||found.checkWarning==null?"":found.checkWarning.trim();
                final String label=found==null?"当前发布尚未提供签名更新清单，可从官方发布页下载。":!warning.isEmpty()?
                    (newer?"已核验新版助手：":"已核验发布：")+found.version()+"\n\n"+warning+"\n此次检查未完整完成，不能确认是否还有更新版本。":
                    newer?"发现新版助手："+found.version():"没有比当前助手更新的可校验发布。";
                runOnUiThread(() -> { updateFinished(); if(destroyed)return; updateProgress.setVisibility(View.GONE); updateStatus.setText(label);
                    try {
                        AlertDialog.Builder dialog=new AlertDialog.Builder(this).setTitle("助手更新").setMessage(label+(newer?"\n\n"+found.notes()+"\n\n只更新助手 APK，安装后仍需单独更新游戏插件。":""))
                            .setNegativeButton("关闭",null).setNeutralButton("官方发布页",(d,w) -> open(ReleaseUpdates.RELEASES));
                        if(newer)dialog.setPositiveButton("下载新版 APK",(d,w) -> downloadAssistant(found)); dialog.show();
                    } catch(Exception e) { write("检查更新未完成："+e.getMessage()); }
                });
            } catch(Exception e) {
                android.util.Log.w("XingShuUpdate","Assistant update check failed",e);
                runOnUiThread(() -> { String failedStage=checkStage; updateFinished(); if(!destroyed) {
                    updateProgress.setVisibility(View.GONE);
                    updateStatus.setText("检查更新未完成。\n"+(failedStage==null?"":failedStage+"\n")+ReleaseUpdates.failureMessage(e));
                    write("更新检查："+ReleaseUpdates.failureMessage(e));
                } });
            }
        });
    }
    private void downloadAssistant(ReleaseUpdates.Update update) {
        if(busy||!acquireUpdateTask())return; busy=true; refresh(); updateStatus.setText("正在连接下载服务器，请保持助手在前台…");
        stopDownloadProgress(); downloadProgress=new DownloadProgress(update.manifest.optJSONObject("android").optLong("size"),SystemClock.elapsedRealtime());
        updateProgress.setVisibility(View.VISIBLE); updateProgress.setIndeterminate(true); updateProgress.setProgress(0);
        updateDetails.setVisibility(View.VISIBLE); progressHandler.post(progressTick);
        runUpdateTask(() -> {
            try {
                File apk=ReleaseUpdates.download(update,getCacheDir(),new ReleaseUpdates.DownloadListener() {
                    public void progress(long received,long total) {
                        runOnUiThread(() -> { if(destroyed||downloadProgress==null)return;
                            downloadProgress.received(received,SystemClock.elapsedRealtime());
                            updateProgress.setIndeterminate(false); updateProgress.setProgress(downloadProgress.percent());
                            updateStatus.setText("正在下载新版 APK，请保持助手在前台…");
                            updateDetails.setText(downloadProgress.description(SystemClock.elapsedRealtime()));
                        });
                    }
                    public void verifying() {
                        runOnUiThread(() -> { if(destroyed)return; stopDownloadProgress();
                            updateProgress.setIndeterminate(false); updateProgress.setProgress(100);
                            updateStatus.setText("下载完成，正在校验 APK 完整性、版本和签名…");
                            updateDetails.setText("下载 100% · 校验通过后才会打开系统安装页面");
                        });
                    }
                }); verifySelfApk(apk,update);
                runOnUiThread(() -> { stopDownloadProgress(); updateFinished(); if(!destroyed) { pendingUpdate=update; updateStatus.setText("新版 APK 校验通过，请确认安装。"); updateDetails.setText("下载和校验已完成，等待系统确认覆盖安装"); requestAssistantInstall(); } });
            } catch(Exception e) { runOnUiThread(() -> { stopDownloadProgress(); updateFinished(); if(!destroyed) { updateProgress.setIndeterminate(false); updateStatus.setText("下载或校验未完成，当前助手保持原样。"); updateDetails.setText("原因："+e.getMessage()+"\n可重新检查更新后重试，或从官方发布页下载 APK。"); write("助手更新："+e.getMessage()); } }); }
        });
    }
    @SuppressWarnings("deprecation")
    private void verifySelfApk(File apk,ReleaseUpdates.Update update) throws Exception {
        android.content.pm.PackageInfo next=getPackageManager().getPackageArchiveInfo(apk.getAbsolutePath(),PackageManager.GET_SIGNATURES);
        android.content.pm.PackageInfo own=getPackageManager().getPackageInfo(getPackageName(),PackageManager.GET_SIGNATURES);
        if(next==null||!getPackageName().equals(next.packageName)||next.versionCode!=update.code()||next.versionCode<=BuildConfig.VERSION_CODE ||
            next.signatures==null||own.signatures==null||next.signatures.length!=1||own.signatures.length!=1||!next.signatures[0].equals(own.signatures[0]))
            throw new IOException("APK 包名、版本或签名与当前助手不一致，拒绝安装");
        java.security.MessageDigest digest=java.security.MessageDigest.getInstance("SHA-256");
        try(InputStream in=new FileInputStream(apk)) { byte[] b=new byte[65536]; int n; while((n=in.read(b))!=-1)digest.update(b,0,n); }
        if(!ReleaseUpdates.hex(digest.digest()).equals(update.manifest.getJSONObject("android").getString("sha256")))throw new IOException("APK 文件已变化");
    }
    private void requestAssistantInstall() {
        try {
            File apk=new File(getCacheDir(),"update.apk"); verifySelfApk(apk,pendingUpdate);
            if(!getPackageManager().canRequestPackageInstalls()) {
                new AlertDialog.Builder(this).setTitle("允许助手更新")
                    .setMessage("请在接下来的系统页面允许本助手安装应用，返回后由系统确认覆盖安装。无需卸载旧版。")
                    .setNegativeButton("暂不安装",null).setPositiveButton("打开设置",(d,w) -> {
                        try { waitingInstallPermission=true; startActivity(new Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+getPackageName()))); }
                        catch(Exception e) { waitingInstallPermission=false; write("请在系统设置中允许星枢安装助手安装应用，然后重试。"); }
                    }).show(); return;
            }
            Uri uri=Uri.parse("content://"+BuildConfig.APPLICATION_ID+".update/update.apk");
            startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(uri,"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION));
        } catch(Exception e) { write("未能打开系统安装页面："+e.getMessage()); }
    }
    @Override protected void onResume() { super.onResume(); if (status!=null)refresh();
        if(waitingInstallPermission) { waitingInstallPermission=false; if(pendingUpdate!=null&&getPackageManager().canRequestPackageInstalls())requestAssistantInstall(); }
    }
    @Override protected void onDestroy() {
        destroyed=true; checkStage=null; progressHandler.removeCallbacks(checkTick); stopDownloadProgress(); Shizuku.removeBinderReceivedListener(received); Shizuku.removeBinderDeadListener(dead);
        Shizuku.removeRequestPermissionResultListener(permission);
        // A foreground install must finish its rollback even if Android recreates the Activity.
        worker.execute(() -> { try { if(local!=null)local.close(); } catch(Exception ignored) {} try { if (Shizuku.pingBinder()) Shizuku.unbindUserService(serviceArgs,connection,true); } catch(Exception ignored) {} });
        worker.shutdown(); super.onDestroy();
    }
}
