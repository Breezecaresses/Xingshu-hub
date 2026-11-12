package app.xingshu.installer;
import android.os.ParcelFileDescriptor;
interface IInstaller {
    void destroy() = 16777114;
    String inspect() = 1;
    String apply(boolean restore, String conflictHash, in ParcelFileDescriptor candidate, in ParcelFileDescriptor vanilla) = 2;
}
