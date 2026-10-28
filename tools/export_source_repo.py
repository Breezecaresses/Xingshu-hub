"""Copy the active source into a separate Git checkout without nested repositories or game binaries."""
import argparse
from pathlib import Path
import posixpath
import re
import shutil
from project_paths import project_document

ROOT = Path(__file__).resolve().parents[1]
FOLDERS = [
    'AndroidInstaller',
    'XingShu-Dev/src/XingShu.Runtime', 'XingShu-Dev/assets/vanilla-ui-r3',
    'XingShu-Dev/tools/MatchLifecycleTests',
    'XingShu-Dev/tools/PlatformProfileConformanceRunner',
    'XingShu-Dev/tools/RuntimeBootstrap',
    'XingShu-Dev/tools/TargetingFixtures', 'XingShu-Dev/tools/OriginalArtworkTests',
    'AstralParty-Hub/src', 'AstralParty-Hub/src-tauri/src',
    'AstralParty-Hub/src-tauri/tests', 'AstralParty-Hub/src-tauri/capabilities',
    'AstralParty-Hub/src-tauri/icons', 'AstralParty-Hub/public',
    'AstralParty-Hub/native-windows',
]
FILES = [
    'CARD_ASSET_INVENTORY_V2.csv', 'card_metadata_official.json',
    'card_names_zh_cn_official.json', 'runtime_card_registry.generated.json',
    'HAND_TRUTH_SOURCE_ARCHITECTURE.md', 'OVERLAY_LIFECYCLE_RECONCILIATION.md', 'GAME_UI_R1.md', 'GAME_UI_R2.md', 'GAME_UI_R3.md', 'GAME_UI_R4.md', 'GAME_UI_R7.md', 'R7_VERIFICATION_20261022.md', 'R7_LIVE_VERIFICATION_20261022.md', 'MONSTER_TARGETING.md',
    'AUTO_FOLLOW_PROTOCOL.md', 'AUTO_FOLLOW_STATE_MACHINE.md',
    'XingShu-Dev/tools/build.ps1', 'XingShu-Dev/tools/generate_platform_profiles_cs.py',
    'XingShu-Dev/tools/run_csharp_platform_conformance.py',
    'XingShu-Dev/tools/TestMonsterTargetRunner.cs', 'XingShu-Dev/tools/TestMonsterTargetContextRunner.cs',
    'XingShu-Dev/tools/test_monster_targeting.ps1',
    'XingShu-Dev/tools/test_decision_stats.ps1', 'XingShu-Dev/tools/test_match_lifecycle.ps1', 'XingShu-Dev/tools/test_preference_migration.ps1', 'XingShu-Dev/tools/test_native_ipc_harness.py',
    'XingShu-Dev/tools/generate_canonical_card_registries.py',
    'XingShu-Dev/tools/build_game_ui_skin.py', 'XingShu-Dev/tools/generate_ui_payloads.py',
    'AstralParty-Hub/package.json', 'AstralParty-Hub/package-lock.json',
    'AstralParty-Hub/index.html', 'AstralParty-Hub/vite.config.ts',
    'AstralParty-Hub/tsconfig.json', 'AstralParty-Hub/tsconfig.node.json',
    'AstralParty-Hub/src-tauri/Cargo.toml', 'AstralParty-Hub/src-tauri/Cargo.lock',
    'AstralParty-Hub/src-tauri/build.rs', 'AstralParty-Hub/src-tauri/tauri.conf.json',
    'AstralParty-Hub/docs/local-managed-watcher.md',
    'AstralParty-Hub/docs/specs/extension_channels.md',
    'AstralParty-Hub/docs/specs/package_manifest_v1_schema.json',
    'tools/export_game_catalog.py',
    'tools/patch_thinking_time.ps1', 'tools/verify_thinking_time.ps1', 'docs/records/RELEASE_PACKAGING_20261024.md', 'docs/records/RELEASE_20261024.md',
    'tools/patch_thinking_clock.cs', 'tools/install_game_ui_r2.ps1', 'tools/install_game_ui_candidate.ps1',
    'tools/prepare_runtime_truth_fix.py', 'tools/install_runtime_truth_fix.ps1',
    'tools/patch_taptap_runtime.cs', 'tools/legacy_runtime_names.json',
    'tools/check_runtime_compatibility.ps1',
    'tools/extract_target_map_graphs.py', 'tools/inspect_target_maps.py',
    'docs/records/MATCH_SYNC_TARGETING_20261024.md',
    'docs/records/STATS_HEALING_20261025.md', 'docs/records/HEALING_SOURCE_RESEARCH_20261025.md',
    'docs/records/MONSTER_TARGETING_20261025.md', 'docs/records/ORIGINAL_ARTWORK_20261025.md',
    'docs/records/NATIVE_ARTWORK_SETTINGS_20261026.md', 'docs/records/RELEASE_20261026.md',
    'tools/check_original_artwork_assets.py', 'XingShu-Dev/tools/build_card_asset_inventory_v2.py',
    'tools/audit_preview_characters.py',
    'tools/audit_monster_cooldowns.py', 'tools/test_audit_monster_cooldowns.py',
    'docs/records/PREVIEW_CHARACTERS_307_308_20261028.md', 'docs/records/MONSTER_COOLDOWN_20261028.md',
    'docs/records/ALL_MONSTER_COOLDOWN_20261029.md',
    'docs/records/MONSTER_COOLDOWN_EVENTS_20261029.md',
    'XingShu-Dev/tools/TestMonsterCooldownRunner.cs', 'XingShu-Dev/tools/test_monster_cooldown.ps1',
    'tools/prepare_android_runtime.py', 'tools/android_runtime_device.py',
    'tools/check_android_runtime.ps1', 'ANDROID_COMPATIBILITY.md',
    'tools/install_hub_watch_join_fix.ps1', 'tools/export_source_repo.py', 'tools/project_paths.py', 'tools/test_source_export.py', 'docs/records/MOBILE_UI_20261024.md',
    'tools/GroupTestInstaller.cs', 'tools/package_group_test.py', 'tools/package_android_preview.py', 'tools/publish_release.py', 'GROUP_DISTRIBUTION_20261021_2.md', 'GROUP_DISTRIBUTION_20261021_3.md',
    'tools/AndroidPreviewInstaller.cs', 'XingShu-Dev/tools/test_preference_migration.ps1',
    'tools/build_android_installer.py', 'ANDROID_PHONE_INSTALL.md', 'GROUP_DISTRIBUTION_20261022_2.md',
    'tools/android_payload.py', 'tools/prepare_android_hub_bundle.py', 'tools/test_android_desktop_installer.py', 'HUB_ANDROID_INSTALL_20261023.md',
    'tools/prepare_mod_library.py', 'tools/test_mod_library_package.py', 'MOD_MANAGEMENT_20261023.md',
    'tools/HubUpdater.cs', 'tools/SignUpdate.java', 'tools/update-public-cert.txt',
    'tools/prepare_update_release.py', 'tools/test_app_updates.py', 'tools/test_release_packages.py', 'APP_UPDATES_20261023.md', 'CHANNEL_COMPATIBILITY_20261023.md',
    'XingShu-Dev/tools/test_assist_ball.ps1', 'XingShu-Dev/tools/test_mobile_slider.ps1', 'XingShu-Dev/tools/test_mobile_widgets.ps1',
    'NATIVE_WINDOWS.md', 'tools/build_native_hub.py', 'tools/test_native_core.py',
    'tools/migrate_native_host.py', 'tools/export_map_guides.py', 'tools/catalog_schema.json', 'docs/guides/RELEASE_PACKAGING.md',
]
SKIP_DIRS = {'.git', '.gradle', 'build', 'bin', 'obj', 'target', 'node_modules', '__pycache__', 'dist', 'release'}
SKIP_SUFFIXES = {'.dll', '.exe', '.pdb', '.bundle', '.7z', '.zip', '.rar', '.log', '.pyc', '.tsbuildinfo'}

