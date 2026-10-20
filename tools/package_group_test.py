"""Build a portable group-test ZIP from the checked local Hub and runtime candidates."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def invoke(exe, *args, ok=True):
    result = subprocess.run([str(exe), *map(str, args)], capture_output=True)
    if (result.returncode == 0) != ok:
        raise RuntimeError(f'{args[0]}: unexpected exit {result.returncode}; {result.stdout!r} {result.stderr!r}')
    return result


def verify_installer(package, profiles, release):
    exe = package / '星枢群测助手.exe'
    invoke(exe, '--verify-package')
    temp_base = Path(tempfile.gettempdir()).resolve()
    temp = Path(tempfile.mkdtemp(prefix='xingshu-install-test-', dir=temp_base)).resolve()
    assert temp.parent == temp_base and temp.name.startswith('xingshu-install-test-')
    try:
        for platform in release['Platforms']:
            profile = next(p for p in profiles if p['platformId'] == platform['Id'])
            fixture = temp / platform['Id']
            fixture.mkdir()
            # The helper never changes these files. A hard link avoids copying the large game assembly.
            try:
                os.link(Path(profile['installRoot']) / 'GameAssembly.dll', fixture / 'GameAssembly.dll')
            except OSError:
                shutil.copy2(Path(profile['installRoot']) / 'GameAssembly.dll', fixture / 'GameAssembly.dll')
            (fixture / platform['ExeName']).write_bytes(b'installer fixture; not executable')
            target = fixture / platform['BundleRelativePath']
            target.parent.mkdir(parents=True)
            baseline = ROOT / 'APX-Dev/build/runtime-truth-fix' / platform['Id'] / ('__data.before.' + profile['containerSha256'].lower()[:12])
            shutil.copy2(baseline, target)
            original = sha(target)
            invoke(exe, '--check', platform['Id'], fixture)
            invoke(exe, '--check-closed', platform['Id'], fixture)
            invoke(exe, '--install', platform['Id'], fixture)
            assert sha(target) == platform['PayloadSha256']
            backup = fixture / '.xingshu-backup' / release['Version'] / platform['Id'] / '__data.before'
            assert sha(backup) == original
            invoke(exe, '--install', platform['Id'], fixture)
            assert sha(backup) == original, 'Repeated installation changed the original backup'
            target.write_bytes(b'unknown game update')
            invoke(exe, '--restore', platform['Id'], fixture, ok=False)
            assert target.read_bytes() == b'unknown game update', 'Restore overwrote a newer game update'
            shutil.copy2(package / platform['Payload'], target)
            invoke(exe, '--restore', platform['Id'], fixture)
            assert sha(target) == original
            invoke(exe, '--restore', platform['Id'], fixture)
            invoke(exe, '--install', platform['Id'], fixture)
            assert sha(target) == platform['PayloadSha256'] and sha(backup) == original
            payload = package / platform['Payload']
            saved = payload.read_bytes()
            try:
                payload.write_bytes(b'corrupted package')
                invoke(exe, '--install', platform['Id'], fixture, ok=False)
                assert sha(target) == platform['PayloadSha256']
            finally:
                payload.write_bytes(saved)
            print(platform['Id'] + ': fresh install, repeat install, restore, reinstall, unknown version and corrupt payload PASS', flush=True)
        print('Package integrity check PASS', flush=True)
    finally:
        assert temp.parent == temp_base and temp.name.startswith('xingshu-install-test-')
        shutil.rmtree(temp)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='0.1.0-test.20261020.3')
    args = parser.parse_args()
    if not all(c.isascii() and (c.isalnum() or c in '.-') for c in args.version):
        raise SystemExit('Invalid version')
    publish = ROOT / '群测发布'
    publish.mkdir(exist_ok=True)
    name = 'XingShu_' + args.version
    destination = publish / name
    archive = publish / (name + '.zip')
    if destination.exists() or archive.exists():
        raise SystemExit('This release already exists; choose a new --version')
    stage = publish / ('.building-' + name + '-' + uuid.uuid4().hex)
    hub = ROOT / 'AstralParty-Hub/src-tauri/target/release/xingshu-hub.exe'
    if hub.stat().st_mtime < (ROOT / 'AstralParty-Hub/dist/index.html').stat().st_mtime:
        raise SystemExit('The current Hub production build has not finished')
    for folder in ('AstralParty-Hub/src', 'AstralParty-Hub/src-tauri/src'):
        if any(p.is_file() and p.suffix in {'.rs', '.ts', '.tsx', '.json', '.css'} and
               p.stat().st_mtime > hub.stat().st_mtime for p in (ROOT / folder).rglob('*')):
            raise SystemExit('Hub sources changed after the production build; rebuild before packaging')
    stage.mkdir()
    (stage / 'Hub').mkdir()
    shutil.copy2(hub, stage / 'Hub/xingshu-hub.exe')
    profiles = json.loads((ROOT / 'AstralParty-Hub/src-tauri/src/xing_shu/platform/platform_profiles.json').read_text('utf-8'))
    deployment = json.loads((ROOT / 'APX-Dev/build/runtime-truth-fix/deployment.json').read_text('utf-8'))
    release = {'FormatVersion': 1, 'Version': args.version, 'HubSha256': sha(hub),
               'ExtensionManagementChannels': ['STEAM_CN', 'STEAM_GLOBAL', 'TAPTAP', 'BILIBILI', 'WEGAME'],
               'CharacterCatalogCount': 37, 'PreviewCharacterCount': 2, 'Platforms': []}
    for profile in profiles:
        platform = profile['platformId']
        if platform not in ('STEAM_CN', 'TAPTAP'):
            continue
        prepared = next(p for p in deployment if p['platform'] == platform)
        source = Path(prepared['candidate'])
        assert sha(source) == prepared['candidateSha256']
        relative = f'Runtime/{platform}/__data'
        target = stage / relative
        target.parent.mkdir(parents=True)
        shutil.copy2(source, target)
        release['Platforms'].append({
            'Id': platform, 'Label': {'STEAM_CN': 'Steam 国服', 'TAPTAP': 'TapTap Windows 版'}[platform],
            'ExeName': profile['processExecutable'], 'GameAssemblySha256': profile['gameRuntimeDllSha256'].lower(),
            'BundleRelativePath': profile['bundleRelativePath'], 'Payload': relative,
            'PayloadSha256': prepared['candidateSha256'],
            'AcceptedHashes': sorted({profile['containerSha256'].lower(), prepared['beforeSha256'].lower()}),
        })
    manifest = stage / 'release.json'
    manifest.write_text(json.dumps(release, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    identity = stage / 'PackageIdentity.cs'
    identity.write_text('internal static class PackageIdentity { internal const string ManifestSha256 = "' + sha(manifest) + '"; }', encoding='utf-8')
    compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    subprocess.run([str(compiler), '/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/nowarn:0649',
                    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
                    '/reference:System.Web.Extensions.dll', '/out:' + str(stage / '星枢群测助手.exe'),
                    str(ROOT / 'tools/GroupTestInstaller.cs'), str(identity)], check=True)
    identity.unlink()
    (stage / '先读我.txt').write_text(f'''星枢 XingShu 群测版 {args.version}
适配：Windows 10/11 64 位，吉星派对 3.3.0 / 资源 026。
运行时安装文件：Steam 国服和 TapTap Windows 版。
Hub 扩展目录管理：Steam 国服、Steam 国际服、TapTap Windows、哔哩哔哩、WeGame；支持同渠道保存多个安装位置。后面三个渠道本版未附带经过验证的运行时安装文件。

安装（不需要 Git、Python、Node.js 或 Rust）
1. 将整个 ZIP 解压到固定文件夹。不要在压缩包里直接打开 exe，也不要单独移动助手。
2. 正常退出游戏。双击“星枢群测助手.exe”。
3. 选择平台，再选择实际游戏程序。Steam 国服通常位于游戏安装目录的 8vJXn6CN/AstralParty_CN.exe；TapTap 请选择吉星派对.exe，不能选择 TapTap 启动器。
4. 点“安装 / 更新插件”。助手会校验渠道和资源版本，备份安装前文件再替换。若资源不匹配，会停止安装；请先用官方平台校验/修复游戏，再更新并进入一次大厅，退出后重试。
5. 主端和观战端各安装一次。通过 Steam / TapTap 官方启动器正常启动游戏，不要从助手直接启动游戏 exe。
6. 点“启动星枢 Hub”，或直接打开 Hub/xingshu-hub.exe。Hub 首次启动需要系统 WebView2 Runtime；Windows 10/11 通常已有，没有时按系统提示安装。
7. 在 Hub 设置中选择自己的游戏目录；游戏账号页分别绑定主账号与观战账号，并保存对应平台/游戏 exe。不要填别人的账号 ID。打开游戏后以实际识别身份为准。
8. 观战端完成公告和登录、进入大厅后，主端创建一局 PVE。选择“我的观战账号”后跟随当前对局；“本机托管验证”仍使用这台电脑上的已登录官方客户端。

本版 Hub 更新
角色中心展示 37 条官方角色配置，其中 2 条名称、技能和立绘尚未完整发布，单列为“官方预更新”，未提供的内容显示待更新，不算作未持有的已上线角色。
角色、皮肤、羁绊与档案采用立绘详情布局；卡牌提供完整竖版大图、效果详情及原生动态异画。特殊异化与场地效果归入地图与场地，筹码另有图鉴。
历史对战按账号保存，可点开单场阵容、结算数据与星枢评分。PVE 显示胜负，PVP 显示名次；结算金币保留展示且不参与评分，辅助转账 20 以上有额外贡献权重。没有返回的指标显示“—”。
包管理器、运行时插件、游戏模组共用“渠道与安装位置”，导入时务必选中正确目录。支持声明渠道的 .xspkg / .zip 包；旧包未声明渠道时仅按 Steam 国服处理。
星枢运行时页也按此渠道和目录显示实际游戏进程、文件指纹核验及当前连接上报版本；磁盘安装状态与正在运行的版本分开显示。
运行时插件页会核验本版内置运行时；其他 DLL 和模组、modimg 卡面资源从实际目录扫描。手动安装项只展示文件，不假定已加载，也不提供删除按钮。
完整解压后从 Hub 文件夹启动，可在插件页“打开群测助手”，自动带入选中的 Steam 国服或 TapTap 目录；打开助手不会自动安装。
独立扩展包格式与渠道声明见“扩展包格式说明.md”。安装、更新和卸载前请退出对应目录的游戏。

插件与测试
F1 / 悬浮球：功能中心。Ctrl+Shift+H：手牌面板。十倍思考时间与自动致谢请在功能设置中开启。
本包包含启动修复、头像旁手牌挂件跨局清理、Hub 等待页清理、观战结束退房逻辑、选人/回合/筹码的本地十倍计时和自动致谢修复。
运行时 1.0.0-library-r3：支持角色和历史战绩同步、旧观战会话清理；功能中心标明项目源码来源。此包同时更新 Hub 图鉴与扩展管理。
已经实测：双端插件启动及身份识别、首局 PVE 四人观战镜像；主端不重启连续进入两局，挂件换为新局手牌。
请重点反馈：同房重开与换房时，观战端连续两局是否自动退房再跟房；自然结算弹窗是否自动退出；收卡/星币/治疗是否分别触发致谢；是否有旧牌残留、卡死、闪退。
上述连续观战、自然结束与三种致谢仍待群测确认，不能视为全部实机通过。

当前边界
完整观战手牌需要已登录的官方观战客户端，本包没有免登录读牌和远程账号池。一局中持续订阅、结束退房；读一次退出后不会继续获得手牌变化。
十倍修改本地计时，服务端截止仍有效。测试包不替换原生变速 version.dll；需要变速时使用插件内的托管模式。
未附带账号登录数据、密码、本机 Hub 数据库或个人运行日志。已有 Hub 用户设置仍保存在系统用户数据目录，切换包不会自动重置它们。
此包为便携测试版本，没有数字签名；请核对发布者和 ZIP 的 SHA256，不需要关闭安全软件。

还原
正常退出对应渠道游戏，助手里选择相同平台和游戏，点“还原安装前版本”。备份保存在游戏目录 .xingshu-backup/{args.version}/，还原完成前请保留。
还原恢复的是“本次安装前版本”；之前已有插件时会恢复旧插件。完全恢复官方版可使用 Steam/TapTap 的游戏校验/修复。
若游戏又更新，助手会拒绝覆盖未知文件，请先使用官方平台修复。退出 Hub 并删除解压目录即可移除便携 Hub；这不会删除用户设置。

反馈时请说明
测试包版本、Windows 版本、游戏渠道/资源版本、单端或双端、操作步骤、预期与实际结果、首次还是下一局、发生时间，以及问题截图。
日志可能包含账号和房间信息，请先隐藏个人信息；不要发密码、验证码或登录票据。
''', encoding='utf-8-sig')
    shutil.copy2(ROOT / 'AstralParty-Hub/docs/specs/extension_channels.md', stage / '扩展包格式说明.md')
    shutil.copy2(ROOT / 'LICENSE', stage / 'LICENSE.txt')
    (stage / '项目来源.txt').write_text('星枢 Hub 与游戏内运行时\nhttps://github.com/Breezecaresses/Xingshu-hub\n\n代码许可见 LICENSE.txt。角色、卡面、配置及游戏资源版权归吉星派对原权利人所有，不随本项目代码许可证重新授权。\n', encoding='utf-8-sig')
    verify_installer(stage, profiles, release)
    entries = sorted(p for p in stage.rglob('*') if p.is_file())
    (stage / 'SHA256SUMS.txt').write_text(''.join(f'{sha(p)}  {p.relative_to(stage).as_posix()}\n' for p in entries), encoding='utf-8')
    stage.rename(destination)
    with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for path in sorted(destination.rglob('*')):
            if path.is_file():
                output.write(path, Path(name) / path.relative_to(destination))
    with zipfile.ZipFile(archive) as output:
        assert output.testzip() is None
        assert len(output.namelist()) == len(entries) + 1
    digest = sha(archive)
    (publish / (name + '.zip.sha256')).write_text(digest + '  ' + archive.name + '\n', encoding='utf-8')
    (publish / '发群说明.txt').write_text(f'''星枢群测版 {args.version}
适配吉星派对 3.3.0 / 资源 026；内置运行时支持 Steam 国服和 TapTap Windows 版。Hub 的目录/扩展管理包含 Steam 国服、国际服、TapTap、B 站和 WeGame，可分别保存安装位置；其他渠道暂未附带运行时。
请发 {archive.name}。完整解压后，先读“先读我.txt”，再打开“星枢群测助手.exe”，选择自己的游戏程序安装；主端与观战端分别安装。助手提供安装前备份和还原，不需要开发环境。

本版更新：37 条角色配置（包含 2 个官方预更新角色）、完整卡牌与动态异画、筹码和地图详情、按账号保存的历史对局详情与评分；PVE 显示胜负，辅助转账贡献提高，结算金币展示保留且不参与评分。
扩展管理已接入实际运行时、独立插件和本地模组扫描，移除原先的示例商城条目，导入/卸载按选中渠道及目录执行。运行时 1.0.0-library-r3 和 Hub 都标明项目来源：https://github.com/Breezecaresses/Xingshu-hub
请重点测试：同房重开/换房连续两局是否换牌、观战能否退房后再跟房、自然结算是否退出，以及收牌/星币/治疗的自动致谢。上述观战与致谢场景仍待群测确认。
完整观战手牌仍需登录官方观战端，远程账号池尚未上线；十倍本地计时受服务端截止限制。

反馈请附：测试包版本、系统/渠道/资源版本、操作步骤、发生时间、首次还是下一局、截图。日志先隐藏个人信息，不要发送密码、验证码或票据。
''', encoding='utf-8-sig')
    print(f'READY: {archive}\nSIZE: {archive.stat().st_size / 1024 / 1024:.2f} MiB\nSHA256: {digest}', flush=True)


if __name__ == '__main__':
    main()
