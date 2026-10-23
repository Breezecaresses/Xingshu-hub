package app.xingshu.installer;

import org.json.*;
import java.net.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.cert.CertificateFactory;
import java.util.Base64;
import javax.net.ssl.HttpsURLConnection;

/** Signed metadata is shared with the desktop updater; URLs are constructed from a fixed repository. */
final class ReleaseUpdates {
    static final String REPO="Breezecaresses/Xingshu-hub", RELEASES="https://github.com/"+REPO+"/releases";
    static final class Update {
        final JSONObject manifest;
        Update(JSONObject m) { manifest=m; }
        long code() throws Exception { return manifest.getLong("sequence"); }
        String version() throws Exception { return manifest.getString("version"); }
        String notes() throws Exception { return manifest.getString("notes"); }
    }
    static JSONObject verify(String envelope,String tag,InputStream certificate) throws Exception {
        if(envelope.length()>300000)throw new IOException("更新清单过大");
        JSONObject e=new JSONObject(envelope);
        byte[] payload=Base64.getDecoder().decode(e.getString("payload"));
        Signature signature=Signature.getInstance("SHA256withRSA");
        String encoded=new String(read(certificate,5000),StandardCharsets.UTF_8).trim();
        signature.initVerify(CertificateFactory.getInstance("X.509").generateCertificate(new ByteArrayInputStream(Base64.getDecoder().decode(encoded))).getPublicKey());
        signature.update(payload);
        if(!signature.verify(Base64.getDecoder().decode(e.getString("signature"))))throw new IOException("更新清单签名校验失败");
        JSONObject m=new JSONObject(new String(payload,StandardCharsets.UTF_8));
        if(m.getInt("schema")!=1 || !REPO.equals(m.getString("repository")) || !tag.matches("v[0-9A-Za-z][0-9A-Za-z.\\-]{0,79}") ||
           !tag.equals(m.getString("tag")) || !tag.substring(1).equals(m.getString("version")) || m.getLong("sequence")<1 || m.getLong("sequence")>2100000000 || m.getString("notes").length()>6000)
            throw new IOException("不支持此更新清单");
        for(String key:new String[]{"desktop","android"}) {
            JSONObject a=m.getJSONObject(key); String asset=a.getString("asset");
            if(!asset.matches("[A-Za-z0-9_.\\-]{1,150}") || !asset.endsWith(key.equals("desktop")?".zip":".apk") ||
               !a.getString("sha256").matches("[0-9a-f]{64}") || a.getLong("size")<1 || a.getLong("size")>1024L*1024*1024)
                throw new IOException("更新文件声明无效");
        }
        if(m.getJSONObject("android").getLong("versionCode")!=m.getLong("sequence"))throw new IOException("安卓版本号不一致");
        return m;
    }
    private static HttpsURLConnection connect(String address) throws Exception {
        for(int i=0;i<6;i++) {
            URL url=new URL(address); String host=url.getHost();
            if(!url.getProtocol().equals("https") || (url.getPort()!=-1&&url.getPort()!=443) ||
               !(host.equals("github.com")||host.equals("api.github.com")||host.equals("release-assets.githubusercontent.com")||host.equals("objects.githubusercontent.com")))throw new IOException("更新地址不属于官方发布服务");
            HttpsURLConnection c=(HttpsURLConnection)url.openConnection(); c.setInstanceFollowRedirects(false);
            c.setConnectTimeout(30000); c.setReadTimeout(60000); c.setRequestProperty("User-Agent","XingShu-Android-Updater/1");
            int status=c.getResponseCode();
            if(status>=300&&status<400) { address=new URL(url,c.getHeaderField("Location")).toString(); c.disconnect(); continue; }
            if(status!=200) { c.disconnect(); throw new IOException(status==403?"更新服务访问受限，请稍后重试":"更新服务返回 "+status); }
            return c;
        }
        throw new IOException("更新下载重定向次数过多");
    }
    static byte[] read(InputStream in,long max) throws IOException {
        ByteArrayOutputStream out=new ByteArrayOutputStream(); byte[] b=new byte[16384]; int n; long total=0;
        while((n=in.read(b))!=-1) { total+=n; if(total>max)throw new IOException("更新响应超过大小限制"); out.write(b,0,n); } return out.toByteArray();
    }
    private static String fetch(String url,int max) throws Exception {
        HttpsURLConnection c=connect(url);
        try(InputStream in=c.getInputStream()) { return new String(read(in,max),StandardCharsets.UTF_8); }finally { c.disconnect(); }
    }
    interface Certificate { InputStream open() throws Exception; }
    static Update check(boolean preview,Certificate certificate) throws Exception {
        JSONArray releases=new JSONArray(fetch("https://api.github.com/repos/"+REPO+"/releases?per_page=30",1800000));
        Update best=null;
        for(int i=0;i<releases.length();i++) {
            JSONObject r=releases.getJSONObject(i);
            if(r.getBoolean("draft")||(!preview&&r.getBoolean("prerelease")))continue;
            String tag=r.getString("tag_name"); if(!tag.matches("v[0-9A-Za-z][0-9A-Za-z.\\-]{0,79}"))continue;
            JSONArray assets=r.getJSONArray("assets"); boolean found=false;
            for(int j=0;j<assets.length();j++)if(assets.getJSONObject(j).getString("name").equals("xingshu-update.json"))found=true;
            if(!found)continue;
            String envelope=fetch(RELEASES+"/download/"+tag+"/xingshu-update.json",300000);
            try(InputStream cert=certificate.open()) { Update update=new Update(verify(envelope,tag,cert)); if(best==null||update.code()>best.code())best=update; }
        }
        return best;
    }
    static File download(Update update,File directory) throws Exception {
        JSONObject asset=update.manifest.getJSONObject("android");
        String address=RELEASES+"/download/"+update.manifest.getString("tag")+"/"+asset.getString("asset");
        File partial=new File(directory,"update.download"), target=new File(directory,"update.apk");
        HttpsURLConnection c=connect(address);
        try {
            long size=asset.getLong("size"), total=0; MessageDigest digest=MessageDigest.getInstance("SHA-256");
            if(c.getContentLengthLong()>=0&&c.getContentLengthLong()!=size)throw new IOException("更新文件大小不符");
            try(InputStream in=c.getInputStream();FileOutputStream out=new FileOutputStream(partial)) {
                byte[] b=new byte[65536]; int n;
                while((n=in.read(b))!=-1) { total+=n; if(total>size)throw new IOException("更新文件超出声明大小"); digest.update(b,0,n); out.write(b,0,n); }
                out.getFD().sync();
            }
            if(total!=size||!hex(digest.digest()).equals(asset.getString("sha256")))throw new IOException("更新 APK 校验失败");
            if(!partial.renameTo(target))throw new IOException("无法保存新版 APK，请重试");
            return target;
        } finally { c.disconnect(); partial.delete(); }
    }
    static String hex(byte[] b) { StringBuilder s=new StringBuilder(); for(byte v:b)s.append(String.format(java.util.Locale.ROOT,"%02x",v&255)); return s.toString(); }
}
