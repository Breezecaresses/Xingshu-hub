"""Build and verify a portable XingShu release from checked local candidates."""
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
import re

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def invoke(exe, *args, ok=True):
    result = subprocess.run([str(exe), *map(str, args)], capture_output=True)
    if (result.returncode == 0) != ok:
        raise RuntimeError(f'{args[0]}: unexpected exit {result.returncode}; {result.stdout!r} {result.stderr!r}')
    return result


def compatible_previous_payloads(publish, platform):
    """Allow upgrades only from completed, intact releases for the same game build."""
    found = {}
    manifests = [p for folder in (ROOT / '群测发布', publish) for p in folder.glob('XingShu_*/release.json')]
    for manifest in manifests:
        previous = json.loads(manifest.read_text('utf-8'))
        for item in previous.get('Platforms', []):
            if (item.get('Id') == platform['Id'] and
                    item.get('GameAssemblySha256') == platform['GameAssemblySha256'] and
                    item.get('BundleRelativePath') == platform['BundleRelativePath']):
                payload = (manifest.parent / item['Payload']).resolve()
                if manifest.parent.resolve() not in payload.parents:
                    raise RuntimeError('Previous release payload escapes its package')
                if payload.is_file() and sha(payload) == item['PayloadSha256']:
                    found[item['PayloadSha256']] = payload
    return found


def stage_mod_library(source, destination):
    """Bundle only the embedded catalog's verified local payloads, never arbitrary archive contents."""
    source = source.resolve(strict=True)
    expected = json.loads((ROOT / 'AstralParty-Hub/src-tauri/src/mod_library.generated.json').read_text('utf-8'))
    if json.loads((source / 'catalog.json').read_text('utf-8')) != expected:
        raise ValueError('Mod library does not match the catalog embedded in this Hub')
    selected = {'catalog.json', '适配说明.md'}
    for item in expected['items']:
        for file in item['files']:
            relative = 'payload/' + file['sha256'] + '.bundle'
            if not re.fullmatch(r'payload/[0-9a-f]{64}\.bundle', relative) or file['source'] != relative:
                raise ValueError('Invalid mod payload name')
            payload = (source / relative).resolve(strict=True)
            if source not in payload.parents or payload.stat().st_size != file['size'] or sha(payload) != file['sha256']:
                raise ValueError('Mod payload is corrupt or escapes its library: ' + relative)
            selected.add(relative)
    destination.mkdir()
    for relative in sorted(selected):
        path = (source / relative).resolve(strict=True)
        if source not in path.parents:
            raise ValueError('Library documentation escapes its directory')
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
        if sha(target) != sha(path):
            raise ValueError('Copied mod library did not verify')
    return {'Manifest': 'ModLibrary/catalog.json', 'ManifestSha256': sha(destination / 'catalog.json'),
            'ItemCount': len(expected['items']), 'PayloadCount': len(selected) - 2, 'LocalAdaptationCandidate': True}


