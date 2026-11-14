package app.xingshu.installer;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.Dialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ComponentName;
import android.content.Intent;
import android.content.ServiceConnection;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.ParcelFileDescriptor;
import android.os.SystemClock;
import android.text.InputType;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.Switch;
import android.widget.TextView;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import rikka.shizuku.Shizuku;

public final class MainActivity extends Activity {
    private static final int REQ_EXPORT_LOG = 1001;
    private static final int TAB_INSTALL = 0;
    private static final int TAB_LOGIN = 1;
    private static final int TAB_WIRELESS = 2;
    private static final int TAB_UPDATES = 3;
    // Only one worker request may write update.apk or update.download at a time.
    private static final AtomicBoolean updateTaskActive = new AtomicBoolean(false);

    private boolean busy, binding, useShizuku, destroyed, waitingInstallPermission;
    private volatile boolean authBusy, webPollCancelled;
    private int activeTab = TAB_INSTALL;

    private IInstaller remote;
    private LocalAdbInstaller local;
    private String lastResult = "";
    private String selectedPackageName = GamePackageCatalog.DEFAULT_PACKAGE;

    private static final class DiscoveredPkg {
        final String packageName;
        final String label;
        final String platform;
        final String versionName;
        final String installState;
        final boolean running;

        DiscoveredPkg(String packageName, String label, String platform, String versionName, String installState, boolean running) {
            this.packageName = packageName;
            this.label = label;
            this.platform = platform;
            this.versionName = versionName;
            this.installState = installState;
            this.running = running;
        }
    }

    private final List<DiscoveredPkg> discoveredPackages = new ArrayList<>();
    private final List<BnSdkTokenExtractor.ChannelToken> detectedTokens = new ArrayList<>();
    private int selectedTokenIndex = 0;
    private MobileAuthHub.BoundAccount boundSession;
    private String cloudSummaryLine = "";

    private ReleaseUpdates.Update availableUpdate;
    private ReleaseUpdates.Update pendingUpdate;
    private long dismissedModalCode = -1;
    private long autoPromptShownCode = -1;

    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final ExecutorService authWorker = Executors.newSingleThreadExecutor();
    private final ExecutorService updateWorker = Executors.newSingleThreadExecutor();
    private final Handler progressHandler = new Handler(Looper.getMainLooper());
    private volatile String checkStage;
    private long checkStarted;
    private DownloadProgress downloadProgress;
    private boolean verifyingDownload;

    // UI references
    private TextView connBadge, connSummary, pkgSummaryView, logView, updateStatus, updateDetails, bannerText;
    private TextView authSummaryView, tokenStatusView, webAuthStatusView;
    private LinearLayout packageChipsRow, tokenListRow;
    private ProgressBar updateProgress;
    private View updateBanner;
    private View[] tabContainers = new View[4];
    private TextView[] tabPills = new TextView[4];
    private Button installBtn, restoreBtn, inspectBtn, scanPackagesBtn, pairBtn, connectBtn, shizukuBtn, updateCheckBtn, exportLogBtn;
    private Button scanTokenBtn, bindQqBtn, hubTokenLoginBtn, webAuthBtn, webAuthCancelBtn, refreshCloudBtn, hubLogoutBtn, launchGameBtn;
    private EditText pairingPort, pairingCode, connectionPort, qqLinkInput;
    private Switch previewUpdates;

    // Live modal controls (null when dialog is dismissed)
    private Dialog updateDialog;
    private ProgressBar modalProgress;
    private TextView modalProgressText;
    private Button modalActionBtn;

    private final Runnable checkTick = new Runnable() {
        @Override public void run() {
            if (destroyed || checkStage == null) return;
            long sec = Math.max(0, (SystemClock.elapsedRealtime() - checkStarted) / 1000);
            updateStatus.setText(checkStage + "（已等待 " + sec + " 秒）");
            updateDetails.setText("若网络超时可检查 Wi-Fi / 代理后重试；助手仍可正常执行安装操作");
            updateDetails.setVisibility(View.VISIBLE);
            progressHandler.postDelayed(this, 1000);
        }
    };

    private final Runnable progressTick = new Runnable() {
        @Override public void run() {
            if (destroyed || downloadProgress == null) return;
            String desc = verifyingDownload ? "下载完成，正在校验 SHA256 与签名…" : downloadProgress.description(SystemClock.elapsedRealtime());
            updateDetails.setText(desc);
            if (!verifyingDownload) {
                int pct = downloadProgress.percent();
                updateProgress.setIndeterminate(false);
                updateProgress.setProgress(pct);
                if (modalProgress != null && updateDialog != null && updateDialog.isShowing()) {
                    modalProgress.setVisibility(View.VISIBLE);
                    modalProgress.setIndeterminate(false);
                    modalProgress.setProgress(pct);
                    modalProgressText.setVisibility(View.VISIBLE);
                    modalProgressText.setText(desc);
                    modalActionBtn.setText("正在下载 " + pct + "%…");
                    modalActionBtn.setEnabled(false);
                }
            } else if (modalProgress != null && updateDialog != null && updateDialog.isShowing()) {
                modalProgress.setVisibility(View.VISIBLE);
                modalProgress.setIndeterminate(true);
                modalProgressText.setVisibility(View.VISIBLE);
                modalProgressText.setText(desc);
                modalActionBtn.setText("正在校验安装包…");
                modalActionBtn.setEnabled(false);
            }
            progressHandler.postDelayed(this, 450);
        }
    };

    private final Shizuku.UserServiceArgs serviceArgs = new Shizuku.UserServiceArgs(
        new ComponentName(BuildConfig.APPLICATION_ID, InstallerService.class.getName()))
        .daemon(false).processNameSuffix("installer").debuggable(false).version(BuildConfig.VERSION_CODE);
    private final Shizuku.OnBinderReceivedListener received = () -> runOnUiThread(this::refresh);
    private final Shizuku.OnBinderDeadListener dead = () -> runOnUiThread(() -> { remote = null; binding = false; refresh(); });
    private final Shizuku.OnRequestPermissionResultListener permission = (code, result) -> runOnUiThread(() -> {
        if (result == PackageManager.PERMISSION_GRANTED) connectShizuku(); else write("未授权。请在 Shizuku 的应用管理中允许星枢安装助手。");
    });
    private final ServiceConnection connection = new ServiceConnection() {
        @Override public void onServiceConnected(ComponentName name, IBinder binder) {
            remote = IInstaller.Stub.asInterface(binder);
            useShizuku = true;
            binding = false;
            refresh();
            scanPackagesAndInspect();
        }
        @Override public void onServiceDisconnected(ComponentName name) { remote = null; binding = false; refresh(); }
    };

    @Override protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        configureSystemBars();
        boundSession = MobileAuthHub.loadSession(getPreferences(0));
        String savedPkg = getPreferences(0).getString("selectedPackageName", GamePackageCatalog.DEFAULT_PACKAGE);
        if (GamePackageCatalog.isValidPackageName(savedPkg)) selectedPackageName = savedPkg;

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(AuroraUi.CANVAS);
        if (Build.VERSION.SDK_INT >= 29) root.setForceDarkAllowed(false);

        root.setOnApplyWindowInsetsListener((v, insets) -> {
            int top = insets.getSystemWindowInsetTop();
            int bottom = insets.getSystemWindowInsetBottom();
            v.setPadding(0, top, 0, bottom);
            return insets;
        });

        root.addView(buildHeader());
        updateBanner = buildUpdateBanner();
        updateBanner.setVisibility(View.GONE);
        root.addView(updateBanner);
        root.addView(buildTabBar());

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setVerticalScrollBarEnabled(false);
        LinearLayout body = new LinearLayout(this);
        body.setOrientation(LinearLayout.VERTICAL);
        int hp = AuroraUi.dp(this, 16);
        body.setPadding(hp, AuroraUi.dp(this, 8), hp, AuroraUi.dp(this, 24));

        tabContainers[TAB_INSTALL] = buildInstallTab();
        tabContainers[TAB_LOGIN] = buildLoginTab();
        tabContainers[TAB_WIRELESS] = buildWirelessTab();
        tabContainers[TAB_UPDATES] = buildUpdatesHelpTab();
        for (View tab : tabContainers) {
            body.addView(tab);
        }
        scroll.addView(body);
        root.addView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        setContentView(root);
        selectTab(TAB_INSTALL);

        Shizuku.addBinderReceivedListenerSticky(received);
        Shizuku.addBinderDeadListener(dead);
        Shizuku.addRequestPermissionResultListener(permission);
        refreshPackageChips();
        refreshAuthUi();
        refresh();

