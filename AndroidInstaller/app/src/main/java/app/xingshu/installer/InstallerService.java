package app.xingshu.installer;

import android.content.Context;
import android.os.Binder;
import android.os.ParcelFileDescriptor;
import java.io.InputStream;

/** Optional Shizuku transport; shares the same fixed transaction engine. */
public final class InstallerService extends IInstaller.Stub {
    private final int ownerUid;
    private final InstallerEngine engine = new InstallerEngine();
    public InstallerService(Context context) { ownerUid=context.getApplicationInfo().uid; }
    private void caller() { if (Binder.getCallingUid()!=ownerUid) throw new SecurityException("调用方不匹配"); }
    @Override public void destroy() {
        int uid=Binder.getCallingUid();
        if(uid!=ownerUid && uid!=2000 && uid!=0) throw new SecurityException("调用方不匹配");
        System.exit(0);
    }
    @Override public synchronized String inspect() { caller(); return engine.inspect(); }
    @Override public synchronized String apply(boolean restore, ParcelFileDescriptor candidate, ParcelFileDescriptor vanilla) {
        caller();
        try (InputStream p=candidate==null?null:new ParcelFileDescriptor.AutoCloseInputStream(candidate);
             InputStream v=vanilla==null?null:new ParcelFileDescriptor.AutoCloseInputStream(vanilla)) {
            return engine.apply(restore,p,v);
        } catch(Exception e) { return "未完成："+e.getMessage(); }
    }
}
