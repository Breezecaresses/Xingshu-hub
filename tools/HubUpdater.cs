// Portable Hub updater. The signed release can update this package, never game directories.
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;

internal static class HubUpdater {
    internal const string Repo = "Breezecaresses/Xingshu-hub";
    internal const string Releases = "https://github.com/" + Repo + "/releases";
    internal static readonly string[] DomesticMirrors = new[] {
        "https://edgeone.gh-proxy.org/",
        "https://gh-proxy.org/",
        "https://ghfast.top/",
    };
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 2000000 };
    // Each parallel response gets its own serializer; instance members are not thread safe.
    internal static Dictionary<string,object> Object(string text) { return new JavaScriptSerializer { MaxJsonLength=2000000 }.Deserialize<Dictionary<string,object>>(text); }
    internal static string S(Dictionary<string,object> o,string k) { return Convert.ToString(o[k]); }
    internal static long N(Dictionary<string,object> o,string k) { return Convert.ToInt64(o[k]); }
    internal static string Hash(string path) { using(var s=File.OpenRead(path)) using(var h=SHA256.Create())return Hex(h.ComputeHash(s)); }
    internal static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
    static string Certificate() { using(var s=typeof(HubUpdater).Assembly.GetManifestResourceStream("update-cert"))using(var r=new StreamReader(s))return r.ReadToEnd().Trim(); }

    internal sealed class TransportException : Exception {
        internal readonly int StatusCode;
        internal TransportException(string message, int statusCode, Exception inner) : base(message, inner) { StatusCode = statusCode; }
    }

    internal static bool IsAllowedUpdateHost(string host) {
        if(String.IsNullOrEmpty(host))return false;
        string h=host.ToLowerInvariant();
        return h=="api.github.com" || h=="github.com" || h=="release-assets.githubusercontent.com" || h=="objects.githubusercontent.com"
            || h=="edgeone.gh-proxy.org" || h=="gh-proxy.org" || h=="ghfast.top";
    }

    internal static void ValidateEndpoint(Uri u) {
        if(u.Scheme!="https" || !u.IsDefaultPort || !String.IsNullOrEmpty(u.UserInfo) || !IsAllowedUpdateHost(u.Host))
            throw new Exception("更新地址不属于受信任的官方或国内加速服务");
    }

    internal static List<string> CandidateUrls(string directUrl,long assetId,bool preferDomestic) {
        var list=new List<string>();
        bool isGitHubRepo=directUrl!=null && directUrl.StartsWith("https://github.com/"+Repo+"/",StringComparison.Ordinal);
        if(preferDomestic && isGitHubRepo) {
            foreach(string mirror in DomesticMirrors)list.Add(mirror+directUrl);
        }
        if(assetId>0)list.Add("https://api.github.com/repos/"+Repo+"/releases/assets/"+assetId);
        if(!String.IsNullOrEmpty(directUrl))list.Add(directUrl);
        if(!preferDomestic && isGitHubRepo) {
            foreach(string mirror in DomesticMirrors)list.Add(mirror+directUrl);
        }
        return list.Distinct(StringComparer.Ordinal).ToList();
    }

    internal static List<string> ParseAtomTags(string xml,bool preview,int maxCount) {
        var tags=new List<string>();
        if(String.IsNullOrEmpty(xml))return tags;
        foreach(Match match in Regex.Matches(xml,@"/releases/tag/(v[0-9A-Za-z][0-9A-Za-z.\-]{0,79})")) {
            string tag=match.Groups[1].Value;
            if(!preview && tag.Contains("-"))continue;
            if(!tags.Contains(tag,StringComparer.Ordinal)) {
                tags.Add(tag);
                if(tags.Count>=maxCount)break;
            }
        }
        return tags;
    }

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
            if(name=="android" && !m.ContainsKey(name))continue;
            var a=(Dictionary<string,object>)m[name];
            string asset=S(a,"asset"), extension=name=="desktop"?".zip":".apk";
            if(!Regex.IsMatch(asset,@"^[A-Za-z0-9_.\-]{1,150}$") || !asset.EndsWith(extension,StringComparison.Ordinal) ||
               !Regex.IsMatch(S(a,"sha256"),"^[0-9a-f]{64}$") || N(a,"size")<1 || N(a,"size")>1024L*1024*1024)
                throw new Exception("更新文件声明无效");
        }
        if(m.ContainsKey("android") && N((Dictionary<string,object>)m["android"],"versionCode")!=N(m,"sequence"))throw new Exception("安卓版本号不一致");
        return m;
    }
    internal static string ManifestName(IEnumerable<string> assets) {
        if(assets.Contains("xingshu-desktop-update.json"))return "xingshu-desktop-update.json";
        return assets.Contains("xingshu-update.json")?"xingshu-update.json":null;
    }
    static TransportException ToTransportException(WebException ex) {
        var resp=ex.Response as HttpWebResponse;
        int code=resp!=null?(int)resp.StatusCode:0;
        string msg=code==403?"更新服务访问受限":code==404?"发布文件不存在":code>0?("更新服务返回 HTTP "+code):ex.Message;
        return new TransportException(msg,code,ex);
    }
    internal const int CheckTimeoutMs=25000;
    const int SourceTimeoutMs=8000;
    static HttpWebRequest UpdateRequest(Uri u,int timeoutMs,int readTimeoutMs) {
        ValidateEndpoint(u);
        var request=(HttpWebRequest)WebRequest.Create(u);
        request.AllowAutoRedirect=false;
        request.Timeout=timeoutMs; request.ReadWriteTimeout=readTimeoutMs; request.UserAgent="XingShu-Updater/2";
        // Proxies share a ServicePoint across hosts. Stalled official tunnels must leave room for mirrors.
        request.ServicePoint.ConnectionLimit=Math.Max(32,request.ServicePoint.ConnectionLimit);
        if(u.Host=="api.github.com" || u.AbsolutePath.StartsWith("/https://api.github.com/",StringComparison.Ordinal)) {
            request.Accept=u.AbsolutePath.Contains("/releases/assets/")?"application/octet-stream":"application/vnd.github+json";
            request.Headers["X-GitHub-Api-Version"]="2022-11-28";
        } else request.Accept="*/*";
        return request;
    }
    internal delegate Task<byte[]> MetadataFetcher(string url,long limit,CancellationToken cancellation);
    static async Task<byte[]> FetchOnceAsync(string url,long limit,CancellationToken cancellation) {
        for(int redirects=0;redirects<6;redirects++) {
            var u=new Uri(url);
            var request=UpdateRequest(u,SourceTimeoutMs,SourceTimeoutMs);
            try {
                cancellation.ThrowIfCancellationRequested();
                using(cancellation.Register(request.Abort))
                using(var response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false)) {
                    if((int)response.StatusCode>=300 && (int)response.StatusCode<400) { url=new Uri(u,response.Headers["Location"]).AbsoluteUri; continue; }
                    if(response.ContentLength>limit)throw new Exception("更新响应超过大小限制");
                    using(var source=response.GetResponseStream())using(var output=new MemoryStream()) {
                        byte[] b=new byte[65536]; int n; long size=0;
                        while((n=await source.ReadAsync(b,0,b.Length,cancellation).ConfigureAwait(false))>0) { size+=n; if(size>limit)throw new Exception("更新响应超过大小限制"); output.Write(b,0,n); }
                        return output.ToArray();
                    }
                }
            } catch(WebException ex) { cancellation.ThrowIfCancellationRequested(); throw ToTransportException(ex); }
              catch(IOException ex) { throw new TransportException(ex.Message,0,ex); }
              catch(SocketException ex) { throw new TransportException(ex.Message,0,ex); }
        }
        throw new TransportException("更新下载重定向次数过多",0,null);
    }
    sealed class MetadataResponse<T> {
        internal T Value;
        internal string Host,Text;
        internal Exception Error;
    }
    static async Task<MetadataResponse<T>> TryMetadataAsync<T>(string url,long limit,Func<string,T> parse,MetadataFetcher fetch,CancellationToken cancellation) {
        try {
            byte[] data=await fetch(url,limit,cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            string text=Encoding.UTF8.GetString(data);
            return new MetadataResponse<T> { Value=parse(text),Host=new Uri(url).Host,Text=text };
        } catch(Exception ex) { return new MetadataResponse<T> { Error=ex }; }
    }
    static Task<MetadataResponse<T>> FetchFastestAsync<T>(IList<string> candidates,long limit,Func<string,T> parse,MetadataFetcher fetch,CancellationToken budget) {
        return RaceSourcesAsync(candidates,(url,cancellation) => TryMetadataAsync(url,limit,parse,fetch,cancellation),budget);
    }
    static async Task<MetadataResponse<T>> RaceSourcesAsync<T>(IList<string> candidates,Func<string,CancellationToken,Task<MetadataResponse<T>>> attempt,CancellationToken budget) {
        using(var race=CancellationTokenSource.CreateLinkedTokenSource(budget)) {
            race.CancelAfter(SourceTimeoutMs);
            var attempts=candidates.Select(url => attempt(url,race.Token)).ToList();
            Task expired=Task.Delay(Timeout.Infinite,race.Token);
            TransportException last=null;
            bool missing=false;
            try {
                while(attempts.Count>0) {
                    Task completed=await Task.WhenAny(attempts.Cast<Task>().Concat(new[]{expired})).ConfigureAwait(false);
                    if(completed==expired)throw new TransportException(missing?"发布文件不存在":"更新通道响应超时，请检查网络后重试。",missing?404:0,null);
                    var task=(Task<MetadataResponse<T>>)completed;
                    attempts.Remove(task);
                    var response=await task.ConfigureAwait(false);
                    if(response.Error==null)return response;
                    var transport=response.Error as TransportException;
                    if(transport!=null) { last=transport; missing|=transport.StatusCode==404; }
                    else if(response.Error is OperationCanceledException)last=new TransportException("更新通道响应超时，请检查网络后重试。",0,response.Error);
                    else throw response.Error; // Signature/schema failures must never become an older "no update" result.
                }
                // Atom cannot list assets. A missing desktop-only manifest must still try the legacy name,
                // even when another unreachable route timed out after the authoritative 404.
                if(missing)throw new TransportException("发布文件不存在",404,last);
                throw last??new TransportException("无法连接到更新服务",0,null);
            } finally { race.Cancel(); } // Abort losing HTTP requests, including redirects and stalled reads.
        }
    }
    internal static string AssetUrl(Dictionary<string,object> m,string component) {
        var a=(Dictionary<string,object>)m[component];
        return Releases+"/download/"+Uri.EscapeDataString(S(m,"tag"))+"/"+Uri.EscapeDataString(S(a,"asset"));
    }
    static long FindAssetId(object[] assets,string name) {
        if(assets==null || String.IsNullOrEmpty(name))return 0;
        foreach(Dictionary<string,object> a in assets) {
            if(String.Equals(S(a,"name"),name,StringComparison.Ordinal))return Math.Max(0,N(a,"id"));
        }
        return 0;
    }
    static bool IsDomesticHost(string host) {
        if(String.IsNullOrEmpty(host))return false;
        string h=host.ToLowerInvariant();
        return h=="edgeone.gh-proxy.org" || h=="gh-proxy.org" || h=="ghfast.top";
    }
    sealed class ReleaseCandidate {
        internal string Tag,Manifest;
        internal object[] Assets;
    }
    static List<ReleaseCandidate> ParseReleases(string text,bool preview) {
        try {
            var releases=new JavaScriptSerializer { MaxJsonLength=2000000 }.Deserialize<object[]>(text);
            if(releases==null)throw new Exception("发布列表为空");
            var candidates=new List<ReleaseCandidate>();
            foreach(Dictionary<string,object> release in releases) {
                if(Convert.ToBoolean(release["draft"]) || (!preview && Convert.ToBoolean(release["prerelease"])))continue;
                string tag=S(release,"tag_name");
                if(!Regex.IsMatch(tag,@"^v[0-9A-Za-z][0-9A-Za-z.\-]{0,79}$"))continue;
                var assets=(object[])release["assets"];
                string name=ManifestName(assets.Cast<Dictionary<string,object>>().Select(a => S(a,"name")));
                if(name!=null && !candidates.Any(c => c.Tag==tag))candidates.Add(new ReleaseCandidate { Tag=tag,Manifest=name,Assets=assets });
            }
            return candidates;
        } catch(Exception ex) { throw new TransportException("更新发布列表格式无效",0,ex); }
    }
    static List<ReleaseCandidate> ParseFeed(string text,bool preview) {
        try {
            var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null };
            var feed=new XmlDocument { XmlResolver=null };
            using(var reader=XmlReader.Create(new StringReader(text),settings))feed.Load(reader);
            if(feed.DocumentElement==null || feed.DocumentElement.LocalName!="feed" || feed.DocumentElement.NamespaceURI!="http://www.w3.org/2005/Atom")
                throw new Exception("发布记录格式无效");
            return ParseAtomTags(text,preview,8).Select(tag => new ReleaseCandidate { Tag=tag }).ToList();
        } catch(Exception ex) { throw new TransportException("更新发布记录格式无效",0,ex); }
    }
    sealed class ReleaseCheck {
        internal MetadataResponse<Dictionary<string,object>> Response;
        internal long DesktopAssetId;
        internal Exception Error;
    }
    static async Task<ReleaseCheck> CheckReleaseAsync(ReleaseCandidate candidate,MetadataFetcher fetch,SemaphoreSlim parallel,CancellationToken budget) {
        bool entered=false;
        try {
            await parallel.WaitAsync(budget).ConfigureAwait(false);
            entered=true;
            foreach(string name in candidate.Manifest!=null?new[]{candidate.Manifest}:new[]{"xingshu-desktop-update.json","xingshu-update.json"}) {
                string url=Releases+"/download/"+Uri.EscapeDataString(candidate.Tag)+"/"+name;
                try {
                    var response=await FetchFastestAsync(CandidateUrls(url,FindAssetId(candidate.Assets,name),true),300000,
                        text => Verify(text,candidate.Tag),fetch,budget).ConfigureAwait(false);
                    return new ReleaseCheck { Response=response,DesktopAssetId=FindAssetId(candidate.Assets,S((Dictionary<string,object>)response.Value["desktop"],"asset")) };
                } catch(TransportException ex) { if(candidate.Manifest!=null || ex.StatusCode!=404)throw; }
            }
            return new ReleaseCheck(); // Atom can include releases without an update manifest.
        } catch(OperationCanceledException ex) { return new ReleaseCheck { Error=new TransportException("检查更新超时，请检查网络后重试。",0,ex) }; }
          catch(Exception ex) { return new ReleaseCheck { Error=ex }; }
        finally { if(entered)parallel.Release(); }
    }
    sealed class CheckResult {
        internal Dictionary<string,object> Result;
        internal long DesktopAssetId;
        internal bool Domestic;
    }
    internal static Dictionary<string,object> Check(long current,bool preview) {
        long bestDesktopAssetId=0;
        bool usedDomesticMirror=false;
        return CheckInternal(current,preview,out bestDesktopAssetId,out usedDomesticMirror);
    }
    // The injected transport lets regression tests reproduce slow/offline routes without using the network.
    internal static Dictionary<string,object> Check(long current,bool preview,MetadataFetcher fetch,int timeoutMs) {
        return CheckAsync(current,preview,fetch,timeoutMs).GetAwaiter().GetResult().Result;
    }
    static Dictionary<string,object> CheckInternal(long current,bool preview,out long bestDesktopAssetId,out bool usedDomesticMirror) {
        var check=CheckAsync(current,preview,FetchOnceAsync,CheckTimeoutMs).GetAwaiter().GetResult();
        bestDesktopAssetId=check.DesktopAssetId; usedDomesticMirror=check.Domestic;
        return check.Result;
    }
    static async Task<CheckResult> CheckAsync(long current,bool preview,MetadataFetcher fetch,int timeoutMs) {
      using(var deadline=new CancellationTokenSource(timeoutMs))
      using(var parallel=new SemaphoreSlim(4)) {
        MetadataResponse<List<ReleaseCandidate>> discovery=null;
        string api="https://api.github.com/repos/"+Repo+"/releases?per_page=30";
        try {
            // Query the same release list through official and allowlisted domestic routes immediately.
            var urls=new[]{api}.Concat(DomesticMirrors.Select(m => m+api)).ToList();
            discovery=await FetchFastestAsync(urls,1800000,text => ParseReleases(text,preview),fetch,deadline.Token).ConfigureAwait(false);
        } catch(TransportException) {}
        if(discovery==null) {
            discovery=await FetchFastestAsync(CandidateUrls("https://github.com/"+Repo+"/releases.atom",0,true),600000,
                text => ParseFeed(text,preview),fetch,deadline.Token).ConfigureAwait(false);
        }
        // Fetch independent historical manifests concurrently, still select the greatest SIGNED sequence.
        // Never assume that API order, Atom order, or a version tag is a trustworthy update sequence.
        var checks=await Task.WhenAll(discovery.Value.Select(c => CheckReleaseAsync(c,fetch,parallel,deadline.Token))).ConfigureAwait(false);
        foreach(var check in checks)if(check.Error!=null && !(check.Error is TransportException))throw check.Error;
        var best=checks.Where(c => c.Response!=null).OrderByDescending(c => N(c.Response.Value,"sequence")).FirstOrDefault();
        bool incomplete=checks.Any(c => c.Error!=null);
        if(best==null && incomplete)throw checks.First(c => c.Error!=null).Error;
        var manifest=best==null?null:best.Response.Value;
        string usedHost=best==null?discovery.Host:best.Response.Host;
        bool domestic=IsDomesticHost(usedHost);
        string sourceLabel=domestic?("国内加速镜像 ("+usedHost+")"):("官方直连 ("+usedHost+")");
        var result=new Dictionary<string,object> {
            {"available",manifest!=null && N(manifest,"sequence")>current}, {"currentSequence",current},
            {"release",manifest}, {"envelope",best==null?null:best.Response.Text}, {"releaseUrl",manifest==null?Releases:Releases+"/tag/"+Uri.EscapeDataString(S(manifest,"tag"))},
            {"downloadSource",sourceLabel},
            {"checkWarning",incomplete?"部分发布信息未读取完成，暂不能确认是否还有更新版本，请稍后重试。":null},
            {"message",manifest==null?"当前发布尚未提供签名更新清单，可打开官方发布页手动下载。":N(manifest,"sequence")>current?"发现可校验的新版本。":incomplete?"已校验部分发布，检查尚未完成。":"没有比当前版本更新的可校验发布。"}
        };
        return new CheckResult { Result=result,DesktopAssetId=best==null?0:best.DesktopAssetId,Domestic=domestic };
      }
    }
    const int DownloadProbeBytes=65536;
    internal delegate Task DownloadProber(string url,long size,CancellationToken cancellation);
    internal delegate void PackageDownloader(string url,string destination,long size,string hash);
    internal static void ValidateDownloadProbe(int status,long length,string contentRange,long size) {
        if(status==206) {
            string expected="bytes 0-"+(Math.Min(size,DownloadProbeBytes)-1)+"/"+size;
            if(contentRange!=expected || (length>=0 && length!=Math.Min(size,DownloadProbeBytes)))
                throw new TransportException("下载通道返回的文件范围不符",0,null);
        } else if(status!=200 || (length>=0 && length!=size)) {
            throw new TransportException("下载通道返回的文件大小不符",0,null);
        }
    }
    static async Task ProbeDownloadAsync(string url,long size,CancellationToken cancellation) {
        for(int redirects=0;redirects<6;redirects++) {
            var u=new Uri(url); var request=UpdateRequest(u,SourceTimeoutMs,SourceTimeoutMs);
            request.AddRange(0,Math.Min(size,DownloadProbeBytes)-1);
            try {
                cancellation.ThrowIfCancellationRequested();
                using(cancellation.Register(request.Abort))
                using(var response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false)) {
                    if((int)response.StatusCode>=300 && (int)response.StatusCode<400) { url=new Uri(u,response.Headers["Location"]).AbsoluteUri; continue; }
                    ValidateDownloadProbe((int)response.StatusCode,response.ContentLength,response.Headers["Content-Range"],size);
                    // Test actual bytes, rather than selecting a fast HEAD response whose body may stall.
                    using(var source=response.GetResponseStream()) {
                        byte[] buffer=new byte[(int)Math.Min(size,DownloadProbeBytes)]; int total=0,n;
                        while(total<buffer.Length && (n=await source.ReadAsync(buffer,total,buffer.Length-total,cancellation).ConfigureAwait(false))>0)total+=n;
                        if(total!=buffer.Length)throw new TransportException("下载通道未返回完整探测数据",0,null);
                        request.Abort(); // A server may ignore Range; never drain the rest of a large probe response.
                    }
                    return;
                }
            } catch(WebException ex) { cancellation.ThrowIfCancellationRequested(); throw ToTransportException(ex); }
              catch(IOException ex) { throw new TransportException(ex.Message,0,ex); }
              catch(SocketException ex) { throw new TransportException(ex.Message,0,ex); }
        }
        throw new TransportException("更新下载重定向次数过多",0,null);
    }
    static async Task<MetadataResponse<string>> TryProbeDownloadAsync(string url,long size,DownloadProber probe,CancellationToken cancellation) {
        try {
            await probe(url,size,cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return new MetadataResponse<string> { Value=url,Host=new Uri(url).Host };
        } catch(Exception ex) { return new MetadataResponse<string> { Error=ex }; }
    }
    static void DownloadOnce(string url,string destination,long size,string hash) {
        for(int redirects=0;redirects<6;redirects++) {
            var u=new Uri(url); var req=UpdateRequest(u,SourceTimeoutMs,60000);
            try {
                using(var response=(HttpWebResponse)req.GetResponse()) {
                    if((int)response.StatusCode>=300&&(int)response.StatusCode<400) { url=new Uri(u,response.Headers["Location"]).AbsoluteUri; continue; }
                    if(response.ContentLength>=0&&response.ContentLength!=size)throw new Exception("更新文件大小不符");
                    try {
                        using(var source=response.GetResponseStream())using(var target=new FileStream(destination,FileMode.CreateNew,FileAccess.Write)) {
                            byte[] b=new byte[65536]; long total=0; int n;
                            while((n=source.Read(b,0,b.Length))>0) { total+=n; if(total>size)throw new Exception("更新文件超出声明大小"); target.Write(b,0,n); }
                            target.Flush(true); if(total!=size)throw new TransportException("下载未完成，请重试",0,null);
                        }
                        if(Hash(destination)!=hash)throw new Exception("更新文件 SHA256 校验失败"); return;
                    } catch { if(File.Exists(destination))File.Delete(destination); throw; }
                }
            } catch(WebException ex) { throw ToTransportException(ex); }
              catch(IOException ex) { throw new TransportException(ex.Message,0,ex); }
              catch(SocketException ex) { throw new TransportException(ex.Message,0,ex); }
        }
        throw new TransportException("更新下载重定向次数过多",0,null);
    }
    static string DownloadWithFallback(IList<string> candidates,string destination,long size,string hash) {
        return DownloadWithFallback(candidates,destination,size,hash,ProbeDownloadAsync,DownloadOnce);
    }
    internal static string DownloadWithFallback(IList<string> candidates,string destination,long size,string hash,DownloadProber probe,PackageDownloader download) {
        TransportException last=null;
        var remaining=candidates.Distinct(StringComparer.Ordinal).ToList();
        while(remaining.Count>0) {
            var source=RaceSourcesAsync(remaining,(url,cancellation) => TryProbeDownloadAsync(url,size,probe,cancellation),CancellationToken.None).GetAwaiter().GetResult();
            remaining.Remove(source.Value);
            try {
                if(File.Exists(destination))File.Delete(destination);
                download(source.Value,destination,size,hash);
                return source.Host;
            } catch(TransportException ex) {
                last=ex;
            }
        }
        throw last??new TransportException("下载更新包失败，请重试或前往官方发布页手动下载",0,null);
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
            long current=N(request,"current"); bool preview=Convert.ToBoolean(request["preview"]);
            long desktopAssetId=0; bool usedDomesticMirror=false;
            var result=CheckInternal(current,preview,out desktopAssetId,out usedDomesticMirror);
            if(args.Length>0&&args[0]=="--download") {
                if(result["checkWarning"]!=null)throw new Exception("更新检查尚未完成，请重新检查后下载");
                if(!Convert.ToBoolean(result["available"]))throw new Exception("未发现可用更新，请重新检查");
                var m=(Dictionary<string,object>)result["release"];
                if(N(m,"sequence")!=N(request,"sequence"))throw new Exception("发布版本已变化，请重新检查后下载");
                var asset=(Dictionary<string,object>)m["desktop"]; string cache=Cache();
                string usedDlHost=DownloadWithFallback(CandidateUrls(AssetUrl(m,"desktop"),desktopAssetId,true),Path.Combine(cache,"update.zip"),N(asset,"size"),S(asset,"sha256"));
                string stage=Path.Combine(cache,"checked"); Directory.CreateDirectory(stage); Extract(Path.Combine(cache,"update.zip"),stage,m);
                File.WriteAllText(Path.Combine(cache,"plan.json"),Json.Serialize(new { tag=S(m,"tag"),envelope=S(result,"envelope") }),new UTF8Encoding(false));
                result["cache"]=cache;
                result["downloadSource"]=IsDomesticHost(usedDlHost)?("国内加速镜像 ("+usedDlHost+")"):("官方源 ("+usedDlHost+")");
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