        // Silently check GitHub/mirror for newer helper builds once on launch
        progressHandler.postDelayed(() -> checkAssistantUpdate(true), 650);
    }

    private void configureSystemBars() {
        Window win = getWindow();
        win.addFlags(WindowManager.LayoutParams.FLAG_DRAWS_SYSTEM_BAR_BACKGROUNDS);
        win.setStatusBarColor(AuroraUi.HEADER_TOP);
        win.setNavigationBarColor(AuroraUi.CANVAS);
        int flags = win.getDecorView().getSystemUiVisibility();
        flags |= View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
        if (Build.VERSION.SDK_INT >= 26) flags |= View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
        win.getDecorView().setSystemUiVisibility(flags);
    }

    private View buildHeader() {
        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setBackground(new GradientDrawable(
            GradientDrawable.Orientation.TL_BR,
            new int[] { AuroraUi.HEADER_TOP, AuroraUi.HEADER_MID, AuroraUi.CANVAS }));
        int px16 = AuroraUi.dp(this, 16), px12 = AuroraUi.dp(this, 12);
        header.setPadding(px16, AuroraUi.dp(this, 12), px16, AuroraUi.dp(this, 12));

        ImageView logo = new ImageView(this);
        logo.setImageResource(R.drawable.ic_xingshu_brand);
        logo.setScaleType(ImageView.ScaleType.FIT_CENTER);
        LinearLayout.LayoutParams logoLp = new LinearLayout.LayoutParams(AuroraUi.dp(this, 44), AuroraUi.dp(this, 44));
        logoLp.rightMargin = px12;
        header.addView(logo, logoLp);

        LinearLayout meta = new LinearLayout(this);
        meta.setOrientation(LinearLayout.VERTICAL);

        LinearLayout titleRow = new LinearLayout(this);
        titleRow.setOrientation(LinearLayout.HORIZONTAL);
        titleRow.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = new TextView(this);
        title.setText("星枢手机助手");
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 17.5f);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setTextColor(AuroraUi.INK);
        titleRow.addView(title);

        TextView verPill = AuroraUi.pill(this, compactVer(BuildConfig.VERSION_NAME), AuroraUi.SURFACE, AuroraUi.BORDER, AuroraUi.INK_MUTED);
        LinearLayout.LayoutParams vp = (LinearLayout.LayoutParams) verPill.getLayoutParams();
        vp.leftMargin = AuroraUi.dp(this, 8);
        titleRow.addView(verPill);
        meta.addView(titleRow);

        TextView sub = new TextView(this);
        sub.setText("适配《吉星派对》 · 内置 " + PayloadSpec.VERSION);
        sub.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f);
        sub.setTextColor(AuroraUi.INK_MUTED);
        meta.addView(sub);

        header.addView(meta, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        connBadge = AuroraUi.pill(this, "待连接", AuroraUi.SURFACE, AuroraUi.BORDER, AuroraUi.INK_MUTED);
        header.addView(connBadge);
        return header;
    }

    private View buildUpdateBanner() {
        LinearLayout wrap = new LinearLayout(this);
        wrap.setOrientation(LinearLayout.HORIZONTAL);
        wrap.setGravity(Gravity.CENTER_VERTICAL);
        wrap.setBackground(AuroraUi.rounded(AuroraUi.ACCENT_SOFT, AuroraUi.ACCENT_BORDER, 14, this));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        int m = AuroraUi.dp(this, 16);
        lp.setMargins(m, AuroraUi.dp(this, 2), m, AuroraUi.dp(this, 8));
        wrap.setLayoutParams(lp);
        wrap.setPadding(AuroraUi.dp(this, 12), AuroraUi.dp(this, 9), AuroraUi.dp(this, 8), AuroraUi.dp(this, 9));

        bannerText = new TextView(this);
        bannerText.setText("⬇ 发现新版本");
        bannerText.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        bannerText.setTypeface(Typeface.DEFAULT_BOLD);
        bannerText.setTextColor(AuroraUi.ACCENT_INK);
        wrap.addView(bannerText, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView openBtn = new TextView(this);
        openBtn.setText("查看更新");
        openBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        openBtn.setTypeface(Typeface.DEFAULT_BOLD);
        openBtn.setTextColor(AuroraUi.INK);
        openBtn.setBackground(AuroraUi.rounded(AuroraUi.SURFACE, AuroraUi.ACCENT_BORDER, 10, this));
        openBtn.setPadding(AuroraUi.dp(this, 10), AuroraUi.dp(this, 5), AuroraUi.dp(this, 10), AuroraUi.dp(this, 5));
        openBtn.setOnClickListener(v -> {
            if (availableUpdate != null) showUpdateModal(availableUpdate);
        });
        wrap.addView(openBtn);

        TextView closeBtn = new TextView(this);
        closeBtn.setText("  ✕ ");
        closeBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f);
        closeBtn.setTextColor(AuroraUi.ACCENT_INK);
        closeBtn.setPadding(AuroraUi.dp(this, 6), AuroraUi.dp(this, 4), AuroraUi.dp(this, 4), AuroraUi.dp(this, 4));
        closeBtn.setOnClickListener(v -> {
            if (availableUpdate != null) try { dismissedModalCode = availableUpdate.code(); } catch (Exception ignored) { }
            updateBanner.setVisibility(View.GONE);
        });
        wrap.addView(closeBtn);
        return wrap;
    }

    private View buildTabBar() {
        LinearLayout track = new LinearLayout(this);
        track.setOrientation(LinearLayout.HORIZONTAL);
        track.setBackground(AuroraUi.rounded(AuroraUi.SURFACE_SUBTLE, AuroraUi.BORDER, 14, this));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        int m = AuroraUi.dp(this, 16);
        lp.setMargins(m, AuroraUi.dp(this, 2), m, AuroraUi.dp(this, 6));
        track.setLayoutParams(lp);
        int p = AuroraUi.dp(this, 4);
        track.setPadding(p, p, p, p);

        String[] labels = { "安装维护", "全端登录", "无线配对", "更新与帮助" };
        for (int i = 0; i < labels.length; i++) {
            final int idx = i;
            TextView pill = new TextView(this);
            pill.setText(labels[i]);
            pill.setGravity(Gravity.CENTER);
            pill.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
            pill.setTypeface(Typeface.DEFAULT_BOLD);
            pill.setPadding(0, AuroraUi.dp(this, 8), 0, AuroraUi.dp(this, 8));
            pill.setOnClickListener(v -> selectTab(idx));
            track.addView(pill, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
            tabPills[i] = pill;
        }
        return track;
    }

    private void selectTab(int index) {
        activeTab = index;
        for (int i = 0; i < tabContainers.length; i++) {
            boolean active = (i == index);
            tabContainers[i].setVisibility(active ? View.VISIBLE : View.GONE);
            tabPills[i].setBackground(active ? AuroraUi.rounded(AuroraUi.SURFACE, AuroraUi.BORDER, 10, this) : null);
            tabPills[i].setTextColor(active ? AuroraUi.INK : AuroraUi.INK_MUTED);
        }
    }

    private View buildInstallTab() {
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);

        // Card 1: Multi-channel Target Selector + Connection Status & Primary Install Actions
        LinearLayout card1 = AuroraUi.card(this);
        card1.addView(AuroraUi.cardTitle(this, "1. 多渠道包识别与插件安装"));
        card1.addView(AuroraUi.caption(this, "支持 TapTap/官服、小米、B站及通用 3.3.0 安卓渠道包。先从对应渠道打开游戏进大厅完成资源更新并退出，再连接调试安装。"));

        connSummary = AuroraUi.callout(this, "调试状态：尚未连接本机调试。请在“无线配对”页配对连接，或使用 Shizuku。");
        card1.addView(connSummary);

        pkgSummaryView = AuroraUi.callout(this, "当前目标渠道：TapTap / 官服（com.feimo.astralparty）");
        card1.addView(pkgSummaryView);

        packageChipsRow = new LinearLayout(this);
        packageChipsRow.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams chipsLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        chipsLp.bottomMargin = AuroraUi.dp(this, 6);
        card1.addView(packageChipsRow, chipsLp);

        scanPackagesBtn = AuroraUi.button(this, "扫描手机已安装游戏渠道", AuroraUi.BtnStyle.SECONDARY, v -> scanInstalledPackages());
        card1.addView(scanPackagesBtn);

        installBtn = AuroraUi.button(this, "安装 / 更新星枢插件", AuroraUi.BtnStyle.PRIMARY, v -> execute(false));
        card1.addView(installBtn);

        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        inspectBtn = AuroraUi.button(this, "检查与冲突扫描", AuroraUi.BtnStyle.SECONDARY, v -> inspect());
        restoreBtn = AuroraUi.button(this, "还原官方原版", AuroraUi.BtnStyle.SECONDARY, v -> execute(true));
        LinearLayout.LayoutParams leftLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        leftLp.rightMargin = AuroraUi.dp(this, 6);
        LinearLayout.LayoutParams rightLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        rightLp.leftMargin = AuroraUi.dp(this, 6);
        row.addView(inspectBtn, leftLp);
        row.addView(restoreBtn, rightLp);
        card1.addView(row);

        LinearLayout navRow = new LinearLayout(this);
        navRow.setOrientation(LinearLayout.HORIZONTAL);
        Button loginNav = AuroraUi.button(this, "全端登录与 QQ 绑定 →", AuroraUi.BtnStyle.ACCENT, v -> selectTab(TAB_LOGIN));
        Button quickCfg = AuroraUi.button(this, "无线配对 / Shizuku →", AuroraUi.BtnStyle.ACCENT, v -> selectTab(TAB_WIRELESS));
        LinearLayout.LayoutParams nl = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        nl.rightMargin = AuroraUi.dp(this, 6);
        LinearLayout.LayoutParams nr = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        nr.leftMargin = AuroraUi.dp(this, 6);
        navRow.addView(loginNav, nl);
        navRow.addView(quickCfg, nr);
        card1.addView(navRow);
        col.addView(card1);

        // Card 2: Diagnostic Output Console
        LinearLayout card2 = AuroraUi.card(this);
        card2.addView(AuroraUi.cardTitle(this, "2. 执行输出与诊断日志"));

        logView = new TextView(this);
        logView.setBackground(AuroraUi.rounded(AuroraUi.SURFACE_LOG, AuroraUi.BORDER, 12, this));
        int lp = AuroraUi.dp(this, 12);
        logView.setPadding(lp, lp, lp, lp);
        logView.setMinLines(8);
        logView.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        logView.setTypeface(Typeface.MONOSPACE);
        logView.setTextColor(AuroraUi.INK);
        logView.setTextIsSelectable(true);
        LinearLayout.LayoutParams logLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        logLp.topMargin = AuroraUi.dp(this, 8);
        logLp.bottomMargin = AuroraUi.dp(this, 10);
        card2.addView(logView, logLp);

        LinearLayout logBar = new LinearLayout(this);
        logBar.setOrientation(LinearLayout.HORIZONTAL);
        exportLogBtn = AuroraUi.button(this, "导出失败诊断日志", AuroraUi.BtnStyle.SECONDARY, v -> exportFailureLog());
        Button clearBtn = AuroraUi.button(this, "清空屏幕输出", AuroraUi.BtnStyle.SUBTLE, v -> logView.setText(""));
        LinearLayout.LayoutParams ebLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1.3f);
        ebLp.rightMargin = AuroraUi.dp(this, 6);
        LinearLayout.LayoutParams cbLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        cbLp.leftMargin = AuroraUi.dp(this, 6);
        logBar.addView(exportLogBtn, ebLp);
        logBar.addView(clearBtn, cbLp);
        card2.addView(logBar);
        col.addView(card2);

        return col;
    }

    private View buildLoginTab() {
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);

        // Card 1: Mobile Hub Cloud Session Card
        LinearLayout statusCard = AuroraUi.card(this);
        statusCard.addView(AuroraUi.cardTitle(this, "1. 手机端全端登录中枢"));
        statusCard.addView(AuroraUi.caption(this, "作为手机端登录与账号绑定中枢，可配合 QQ Bot 的「趴登录」或查看助手已同步的云端档案。"));
        authSummaryView = AuroraUi.callout(this, "当前未登录助手云端会话（可选择下方任一方式登录或直连 QQ Bot）。");
        statusCard.addView(authSummaryView);

        LinearLayout sessionRow = new LinearLayout(this);
        sessionRow.setOrientation(LinearLayout.HORIZONTAL);
        refreshCloudBtn = AuroraUi.button(this, "刷新云端战绩档案", AuroraUi.BtnStyle.SECONDARY, v -> refreshHubAccountSummary());
        hubLogoutBtn = AuroraUi.button(this, "退出当前会话", AuroraUi.BtnStyle.SUBTLE, v -> logoutHubSession());
        LinearLayout.LayoutParams sl = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1.3f);
        sl.rightMargin = AuroraUi.dp(this, 6);
        LinearLayout.LayoutParams sr = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        sr.leftMargin = AuroraUi.dp(this, 6);
        sessionRow.addView(refreshCloudBtn, sl);
        sessionRow.addView(hubLogoutBtn, sr);
        statusCard.addView(sessionRow);
        col.addView(statusCard);

        // Card 2: Native BnSdk Token Scan & One-Tap QQ Bind / Hub Auth
        LinearLayout tokenCard = AuroraUi.card(this);
        tokenCard.addView(AuroraUi.cardTitle(this, "2. 渠道免短信识别与 QQ 授权（TapTap / 小米 / B站）"));
        tokenCard.addView(AuroraUi.caption(this, "先打开渠道游戏登录一次，再点扫描读取本机 BnSdk 实时票据；既可粘贴 QQ 的「趴登录」链接完成绑定，也可直接登录助手查看数据。"));
        tokenStatusView = AuroraUi.callout(this, "尚未扫描本机渠道登录日志（需在「无线配对」连接调试或使用 Shizuku）。");
        tokenCard.addView(tokenStatusView);

        tokenListRow = new LinearLayout(this);
        tokenListRow.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams tlLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tlLp.bottomMargin = AuroraUi.dp(this, 6);
        tokenCard.addView(tokenListRow, tlLp);

        scanTokenBtn = AuroraUi.button(this, "扫描本机渠道登录票据", AuroraUi.BtnStyle.ACCENT, v -> scanChannelTokens());
        tokenCard.addView(scanTokenBtn);

        LinearLayout linkHeader = new LinearLayout(this);
        linkHeader.setOrientation(LinearLayout.HORIZONTAL);
        linkHeader.setGravity(Gravity.CENTER_VERTICAL);
        TextView linkLabel = AuroraUi.caption(this, "QQ Bot 登录链接（发送「趴登录」获取）：");
        linkLabel.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        Button pasteBtn = AuroraUi.button(this, "粘贴剪贴板", AuroraUi.BtnStyle.SUBTLE, v -> pasteClipboardToLink());
        pasteBtn.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        pasteBtn.setPadding(AuroraUi.dp(this, 10), AuroraUi.dp(this, 6), AuroraUi.dp(this, 10), AuroraUi.dp(this, 6));
        pasteBtn.setMinHeight(AuroraUi.dp(this, 32));
        linkHeader.addView(linkLabel);
        linkHeader.addView(pasteBtn);
        tokenCard.addView(linkHeader);

        qqLinkInput = AuroraUi.input(this, "https://124.221.176.134/login#...", false);
        LinearLayout.LayoutParams qlLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        qlLp.bottomMargin = AuroraUi.dp(this, 8);
        tokenCard.addView(qqLinkInput, qlLp);

        bindQqBtn = AuroraUi.button(this, "用选中渠道授权给 QQ Bot（趴登录）", AuroraUi.BtnStyle.PRIMARY, v -> bindQqWithChannelToken());
        tokenCard.addView(bindQqBtn);
        hubTokenLoginBtn = AuroraUi.button(this, "用选中渠道直接登录助手查看数据", AuroraUi.BtnStyle.SECONDARY, v -> startHubChannelAuth());
        tokenCard.addView(hubTokenLoginBtn);
        col.addView(tokenCard);

        // Card 3: Official Phone SMS / Steam Web Authorization
        LinearLayout webCard = AuroraUi.card(this);
        webCard.addView(AuroraUi.cardTitle(this, "3. 官服手机号短信 / Steam 网页授权"));
        webCard.addView(AuroraUi.caption(this, "游玩官服手机号账号或 Steam 国服的玩家，可点下方打开官方授权页输入验证码并同步回助手。"));
        webAuthStatusView = AuroraUi.callout(this, "等待发起官网短信 / Steam 授权。");
        webCard.addView(webAuthStatusView);
        webAuthBtn = AuroraUi.button(this, "打开官服 / Steam 授权网页", AuroraUi.BtnStyle.SECONDARY, v -> startWebAuth());
        webCard.addView(webAuthBtn);
        webAuthCancelBtn = AuroraUi.button(this, "停止等待网页授权", AuroraUi.BtnStyle.SUBTLE, v -> {
            webPollCancelled = true;
            webAuthStatusView.setText("已取消网页授权轮询。");
            refreshAuthUi();
        });
        webCard.addView(webAuthCancelBtn);
        col.addView(webCard);

        // Card 4: Universal Zero-Adaptation In-Game Bind (All Android Channels)
        LinearLayout uniCard = AuroraUi.card(this);
        uniCard.addView(AuroraUi.cardTitle(this, "4. 全渠道通用免适配同步（华为 / OPPO / vivo / 4399 / 应用宝等）"));
        uniCard.addView(AuroraUi.callout(this,
            "无需逐家适配厂商登录 SDK，一次接入所有安卓渠道：\n"
            + "1. 先在「安装维护」选中目标渠道并安装星枢插件；\n"
            + "2. 启动游戏点击星形悬浮球 →「手牌监视」→「绑定 QQ 全端同步」；\n"
            + "3. 复制生成的「趴绑定 XXXXXXXX」发送给 QQ Bot，进大厅与结算后自动全量同步。"));
        launchGameBtn = AuroraUi.button(this, "启动当前选中游戏渠道", AuroraUi.BtnStyle.ACCENT, v -> launchSelectedGame());
        uniCard.addView(launchGameBtn);
        col.addView(uniCard);

        return col;
    }

    private View buildWirelessTab() {
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);

        LinearLayout devCard = AuroraUi.card(this);
        devCard.addView(AuroraUi.cardTitle(this, "第一步 · 开启系统无线调试"));
        devCard.addView(AuroraUi.caption(this, "在系统“开发者选项 → 无线调试”中开启开关；建议配合分屏或小窗查看配对码。"));
        devCard.addView(AuroraUi.button(this, "打开系统开发者选项", AuroraUi.BtnStyle.ACCENT, v -> {
            try { startActivity(new Intent(android.provider.Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS)); }
            catch (Exception e) { write("请手动打开系统设置 → 开发者选项 → 无线调试。"); selectTab(0); }
        }));
        col.addView(devCard);

        LinearLayout pairCard = AuroraUi.card(this);
        pairCard.addView(AuroraUi.cardTitle(this, "第二步 · ① 首次配对手机"));
        pairCard.addView(AuroraUi.caption(this, "点击系统的“使用配对码配对设备”，保持弹窗不关闭，将端口（或完整 IP:端口）与 6 位配对码填入下方："));

        LinearLayout inputs = new LinearLayout(this);
        inputs.setOrientation(LinearLayout.HORIZONTAL);
        pairingPort = AuroraUi.input(this, "配对端口 / IP:端口", false);
        pairingCode = AuroraUi.input(this, "6 位配对码", true);
        LinearLayout.LayoutParams p1 = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1.35f);
        p1.rightMargin = AuroraUi.dp(this, 6);
        LinearLayout.LayoutParams p2 = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        p2.leftMargin = AuroraUi.dp(this, 6);
        inputs.addView(pairingPort, p1);
        inputs.addView(pairingCode, p2);
        LinearLayout.LayoutParams inWrap = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        inWrap.topMargin = AuroraUi.dp(this, 6);
        inWrap.bottomMargin = AuroraUi.dp(this, 8);
        pairCard.addView(inputs, inWrap);

        pairBtn = AuroraUi.button(this, "配对这部手机", AuroraUi.BtnStyle.PRIMARY, v -> pairLocal());
        pairCard.addView(pairBtn);
        col.addView(pairCard);

        LinearLayout connCard = AuroraUi.card(this);
        connCard.addView(AuroraUi.cardTitle(this, "第三步 · ② 连接本机无线调试"));
        connCard.addView(AuroraUi.caption(this, "配对成功后关闭弹窗，填写“无线调试”主页面显示的连接端口（每次重开无线调试端口可能会变化）："));
        connectionPort = AuroraUi.input(this, "连接端口或 IP:端口（无线调试主页面）", false);
        connectionPort.setText(getPreferences(0).getString("connectionPort", ""));
        LinearLayout.LayoutParams cpLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cpLp.topMargin = AuroraUi.dp(this, 6);
        cpLp.bottomMargin = AuroraUi.dp(this, 8);
        connCard.addView(connectionPort, cpLp);
        connectBtn = AuroraUi.button(this, "连接本机无线调试并检测", AuroraUi.BtnStyle.PRIMARY, v -> connectLocal());
        connCard.addView(connectBtn);
        col.addView(connCard);

        LinearLayout shizukuCard = AuroraUi.card(this);
        shizukuCard.addView(AuroraUi.cardTitle(this, "备选方式 · Shizuku 授权"));
        shizukuCard.addView(AuroraUi.caption(this, "若你平时已经常驻运行 Shizuku，可直接点击下方按钮授权并接入。"));
        shizukuBtn = AuroraUi.button(this, "连接 Shizuku 服务", AuroraUi.BtnStyle.SECONDARY, v -> connectShizuku());
        shizukuCard.addView(shizukuBtn);
        col.addView(shizukuCard);

        return col;
    }

    private View buildUpdatesHelpTab() {
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);

        LinearLayout updCard = AuroraUi.card(this);
        updCard.addView(AuroraUi.cardTitle(this, "助手版本与在线更新"));

        updateStatus = new TextView(this);
        updateStatus.setText("当前助手版本：" + BuildConfig.VERSION_NAME + "\n内置游戏插件：" + PayloadSpec.VERSION);
        updateStatus.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        updateStatus.setLineSpacing(0f, 1.35f);
        updateStatus.setTextColor(AuroraUi.INK);
        LinearLayout.LayoutParams usLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        usLp.topMargin = AuroraUi.dp(this, 4);
        usLp.bottomMargin = AuroraUi.dp(this, 8);
        updCard.addView(updateStatus, usLp);

        updateProgress = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        updateProgress.setMax(100);
        updateProgress.setVisibility(View.GONE);
        LinearLayout.LayoutParams pbLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        pbLp.bottomMargin = AuroraUi.dp(this, 6);
        updCard.addView(updateProgress, pbLp);

        updateDetails = new TextView(this);
        updateDetails.setVisibility(View.GONE);
        updateDetails.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        updateDetails.setTextColor(AuroraUi.INK_MUTED);
        LinearLayout.LayoutParams udLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        udLp.bottomMargin = AuroraUi.dp(this, 8);
        updCard.addView(updateDetails, udLp);

        previewUpdates = new Switch(this);
        previewUpdates.setText("接收测试版 / 群测更新推送");
        previewUpdates.setTextColor(AuroraUi.INK);
        previewUpdates.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        previewUpdates.setChecked(getPreferences(0).getBoolean("previewUpdates", true));
        previewUpdates.setOnCheckedChangeListener((b, checked) ->
            getPreferences(0).edit().putBoolean("previewUpdates", checked).apply());
        LinearLayout.LayoutParams swLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        swLp.topMargin = AuroraUi.dp(this, 2);
        swLp.bottomMargin = AuroraUi.dp(this, 10);
        updCard.addView(previewUpdates, swLp);

        LinearLayout uRow = new LinearLayout(this);
        uRow.setOrientation(LinearLayout.HORIZONTAL);
        updateCheckBtn = AuroraUi.button(this, "检查助手更新", AuroraUi.BtnStyle.PRIMARY, v -> checkAssistantUpdate(false));
        Button relBtn = AuroraUi.button(this, "官方发布页", AuroraUi.BtnStyle.SECONDARY, v -> open(ReleaseUpdates.RELEASES));
        LinearLayout.LayoutParams l1 = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1.3f);
        l1.rightMargin = AuroraUi.dp(this, 6);
        LinearLayout.LayoutParams l2 = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        l2.leftMargin = AuroraUi.dp(this, 6);
        uRow.addView(updateCheckBtn, l1);
        uRow.addView(relBtn, l2);
        updCard.addView(uRow);
        col.addView(updCard);

        LinearLayout docCard = AuroraUi.card(this);
        docCard.addView(AuroraUi.cardTitle(this, "安装文档与开源许可"));
        docCard.addView(AuroraUi.caption(this, "遇到热更新替换、无线配对端口或手牌权限说明时可在此查阅完整图文指南。"));
        docCard.addView(AuroraUi.button(this, "查看安装前与还原指南", AuroraUi.BtnStyle.SECONDARY, v -> showDoc("手机安装与还原指南", HelpContent.INSTALL)));
        docCard.addView(AuroraUi.button(this, "查看游戏内悬浮球说明", AuroraUi.BtnStyle.SECONDARY, v -> showDoc("星枢游戏内使用说明", HelpContent.USAGE)));
        docCard.addView(AuroraUi.button(this, "第三方开源组件声明", AuroraUi.BtnStyle.SUBTLE, v -> showNotices()));
        col.addView(docCard);

        return col;
    }

    private void showNotices() {
        try (InputStream in = getAssets().open("THIRD_PARTY_NOTICES.txt")) {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] b = new byte[4096];
            int n;
            while ((n = in.read(b)) != -1) out.write(b, 0, n);
            showDoc("开源组件声明", out.toString("UTF-8"));
        } catch (Exception e) {
            write(e.getMessage());
        }
    }

    private void showDoc(String title, String content) {
        ScrollView sv = new ScrollView(this);
        TextView tv = new TextView(this);
        int p = AuroraUi.dp(this, 18);
        tv.setPadding(p, AuroraUi.dp(this, 12), p, p);
        tv.setText(content);
        tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        tv.setLineSpacing(0f, 1.38f);
        tv.setTextColor(AuroraUi.INK);
        sv.addView(tv);
        new AlertDialog.Builder(this).setTitle(title).setView(sv).setPositiveButton("知道了", null).show();
    }

    private void open(String url) {
        try { startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url))); }
        catch (Exception e) { write(e.getMessage()); }
    }

    private void write(String s) {
        if (!destroyed && logView != null) logView.append(s + "\n");
    }

    private int port(EditText field) {
        return LocalPort.parse(field.getText().toString());
    }

    private LocalAdbInstaller local() throws Exception {
        if (local == null) local = new LocalAdbInstaller(this);
        return local;
    }

    private void runWorker(String message, WorkerTask task) {
        if (busy) return;
        busy = true;
        lastResult = "";
        write(message);
        refresh();
        worker.execute(() -> {
            String out;
            try {
                out = task.run();
            } catch (Throwable e) {
                // Native pairing libraries can throw linkage errors; report without crashing UI.
                String m = e.getMessage();
                out = "操作未完成：" + (m == null || m.isEmpty() ? e.getClass().getSimpleName() : m);
            }
            final String finalOut = out;
            runOnUiThread(() -> {
                busy = false;
                lastResult = finalOut;
                write(finalOut);
                refresh();
                if (!isFinishing() && !destroyed) {
                    new AlertDialog.Builder(this).setTitle("处理结果").setMessage(finalOut).setPositiveButton("知道了", null).show();
                }
            });
        });
    }

    private void pairLocal() {
        try {
            final int p = port(pairingPort);
            final String code = pairingCode.getText().toString().trim();
            if (!code.matches("[0-9]{6}")) {
                write("请输入系统弹窗中的 6 位数字配对码。");
                selectTab(TAB_INSTALL);
                return;
            }
            selectTab(TAB_INSTALL);
            runWorker("正在配对本机无线调试…", () -> {
                local().pair(p, code);
                useShizuku = false;
                return "配对成功！请关闭系统配对码弹窗，在“无线配对”页填写主页面连接端口后点击“连接本机无线调试并检测”。";
            });
        } catch (Exception e) {
            write(e.getMessage());
            selectTab(TAB_INSTALL);
        }
    }

    private void connectLocal() {
        try {
            final int p = port(connectionPort);
            getPreferences(0).edit().putString("connectionPort", String.valueOf(p)).apply();
            selectTab(TAB_INSTALL);
            runWorker("正在连接本机无线调试并扫描已装渠道包…", () -> {
                local().connect(p);
                useShizuku = false;
                String scanJson = local().scanPackages();
                String resolvedPkg = applyScannedPackagesFromBg(scanJson);
                return "已连接本机无线调试。\n" + local().inspect(resolvedPkg);
            });
        } catch (Exception e) {
            write(e.getMessage());
            selectTab(TAB_INSTALL);
        }
    }

    private void scanPackagesAndInspect() {
        if (busy) return;
        runWorker("正在扫描手机内已安装的游戏渠道…", () -> {
            String scanJson = useShizuku ? remote.scanPackages() : local().scanPackages();
            String resolvedPkg = applyScannedPackagesFromBg(scanJson);
            String inspectOut = useShizuku ? remote.inspect(resolvedPkg) : local().inspect(resolvedPkg);
            return inspectOut;
        });
    }

    private void scanInstalledPackages() {
        boolean shizukuReady = useShizuku && remote != null;
        boolean localReady = !useShizuku && local != null && local.connected();
        if (!shizukuReady && !localReady) {
            write("请先连接无线调试或 Shizuku，再扫描渠道包。");
            return;
        }
        runWorker("正在扫描手机内所有 3.3.0 渠道包…", () -> {
            String scanJson = useShizuku ? remote.scanPackages() : local().scanPackages();
            applyScannedPackagesFromBg(scanJson);
            if (discoveredPackages.isEmpty()) {
                return "未扫描到《吉星派对》渠道包，请确认已安装游戏 3.3.0 并进过一次大厅。";
            }
            StringBuilder sb = new StringBuilder("已识别以下已安装渠道包：\n");
            for (DiscoveredPkg pkg : discoveredPackages) {
                sb.append("· ").append(pkg.label).append("（").append(pkg.packageName).append("，")
                    .append(pkg.versionName).append("）：").append(installStateLabel(pkg.installState)).append("\n");
            }
            return sb.toString().trim();
        });
    }

    private String applyScannedPackagesFromBg(String scanJson) {
        List<DiscoveredPkg> parsed = new ArrayList<>();
        try {
            Map<String, Object> root = MiniJson.parseObject(scanJson);
            Object rawList = root.get("packages");
            if (rawList instanceof List<?>) {
                for (Object item : (List<?>) rawList) {
                    if (!(item instanceof Map<?, ?>)) continue;
                    @SuppressWarnings("unchecked")
                    Map<String, Object> m = (Map<String, Object>) item;
                    String pkgName = MiniJson.stringField(m, "packageName");
                    if (!GamePackageCatalog.isValidPackageName(pkgName)) continue;
                    parsed.add(new DiscoveredPkg(
                        pkgName,
                        MiniJson.stringField(m, "label"),
                        MiniJson.stringField(m, "platform"),
                        MiniJson.stringField(m, "versionName"),
                        MiniJson.stringField(m, "installState"),
                        Boolean.TRUE.equals(m.get("running"))
                    ));
                }
            }
        } catch (Exception ignored) { }

        String nextSelected = selectedPackageName;
        boolean currentPresent = false;
        String first330 = null;
        for (DiscoveredPkg pkg : parsed) {
            if (pkg.packageName.equals(nextSelected)) currentPresent = true;
            if (first330 == null && "3.3.0".equals(pkg.versionName)) first330 = pkg.packageName;
        }
        if (!currentPresent) {
            if (first330 != null) nextSelected = first330;
            else if (!parsed.isEmpty()) nextSelected = parsed.get(0).packageName;
        }
        final String chosen = nextSelected;
        runOnUiThread(() -> {
            discoveredPackages.clear();
            discoveredPackages.addAll(parsed);
            selectPackage(chosen);
        });
        return chosen;
    }

    private void selectPackage(String pkg) {
        if (GamePackageCatalog.isValidPackageName(pkg)) {
            selectedPackageName = pkg;
            getPreferences(0).edit().putString("selectedPackageName", pkg).apply();
        }
        refreshPackageChips();
    }

    private static String installStateLabel(String state) {
        switch (state) {
            case "installed": return "已装本版星枢";
            case "vanilla": return "官方原版";
            case "upgrade": return "旧版星枢可升";
            case "conflict": return "同位插件冲突";
            case "no_catalog": return "请先进入大厅";
            case "no_bundle": return "缺少资源文件";
            case "version_mismatch": return "版本非 3.3.0";
            default: return "待检查";
        }
    }

    private void refreshPackageChips() {
        if (pkgSummaryView == null || packageChipsRow == null) return;
        GamePackageCatalog.ChannelMeta meta = GamePackageCatalog.classifyPackage(selectedPackageName);
        DiscoveredPkg active = null;
        for (DiscoveredPkg item : discoveredPackages) {
            if (item.packageName.equals(selectedPackageName)) {
                active = item;
                break;
            }
        }
        if (active == null) {
            pkgSummaryView.setText("当前目标渠道：" + meta.label + "（" + selectedPackageName + "）");
        } else {
            pkgSummaryView.setText("当前目标渠道：" + active.label + "（" + active.packageName + "）\n版本 "
                + active.versionName + " · 状态：" + installStateLabel(active.installState)
                + (active.running ? " · 游戏运行中" : ""));
        }
        if (launchGameBtn != null) {
            launchGameBtn.setText("启动当前选中渠道：" + meta.label);
        }

        packageChipsRow.removeAllViews();
        if (discoveredPackages.isEmpty()) return;
        for (DiscoveredPkg item : discoveredPackages) {
            boolean selected = item.packageName.equals(selectedPackageName);
            Button chip = AuroraUi.button(
                this,
                (selected ? "✓ " : "○ ") + item.label + "（" + item.packageName + "）· " + installStateLabel(item.installState),
                selected ? AuroraUi.BtnStyle.ACCENT : AuroraUi.BtnStyle.SUBTLE,
                v -> selectPackage(item.packageName)
            );
            chip.setMinHeight(AuroraUi.dp(this, 36));
            chip.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
            chip.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
            chip.setPadding(AuroraUi.dp(this, 12), AuroraUi.dp(this, 6), AuroraUi.dp(this, 12), AuroraUi.dp(this, 6));
            packageChipsRow.addView(chip);
        }
    }

    private void refresh() {
        boolean shizukuReady = useShizuku && remote != null;
        boolean localReady = !useShizuku && local != null && local.connected();
        boolean connected = shizukuReady || localReady;

        installBtn.setEnabled(!busy && connected);
        restoreBtn.setEnabled(!busy && connected);
        inspectBtn.setEnabled(!busy && connected);
        if (scanPackagesBtn != null) scanPackagesBtn.setEnabled(!busy && connected);
        pairBtn.setEnabled(!busy);
        connectBtn.setEnabled(!busy);
        shizukuBtn.setEnabled(!busy && !binding);
        updateCheckBtn.setEnabled(!updateTaskActive.get() && !binding);
        previewUpdates.setEnabled(!updateTaskActive.get());
        pairingPort.setEnabled(!busy);
        pairingCode.setEnabled(!busy);
        connectionPort.setEnabled(!busy);
        refreshAuthUi();

        if (shizukuReady) {
            connBadge.setText("Shizuku 已连接");
            AuroraUi.tintPill(connBadge, AuroraUi.OK_SOFT, AuroraUi.OK_BORDER, AuroraUi.OK_INK, this);
            connSummary.setText("当前通道：Shizuku 已就绪 · 可多渠道检测、安装/更新或扫描登录票据。");
        } else if (localReady) {
            connBadge.setText("无线调试已连接");
            AuroraUi.tintPill(connBadge, AuroraUi.OK_SOFT, AuroraUi.OK_BORDER, AuroraUi.OK_INK, this);
            connSummary.setText("当前通道：本机无线调试 (127.0.0.1) 已就绪 · 可多渠道检测、安装或扫描登录票据。");
        } else if (binding) {
            connBadge.setText("正在连接…");
            AuroraUi.tintPill(connBadge, AuroraUi.ACCENT_SOFT, AuroraUi.ACCENT_BORDER, AuroraUi.ACCENT_INK, this);
            connSummary.setText("正在请求或绑定 Shizuku 调试服务…");
        } else {
            connBadge.setText("待连接");
            AuroraUi.tintPill(connBadge, AuroraUi.SURFACE, AuroraUi.BORDER, AuroraUi.INK_MUTED, this);
            connSummary.setText("尚未连接调试通道：请到“无线配对”页填写端口并连接，或授权 Shizuku。");
        }
    }

    private void refreshAuthUi() {
        boolean connected = (useShizuku && remote != null) || (!useShizuku && local != null && local.connected());
        if (authSummaryView != null) {
            if (boundSession == null) {
                authSummaryView.setText("当前未登录助手云端会话（可扫码/渠道/网页登录，也可直接进游戏使用「趴绑定」）。");
            } else {
                StringBuilder sb = new StringBuilder();
                sb.append("已接入账号：").append(boundSession.nick.isEmpty() ? ("UID " + boundSession.uid) : boundSession.nick)
                    .append("（UID ").append(boundSession.uid).append(" · ").append(boundSession.platform).append("）");
                if (!cloudSummaryLine.isEmpty()) sb.append("\n").append(cloudSummaryLine);
                authSummaryView.setText(sb.toString());
            }
        }
        boolean hasToken = !detectedTokens.isEmpty() && selectedTokenIndex >= 0 && selectedTokenIndex < detectedTokens.size();
        if (scanTokenBtn != null) scanTokenBtn.setEnabled(!authBusy && connected);
        if (bindQqBtn != null) bindQqBtn.setEnabled(!authBusy && hasToken);
        if (hubTokenLoginBtn != null) hubTokenLoginBtn.setEnabled(!authBusy && hasToken);
        if (webAuthBtn != null) webAuthBtn.setEnabled(!authBusy);
        if (webAuthCancelBtn != null) webAuthCancelBtn.setVisibility(authBusy && !webPollCancelled ? View.VISIBLE : View.GONE);
        if (refreshCloudBtn != null) refreshCloudBtn.setEnabled(!authBusy && boundSession != null && !boundSession.token.isEmpty());
        if (hubLogoutBtn != null) hubLogoutBtn.setEnabled(!authBusy && boundSession != null);
        if (launchGameBtn != null) launchGameBtn.setEnabled(!authBusy);
    }

    private void pasteClipboardToLink() {
        try {
            ClipboardManager cm = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
            if (cm != null && cm.hasPrimaryClip() && cm.getPrimaryClip() != null && cm.getPrimaryClip().getItemCount() > 0) {
                CharSequence cs = cm.getPrimaryClip().getItemAt(0).coerceToText(this);
                if (cs != null && qqLinkInput != null) {
                    qqLinkInput.setText(cs.toString().trim());
                    return;
                }
            }
            if (tokenStatusView != null) tokenStatusView.setText("剪贴板为空，请先复制 QQ Bot 发来的登录链接。");
        } catch (Exception e) {
            if (tokenStatusView != null) tokenStatusView.setText("读取剪贴板失败：" + e.getMessage());
        }
    }

    private void scanChannelTokens() {
        if (authBusy) return;
        boolean shizukuReady = useShizuku && remote != null;
        boolean localReady = !useShizuku && local != null && local.connected();
        if (!shizukuReady && !localReady) {
            if (tokenStatusView != null) tokenStatusView.setText("请先在“无线配对”连接本机调试或授权 Shizuku，再扫描渠道登录日志。");
            return;
        }
        authBusy = true;
        if (tokenStatusView != null) tokenStatusView.setText("正在扫描本机 BN_DEBUG 渠道登录票据…");
        refreshAuthUi();
        authWorker.execute(() -> {
            List<BnSdkTokenExtractor.ChannelToken> parsed = new ArrayList<>();
            String err = "";
            try {
                String json = shizukuReady ? remote.readChannelLogcat() : local().readChannelLogcat();
                Map<String, Object> root = MiniJson.parseObject(json);
                err = MiniJson.stringField(root, "error");
                Object rawToks = root.get("tokens");
                if (rawToks instanceof List<?>) {
                    for (Object item : (List<?>) rawToks) {
                        if (!(item instanceof Map<?, ?>)) continue;
                        @SuppressWarnings("unchecked")
                        Map<String, Object> m = (Map<String, Object>) item;
                        String platform = MiniJson.stringField(m, "platform");
                        String appId = MiniJson.stringField(m, "appId");
                        String label = MiniJson.stringField(m, "label");
                        String userId = MiniJson.stringField(m, "userId");
                        String sdkToken = MiniJson.stringField(m, "sdkToken");
                        boolean nat = Boolean.TRUE.equals(m.get("nativeSupported"));
                        if (!sdkToken.isEmpty()) {
                            parsed.add(new BnSdkTokenExtractor.ChannelToken(platform, appId, label, userId, sdkToken, nat));
                        }
                    }
                }
            } catch (Exception e) {
                err = e.getMessage();
            }
            final String finalErr = err;
            runOnUiThread(() -> {
                authBusy = false;
                detectedTokens.clear();
                detectedTokens.addAll(parsed);
                selectedTokenIndex = 0;
                refreshTokenChips(finalErr);
                refreshAuthUi();
            });
        });
    }

    private void refreshTokenChips(String err) {
        if (tokenStatusView == null || tokenListRow == null) return;
        tokenListRow.removeAllViews();
        if (err != null && !err.isEmpty()) {
            tokenStatusView.setText("扫描渠道票据失败：" + err);
            return;
        }
        if (detectedTokens.isEmpty()) {
            tokenStatusView.setText("暂未发现有效的 BN_DEBUG 登录票据。\n请先打开渠道游戏（如小米/TapTap/B站）登录一次进入大厅，再切回助手点扫描；其他长尾渠道也可直接在游戏内点「绑定 QQ 全端同步」。");
            return;
        }
        tokenStatusView.setText("已识别到 " + detectedTokens.size() + " 条渠道票据，点击下方选择要授权的渠道：");
        for (int i = 0; i < detectedTokens.size(); i++) {
            final int idx = i;
            BnSdkTokenExtractor.ChannelToken tok = detectedTokens.get(i);
            boolean selected = (idx == selectedTokenIndex);
            Button chip = AuroraUi.button(
                this,
                (selected ? "✓ " : "○ ") + tok.displaySummary(),
                selected ? AuroraUi.BtnStyle.ACCENT : AuroraUi.BtnStyle.SUBTLE,
                v -> {
                    selectedTokenIndex = idx;
                    refreshTokenChips("");
                    refreshAuthUi();
                }
            );
            chip.setMinHeight(AuroraUi.dp(this, 36));
            chip.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
            chip.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
            chip.setPadding(AuroraUi.dp(this, 12), AuroraUi.dp(this, 6), AuroraUi.dp(this, 12), AuroraUi.dp(this, 6));
            tokenListRow.addView(chip);
        }
    }

    private BnSdkTokenExtractor.ChannelToken currentSelectedToken() {
        if (detectedTokens.isEmpty() || selectedTokenIndex < 0 || selectedTokenIndex >= detectedTokens.size()) return null;
        return detectedTokens.get(selectedTokenIndex);
    }

    private void bindQqWithChannelToken() {
        if (authBusy) return;
        final BnSdkTokenExtractor.ChannelToken tok = currentSelectedToken();
        if (tok == null) {
            if (tokenStatusView != null) tokenStatusView.setText("请先扫描并选中一个渠道登录票据。");
            return;
        }
        final String link = qqLinkInput == null ? "" : qqLinkInput.getText().toString().trim();
        try {
            BnSdkTokenExtractor.parseLoginLink(link);
        } catch (IOException e) {
            if (tokenStatusView != null) tokenStatusView.setText(e.getMessage());
            return;
        }
        authBusy = true;
        if (tokenStatusView != null) tokenStatusView.setText("正在向星枢服务器验证 " + tok.label + " 票据并绑定 QQ…");
        refreshAuthUi();
        authWorker.execute(() -> {
            MobileAuthHub.BoundAccount res = null;
            String err = "";
            try {
                res = MobileAuthHub.completeQqLinkWithChannelToken(link, tok);
            } catch (Exception e) {
                err = e.getMessage();
            }
            final MobileAuthHub.BoundAccount finalRes = res;
            final String finalErr = err;
            runOnUiThread(() -> {
                authBusy = false;
                refreshAuthUi();
                if (finalRes != null) {
                    String msg = "QQ Bot 授权成功！\n已绑定：" + finalRes.nick + "（UID " + finalRes.uid + " · " + tok.label + "）\n\n可以回到 QQ 群发送「趴数据」或「趴小镇」验证。";
                    if (tokenStatusView != null) tokenStatusView.setText(msg);
                    if (!isFinishing() && !destroyed) {
                        new AlertDialog.Builder(this).setTitle("QQ 绑定成功").setMessage(msg).setPositiveButton("知道了", null).show();
                    }
                } else {
                    String tip = finalErr == null || finalErr.isEmpty() ? "绑定未完成" : finalErr;
                    if (tokenStatusView != null) tokenStatusView.setText("授权未完成：" + tip);
                    if (!isFinishing() && !destroyed) {
                        new AlertDialog.Builder(this).setTitle("授权提示").setMessage(tip).setPositiveButton("知道了", null).show();
                    }
                }
            });
        });
    }

    private void startHubChannelAuth() {
        if (authBusy) return;
        final BnSdkTokenExtractor.ChannelToken tok = currentSelectedToken();
        if (tok == null) {
            if (tokenStatusView != null) tokenStatusView.setText("请先扫描并选中一个渠道登录票据。");
            return;
        }
        authBusy = true;
        webPollCancelled = false;
        if (tokenStatusView != null) tokenStatusView.setText("正在通过 " + tok.label + " 票据登录助手云端账号…");
        refreshAuthUi();
        final String deviceId = MobileAuthHub.getOrCreateDeviceId(getPreferences(0));
        authWorker.execute(() -> {
            try {
                MobileAuthHub.DeviceStart start = MobileAuthHub.startChannelHubLogin(deviceId, tok);
                long deadline = SystemClock.elapsedRealtime() + 75000L;
                while (SystemClock.elapsedRealtime() < deadline && !destroyed && !webPollCancelled) {
                    MobileAuthHub.PollOutcome poll = MobileAuthHub.pollDeviceLogin(start.requestId, start.verifier);
                    if ("succeeded".equals(poll.status) && poll.account != null) {
                        MobileAuthHub.saveSession(getPreferences(0), poll.account);
                        String summary = "";
                        try { summary = MobileAuthHub.fetchAccountSummary(poll.account.token); } catch (Exception ignored) { }
                        final MobileAuthHub.BoundAccount acct = poll.account;
                        final String sum = summary;
                        runOnUiThread(() -> {
                            authBusy = false;
                            boundSession = acct;
                            cloudSummaryLine = sum;
                            if (tokenStatusView != null) tokenStatusView.setText("助手渠道登录成功：" + acct.nick + "（UID " + acct.uid + "）");
                            refreshAuthUi();
                        });
                        return;
                    }
                    if ("failed".equals(poll.status) || "expired".equals(poll.status) || "superseded".equals(poll.status)) {
                        String msg = poll.error != null && !poll.error.isEmpty() ? poll.error : "渠道登录未完成，请先退出同账号游戏客户端后重试。";
                        throw new IOException(msg);
                    }
                    Thread.sleep(1500);
                }
                throw new IOException("等待渠道登录超时，请重试");
            } catch (Exception e) {
                String msg = e.getMessage() == null ? "渠道登录失败" : e.getMessage();
                runOnUiThread(() -> {
                    authBusy = false;
                    if (tokenStatusView != null) tokenStatusView.setText(msg);
                    refreshAuthUi();
                });
            }
        });
    }

    private void startWebAuth() {
        if (authBusy) return;
        authBusy = true;
        webPollCancelled = false;
        if (webAuthStatusView != null) webAuthStatusView.setText("正在向星枢服务器申请官服 / Steam 授权页…");
        refreshAuthUi();
        final String deviceId = MobileAuthHub.getOrCreateDeviceId(getPreferences(0));
        authWorker.execute(() -> {
            try {
                final MobileAuthHub.DeviceStart start = MobileAuthHub.startWebDeviceLogin(deviceId);
                runOnUiThread(() -> {
                    if (webAuthStatusView != null) {
                        webAuthStatusView.setText("请在打开的浏览器中完成手机号短信或 Steam 验证，完成后自动返回同步…");
                    }
                    open(start.loginUrl);
                });
                long deadline = SystemClock.elapsedRealtime() + 180000L;
                while (SystemClock.elapsedRealtime() < deadline && !destroyed && !webPollCancelled) {
                    Thread.sleep(2000);
                    if (webPollCancelled || destroyed) break;
                    MobileAuthHub.PollOutcome poll = MobileAuthHub.pollDeviceLogin(start.requestId, start.verifier);
                    if ("succeeded".equals(poll.status) && poll.account != null) {
                        MobileAuthHub.saveSession(getPreferences(0), poll.account);
                        String summary = "";
                        try { summary = MobileAuthHub.fetchAccountSummary(poll.account.token); } catch (Exception ignored) { }
                        final MobileAuthHub.BoundAccount acct = poll.account;
                        final String sum = summary;
                        runOnUiThread(() -> {
                            authBusy = false;
                            boundSession = acct;
                            cloudSummaryLine = sum;
                            if (webAuthStatusView != null) {
                                webAuthStatusView.setText("已通过官网验证登录：" + acct.nick + "（UID " + acct.uid + "）");
                            }
                            refreshAuthUi();
                        });
                        return;
                    }
                    if ("failed".equals(poll.status) || "expired".equals(poll.status) || "superseded".equals(poll.status)) {
                        throw new IOException(poll.error != null && !poll.error.isEmpty() ? poll.error : "网页授权已取消或超时");
                    }
                }
                if (!webPollCancelled && !destroyed) {
                    throw new IOException("等待网页验证超时，请重新点击发起。");
                }
                runOnUiThread(() -> {
                    authBusy = false;
                    refreshAuthUi();
                });
            } catch (Exception e) {
                String msg = e.getMessage() == null ? "网页授权失败" : e.getMessage();
                runOnUiThread(() -> {
                    authBusy = false;
                    if (webAuthStatusView != null) webAuthStatusView.setText(msg);
                    refreshAuthUi();
                });
            }
        });
    }

    private void refreshHubAccountSummary() {
        if (authBusy || boundSession == null || boundSession.token.isEmpty()) return;
        authBusy = true;
        refreshAuthUi();
        final String tok = boundSession.token;
        authWorker.execute(() -> {
            String line = "";
            boolean expired = false;
            String err = "";
            try {
                line = MobileAuthHub.fetchAccountSummary(tok);
            } catch (Exception e) {
                if ("UNAUTHORIZED".equals(e.getMessage())) expired = true;
                else err = e.getMessage();
            }
            final String finalLine = line;
            final boolean isExpired = expired;
            final String finalErr = err;
            runOnUiThread(() -> {
                authBusy = false;
                if (isExpired) {
                    MobileAuthHub.clearSession(getPreferences(0));
                    boundSession = null;
                    cloudSummaryLine = "";
                    if (authSummaryView != null) authSummaryView.setText("会话已过期，请重新扫码或渠道登录。");
                } else if (!finalErr.isEmpty()) {
                    if (authSummaryView != null) authSummaryView.setText("刷新失败：" + finalErr);
                } else {
                    cloudSummaryLine = finalLine;
                }
                refreshAuthUi();
            });
        });
    }

    private void logoutHubSession() {
        final MobileAuthHub.BoundAccount prev = boundSession;
        MobileAuthHub.clearSession(getPreferences(0));
        boundSession = null;
        cloudSummaryLine = "";
        refreshAuthUi();
        if (prev != null && !prev.token.isEmpty()) {
            authWorker.execute(() -> MobileAuthHub.logout(prev.token));
        }
    }

    private void launchSelectedGame() {
        final String pkg = GamePackageCatalog.isValidPackageName(selectedPackageName)
            ? selectedPackageName : GamePackageCatalog.DEFAULT_PACKAGE;
        boolean shizukuReady = useShizuku && remote != null;
        boolean localReady = !useShizuku && local != null && local.connected();
        if (shizukuReady || localReady) {
            authBusy = true;
            refreshAuthUi();
            authWorker.execute(() -> {
                String out;
                try {
                    out = shizukuReady ? remote.launchGame(pkg) : local().launchGame(pkg);
                } catch (Exception e) {
                    out = "启动失败：" + e.getMessage();
                }
                final String finalOut = out;
                runOnUiThread(() -> {
                    authBusy = false;
                    write(finalOut);
                    refreshAuthUi();
                });
            });
            return;
        }
        try {
            Intent launch = getPackageManager().getLaunchIntentForPackage(pkg);
            if (launch != null) {
                launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                startActivity(launch);
                return;
            }
        } catch (Exception ignored) { }
        write("未检测到系统启动入口；请在系统桌面打开对应游戏渠道，或先连接无线调试。");
    }

    private void connectShizuku() {
        if (busy || binding) return;
        useShizuku = true;
        try {
            if (!Shizuku.pingBinder()) {
                write("未检测到 Shizuku 服务。可先启动 Shizuku，或使用“无线配对”页自带的一步配对。");
                refresh();
                return;
            }
            if (Shizuku.checkSelfPermission() != PackageManager.PERMISSION_GRANTED) {
                if (Shizuku.shouldShowRequestPermissionRationale()) write("请在 Shizuku 的应用管理中允许星枢安装助手，然后返回连接。");
                else Shizuku.requestPermission(1);
                refresh();
                return;
            }
            if (remote == null) {
                binding = true;
                Shizuku.bindUserService(serviceArgs, connection);
                progressHandler.postDelayed(() -> {
                    if (!destroyed && remote == null) {
                        binding = false;
                        write("Shizuku 连接超时。请确认 Shizuku 在后台正常运行，或改用“无线配对”。");
                        refresh();
                    }
                }, 15000);
            } else {
                scanPackagesAndInspect();
            }
            refresh();
        } catch (Exception e) {
            binding = false;
            write("Shizuku 连接失败：" + e.getMessage());
            refresh();
        }
    }

    private void inspect() {
        run(false, false, null);
    }

    private void execute(boolean undo) {
        if (busy) return;
        final String pkg = GamePackageCatalog.isValidPackageName(selectedPackageName)
            ? selectedPackageName : GamePackageCatalog.DEFAULT_PACKAGE;
        busy = true;
        lastResult = "";
        write(undo ? "正在检查官方原版恢复条件（" + pkg + "）…" : "正在检查资源版本与冲突（" + pkg + "）…");
        refresh();
        worker.execute(() -> {
            String result;
            try {
                result = useShizuku ? remote.inspect(pkg) : local().inspect(pkg);
            } catch (Exception e) {
                result = "检查未完成：" + e.getMessage();
            }
            final String out = result;
            runOnUiThread(() -> {
                busy = false;
                lastResult = out;
                write(out);
                refresh();
                if (!out.startsWith("游戏版本匹配") || out.contains("游戏仍在运行")) {
                    new AlertDialog.Builder(this).setTitle("安装检查未通过").setMessage(out).setPositiveButton("知道了", null).show();
                    return;
                }
                if (!undo && out.contains("同位置覆盖冲突")) {
                    Matcher match = Pattern.compile("冲突文件SHA256：([a-f0-9]{64})").matcher(out);
                    if (!match.find()) { write("无法确认冲突文件校验和，请重新检查。"); return; }
                    final String conflictHash = match.group(1);
                    new AlertDialog.Builder(this).setTitle("检测到同定位冲突或变体资源")
                        .setMessage("当前位置已被其他同定位插件（如 APX）或非本版缓存占用。\n\n继续后会先按 SHA256 完整备份原文件，再清理冲突并替换为星枢。")
                        .setNegativeButton("暂不安装", null)
                        .setPositiveButton("备份整理并安装星枢", (d, w) -> run(true, false, conflictHash))
                        .show();
                    return;
                }
                run(true, undo, null);
            });
        });
    }

    private void run(boolean apply, boolean undo, String conflictHash) {
        if (busy) return;
        final String pkg = GamePackageCatalog.isValidPackageName(selectedPackageName)
            ? selectedPackageName : GamePackageCatalog.DEFAULT_PACKAGE;
        if (!useShizuku) {
            runWorker(apply ? (undo ? "正在还原官方原版（" + pkg + "）…" : "正在写入并校验星枢插件（" + pkg + "）…") : "正在检查当前版本与路径…",
                () -> apply ? local().apply(pkg, undo, conflictHash) : local().inspect(pkg));
            return;
        }
        if (remote == null) { write("请先连接 Shizuku 或本机无线调试。"); return; }
        final IInstaller service = remote;
        runWorker(apply ? (undo ? "正在通过 Shizuku 还原原版（" + pkg + "）…" : "正在通过 Shizuku 写入并校验星枢（" + pkg + "）…") : "正在检查游戏版本与插件状态…", () -> {
            if (!apply) return service.inspect(pkg);
            File patch = undo ? null : stage("candidate.__data"), vanilla = stage("vanilla.__data");
            try (ParcelFileDescriptor p = undo ? null : ParcelFileDescriptor.open(patch, ParcelFileDescriptor.MODE_READ_ONLY);
                 ParcelFileDescriptor v = ParcelFileDescriptor.open(vanilla, ParcelFileDescriptor.MODE_READ_ONLY)) {
                return service.apply(pkg, undo, conflictHash, p, v);
            } finally {
                if (patch != null) patch.delete();
                if (vanilla != null) vanilla.delete();
            }
        });
    }

    private File stage(String asset) throws IOException {
        File f = new File(getCacheDir(), asset);
        try (InputStream in = getAssets().open(asset); FileOutputStream out = new FileOutputStream(f)) {
            byte[] b = new byte[65536];
            int n;
            while ((n = in.read(b)) != -1) out.write(b, 0, n);
            out.getFD().sync();
        }
        return f;
    }

    // ---- Self Update + Banner + Modal Dialog ----

    private void updateFinished() {
        checkStage = null;
        progressHandler.removeCallbacks(checkTick);
        if (!destroyed) refresh();
    }

    private void stopDownloadProgress() {
        progressHandler.removeCallbacks(progressTick);
        downloadProgress = null;
        verifyingDownload = false;
    }

    private boolean acquireUpdateTask(boolean silent) {
        if (updateTaskActive.compareAndSet(false, true)) return true;
        if (!silent) write("上一次更新检查或下载尚未结束，请稍候。");
        return false;
    }

    private void runUpdateTask(Runnable task) {
        updateWorker.execute(() -> {
            try { task.run(); }
            finally {
                updateTaskActive.set(false);
                if (!destroyed) runOnUiThread(this::refresh);
            }
        });
    }

    private void checkAssistantUpdate(boolean silent) {
        if (!acquireUpdateTask(silent)) return;
        stopDownloadProgress();
        updateProgress.setVisibility(View.VISIBLE);
        updateProgress.setIndeterminate(true);
        checkStarted = SystemClock.elapsedRealtime();
        checkStage = "正在连接官方更新服务…";
        progressHandler.removeCallbacks(checkTick);
        progressHandler.post(checkTick);
        updateStatus.setText("正在检查新版本…");
        updateDetails.setVisibility(View.VISIBLE);
        final boolean preview = previewUpdates.isChecked();
        refresh();

        runUpdateTask(() -> {
            try {
                final ReleaseUpdates.Update found = ReleaseUpdates.check(
                    preview,
                    () -> getAssets().open("update-public-cert.txt"),
                    label -> runOnUiThread(() -> { if (!destroyed) checkStage = label; }));
                runOnUiThread(() -> {
                    updateFinished();
                    updateProgress.setVisibility(View.GONE);
                    if (destroyed) return;
                    if (found == null) {
                        updateStatus.setText("当前已是最新官方版本（" + BuildConfig.VERSION_NAME + "）");
                        updateDetails.setVisibility(View.GONE);
                        if (!silent) {
                            new AlertDialog.Builder(this).setTitle("已是最新版本")
                                .setMessage("未检测到可更新的官方签名版本。\n\n当前版本：" + BuildConfig.VERSION_NAME)
                                .setPositiveButton("知道了", null).show();
                        }
                        return;
                    }
                    try {
                        boolean newer = found.code() > BuildConfig.VERSION_CODE;
                        if (newer) {
                            availableUpdate = found;
                            bannerText.setText("⬇ 发现新版本 " + found.version());
                            if (dismissedModalCode != found.code()) updateBanner.setVisibility(View.VISIBLE);
                            updateStatus.setText("发现新版本：" + found.version() + "（当前 " + BuildConfig.VERSION_NAME + "）");
                            updateDetails.setText("点击下方“检查助手更新”或顶部横幅即可下载安装");
                            if (!silent || autoPromptShownCode != found.code()) {
                                autoPromptShownCode = found.code();
                                showUpdateModal(found);
                            }
                        } else {
                            availableUpdate = null;
                            updateBanner.setVisibility(View.GONE);
                            updateStatus.setText("已是最新版本：" + BuildConfig.VERSION_NAME);
                            updateDetails.setVisibility(View.GONE);
                            if (!silent) {
                                new AlertDialog.Builder(this).setTitle("已是最新版本")
                                    .setMessage("当前版本 " + BuildConfig.VERSION_NAME + " 已是最新（远程最新：" + found.version() + "）。")
                                    .setNeutralButton("官方发布页", (d, w) -> open(ReleaseUpdates.RELEASES))
                                    .setPositiveButton("知道了", null).show();
                            }
                        }
                    } catch (Exception e) {
                        if (!silent) write("检查更新失败：" + e.getMessage());
                    }
                });
            } catch (Exception e) {
                android.util.Log.w("XingShuUpdate", "Assistant update check failed", e);
                final String failedStage = checkStage;
                runOnUiThread(() -> {
                    updateFinished();
                    updateProgress.setVisibility(View.GONE);
                    if (destroyed) return;
                    if (silent) {
                        updateStatus.setText("助手版本：" + BuildConfig.VERSION_NAME + "（后台检查暂未连通网络）");
                        updateDetails.setVisibility(View.GONE);
                    } else {
                        updateStatus.setText("检查更新未完成：" + ReleaseUpdates.failureMessage(e));
                        updateDetails.setText(failedStage == null ? "" : failedStage);
                        write("更新检查：" + ReleaseUpdates.failureMessage(e));
                    }
                });
            }
        });
    }

    private void showUpdateModal(ReleaseUpdates.Update update) {
        if (destroyed || isFinishing()) return;
        if (updateDialog != null && updateDialog.isShowing()) return;

        String ver;
        String notes;
        long totalBytes = 0;
        long code = 0;
        try {
            ver = update.version();
            notes = update.notes();
            code = update.code();
            if (update.manifest != null && update.manifest.optJSONObject("android") != null) {
                totalBytes = update.manifest.optJSONObject("android").optLong("size", 0);
            }
        } catch (Exception e) {
            write("读取更新信息失败：" + e.getMessage());
            return;
        }

        Dialog dlg = new Dialog(this);
        dlg.requestWindowFeature(Window.FEATURE_NO_TITLE);
        Window w = dlg.getWindow();
        if (w != null) {
            w.setBackgroundDrawable(new ColorDrawable(Color.TRANSPARENT));
            w.setDimAmount(0.50f);
        }

        LinearLayout sheet = new LinearLayout(this);
        sheet.setOrientation(LinearLayout.VERTICAL);
        sheet.setBackground(AuroraUi.rounded(AuroraUi.SURFACE, AuroraUi.BORDER, 22, this));
        int pad = AuroraUi.dp(this, 20);
        sheet.setPadding(pad, pad, pad, AuroraUi.dp(this, 16));

        // Top row: download badge + dismiss X
        LinearLayout topRow = new LinearLayout(this);
        topRow.setOrientation(LinearLayout.HORIZONTAL);
        topRow.setGravity(Gravity.CENTER_VERTICAL);

        TextView iconBox = new TextView(this);
        iconBox.setText("↓");
        iconBox.setGravity(Gravity.CENTER);
        iconBox.setTextSize(TypedValue.COMPLEX_UNIT_SP, 18f);
        iconBox.setTypeface(Typeface.DEFAULT_BOLD);
        iconBox.setTextColor(AuroraUi.ACCENT_INK);
        iconBox.setBackground(AuroraUi.rounded(AuroraUi.ACCENT_SOFT, AuroraUi.ACCENT_BORDER, 12, this));
        topRow.addView(iconBox, new LinearLayout.LayoutParams(AuroraUi.dp(this, 38), AuroraUi.dp(this, 38)));

        View spacer = new View(this);
        topRow.addView(spacer, new LinearLayout.LayoutParams(0, 1, 1f));

        TextView closeX = new TextView(this);
        closeX.setText("✕");
        closeX.setGravity(Gravity.CENTER);
        closeX.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f);
        closeX.setTextColor(AuroraUi.INK_MUTED);
        closeX.setBackground(AuroraUi.rounded(AuroraUi.SURFACE_SUBTLE, 0, 16, this));
        closeX.setOnClickListener(v -> dlg.dismiss());
        topRow.addView(closeX, new LinearLayout.LayoutParams(AuroraUi.dp(this, 30), AuroraUi.dp(this, 30)));
        sheet.addView(topRow);

        TextView titleTv = new TextView(this);
        titleTv.setText("发现新版本 " + ver);
        titleTv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f);
        titleTv.setTypeface(Typeface.DEFAULT_BOLD);
        titleTv.setTextColor(AuroraUi.INK);
        LinearLayout.LayoutParams tLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tLp.topMargin = AuroraUi.dp(this, 14);
        sheet.addView(titleTv, tLp);

        TextView curTv = new TextView(this);
        curTv.setText("当前版本 " + BuildConfig.VERSION_NAME);
        curTv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        curTv.setTextColor(AuroraUi.INK_MUTED);
        LinearLayout.LayoutParams cLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cLp.topMargin = AuroraUi.dp(this, 3);
        cLp.bottomMargin = AuroraUi.dp(this, 14);
        sheet.addView(curTv, cLp);

        // Scrollable release notes box
        ScrollView notesScroll = new ScrollView(this);
        notesScroll.setBackground(AuroraUi.rounded(AuroraUi.SURFACE_SUBTLE, AuroraUi.BORDER, 14, this));
        TextView notesTv = new TextView(this);
        int np = AuroraUi.dp(this, 14);
        notesTv.setPadding(np, np, np, np);
        String bodyText = "# 星枢手机助手 " + ver + "\n\n"
            + (notes == null || notes.trim().isEmpty() ? "修复稳定性并优化移动端体验。" : notes.trim());
        notesTv.setText(bodyText);
        notesTv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        notesTv.setLineSpacing(0f, 1.4f);
        notesTv.setTextColor(AuroraUi.INK);
        notesScroll.addView(notesTv);
        int maxNotesH = Math.min(AuroraUi.dp(this, 230), (int) (getResources().getDisplayMetrics().heightPixels * 0.30f));
        LinearLayout.LayoutParams nsLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, maxNotesH);
        nsLp.bottomMargin = AuroraUi.dp(this, 14);
        sheet.addView(notesScroll, nsLp);

        // Inline download progress section inside modal
        modalProgress = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        modalProgress.setMax(100);
        modalProgress.setVisibility(View.GONE);
        LinearLayout.LayoutParams mpLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        mpLp.bottomMargin = AuroraUi.dp(this, 4);
        sheet.addView(modalProgress, mpLp);

        modalProgressText = new TextView(this);
        modalProgressText.setVisibility(View.GONE);
        modalProgressText.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        modalProgressText.setTextColor(AuroraUi.INK_MUTED);
        LinearLayout.LayoutParams mptLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        mptLp.bottomMargin = AuroraUi.dp(this, 10);
        sheet.addView(modalProgressText, mptLp);

        // Prominent gradient pill CTA
        boolean alreadyDownloaded = false;
        try {
            alreadyDownloaded = pendingUpdate != null && pendingUpdate.code() == code && new File(getCacheDir(), "update.apk").exists();
        } catch (Exception ignored) { }

        String sizeSuffix = totalBytes > 0 ? " · " + AuroraUi.formatMb(totalBytes) : "";
        String ctaLabel = alreadyDownloaded ? ("立即安装 " + ver) : ("↓  下载并安装 " + ver + sizeSuffix);

        modalActionBtn = new Button(this);
        modalActionBtn.setAllCaps(false);
        modalActionBtn.setText(ctaLabel);
        modalActionBtn.setTextColor(Color.WHITE);
        modalActionBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f);
        modalActionBtn.setTypeface(Typeface.DEFAULT_BOLD);
        modalActionBtn.setStateListAnimator(null);
        modalActionBtn.setBackground(AuroraUi.gradientPill(0xFFFF7A2F, 0xFFF43F5E, 24, this));
        LinearLayout.LayoutParams ctaLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, AuroraUi.dp(this, 48));
        ctaLp.bottomMargin = AuroraUi.dp(this, 8);
        boolean finalAlreadyDownloaded = alreadyDownloaded;
        modalActionBtn.setOnClickListener(v -> {
            if (finalAlreadyDownloaded) {
                dlg.dismiss();
                requestAssistantInstall();
            } else {
                downloadAssistant(update);
            }
        });
        sheet.addView(modalActionBtn, ctaLp);

        TextView laterBtn = new TextView(this);
        laterBtn.setText("稍后再说");
        laterBtn.setGravity(Gravity.CENTER);
        laterBtn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f);
        laterBtn.setTextColor(AuroraUi.INK_MUTED);
        laterBtn.setPadding(0, AuroraUi.dp(this, 8), 0, AuroraUi.dp(this, 10));
        laterBtn.setOnClickListener(v -> dlg.dismiss());
        sheet.addView(laterBtn, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView secFooter = new TextView(this);
        secFooter.setText("安装前会校验文件 SHA256、版本号与官方签名，最后由系统确认安装。");
        secFooter.setGravity(Gravity.CENTER);
        secFooter.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f);
        secFooter.setTextColor(AuroraUi.INK_SUBTLE);
        sheet.addView(secFooter);

        dlg.setContentView(sheet);
        if (w != null) {
            int dialogWidth = Math.min(AuroraUi.dp(this, 392), (int) (getResources().getDisplayMetrics().widthPixels * 0.90f));
            w.setLayout(dialogWidth, ViewGroup.LayoutParams.WRAP_CONTENT);
        }
        dlg.setOnDismissListener(d -> {
            updateDialog = null;
            modalProgress = null;
            modalProgressText = null;
            modalActionBtn = null;
        });
        updateDialog = dlg;
        dlg.show();
    }

    private void downloadAssistant(ReleaseUpdates.Update update) {
        if (!acquireUpdateTask(false)) return;
        stopDownloadProgress();
        long assetSize = 0;
        if (update.manifest != null && update.manifest.optJSONObject("android") != null) {
            assetSize = update.manifest.optJSONObject("android").optLong("size", 0);
        }
        downloadProgress = new DownloadProgress(assetSize, SystemClock.elapsedRealtime());
        verifyingDownload = false;

        updateStatus.setText("正在下载新版助手，请保持应用在前台…");
        updateProgress.setVisibility(View.VISIBLE);
        updateProgress.setIndeterminate(true);
        updateDetails.setVisibility(View.VISIBLE);
        updateDetails.setText("正在连接下载服务器 / 国内镜像…");
        if (modalProgress != null && updateDialog != null && updateDialog.isShowing()) {
            modalProgress.setVisibility(View.VISIBLE);
            modalProgress.setIndeterminate(true);
            modalProgressText.setVisibility(View.VISIBLE);
            modalProgressText.setText("正在连接下载服务器 / 国内镜像…");
            modalActionBtn.setText("正在准备下载…");
            modalActionBtn.setEnabled(false);
        }
        progressHandler.post(progressTick);
        refresh();

        runUpdateTask(() -> {
            try {
                File apk = ReleaseUpdates.download(update, getCacheDir(), new ReleaseUpdates.DownloadListener() {
                    @Override public void progress(long received, long total) {
                        runOnUiThread(() -> {
                            if (downloadProgress != null) downloadProgress.received(received, SystemClock.elapsedRealtime());
                        });
                    }
                    @Override public void verifying() {
                        runOnUiThread(() -> { verifyingDownload = true; });
                    }
                });
                verifySelfApk(apk, update);
                runOnUiThread(() -> {
                    stopDownloadProgress();
                    updateFinished();
                    updateProgress.setVisibility(View.GONE);
                    if (destroyed) return;
                    pendingUpdate = update;
                    updateStatus.setText("新版 APK 校验通过，正在调起系统安装器…");
                    updateDetails.setText("SHA256 与签名一致；更新助手后请再点击安装插件更新");
                    if (updateDialog != null && updateDialog.isShowing()) updateDialog.dismiss();
                    requestAssistantInstall();
                });
            } catch (Exception e) {
                android.util.Log.w("XingShuUpdate", "Assistant update download failed", e);
                runOnUiThread(() -> {
                    stopDownloadProgress();
                    updateFinished();
                    updateProgress.setVisibility(View.GONE);
                    if (destroyed) return;
                    String msg = ReleaseUpdates.failureMessage(e);
                    updateStatus.setText("下载或校验失败：" + msg);
                    updateDetails.setVisibility(View.GONE);
                    write("助手更新下载失败：" + msg);
                    if (modalActionBtn != null && updateDialog != null && updateDialog.isShowing()) {
                        modalProgress.setVisibility(View.GONE);
                        modalProgressText.setText("下载未完成：" + msg);
                        modalActionBtn.setText("重试下载");
                        modalActionBtn.setEnabled(true);
                    }
                });
            }
        });
    }

    @SuppressWarnings("deprecation")
    private void verifySelfApk(File apk, ReleaseUpdates.Update update) throws Exception {
        PackageInfo next = getPackageManager().getPackageArchiveInfo(apk.getAbsolutePath(), PackageManager.GET_SIGNATURES);
        PackageInfo own = getPackageManager().getPackageInfo(getPackageName(), PackageManager.GET_SIGNATURES);
        if (next == null || !getPackageName().equals(next.packageName)
            || next.versionCode != update.code()
            || next.versionCode <= BuildConfig.VERSION_CODE
            || next.signatures == null || own.signatures == null
            || next.signatures.length != 1 || own.signatures.length != 1
            || !next.signatures[0].equals(own.signatures[0])) {
            throw new SecurityException("安装包签名或版本号校验未通过");
        }
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream in = new java.io.FileInputStream(apk)) {
            byte[] b = new byte[65536];
            int n;
            while ((n = in.read(b)) != -1) digest.update(b, 0, n);
        }
        if (!ReleaseUpdates.hex(digest.digest()).equals(update.manifest.getJSONObject("android").getString("sha256"))) {
            throw new IOException("APK 文件哈希已变化");
        }
    }

    private void requestAssistantInstall() {
        try {
            File apk = new File(getCacheDir(), "update.apk");
            if (pendingUpdate == null || !apk.exists()) return;
            verifySelfApk(apk, pendingUpdate);
            if (Build.VERSION.SDK_INT >= 26 && !getPackageManager().canRequestPackageInstalls()) {
                new AlertDialog.Builder(this).setTitle("需要允许安装应用")
                    .setMessage("Android 系统需要授予“星枢手机助手”安装未知应用权限后，才能完成本次自身覆盖更新。")
                    .setNegativeButton("取消", null)
                    .setPositiveButton("前往设置授权", (d, w) -> {
                        try {
                            waitingInstallPermission = true;
                            startActivity(new Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + getPackageName())));
                        } catch (Exception e) {
                            waitingInstallPermission = false;
                            write("请在系统设置中允许星枢手机助手安装应用，然后重新点击安装。");
                        }
                    }).show();
                return;
            }
            Uri uri = Uri.parse("content://" + BuildConfig.APPLICATION_ID + ".update/update.apk");
            startActivity(new Intent(Intent.ACTION_VIEW)
                .setDataAndType(uri, "application/vnd.android.package-archive")
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK));
        } catch (Exception e) {
            write("未能打开系统安装页面：" + e.getMessage());
        }
    }

    private String buildDiagnosticReport() {
        StringBuilder sb = new StringBuilder();
        sb.append("=== 星枢手机安装助手 · 诊断报告 ===\n");
        sb.append("导出时间: ").append(new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.CHINA).format(new Date())).append("\n");
        sb.append("助手版本: ").append(BuildConfig.VERSION_NAME).append(" (code=").append(BuildConfig.VERSION_CODE).append(")\n");
        sb.append("内置插件版本: ").append(PayloadSpec.VERSION).append("\n");
        sb.append("设备信息: ").append(Build.MODEL).append(" / Android ").append(Build.VERSION.RELEASE).append(" (API ").append(Build.VERSION.SDK_INT).append(")\n");
        sb.append("当前选中渠道包名: ").append(selectedPackageName).append("\n");
        sb.append("已发现渠道数量: ").append(discoveredPackages.size()).append("\n");
        sb.append("默认资源相对路径: ").append(PayloadSpec.RELATIVE).append("\n");
        sb.append("期望清单 SHA256: ").append(PayloadSpec.CATALOG).append("\n");
        sb.append("连接模式: ").append(useShizuku ? "Shizuku" : "本机无线调试(LocalADB)").append("\n\n");
        if (!lastResult.isEmpty()) sb.append("--- 最近检查/执行结果 ---\n").append(lastResult).append("\n\n");
        sb.append("--- 界面日志 ---\n").append(logView != null ? logView.getText().toString() : "");
        return sb.toString();
    }

    private void exportFailureLog() {
        try {
            String ts = new SimpleDateFormat("yyyyMMdd-HHmmss", Locale.CHINA).format(new Date());
            Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT)
                .addCategory(Intent.CATEGORY_OPENABLE)
                .setType("text/plain")
                .putExtra(Intent.EXTRA_TITLE, "XingShu-Android-Failure-" + ts + ".log");
            startActivityForResult(intent, REQ_EXPORT_LOG);
        } catch (Exception e) {
            write("无法打开系统文件保存窗口：" + e.getMessage());
        }
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQ_EXPORT_LOG && resultCode == RESULT_OK && data != null && data.getData() != null) {
            try (OutputStream out = getContentResolver().openOutputStream(data.getData())) {
                if (out == null) throw new IOException("系统未返回写入流");
                out.write(buildDiagnosticReport().getBytes(StandardCharsets.UTF_8));
                out.flush();
                write("已导出失败诊断日志。");
            } catch (Exception e) {
                write("导出失败日志出错：" + e.getMessage());
            }
        }
    }

    @Override protected void onResume() {
        super.onResume();
        if (connBadge != null) refresh();
        if (waitingInstallPermission) {
            waitingInstallPermission = false;
            if (pendingUpdate != null && (Build.VERSION.SDK_INT < 26 || getPackageManager().canRequestPackageInstalls())) {
                requestAssistantInstall();
            }
        }
    }

    @Override protected void onDestroy() {
        destroyed = true;
        checkStage = null;
        progressHandler.removeCallbacks(checkTick);
        stopDownloadProgress();
        if (updateDialog != null && updateDialog.isShowing()) updateDialog.dismiss();
        Shizuku.removeBinderReceivedListener(received);
        Shizuku.removeBinderDeadListener(dead);
        Shizuku.removeRequestPermissionResultListener(permission);
        worker.execute(() -> {
            try { if (local != null) local.close(); } catch (Exception ignored) { }
            try { if (Shizuku.pingBinder()) Shizuku.unbindUserService(serviceArgs, connection, true); } catch (Exception ignored) { }
        });
        worker.shutdown();
        authWorker.shutdownNow();
        updateWorker.shutdownNow();
        super.onDestroy();
    }

    private static String compactVer(String full) {
        if (full == null) return "";
        return full.length() > 20 ? full.replace("-test.", " · ") : full;
    }

    private interface WorkerTask {
        String run() throws Exception;
    }

    /** XingShu Aurora Light UI toolkit shared across tabs and dialogs. */
    private static final class AuroraUi {
        static final int CANVAS = 0xFFFCFBF8;
        static final int HEADER_TOP = 0xFFFFF4EA;
        static final int HEADER_MID = 0xFFFDF2F7;
        static final int SURFACE = 0xFFFFFFFF;
        static final int SURFACE_SUBTLE = 0xFFF7F5EF;
        static final int SURFACE_LOG = 0xFFFAF9F5;
        static final int BORDER = 0xFFE6E0D4;
        static final int INK = 0xFF1C1C1C;
        static final int INK_MUTED = 0xFF605D56;
        static final int INK_SUBTLE = 0xFF8C877E;
        static final int ACCENT_SOFT = 0xFFFFF1E5;
        static final int ACCENT_BORDER = 0xFFF5CFAF;
        static final int ACCENT_INK = 0xFFB84B08;
        static final int OK_SOFT = 0xFFE8F6ED;
        static final int OK_BORDER = 0xFFBBE4C8;
        static final int OK_INK = 0xFF15803D;

        enum BtnStyle { PRIMARY, SECONDARY, ACCENT, SUBTLE }

        static int dp(Activity a, int dp) {
            return Math.round(dp * a.getResources().getDisplayMetrics().density);
        }

        static GradientDrawable rounded(int fill, int stroke, int radiusDp, Activity a) {
            GradientDrawable d = new GradientDrawable();
            d.setColor(fill);
            d.setCornerRadius(dp(a, radiusDp));
            if (stroke != 0) d.setStroke(Math.max(1, Math.round(a.getResources().getDisplayMetrics().density)), stroke);
            return d;
        }

        static GradientDrawable gradientPill(int startColor, int endColor, int radiusDp, Activity a) {
            GradientDrawable d = new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT, new int[] { startColor, endColor });
            d.setCornerRadius(dp(a, radiusDp));
            return d;
        }

        static TextView pill(Activity a, String text, int fill, int stroke, int ink) {
            TextView tv = new TextView(a);
            tv.setText(text);
            tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f);
            tv.setTypeface(Typeface.DEFAULT_BOLD);
            tv.setPadding(dp(a, 9), dp(a, 3), dp(a, 9), dp(a, 3));
            tintPill(tv, fill, stroke, ink, a);
            tv.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            return tv;
        }

        static void tintPill(TextView tv, int fill, int stroke, int ink, Activity a) {
            tv.setBackground(rounded(fill, stroke, 99, a));
            tv.setTextColor(ink);
        }

        static LinearLayout card(Activity a) {
            LinearLayout card = new LinearLayout(a);
            card.setOrientation(LinearLayout.VERTICAL);
            card.setBackground(rounded(SURFACE, BORDER, 16, a));
            int p = dp(a, 15);
            card.setPadding(p, dp(a, 14), p, dp(a, 14));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.topMargin = dp(a, 10);
            card.setLayoutParams(lp);
            return card;
        }

        static TextView cardTitle(Activity a, String text) {
            TextView tv = new TextView(a);
            tv.setText(text);
            tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f);
            tv.setTypeface(Typeface.DEFAULT_BOLD);
            tv.setTextColor(INK);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.bottomMargin = dp(a, 4);
            tv.setLayoutParams(lp);
            return tv;
        }

        static TextView caption(Activity a, String text) {
            TextView tv = new TextView(a);
            tv.setText(text);
            tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
            tv.setLineSpacing(0f, 1.35f);
            tv.setTextColor(INK_MUTED);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.bottomMargin = dp(a, 8);
            tv.setLayoutParams(lp);
            return tv;
        }

        static TextView callout(Activity a, String text) {
            TextView tv = new TextView(a);
            tv.setText(text);
            tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
            tv.setLineSpacing(0f, 1.32f);
            tv.setTextColor(INK);
            tv.setBackground(rounded(SURFACE_SUBTLE, BORDER, 11, a));
            int p = dp(a, 11);
            tv.setPadding(p, dp(a, 9), p, dp(a, 9));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.bottomMargin = dp(a, 10);
            tv.setLayoutParams(lp);
            return tv;
        }

        static EditText input(Activity a, String hint, boolean pin) {
            EditText f = new EditText(a);
            f.setHint(hint);
            f.setHintTextColor(INK_SUBTLE);
            f.setTextColor(INK);
            f.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f);
            f.setSingleLine(true);
            f.setBackground(rounded(SURFACE_SUBTLE, BORDER, 11, a));
            int h = dp(a, 12), v = dp(a, 10);
            f.setPadding(h, v, h, v);
            f.setSaveEnabled(false);
            f.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);
            if (pin) f.setInputType(InputType.TYPE_CLASS_NUMBER | InputType.TYPE_NUMBER_VARIATION_PASSWORD);
            return f;
        }

        static Button button(Activity a, String text, BtnStyle style, View.OnClickListener listener) {
            Button b = new Button(a);
            b.setAllCaps(false);
            b.setText(text);
            b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f);
            b.setTypeface(Typeface.DEFAULT_BOLD);
            b.setStateListAnimator(null);
            int fill, stroke, ink;
            switch (style) {
                case PRIMARY:
                    fill = INK; stroke = INK; ink = Color.WHITE;
                    break;
                case ACCENT:
                    fill = ACCENT_SOFT; stroke = ACCENT_BORDER; ink = ACCENT_INK;
                    break;
                case SUBTLE:
                    fill = SURFACE_SUBTLE; stroke = BORDER; ink = INK_MUTED;
                    break;
                default:
                    fill = SURFACE; stroke = BORDER; ink = INK;
                    break;
            }
            b.setBackground(rounded(fill, stroke, 12, a));
            b.setTextColor(ink);
            b.setMinimumHeight(0);
            b.setMinHeight(dp(a, 42));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.bottomMargin = dp(a, 8);
            b.setLayoutParams(lp);
            b.setOnClickListener(listener);
            return b;
        }

        static String formatMb(long bytes) {
            return String.format(Locale.US, "%.1f MB", bytes / 1048576.0);
        }
    }
}
