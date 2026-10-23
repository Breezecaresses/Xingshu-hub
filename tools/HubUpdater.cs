// Portable Hub updater. The signed release can update this package, never game directories.
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class HubUpdater {
    internal const string Repo = "Breezecaresses/Xingshu-hub";
    internal const string Releases = "https://github.com/" + Repo + "/releases";
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 2000000 };
    internal static Dictionary<string,object> Object(string text) { return Json.Deserialize<Dictionary<string,object>>(text); }
    internal static string S(Dictionary<string,object> o,string k) { return Convert.ToString(o[k]); }
    internal static long N(Dictionary<string,object> o,string k) { return Convert.ToInt64(o[k]); }
    internal static string Hash(string path) { using(var s=File.OpenRead(path)) using(var h=SHA256.Create())return Hex(h.ComputeHash(s)); }
    internal static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
    static string Certificate() { using(var s=typeof(HubUpdater).Assembly.GetManifestResourceStream("update-cert"))using(var r=new StreamReader(s))return r.ReadToEnd().Trim(); }
    internal static Dictionary<string,object> Verify(string envelope,string tag) {
        if(envelope.Length>300000)throw new Exception("更新清单过大");
        var e=Object(envelope); byte[] data=Convert.FromBase64String(S(e,"payload")), signature=Convert.FromBase64String(S(e,"signature"));
        using(var cert=new X509Certificate2(Convert.FromBase64String(Certificate())))
        using(var rsa=cert.GetRSAPublicKey()) {
            if(!rsa.VerifyData(data,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new Exception("更新清单签名校验失败");
        }
        var m=Object(Encoding.UTF8.GetString(data));
        if(N(m,"schema")!=1 || S(m,"repository")!=Repo || S(m,"tag")!=tag || !Regex.IsMatch(tag,@"^v[0-9A-Za-z][0-9A-Za-z.\-]{0,79}$") ||
           S(m,"version")!=tag.Substring(1) || N(m,"sequence")<1 || N(m,"sequence")>2100000000 || S(m,"notes").Length>6000)
            throw new Exception("不支持此更新清单");
        foreach(string name in new[]{"desktop","android"}) {
            var a=(Dictionary<string,object>)m[name];
            string asset=S(a,"asset"), extension=name=="desktop"?".zip":".apk";
            if(!Regex.IsMatch(asset,@"^[A-Za-z0-9_.\-]{1,150}$") || !asset.EndsWith(extension,StringComparison.Ordinal) ||
               !Regex.IsMatch(S(a,"sha256"),"^[0-9a-f]{64}$") || N(a,"size")<1 || N(a,"size")>1024L*1024*1024)
                throw new Exception("更新文件声明无效");
        }
        if(N((Dictionary<string,object>)m["android"],"versionCode")!=N(m,"sequence"))throw new Exception("安卓版本号不一致");
        return m;
    }
    static byte[] Fetch(string url,long limit) {
        // GitHub redirects to its asset CDN. Each redirect is checked before following it.
        for(int redirects=0;redirects<6;redirects++) {
            var u=new Uri(url); string host=u.Host.ToLowerInvariant();
            if(u.Scheme!="https" || !u.IsDefaultPort || !(host=="api.github.com" || host=="github.com" || host=="release-assets.githubusercontent.com" || host=="objects.githubusercontent.com"))
                throw new Exception("更新地址不属于官方发布服务");
            var request=(HttpWebRequest)WebRequest.Create(u); request.AllowAutoRedirect=false;
            request.Timeout=30000; request.ReadWriteTimeout=60000; request.UserAgent="XingShu-Updater/1";
            request.Accept="application/vnd.github+json";
            using(var response=(HttpWebResponse)request.GetResponse()) {
                if((int)response.StatusCode>=300 && (int)response.StatusCode<400) { url=new Uri(u,response.Headers["Location"]).AbsoluteUri; continue; }
                if(response.ContentLength>limit)throw new Exception("更新响应超过大小限制");
                using(var source=response.GetResponseStream())using(var output=new MemoryStream()) {
                    byte[] b=new byte[65536]; int n; long size=0;
                    while((n=source.Read(b,0,b.Length))>0) { size+=n; if(size>limit)throw new Exception("更新响应超过大小限制"); output.Write(b,0,n); }
                    return output.ToArray();
                }
            }
        }
        throw new Exception("更新下载重定向次数过多");
    }
    internal static string AssetUrl(Dictionary<string,object> m,string component) {
        var a=(Dictionary<string,object>)m[component];
        return Releases+"/download/"+Uri.EscapeDataString(S(m,"tag"))+"/"+Uri.EscapeDataString(S(a,"asset"));
    }
    internal static Dictionary<string,object> Check(long current,bool preview) {
        var releases=Json.Deserialize<object[]>(Encoding.UTF8.GetString(Fetch("https://api.github.com/repos/"+Repo+"/releases?per_page=30",1800000)));
        Dictionary<string,object> best=null; string envelope=null;
        foreach(var raw in releases) {
            var release=(Dictionary<string,object>)raw;
            if(Convert.ToBoolean(release["draft"]) || (!preview && Convert.ToBoolean(release["prerelease"])))continue;
            string tag=S(release,"tag_name"); if(!Regex.IsMatch(tag,@"^v[0-9A-Za-z][0-9A-Za-z.\-]{0,79}$"))continue;
            if(!((object[])release["assets"]).Cast<Dictionary<string,object>>().Any(a => S(a,"name")=="xingshu-update.json"))continue;
            string text=Encoding.UTF8.GetString(Fetch(Releases+"/download/"+Uri.EscapeDataString(tag)+"/xingshu-update.json",300000));
            var m=Verify(text,tag); // Fail closed on a tampered published update; never silently fall back.
            if(best==null || N(m,"sequence")>N(best,"sequence")) { best=m; envelope=text; }
        }
        return new Dictionary<string,object> {
            {"available",best!=null && N(best,"sequence")>current}, {"currentSequence",current},
            {"release",best}, {"envelope",envelope}, {"releaseUrl",best==null?Releases:Releases+"/tag/"+Uri.EscapeDataString(S(best,"tag"))},
            {"message",best==null?"当前发布尚未提供签名更新清单，可打开官方发布页手动下载。":N(best,"sequence")>current?"发现可校验的新版本。":"没有比当前版本更新的可校验发布。"}
        };
    }
    static void Download(string url,string destination,long size,string hash) {
        // Signed size and hash are checked before the archive is used. Do not retain partial downloads.
        for(int redirects=0;redirects<6;redirects++) {
            var u=new Uri(url); string h=u.Host.ToLowerInvariant();
            if(u.Scheme!="https" || !u.IsDefaultPort || !(h=="github.com"||h=="release-assets.githubusercontent.com"||h=="objects.githubusercontent.com"))throw new Exception("下载地址无效");
            var req=(HttpWebRequest)WebRequest.Create(u); req.AllowAutoRedirect=false; req.Timeout=30000; req.ReadWriteTimeout=60000; req.UserAgent="XingShu-Updater/1";
            using(var response=(HttpWebResponse)req.GetResponse()) {
                if((int)response.StatusCode>=300&&(int)response.StatusCode<400) { url=new Uri(u,response.Headers["Location"]).AbsoluteUri; continue; }
                if(response.ContentLength>=0&&response.ContentLength!=size)throw new Exception("更新文件大小不符");
                try {
                    using(var source=response.GetResponseStream())using(var target=new FileStream(destination,FileMode.CreateNew,FileAccess.Write)) {
                        byte[] b=new byte[65536]; long total=0; int n;
                        while((n=source.Read(b,0,b.Length))>0) { total+=n; if(total>size)throw new Exception("更新文件超出声明大小"); target.Write(b,0,n); }
                        target.Flush(true); if(total!=size)throw new Exception("下载未完成，请重试");
                    }
                    if(Hash(destination)!=hash)throw new Exception("更新文件 SHA256 校验失败"); return;
                } catch { if(File.Exists(destination))File.Delete(destination); throw; }
            }
        }
        throw new Exception("更新下载重定向次数过多");
    }
    internal static string SafeRelative(string value) {
        if(value.IndexOf('\\')>=0 || value.StartsWith("/") || value.Length>220)throw new Exception("更新包路径无效");
        var parts=value.Split('/');
        foreach(string part in parts) if(part.Length==0 || part=="." || part==".." || part.EndsWith(".") || part.EndsWith(" ") ||
            part.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)",RegexOptions.IgnoreCase))throw new Exception("更新包包含不安全路径");
        return String.Join(Path.DirectorySeparatorChar.ToString(),parts);
    }
    static void NoLinks(string path) {
        for(var p=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)));p!=null;p=p.Parent)
            if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new Exception("更新目录包含链接，请移到普通文件夹后更新");
        if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new Exception("更新文件不能是链接");
    }
    internal static Dictionary<string,string> Extract(string archive,string stage,Dictionary<string,object> manifest) {
        var asset=(Dictionary<string,object>)manifest["desktop"];
        if(new FileInfo(archive).Length!=N(asset,"size") || Hash(archive)!=S(asset,"sha256"))throw new Exception("更新包校验失败");
        var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); string top=null; long total=0;
        using(var zip=ZipFile.OpenRead(archive)) {
            if(zip.Entries.Count>10000)throw new Exception("更新包文件过多");
            foreach(var e in zip.Entries) {
                string name=e.FullName; if(name.EndsWith("/"))continue;
                if(((e.ExternalAttributes>>16)&0xf000)==0xa000 || (e.ExternalAttributes&0x400)!=0)throw new Exception("更新包包含文件链接");
                SafeRelative(name); int slash=name.IndexOf('/'); if(slash<1)throw new Exception("更新包缺少顶层目录");
                if(top==null)top=name.Substring(0,slash);
                if(!top.StartsWith("XingShu_",StringComparison.Ordinal) || name.Substring(0,slash)!=top)throw new Exception("更新包目录结构不符");
                string relative=name.Substring(slash+1), safe=SafeRelative(relative);
                bool allowed=relative=="Hub/xingshu-hub.exe" || relative.StartsWith("Android/",StringComparison.Ordinal) || relative.StartsWith("Runtime/",StringComparison.Ordinal) ||
                    relative=="星枢安装助手.exe" || (!relative.Contains("/")&&(relative.EndsWith(".txt")||relative.EndsWith(".md")||relative=="release.json"));
                if(!allowed || files.ContainsKey(relative))throw new Exception("更新包含重复文件或不支持的文件："+relative);
                total+=e.Length; if(total>1500L*1024*1024)throw new Exception("更新包解压大小超限");
                string target=Path.Combine(stage,safe); NoLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target));
                using(var source=e.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write))source.CopyTo(output);
                if(new FileInfo(target).Length!=e.Length)throw new Exception("更新包文件不完整"); files.Add(relative,Hash(target));
            }
        }
        if(!files.ContainsKey("Hub/xingshu-hub.exe") || !files.ContainsKey("release.json"))throw new Exception("不是完整的电脑版更新包");
        var release=Object(File.ReadAllText(Path.Combine(stage,"release.json")));
        if(S(release,"Version")!=S(manifest,"version") || S(release,"HubSha256")!=files["Hub/xingshu-hub.exe"])throw new Exception("Hub 版本或指纹与更新包不符");
        if(release.ContainsKey("LocalWorktreeCandidate")&&Convert.ToBoolean(release["LocalWorktreeCandidate"]))throw new Exception("本地候选不能作为在线更新包");
        return files;
    }
    internal static void Apply(string root,string stage,Dictionary<string,string> files,string backup,int failAt) {
        root=Path.GetFullPath(root); Directory.CreateDirectory(backup);
        var old=new Dictionary<string,string>(); var written=new List<string>();
        // Complete all preflight checks and backups before modifying the first package file.
        foreach(var file in files) {
            string rel=SafeRelative(file.Key), target=Path.Combine(root,rel), source=Path.Combine(stage,rel); NoLinks(target); NoLinks(source);
            if(Hash(source)!=file.Value)throw new Exception("暂存更新文件已变化");
            old[file.Key]=File.Exists(target)?Hash(target):null;
            if(File.Exists(target)) { string b=Path.Combine(backup,rel); Directory.CreateDirectory(Path.GetDirectoryName(b)); File.Copy(target,b,false); if(Hash(b)!=old[file.Key])throw new Exception("更新前备份校验失败"); }
        }
        File.WriteAllText(Path.Combine(backup,"before.json"),Json.Serialize(old),new UTF8Encoding(false));
        try {
            foreach(var file in files) {
                string rel=SafeRelative(file.Key), target=Path.Combine(root,rel);
                NoLinks(target); string actual=File.Exists(target)?Hash(target):null;
                if(actual!=old[file.Key])throw new Exception("更新期间文件被其他程序修改，已停止");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string temp=target+".update-"+Guid.NewGuid().ToString("N");
                try { File.Copy(Path.Combine(stage,rel),temp,false); if(Hash(temp)!=file.Value)throw new Exception("复制更新文件校验失败");
                    if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
                } finally { if(File.Exists(temp))File.Delete(temp); }
                written.Add(file.Key); if(Hash(target)!=file.Value)throw new Exception("写入后校验失败");
                if(failAt>0&&written.Count==failAt)throw new IOException("Injected update failure");
            }
            File.WriteAllText(Path.Combine(backup,"completed.txt"),DateTime.UtcNow.ToString("o"));
        } catch(Exception failure) {
            var errors=new List<string>();
            foreach(string name in written.AsEnumerable().Reverse())try {
                string target=Path.Combine(root,SafeRelative(name)); NoLinks(target);
                if(Hash(target)!=files[name])throw new Exception("文件已被其他程序改写");
                if(old[name]==null)File.Delete(target); else { string b=Path.Combine(backup,SafeRelative(name)); if(Hash(b)!=old[name])throw new Exception("备份已变化");
                    string t=target+".rollback-"+Guid.NewGuid().ToString("N"); File.Copy(b,t); File.Replace(t,target,null); if(Hash(target)!=old[name])throw new Exception("还原校验失败"); }
            } catch(Exception e) { errors.Add(name+": "+e.Message); }
            if(errors.Count>0)throw new Exception("更新中断，部分文件需用完整 ZIP 恢复。备份："+backup+"\n"+String.Join("\n",errors));
            throw new Exception("更新未完成，已恢复更新前文件："+failure.Message);
        }
    }
    static string Cache() { string p=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"XingShu","Updates",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
    static void CheckPackageProcesses(string root, int parentPid) {
        foreach(var p in Process.GetProcesses())try {
            if(p.Id!=Process.GetCurrentProcess().Id && p.Id!=parentPid &&
                p.MainModule.FileName.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new IOException("本包内程序仍在运行："+p.ProcessName+"（PID "+p.Id+"）。请等待安装任务完成并关闭配套程序，再重试更新。");
        } catch(System.ComponentModel.Win32Exception) {} catch(InvalidOperationException) {}
    }
    static void Print(object value) { Console.WriteLine(Json.Serialize(value)); Console.Out.Flush(); }
    [STAThread] static int Main(string[] args) {
        // A GUI parent has no console code page. Always use the same UTF-8 IPC as Rust.
        Console.InputEncoding=new UTF8Encoding(false);
        Console.OutputEncoding=new UTF8Encoding(false);
        bool applying=args.Length>0&&args[0]=="--apply", sentReady=false, parentExited=false; string original=null, cachePath=null;
        try {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var request=Object(Console.In.ReadLine());
            if(applying) {
                string cache=S(request,"cache"), root=S(request,"root"); cachePath=cache;
                var plan=Object(File.ReadAllText(Path.Combine(cache,"plan.json"))); var m=Verify(S(plan,"envelope"),S(plan,"tag"));
                if(N(m,"sequence")<=N(request,"current"))throw new Exception("拒绝安装旧版本");
                string stage=Path.Combine(cache,"apply-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
                var files=Extract(Path.Combine(cache,"update.zip"),stage,m);
                original=Path.Combine(Path.GetFullPath(root),"Hub","xingshu-hub.exe"); NoLinks(original);
                int pid=Convert.ToInt32(request["pid"]); var parent=Process.GetProcessById(pid);
                if(!String.Equals(Path.GetFullPath(parent.MainModule.FileName),original,StringComparison.OrdinalIgnoreCase))throw new Exception("更新目标与当前 Hub 不一致");
                // A process-held lock prevents two workers writing the same package.
                string lockfile=Path.Combine(root,".xingshu-update.lock"); NoLinks(lockfile);
                using(var gate=new FileStream(lockfile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                    // Reject occupied files while the original Hub is still open.
                    CheckPackageProcesses(root,pid);
                    Print(new { ready=true });
                    sentReady=true;
                    if(!parent.WaitForExit(45000))throw new Exception("Hub 未退出，取消本次更新");
                    parentExited=true;
                    // Do not overwrite another running Hub or maintenance worker from this package.
                    CheckPackageProcesses(root,pid);
                    Apply(root,stage,files,Path.Combine(root,".xingshu-updates",DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N")),0);
                }
                Process.Start(new ProcessStartInfo(original) { WorkingDirectory=Path.GetDirectoryName(original),UseShellExecute=false }); return 0;
            }
            long current=N(request,"current"); bool preview=Convert.ToBoolean(request["preview"]); var result=Check(current,preview);
            if(args.Length>0&&args[0]=="--download") {
                if(!Convert.ToBoolean(result["available"]))throw new Exception("未发现可用更新，请重新检查");
                var m=(Dictionary<string,object>)result["release"];
                if(N(m,"sequence")!=N(request,"sequence"))throw new Exception("发布版本已变化，请重新检查后下载");
                var asset=(Dictionary<string,object>)m["desktop"]; string cache=Cache();
                Download(AssetUrl(m,"desktop"),Path.Combine(cache,"update.zip"),N(asset,"size"),S(asset,"sha256"));
                string stage=Path.Combine(cache,"checked"); Directory.CreateDirectory(stage); Extract(Path.Combine(cache,"update.zip"),stage,m);
                File.WriteAllText(Path.Combine(cache,"plan.json"),Json.Serialize(new { tag=S(m,"tag"),envelope=S(result,"envelope") }),new UTF8Encoding(false));
                result["cache"]=cache;
            }
            result.Remove("envelope"); Print(result); return 0;
        } catch(Exception e) {
            if(!sentReady)Print(new { error=e.Message });
            if(applying&&sentReady) { if(cachePath!=null)try { File.WriteAllText(Path.Combine(cachePath,"update-error.txt"),e.ToString()); } catch {}
                MessageBox.Show(e.Message+"\n更新不会改动游戏目录。", "星枢更新未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                if(parentExited&&original!=null&&File.Exists(original))try { Process.Start(new ProcessStartInfo(original){UseShellExecute=false}); } catch {} }
            return 1;
        }
    }
}