# Working notes stay beside the local build scripts. The public source tree has
# one documentation entry point instead of accumulating dated files at its root.
GUIDES = {'ANDROID_COMPATIBILITY.md', 'ANDROID_PHONE_INSTALL.md',
          'HAND_TRUTH_SOURCE_ARCHITECTURE.md', 'OVERLAY_LIFECYCLE_RECONCILIATION.md',
          'AUTO_FOLLOW_PROTOCOL.md', 'AUTO_FOLLOW_STATE_MACHINE.md',
          'MONSTER_TARGETING.md', 'NATIVE_WINDOWS.md'}
DOC_PATHS = {name: 'docs/' + ('guides/' if name in GUIDES else 'records/') + name
             for name in FILES if name.endswith('.md') and '/' not in name}

def normalized_bytes(data):
    return b'\n'.join(line.rstrip(b'\r') for line in data.split(b'\n'))

def rewrite_doc_links(content, old_path, new_path):
    def replace(match):
        target = match.group(2)
        if re.match(r'^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|/|#)', target):
            return match.group(0)
        path, separator, fragment = target.partition('#')
        resolved = posixpath.normpath(posixpath.join(posixpath.dirname(old_path), path))
        mapped = DOC_PATHS.get(resolved, resolved)
        relative = posixpath.relpath(mapped, posixpath.dirname(new_path) or '.')
        return match.group(1) + relative + (separator + fragment if separator else '') + match.group(3)
    return re.sub(r'(!?\[[^\]]*\]\()([^\s)]+)(\))', replace, content)

