package app.xingshu.installer;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.Base64;

/** Short-lived shell entry point: fixed payload and three fixed operations only. */
public final class LocalInstallerMain {
    static final String STAGING = "/data/local/tmp/xingshu-phone/"+PayloadSpec.PATCH;
    public static void main(String[] args) {
        String result;
        try {
            int uid=android.os.Process.myUid();
            if(uid!=2000 && uid!=0) throw new SecurityException("需要本机无线调试授权");
            if(args.length!=1) throw new IllegalArgumentException("操作不匹配");
            InstallerEngine engine=new InstallerEngine();
            switch(args[0]) {
                case "inspect": result=engine.inspect(); break;
                case "install":
                case "restore":
                    boolean restore=args[0].equals("restore");
                    try(InputStream p=restore?null:new FileInputStream(new File(STAGING,"candidate.__data"));
                        InputStream v=new FileInputStream(new File(STAGING,"vanilla.__data"))) {
                        result=engine.apply(restore,p,v);
                    }
                    break;
                default: throw new IllegalArgumentException("操作不匹配");
            }
        } catch(Throwable e) { result="未完成："+e.getClass().getSimpleName()+" "+e.getMessage(); }
        System.out.println("XINGSHU_RESULT:"+Base64.getEncoder().encodeToString(result.getBytes(StandardCharsets.UTF_8)));
        System.out.flush();
        System.exit(0);
    }
}
