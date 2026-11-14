package app.xingshu.installer;

import android.content.Context;
import android.os.Binder;
import android.os.ParcelFileDescriptor;
import java.io.InputStream;

/** Optional Shizuku transport; shares the same multi-channel transaction engine. */
public final class InstallerService extends IInstaller.Stub {
    private final int ownerUid;
    private final InstallerEngine engine = new InstallerEngine();

    public InstallerService(Context context) {
        ownerUid = context.getApplicationInfo().uid;
    }

    private void caller() {
        if (Binder.getCallingUid() != ownerUid) throw new SecurityException("调用方不匹配");
    }

    @Override
    public void destroy() {
        int uid = Binder.getCallingUid();
        if (uid != ownerUid && uid != 2000 && uid != 0) throw new SecurityException("调用方不匹配");
        System.exit(0);
    }

    @Override
    public synchronized String scanPackages() {
        caller();
        return engine.scanPackages();
    }

    @Override
    public synchronized String inspect(String packageName) {
        caller();
        return engine.inspect(packageName);
    }

    @Override
    public synchronized String apply(String packageName, boolean restore, String conflictHash,
            ParcelFileDescriptor candidate, ParcelFileDescriptor vanilla) {
        caller();
        try (InputStream p = candidate == null ? null : new ParcelFileDescriptor.AutoCloseInputStream(candidate);
             InputStream v = vanilla == null ? null : new ParcelFileDescriptor.AutoCloseInputStream(vanilla)) {
            return engine.apply(packageName, restore, conflictHash, p, v);
        } catch (Exception e) {
            return "未完成：" + e.getMessage();
        }
    }

    @Override
    public synchronized String readChannelLogcat() {
        caller();
        return engine.readChannelTokensJson();
    }

    @Override
    public synchronized String launchGame(String packageName) {
        caller();
        return engine.launchGame(packageName);
    }
}