def migrate_root_documents(destination):
    # Check all old files before removing any. Never discard checkout-only edits.
    stale = []
    for name in DOC_PATHS:
        old = destination / name
        if old.exists():
            if normalized_bytes(old.read_bytes()) != normalized_bytes(project_document(ROOT, name).read_bytes()):
                raise SystemExit(f'Root document has checkout-only changes; merge it first: {old}')
            stale.append(old)
    for old in stale:
        old.unlink()
    for name in ('README.md', 'README_CN.md'):
        path = destination / name
        if path.exists():
            before = normalized_bytes(path.read_bytes()).decode('utf-8')
            after = rewrite_doc_links(before, name, name)
            if 'docs/README.md' not in after:
                heading, separator, body = after.partition('\n')
                after = heading + '\n\n[项目文档与验证记录](docs/README.md)\n\n' + body.lstrip('\n')
            if after != before:
                path.write_bytes(after.encode('utf-8'))
    index = '# 项目文档\n\n使用与架构说明集中在 `guides/`，历史更新和验证记录集中在 `records/`。\n\n'
    for directory, heading in (('guides', '使用与架构'), ('records', '更新与验证记录')):
        index += '## ' + heading + '\n\n'
        entries = dict(DOC_PATHS)
        entries.update({Path(name).name: name for name in FILES if name.startswith('docs/') and name.endswith('.md')})
        for name, mapped in sorted(entries.items()):
            if mapped.startswith('docs/' + directory + '/'):
                index += f'- [{name}]({directory}/{name})\n'
        index += '\n'
    (destination / 'docs/README.md').write_bytes((index.rstrip() + '\n').encode('utf-8'))

