package app.xingshu.installer;

import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.Base64;

/** Short-lived shell entry point under UID 2000 for multi-channel install, package discovery and BnSdk scan. */
public final class LocalInstallerMain {
    static final String STAGING = "/data/local/tmp/xingshu-phone/" + PayloadSpec.PATCH;

    public static void main(String[] args) {
        String result;
        try {
            int uid = android.os.Process.myUid();
            if (uid != 2000 && uid != 0) throw new SecurityException("需要本机无线调试授权");
            if (args.length < 1 || args.length > 2) throw new IllegalArgumentException("操作不匹配");

            InstallerEngine engine = new InstallerEngine();
            String operation = args[0];
            String packageName = args.length >= 2 ? GamePackageCatalog.requireValidPackageName(args[1]) : GamePackageCatalog.DEFAULT_PACKAGE;
            String conflictHash = null;
            if (operation.startsWith("install-conflict:")) {
                conflictHash = operation.substring("install-conflict:".length());
                if (!conflictHash.matches("[a-f0-9]{64}")) throw new IllegalArgumentException("冲突校验值无效");
                operation = "install";
            }

            switch (operation) {
                case "scan":
                    result = engine.scanPackages();
                    break;
                case "channel-logcat":
                    result = engine.readChannelTokensJson();
                    break;
                case "launch":
                    result = engine.launchGame(packageName);
                    break;
                case "inspect":
                    result = engine.inspect(packageName);
                    break;
                case "install":
                case "restore":
                    boolean restore = operation.equals("restore");
                    try (InputStream p = restore ? null : new FileInputStream(new File(STAGING, "candidate.__data"));
                         InputStream v = new FileInputStream(new File(STAGING, "vanilla.__data"))) {
                        result = engine.apply(packageName, restore, conflictHash, p, v);
                    }
                    break;
                default:
                    throw new IllegalArgumentException("操作不匹配");
            }
        } catch (Throwable e) {
            result = "未完成：" + e.getClass().getSimpleName() + " " + e.getMessage();
        }
        System.out.println("XINGSHU_RESULT:" + Base64.getEncoder().encodeToString(result.getBytes(StandardCharsets.UTF_8)));
        System.out.flush();
        System.exit(0);
    }
}
