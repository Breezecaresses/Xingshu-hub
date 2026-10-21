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
    parser.add_argument('--phone-apk',type=Path,required=True)
    parser.add_argument('--vanilla',type=Path,required=True)
    args=parser.parse_args()
    import re
    if not re.fullmatch('[a-f0-9]{40}',args.source_commit): raise SystemExit('Expected full source commit')
    manifest=json.loads(args.manifest.read_text('utf8'))
    candidate=Path(manifest['candidate'])
    assert sha(candidate)==manifest['candidateSha256']
    assert manifest['platform']=='ANDROID_CN' and manifest['nativeLibraries']==[]
    assert sha(args.vanilla)==manifest['originalSha256']
    with zipfile.ZipFile(args.phone_apk) as apk:
        assert hashlib.sha256(apk.read('assets/candidate.__data')).hexdigest()==manifest['candidateSha256']
        assert hashlib.sha256(apk.read('assets/vanilla.__data')).hexdigest()==manifest['originalSha256']
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
    shutil.copy2(args.phone_apk,destination/'星枢手机安装助手.apk')
    shutil.copy2(ROOT/'ANDROID_PHONE_INSTALL.md',destination/'手机安装与手动替换.md')
    manual=destination/'手动替换'; manual.mkdir()
    shutil.copy2(candidate,manual/'__data')
    shutil.copy2(args.vanilla,manual/'官方原版.__data')
    shutil.copy2(ROOT/'ANDROID_PHONE_INSTALL.md',manual/'替换说明.md')
    (manual/'文件校验.json').write_text(json.dumps({**manifest,
        'candidate':'__data','vanilla':'官方原版.__data',
        'supportedPreviousSha256':['3d94697f4dc6d64c2cb5fcca56339090f54e920cd7dab6de7bcc1912347e5a0f']},ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    compiler=Path(r'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe')
    installer=destination/'星枢安卓安装助手.exe'
    subprocess.run([str(compiler),'/nologo','/target:winexe','/out:'+str(installer),
                    '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
                    '/reference:System.Web.Extensions.dll',str(ROOT/'tools/AndroidPreviewInstaller.cs')],check=True)
    subprocess.run([str(installer),'--verify-package'],check=True)
    (destination/'先读我.txt').write_text(f'''星枢安卓预览 {args.version}
适配游戏 com.feimo.astralparty 3.3.0，已在 TapTap 内置 MuMu 5 号实例验证。
包含手机 APK 安装助手、电脑 ADB 安装助手与手动替换文件。
手机 APK 是独立安装工具，通过本机无线调试配对和连接即可使用，无需电脑或 Shizuku 通知，不替换游戏 APK。
小米 14 Pro Android 16 / HyperOS 3 已验证配对、连接、还原与安装；其他机型权限仍需实测。

本版：安卓悬浮球默认大小 64、最小 48，旧默认 44 自动迁移为 64。
修复自动隐藏设置不生效和触屏残留悬停；启用贴边隐藏后闲置 2 秒收起，保留可点击露出区域。
保留粉黄界面、移动步数、模组加载、触屏叠放手牌；桌面网桥仍暂停。

手机安装：
1. 安装“星枢手机安装助手.apk”。在官方游戏更新资源并进过大厅后正常退出。
2. 将助手与系统无线调试页面分屏。打开“使用配对码配对设备”，在助手填写配对端口和六位码，点“配对这部手机”。
3. 配对后关闭配对码弹窗，从无线调试主页面读取连接端口，在助手填写并连接。两个端口不同；支持粘贴完整IP:端口，只连接本机。
4. 检查游戏与目录权限，再点击“安装 / 更新星枢”。撤销时点“还原官方原版”；必须等待SHA256校验通过。
5. 支持原版、本版和已验证预览4，未知资源禁止覆盖。游戏运行时拒绝替换；安装后可关闭无线调试。
6. 已正常运行的Shizuku可选用，不是安装前提。普通APK仍需用户完成系统调试授权。
详细步骤、手动文件位置、系统权限处理见“手机安装与手动替换.md”。

安装（电脑连接手机，需已安装 Android platform-tools；不需要 Python）：
1. 在官方游戏中完成资源更新并进过一次大厅，正常退出。
2. 连接已授权的设备；执行 adb devices 确认序列号，只选择自己的目标设备。
3. 完整解压，运行“星枢安卓安装助手.exe”，选择 adb.exe、刷新设备并选择目标，点击“安装 / 更新”。
4. 安装器先核对游戏版本与原版缓存 SHA256，备份原版后才安装；不清除账号数据、不修改 APK、不启用 root。
5. 正常从官方渠道启动游戏。助手保留每台设备的备份与记录，后续新版继续使用；未知缓存会被拒绝。
6. 以前用 Python 脚本装过预览的设备，请先沿用原 backup 文件夹和 deployment.json 还原，再使用助手。

还原：选择原设备，点击“还原原版”。备份在 %LOCALAPPDATA%/XingShu/AndroidBackups 中，请保留。
游戏仍在运行时助手会拒绝替换，请正常退出后重试。资源版本不匹配时使用官方游戏修复，再更新并重进大厅。
备用 Python 命令行工具也保留在包内。手机与电脑安装记录分别保存；跨工具出现未知版本提示时先还原原版。

十倍思考仍受服务端截止限制。MuMu 曾出现一次原生音频引擎崩溃，未确认原因；完整已知边界见兼容性说明。
源码：https://github.com/Breezecaresses/Xingshu-hub
提交：{args.source_commit}
游戏资源版权归原权利人；不含账号凭据、个人日志或原生变速 DLL。
''',encoding='utf-8-sig')
    entries=sorted(p for p in destination.rglob('*') if p.is_file())
    assert all('apx' not in str(p.relative_to(destination)).lower() for p in entries)
    (destination/'SHA256SUMS.txt').write_text(''.join(f'{sha(p)}  {p.relative_to(destination).as_posix()}\n' for p in entries),encoding='utf8')
    with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED,compresslevel=9) as output:
        for file in sorted(destination.rglob('*')):
            if file.is_file(): output.write(file,Path(name)/file.relative_to(destination))
    with zipfile.ZipFile(archive) as output: assert output.testzip() is None
    Path(str(archive)+'.sha256').write_text(sha(archive)+'  '+archive.name+'\n',encoding='utf8')
    print(f'READY: {archive}\nSIZE: {archive.stat().st_size/1024/1024:.2f} MiB\nSHA256: {sha(archive)}')
    manual_archive=publish/('XingShu_Android_Manual_'+args.version+'.zip')
    with zipfile.ZipFile(manual_archive,'x',zipfile.ZIP_DEFLATED,compresslevel=9) as output:
        for file in sorted(manual.iterdir()): output.write(file,Path(manual_archive.stem)/file.name)
    with zipfile.ZipFile(manual_archive) as output: assert output.testzip() is None
    Path(str(manual_archive)+'.sha256').write_text(sha(manual_archive)+'  '+manual_archive.name+'\n',encoding='utf8')
    print(f'MANUAL: {manual_archive}\nSHA256: {sha(manual_archive)}')

if __name__=='__main__': main()
