"""Package the verified Android cache preview separately from the Windows installer."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import zipfile

ROOT=Path(__file__).resolve().parents[1]
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest',type=Path,required=True)
    parser.add_argument('--version',required=True)
    parser.add_argument('--source-commit',required=True)
    args=parser.parse_args()
    import re
    if not re.fullmatch('[a-f0-9]{40}',args.source_commit): raise SystemExit('Expected full source commit')
    manifest=json.loads(args.manifest.read_text('utf8'))
    candidate=Path(manifest['candidate'])
    assert sha(candidate)==manifest['candidateSha256']
    assert manifest['platform']=='ANDROID_CN' and manifest['nativeLibraries']==[]
    name='XingShu_Android_'+args.version
    publish=ROOT/'发布包'; publish.mkdir(exist_ok=True)
    destination=publish/name
    archive=publish/(name+'.zip')
    assert not destination.exists() and not archive.exists(), 'Choose a new version'
    destination.mkdir()
    shutil.copy2(candidate,destination/'__data')
    manifest['candidate']='__data'
    manifest['sourceCommit']=args.source_commit
    (destination/'candidate.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    shutil.copy2(ROOT/'tools/android_runtime_device.py',destination/'android_runtime_device.py')
    shutil.copy2(ROOT/'ANDROID_COMPATIBILITY.md',destination/'兼容性说明.md')
    shutil.copy2(ROOT/'LICENSE',destination/'LICENSE.txt')
    compiler=Path(r'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe')
    installer=destination/'星枢安卓安装助手.exe'
    subprocess.run([str(compiler),'/nologo','/target:winexe','/out:'+str(installer),
                    '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
                    '/reference:System.Web.Extensions.dll',str(ROOT/'tools/AndroidPreviewInstaller.cs')],check=True)
    subprocess.run([str(installer),'--verify-package'],check=True)
    (destination/'先读我.txt').write_text(f'''星枢安卓预览 {args.version}
适配游戏 com.feimo.astralparty 3.3.0，已在 TapTap 内置 MuMu 5 号实例验证。
这是外部托管程序集缓存预览，不是独立 APK，也不是 Windows 安装文件。
真机、其他游戏资源版本及系统受限的 Android/data 目录尚未验证。

本版：手机对局统计与统计挂件补充移动步数，服务器结算后校准。
功能页改名为“模组加载”，页内称“立绘替换”；运行时文件统一星枢命名。
保留粉黄界面、缩放及触屏叠放手牌。桌面网桥暂不启动，未来服务器查看手牌的接入结构保留。

安装（电脑连接手机，需已安装 Android platform-tools；不需要 Python）：
1. 在官方游戏中完成资源更新并进过一次大厅，正常退出。
2. 连接已授权的设备；执行 adb devices 确认序列号，只选择自己的目标设备。
3. 完整解压，运行“星枢安卓安装助手.exe”，选择 adb.exe、刷新设备并选择目标，点击“安装 / 更新”。
4. 安装器先核对游戏版本与原版缓存 SHA256，备份原版后才安装；不清除账号数据、不修改 APK、不启用 root。
5. 正常从官方渠道启动游戏。助手保留每台设备的备份与记录，后续新版继续使用；未知缓存会被拒绝。
6. 以前用 Python 脚本装过预览的设备，请先沿用原 backup 文件夹和 deployment.json 还原，再使用助手。

还原：选择原设备，点击“还原原版”。备份在 %LOCALAPPDATA%/XingShu/AndroidBackups 中，请保留。
游戏仍在运行时助手会拒绝替换，请正常退出后重试。资源版本不匹配时使用官方游戏修复，再更新并重进大厅。
备用 Python 命令行工具也保留在包内；纯手机独立 APK 安装器尚未提供。

十倍思考仍受服务端截止限制。MuMu 曾出现一次原生音频引擎崩溃，未确认原因；完整已知边界见兼容性说明。
源码：https://github.com/Breezecaresses/Xingshu-hub
提交：{args.source_commit}
游戏资源版权归原权利人；不含账号凭据、个人日志或原生变速 DLL。
''',encoding='utf-8-sig')
    entries=sorted(p for p in destination.rglob('*') if p.is_file())
    assert all('apx' not in str(p.relative_to(destination)).lower() for p in entries)
    (destination/'SHA256SUMS.txt').write_text(''.join(f'{sha(p)}  {p.relative_to(destination).as_posix()}\n' for p in entries),encoding='utf8')
    with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED,compresslevel=9) as output:
        for file in sorted(destination.iterdir()): output.write(file,Path(name)/file.name)
    with zipfile.ZipFile(archive) as output: assert output.testzip() is None
    Path(str(archive)+'.sha256').write_text(sha(archive)+'  '+archive.name+'\n',encoding='utf8')
    print(f'READY: {archive}\nSIZE: {archive.stat().st_size/1024/1024:.2f} MiB\nSHA256: {sha(archive)}')

if __name__=='__main__': main()
