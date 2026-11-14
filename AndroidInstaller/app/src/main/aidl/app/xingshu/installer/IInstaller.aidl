package app.xingshu.installer;

import android.os.ParcelFileDescriptor;

interface IInstaller {
    void destroy() = 16777114;
    String scanPackages() = 1;
    String inspect(String packageName) = 2;
    String apply(String packageName, boolean restore, String conflictHash, in ParcelFileDescriptor candidate, in ParcelFileDescriptor vanilla) = 3;
    String readChannelLogcat() = 4;
    String launchGame(String packageName) = 5;
}