def initialize_file(path, content):
    # Published documentation and ignore rules are maintained in the checkout.
    # Seed them for a new export without overwriting later user edits.
    if not path.exists():
        path.write_text(content, encoding='utf-8')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    destination = args.destination.resolve()
    if destination == ROOT or ROOT in destination.parents:
        raise SystemExit('Use a separate source checkout outside the active workspace')
    destination.mkdir(parents=True, exist_ok=True)
    if any(destination.iterdir()) and not (destination / '.xingshu-source-export').exists():
        raise SystemExit('Refusing to overwrite an unrelated directory')
    selected = {project_document(ROOT, item) if item in DOC_PATHS else ROOT / item for item in FILES}
    for folder in FOLDERS:
        if (ROOT / folder).exists():
            selected.update(p for p in (ROOT / folder).rglob('*') if p.is_file())
    count = size = 0
    for source in sorted(selected):
        if not source.is_file():
            raise SystemExit(f'Missing source file: {source.relative_to(ROOT)}')
        relative = source.relative_to(ROOT)
        if any(part in SKIP_DIRS for part in relative.parts) or source.suffix.lower() in SKIP_SUFFIXES:
            continue
        # Hub is a desktop app; the icon CLI also emits unused mobile launchers.
        if relative.parts[:3] == ('AstralParty-Hub', 'src-tauri', 'icons') and len(relative.parts) > 4 and relative.parts[3] in {'android', 'ios'}:
            continue
        if source.name.startswith('.env') or 'credentials' in source.name.lower() or source.name == 'local.properties' or source.suffix.lower() in {'.jks', '.keystore', '.apk'}:
            continue
        target_relative = DOC_PATHS.get(relative.as_posix(), relative.as_posix())
        target = destination / target_relative
        if destination not in target.resolve().parents:
            raise SystemExit('Target escaped source checkout')
        target.parent.mkdir(parents=True, exist_ok=True)
        if source.suffix.lower() in {'.cs', '.rs', '.ts', '.tsx', '.json', '.css', '.ps1', '.py', '.md', '.toml', '.html', '.lock', '.java', '.gradle', '.xml', '.aidl', '.txt', '.c', '.cpp', '.h', '.xaml', '.csproj', '.manifest'}:
            normalized = normalized_bytes(source.read_bytes())
            if source.suffix.lower() == '.md':
                normalized = rewrite_doc_links(normalized.decode('utf-8'), relative.as_posix(), target_relative).encode('utf-8')
            existing = target.read_bytes() if target.exists() else b''
            previous = b'\n'.join(line.rstrip(b'\r') for line in existing.split(b'\n'))
            if normalized != previous:
                # Avoid newline-only changes; preserve the checkout's existing line style.
                ending = b'\r\n' if b'\r\n' in existing else b'\n'
                target.write_bytes(normalized.replace(b'\n', ending))
        else:
            shutil.copy2(source, target)
        count += 1
        size += source.stat().st_size
    (destination / '.xingshu-source-export').write_text('Active source export; generated files can be refreshed from tools/export_source_repo.py\n', encoding='utf-8')
    initialize_file(destination / '.gitignore', '''**/node_modules/
**/target/
**/bin/
**/obj/
**/build/
**/dist/
**/release/
**/references/
**/__pycache__/
.env*
*.dll
*.exe
*.pdb
*.bundle
*.log
*.pyc
*.tsbuildinfo
*.7z
*.rar
*.zip
''')
    initialize_file(destination / 'README.md', '''# 星枢 XingShu

星枢 Hub（React / Tauri / Rust）与游戏 Runtime（C#）的当前源码。

此目录是独立 Git 源码仓库，来自正在调试的工作区；原工作区及其 XingShu-Dev Git 历史保持原样。后续在原工作区修改时，运行 `python tools/export_source_repo.py <本仓库路径>` 同步源码，再审阅 Git 差异。

## 构建

Hub：进入 `AstralParty-Hub`，执行 `npm ci`、`npm run tauri -- build --no-bundle`。需要 Node.js、Rust、Windows C++ 构建工具与 WebView2。

Runtime：执行 `XingShu-Dev/tools/build.ps1 -Configuration Release`，需要 Visual Studio MSBuild、Python。从自己的合法游戏安装提取对应版本 `AstralParty.Runtime.dll`，放入项目文件默认的 `XingShu-Dev/references/updated_20261019_verified/`，或通过 `AstralPartyRuntimePath` 属性指定；其余引用位于 `XingShu-Dev/references/{unity,framework,thirdparty}`。图标位于 `XingShu-Dev/assets/vanilla-ui-r3`。Cecil 补丁工具另需 Mono.Cecil 与 Mono.Cecil.Rocks 放入 `XingShu-Dev/tools/lib`。本仓库不包含游戏程序集、游戏容器、账号凭据或运行日志。

部署脚本仍包含本机已验证的游戏路径，其他电脑使用前应修改并验证目标位置。跨平台启动按账号中心绑定和对应 exe 设置路由；Steam / TapTap 有本机测试环境，其他平台尚无实机验证。

## 当前功能与边界

跨局清理同时覆盖游戏挂件、手牌真值缓存与 Hub 等待页；结束时释放观战房间。观战端在每局期间持续订阅，退出后不再获得后续手牌变化。托管 Worker 的当前状态见 [本机托管说明](AstralParty-Hub/docs/local-managed-watcher.md)。

十倍思考时间修改选人及本地操作计时；不能据此承诺服务端强制截止也延长十倍。免登录全量手牌、远程独立账号池尚未实现。

自动化回归：`npm run test:resolver`、`npm run test:auto-follow`、`npm run test:runtime-truth-closure`；Rust `cargo test --lib local_watcher` 及 `cargo test --lib configured_platform_match_end_releases_only_the_spectator`。C# 独立生命周期测试位于 `XingShu-Dev/tools/MatchLifecycleTests`。
''')
    migrate_root_documents(destination)
    print(f'Exported {count} source files, {size / 1024 / 1024:.2f} MiB to {destination}')

if __name__ == '__main__':
    main()
