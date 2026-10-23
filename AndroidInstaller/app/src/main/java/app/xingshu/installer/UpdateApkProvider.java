package app.xingshu.installer;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.*;

/** Only the verified updater APK is shared with the system package installer. */
public final class UpdateApkProvider extends ContentProvider {
    public boolean onCreate() { return true; }
    private File apk(Uri uri) throws FileNotFoundException {
        if(!BuildConfig.APPLICATION_ID.concat(".update").equals(uri.getAuthority()) || !"/update.apk".equals(uri.getPath()))throw new FileNotFoundException();
        return new File(getContext().getCacheDir(),"update.apk");
    }
    public String getType(Uri uri) { return "application/vnd.android.package-archive"; }
    public ParcelFileDescriptor openFile(Uri uri,String mode) throws FileNotFoundException {
        if(!"r".equals(mode))throw new FileNotFoundException(); return ParcelFileDescriptor.open(apk(uri),ParcelFileDescriptor.MODE_READ_ONLY);
    }
    public Cursor query(Uri uri,String[] columns,String selection,String[] args,String order) {
        try { File f=apk(uri); if(columns==null)columns=new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE};
            MatrixCursor c=new MatrixCursor(columns,1); Object[] row=new Object[columns.length];
            for(int i=0;i<columns.length;i++)row[i]=OpenableColumns.DISPLAY_NAME.equals(columns[i])?"XingShu-Installer.apk":OpenableColumns.SIZE.equals(columns[i])?f.length():null;
            c.addRow(row); return c;
        } catch(FileNotFoundException e) { return null; }
    }
    public Uri insert(Uri u,ContentValues v) { throw new UnsupportedOperationException(); }
    public int update(Uri u,ContentValues v,String s,String[] a) { throw new UnsupportedOperationException(); }
    public int delete(Uri u,String s,String[] a) { throw new UnsupportedOperationException(); }
}