def verify_installer(package, profiles, release, deployment):
    exe = package / '星枢安装助手.exe'
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
            baseline = ROOT / 'XingShu-Dev/build/runtime-truth-fix' / platform['Id'] / ('__data.before.' + profile['containerSha256'].lower()[:12])
            if not baseline.is_file():
                baseline = Path(next(p['backup'] for p in deployment if p['platform'] == platform['Id']))
            assert sha(baseline) == profile['containerSha256'].lower(), 'Fresh-install fixture must use the audited official baseline'
            shutil.copy2(baseline, target)
            original = sha(target)
            invoke(exe, '--check', platform['Id'], fixture)
            for other in release['Platforms']:
                if other['Id'] == platform['Id']:
                    continue
                invoke(exe, '--install', other['Id'], fixture, ok=False)
                assert sha(target) == original, 'Cross-channel installation changed a game file'
            if len(release['Platforms']) == 1:
                other = 'TAPTAP' if platform['Id'] == 'STEAM_CN' else 'STEAM_CN'
                invoke(exe, '--install', other, fixture, ok=False)
                assert sha(target) == original, 'Single-channel package changed another channel'
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
            for previous_index, (previous_hash, previous_payload) in enumerate(compatible_previous_payloads(ROOT / '发布包', platform).items()):
                # Keep the nested fixture below .NET Framework's legacy path limit.
                upgrade = temp / (platform['Id'] + '-' + str(previous_index))
                upgrade.mkdir()
                os.link(fixture / 'GameAssembly.dll', upgrade / 'GameAssembly.dll')
                (upgrade / platform['ExeName']).write_bytes(b'installer fixture; not executable')
                upgrade_target = upgrade / platform['BundleRelativePath']
                upgrade_target.parent.mkdir(parents=True)
                shutil.copy2(previous_payload, upgrade_target)
                invoke(exe, '--install', platform['Id'], upgrade)
                assert sha(upgrade_target) == platform['PayloadSha256']
                if previous_hash == platform['PayloadSha256']:
                    # The desktop runtime is unchanged in a phone-helper release.
                    # A no-op must not invent a restorable pre-install snapshot.
                    assert not (upgrade / '.xingshu-backup').exists()
                    invoke(exe, '--restore', platform['Id'], upgrade, ok=False)
                    assert sha(upgrade_target) == previous_hash
                    continue
                invoke(exe, '--restore', platform['Id'], upgrade)
                assert sha(upgrade_target) == previous_hash
            print(platform['Id'] + ': fresh install, repeat install, restore, reinstall, unknown version and corrupt payload PASS', flush=True)
            print(platform['Id'] + ': compatible previous group-test upgrades and restores PASS', flush=True)
        print('Package integrity check PASS', flush=True)
    finally:
        assert temp.parent == temp_base and temp.name.startswith('xingshu-install-test-')
        shutil.rmtree(temp)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='0.1.0-test.20261021.3')
    parser.add_argument('--source-commit', required=True)
    parser.add_argument('--platform', choices=['all','STEAM_CN','TAPTAP','BILIBILI','WEGAME'], default='all')
    parser.add_argument('--deployment', type=Path, default=ROOT / 'XingShu-Dev/build/runtime-truth-fix/deployment.json')
    parser.add_argument('--android-bundle', type=Path, help='Verified Android companion directory from prepare_android_hub_bundle.py')
    parser.add_argument('--mod-library', type=Path, help='Local texture adaptation candidate; not automatically uploaded or relicensed')
    parser.add_argument('--local-build', action='store_true', help='Mark a local worktree candidate; SourceCommit is a baseline, not a published source tag')
    args = parser.parse_args()
    if not all(c.isascii() and (c.isalnum() or c in '.-') for c in args.version):
        raise SystemExit('Invalid version')
    if not re.fullmatch('[0-9a-f]{40}', args.source_commit):
        raise SystemExit('Expected the full verified source commit SHA')
    version_source = (ROOT / 'XingShu-Dev/src/XingShu.Runtime/XingShu/Runtime/XingShuRuntimeHost.cs').read_text('utf-8')
    runtime_version = re.search(r'#else\s+public const string RuntimeVersion = "([^"]+)"', version_source).group(1)
    hub_version = json.loads((ROOT / 'AstralParty-Hub/package.json').read_text('utf-8'))['version']
    publish = ROOT / '发布包'
    publish.mkdir(exist_ok=True)
    channel = {'all':'','STEAM_CN':'Steam_','TAPTAP':'TapTap_','BILIBILI':'Bilibili_','WEGAME':'WeGame_'}[args.platform]
    name = 'XingShu_' + channel + args.version
    destination = publish / name
    archive = publish / (name + '.zip')
    if destination.exists() or archive.exists():
        raise SystemExit('This release already exists; choose a new --version')
    stage = publish / ('.building-' + name + '-' + uuid.uuid4().hex)
    hub = ROOT / 'AstralParty-Hub/src-tauri/target/release/xingshu-hub.exe'
    if hub.stat().st_mtime < (ROOT / 'AstralParty-Hub/dist/index.html').stat().st_mtime:
        raise SystemExit('The current Hub production build has not finished')
    if any((ROOT / p).stat().st_mtime > hub.stat().st_mtime for p in (
        'AstralParty-Hub/src-tauri/build.rs', 'tools/HubUpdater.cs', 'tools/update-public-cert.txt')):
        raise SystemExit('Bundled updater changed after the Hub production build; rebuild first')
    for folder in ('AstralParty-Hub/src', 'AstralParty-Hub/src-tauri/src'):
        if any(p.is_file() and p.suffix in {'.rs', '.ts', '.tsx', '.json', '.css'} and
               p.stat().st_mtime > hub.stat().st_mtime for p in (ROOT / folder).rglob('*')):
            raise SystemExit('Hub sources changed after the production build; rebuild before packaging')
    stage.mkdir()
    (stage / 'Hub').mkdir()
    shutil.copy2(hub, stage / 'Hub/xingshu-hub.exe')
    profiles = json.loads((ROOT / 'AstralParty-Hub/src-tauri/src/xing_shu/platform/platform_profiles.json').read_text('utf-8'))
    deployment = json.loads(args.deployment.read_text('utf-8-sig'))
    release = {'FormatVersion': 1, 'Version': args.version, 'HubSha256': sha(hub),
               'SourceCommit': args.source_commit, 'RuntimeVersion': runtime_version, 'HubVersion': hub_version,
               'ExtensionManagementChannels': ['STEAM_CN', 'STEAM_GLOBAL', 'TAPTAP', 'BILIBILI', 'WEGAME'],
               'CharacterCatalogCount': 37, 'PreviewCharacterCount': 2, 'Platforms': []}
    update_build = json.loads((ROOT / 'AstralParty-Hub/src-tauri/src/update_build.json').read_text('utf8'))
    if args.version != update_build['version'] and not args.local_build:
        raise SystemExit('Published version must match the build identity embedded in Hub')
    release.update(HubBuildVersion=update_build['version'], HubBuildSequence=update_build['sequence'])
    if args.local_build:
        release['LocalWorktreeCandidate'] = True
    if args.mod_library:
        if not args.local_build:
            raise SystemExit('The current third-party adaptation library is a local candidate; use --local-build')
        release['ModLibrary'] = stage_mod_library(args.mod_library, stage / 'ModLibrary')
    if args.android_bundle:
        from prepare_android_hub_bundle import verify
        android = args.android_bundle.resolve()
        info = verify(android)
        shutil.copytree(android, stage / 'Android')
        release['Android'] = {'RuntimeVersion': info['runtimeVersion'], 'Manifest': 'Android/bridge.json',
                              'ManifestSha256': sha(stage / 'Android/bridge.json')}
    for profile in profiles:
        platform = profile['platformId']
        if platform not in ('STEAM_CN', 'TAPTAP', 'BILIBILI', 'WEGAME'):
            continue
        if args.platform != 'all' and platform != args.platform:
            continue
        prepared = next(p for p in deployment if p['platform'] == platform)
        source = Path(prepared['candidate'])
        assert sha(source) == prepared['candidateSha256']
        relative = f'Runtime/{platform}/__data'
        target = stage / relative
        target.parent.mkdir(parents=True)
        shutil.copy2(source, target)
        platform_release = {
            'Id': platform, 'Label': {'STEAM_CN': 'Steam 国服', 'TAPTAP': 'TapTap Windows 版', 'BILIBILI': '哔哩哔哩 Windows 版', 'WEGAME': 'WeGame Windows 版'}[platform],
            'ExeName': profile['processExecutable'], 'GameAssemblySha256': profile['gameRuntimeDllSha256'].lower(),
            'BundleRelativePath': profile['bundleRelativePath'], 'Payload': relative,
            'PayloadSha256': prepared['candidateSha256'],
            'AcceptedHashes': sorted({profile['containerSha256'].lower(), prepared['beforeSha256'].lower()}),
        }
        platform_release['AcceptedHashes'] = sorted(set(platform_release['AcceptedHashes']) |
            set(compatible_previous_payloads(publish, platform_release)))
        release['Platforms'].append(platform_release)
    manifest = stage / 'release.json'
    manifest.write_text(json.dumps(release, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    identity = stage / 'PackageIdentity.cs'
    identity.write_text('internal static class PackageIdentity { internal const string ManifestSha256 = "' + sha(manifest) + '"; }', encoding='utf-8')
    compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    subprocess.run([str(compiler), '/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/nowarn:0649',
                    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
                    '/reference:System.Web.Extensions.dll', '/out:' + str(stage / '星枢安装助手.exe'),
                    str(ROOT / 'tools/GroupTestInstaller.cs'), str(identity)], check=True)
    identity.unlink()
    channel_label = '、'.join(p['Label'] for p in release['Platforms'])
    android_note = '''
安卓手机安装已合并到电脑版 Hub：
进入“星枢包管理器 → 安卓手机安装 / 更新 → 连接手机”。USB 接线并在手机允许调试后点“检测手机”；也可展开无线连接，按手机的配对地址和连接地址分别操作。
选择手机后点“安装 / 更新星枢”，等待 SHA256 校验通过。可还原官方原版，也可点“安装手机助手 APK”，以后在手机上独立维护。
已包含配套 ADB 与手机文件，无需另找 adb.exe。请保留与 Hub 同级的 Android 文件夹；多台设备需明确选择目标，游戏运行时拒绝替换。
''' if args.android_bundle else ''
    local_note = '本包为本地工作区候选，尚未上传发布；SourceCommit 记录源码基线，未代表此候选的完整发布标签。\n' if args.local_build else ''
    mod_note = '''本地模组库包含本机下载原包的纹理适配候选，作者和原包名称保留在游戏模组页及 ModLibrary/适配说明.md。只支持已核验的 Steam 国服 3.3.0；TapTap 与安卓不可沿用。模型、动画和语音待适配部分不会安装。资源映射与文件事务已检查，游戏中显示仍待实测。第三方资源不随本项目代码许可重新授权；当前包未上传发布。\n''' if args.mod_library else ''
    (stage / '先读我.txt').write_text(f'''星枢 XingShu 群发测试版 {args.version}
{local_note}
适配：Windows 10/11 64 位，吉星派对 3.3.0 / 资源 026。
本下载项包含的运行时：{channel_label}。其他端请下载同一 Release 的对应 Assets。
已安装相同版本运行时会保持原样，无需重复写入；请保留原版助手和既有备份，使用对应安装版本还原。
Hub 扩展目录管理：Steam 国服、Steam 国际服、TapTap Windows、哔哩哔哩、WeGame；支持同渠道保存多个安装位置。Steam 国际服本版未附带经过验证的运行时安装文件。

安装（不需要 Git、Python、Node.js 或 Rust）
1. 将整个 ZIP 解压到固定文件夹。不要在压缩包里直接打开 exe，也不要单独移动助手。
2. 正常退出游戏。双击“星枢安装助手.exe”。
3. 选择平台，再选择实际游戏程序。Steam 国服通常位于游戏安装目录的 8vJXn6CN/AstralParty_CN.exe；TapTap、哔哩哔哩与 WeGame 请选择各自目录中的吉星派对.exe，不能选择渠道启动器。哔哩哔哩通常位于 AstralParty Game 子目录；三个同名游戏程序按 GameAssembly 与资源指纹区分。
4. 点“安装 / 更新插件”。助手会校验渠道和资源版本，备份安装前文件再替换。若资源不匹配，会停止安装；请先用官方平台校验/修复游戏，再更新并进入一次大厅，退出后重试。
5. 主端和观战端各安装一次。通过各渠道官方启动器正常启动游戏，不要从助手直接启动游戏 exe。
6. 点“启动星枢 Hub”，或直接打开 Hub/xingshu-hub.exe。Hub 首次启动需要系统 WebView2 Runtime；Windows 10/11 通常已有，没有时按系统提示安装。
7. 在 Hub 设置中选择自己的游戏目录；游戏账号页分别绑定主账号与观战账号，并保存对应平台/游戏 exe。不要填别人的账号 ID。打开游戏后以实际识别身份为准。
8. 观战端完成公告和登录、进入大厅后，主端创建一局 PVE。选择“我的观战账号”后跟随当前对局；“本机托管验证”仍使用这台电脑上的已登录官方客户端。

本版 Hub 更新
设置页提供“检查更新”：核验官方 Release 的签名清单，再下载、校验并更新重启 Hub。首次使用需手动安装支持此入口的版本；旧发布没有签名清单时会明确提示前往官方发布页。手机助手首页提供“检查助手更新”，APK 覆盖安装由系统确认。两者都不代替游戏内插件更新；请完成其他安装/还原任务后再更新软件。
角色中心展示 37 条官方角色配置，其中 2 条名称、技能和立绘尚未完整发布，单列为“官方预更新”，未提供的内容显示待更新，不算作未持有的已上线角色。
角色、皮肤、羁绊与档案采用立绘详情布局；卡牌提供完整竖版大图、效果详情及原生动态异画。特殊异化与场地效果归入地图与场地，筹码另有图鉴。
历史对战按账号保存，可点开单场阵容、结算数据与星枢评分。PVE 显示胜负，PVP 显示名次；结算金币保留展示且不参与评分，辅助转账 20 以上有额外贡献权重。没有返回的指标显示“—”。
手机、电脑插件、统计挂件及 Hub 历史详情均增加“移动步数”；局内显示已确认累计步数，结算及回放使用服务器 MovePoint 数据校准。旧历史缺少步数时显示“—”，重新读取可补齐可用的回放结算。
星枢包管理器只管理星枢的电脑/安卓安装、更新和还原；游戏模组统一管理外部 DLL、加速组件及资源替换，操作前务必选中正确渠道和目录。
系统诊断中的游戏组件检查按所选渠道和目录显示实际游戏进程、文件指纹及当前连接上报版本；磁盘安装状态与正在运行的版本分开显示。
独立“星枢运行时”“运行时插件”与“导入安装包”页面已合并。系统诊断内的游戏组件检查负责核验文件、进程和实时连接；游戏模组页提供已知 DLL 冲突指纹、待核验组件归档、本地模组库与安装记录。手动文件不假定已加载，不自动删除。支持星枢协调的原生加速 DLL 会保留。
完整解压后从 Hub 文件夹启动，可在星枢包管理器“打开安装助手”，自动带入选中的电脑渠道与目录；打开助手不会自动安装。
{android_note}
{mod_note}
游戏模组中的“安装模组包”仅用于作者提供 manifest.json 的 .xspkg / .zip，普通 RAR 不能直接安装。格式与渠道声明见“扩展包格式说明.md”；旧包未声明渠道时仅按 Steam 国服处理。安装、更新和卸载前请退出对应目录的游戏。

插件与测试
F1 / 悬浮球：功能中心。Ctrl+Shift+H：手牌面板。十倍思考时间与自动致谢请在功能设置中开启。
本包包含启动修复、头像旁手牌挂件跨局清理、Hub 等待页清理、观战结束退房逻辑、选人/回合/筹码的本地十倍计时和自动致谢修复。
运行时 {runtime_version}：粉黄功能中心、半透明框架、游戏字体与星形入口统一设计。手牌挂件采用交错叠放长卡，支持10张以上同排、悬停抬牌查看详情，默认整体60%，可在手牌监视页调节40%–120%，自动保存。挂件标题可拖动、收起、选择P1–P4，设置按钮直达调节页。
对局统计挂件与功能中心保留三排九项：击杀、死亡、输出、承伤、治疗、累计金币、转账、行动回合、移动步数；局内暂不展示结算金币。无数据保留“—”，结算评分按星枢规则计算。
地图上的击飞预测使用清晰分段颜色和底板；怪物技能冷却仅在地图显示，战斗中概率与名字横向显示。怪物索敌只按确认的前进方向逐格预测，排除来路；已修正箭头数据的读取，有明确路线时优先采用实际方向。仍仅作提示，不修改游戏 AI。
十倍思考时间使用真实时钟，避免被倍速加快；底部进度条按当前操作归属更新，避免旧计时和转发显示反复覆盖。原生加速滑条0.25–10倍、快捷按钮最高5倍；原生接口保留，组件需另行提供。游戏内加速可直接使用。
突击门、技能和指向牌选择时保留镜头移动及自定义缩放，拉远时名字和指向线缩小。不同技能和地图场景仍欢迎反馈。
已经实测：双端插件启动及身份识别、首局 PVE 四人观战镜像；主端不重启连续进入两局，挂件换为新局手牌。
请重点反馈：同房重开与换房时，观战端连续两局是否自动退房再跟房；自然结算弹窗是否自动退出；收卡/星币/治疗是否分别触发致谢；是否有旧牌残留、卡死、闪退。
连续观战、自然结束与三种致谢尚未穷尽全部场景；若遇异常请附复现步骤反馈。

当前边界
完整观战手牌需要已登录的官方观战客户端，本包没有免登录读牌和远程账号池。一局中持续订阅、结束退房；读一次退出后不会继续获得手牌变化。
十倍修改本地计时，服务端截止仍有效。本包不分发原生变速 version.dll：当前原型的上游授权未明确，项目限定其仅供本地验证；已有原生组件的用户可继续使用。
未附带账号登录数据、密码、本机 Hub 数据库或个人运行日志。已有 Hub 用户设置仍保存在系统用户数据目录，切换包不会自动重置它们。
此包为便携版本，没有数字签名；请核对发布者和 ZIP 的 SHA256，不需要关闭安全软件。

还原
正常退出对应渠道游戏，助手里选择相同平台和游戏，点“还原安装前版本”。备份保存在游戏目录 .xingshu-backup/{args.version}/，还原完成前请保留。
还原恢复的是“本次安装前版本”；之前已有插件时会恢复旧插件。完全恢复官方版可使用 Steam/TapTap 的游戏校验/修复。
若游戏又更新，助手会拒绝覆盖未知文件，请先使用官方平台修复。退出 Hub 并删除解压目录即可移除便携 Hub；这不会删除用户设置。

反馈时请说明
版本、Windows 版本、游戏渠道/资源版本、单端或双端、操作步骤、预期与实际结果、首次还是下一局、发生时间，以及问题截图。
日志可能包含账号和房间信息，请先隐藏个人信息；不要发密码、验证码或登录票据。
''', encoding='utf-8-sig')
    shutil.copy2(ROOT / 'AstralParty-Hub/docs/specs/extension_channels.md', stage / '扩展包格式说明.md')
    shutil.copy2(ROOT / 'LICENSE', stage / 'LICENSE.txt')
    (stage / '项目来源.txt').write_text('星枢 Hub 与游戏内运行时\nhttps://github.com/Breezecaresses/Xingshu-hub\n\n代码许可见 LICENSE.txt。角色、卡面、配置及游戏资源版权归吉星派对原权利人所有，不随本项目代码许可证重新授权。\n', encoding='utf-8-sig')
    verify_installer(stage, profiles, release, deployment)
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
    group_note = f'''星枢群发测试版 {args.version}
适配吉星派对3.3.0／资源026，本下载项包含{channel_label}运行时。

安装：完整解压，先看“先读我.txt”。退出游戏，用“星枢安装助手.exe”选择实际游戏程序安装；主端、观战端分别安装。支持备份还原及旧群测版升级。

更新：
• 修正怪物箭头读取；局内统计保留三排，移除结算金币、保留移动步数。
• 电脑版Hub合并手机USB/无线连接、安装更新、还原及助手APK下发，附配套ADB。
• 包管理器专管星枢安装；状态检查并入系统诊断；外部MOD统一放在游戏模组。
• 移除独立运行时、运行时插件和导入入口；模组页保留带清单的包安装。
• 增加DLL指纹与冲突检查、待核验文件副本归档；兼容的协调加速DLL保留。
• 本机下载MOD已整理为部件与互斥方案，模型/语音及游戏显示仍待适配或验证。

使用反馈：同房／换房连续两局换牌、观战退房再跟房、自然结算退出、收牌／星币／治疗致谢。
完整手牌需登录官方观战端；十倍计时受服务端限制。
反馈附版本、渠道、步骤、时间及截图；日志隐藏个人信息。
源码：https://github.com/Breezecaresses/Xingshu-hub
'''
    if len(group_note) > 600:
        raise RuntimeError(f'Release announcement exceeds 600 characters: {len(group_note)}')
    (publish / '发布说明.txt').write_text(group_note, encoding='utf-8-sig')
    print(f'READY: {archive}\nSIZE: {archive.stat().st_size / 1024 / 1024:.2f} MiB\nSHA256: {digest}', flush=True)


if __name__ == '__main__':
    main()
