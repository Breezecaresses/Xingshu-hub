"""Copy the active source into a separate Git checkout without nested repositories or game binaries."""
import argparse
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[1]
FOLDERS = [
    'APX-Dev/src/APX.hooks', 'APX-Dev/assets/vanilla-ui-r3',
    'APX-Dev/tools/MatchLifecycleTests',
    'APX-Dev/tools/RuntimeBootstrap',
    'AstralParty-Hub/src', 'AstralParty-Hub/src-tauri/src',
    'AstralParty-Hub/src-tauri/tests', 'AstralParty-Hub/src-tauri/capabilities',
    'AstralParty-Hub/src-tauri/icons', 'AstralParty-Hub/public',
]
FILES = [
    'CARD_ASSET_INVENTORY_V2.csv', 'card_metadata_official.json',
    'card_names_zh_cn_official.json', 'runtime_card_registry.generated.json',
    'HAND_TRUTH_SOURCE_ARCHITECTURE.md', 'OVERLAY_LIFECYCLE_RECONCILIATION.md',
    'AUTO_FOLLOW_PROTOCOL.md', 'AUTO_FOLLOW_STATE_MACHINE.md',
    'APX-Dev/tools/build.ps1', 'APX-Dev/tools/generate_platform_profiles_cs.py',
    'APX-Dev/tools/generate_canonical_card_registries.py',
    'AstralParty-Hub/package.json', 'AstralParty-Hub/package-lock.json',
    'AstralParty-Hub/index.html', 'AstralParty-Hub/vite.config.ts',
    'AstralParty-Hub/tsconfig.json', 'AstralParty-Hub/tsconfig.node.json',
    'AstralParty-Hub/src-tauri/Cargo.toml', 'AstralParty-Hub/src-tauri/Cargo.lock',
    'AstralParty-Hub/src-tauri/build.rs', 'AstralParty-Hub/src-tauri/tauri.conf.json',
    'AstralParty-Hub/docs/local-managed-watcher.md',
    'AstralParty-Hub/docs/specs/extension_channels.md',
    'AstralParty-Hub/docs/specs/package_manifest_v1_schema.json',
    'tools/export_game_catalog.py',
    'tools/patch_thinking_time.ps1', 'tools/verify_thinking_time.ps1',
    'tools/prepare_runtime_truth_fix.py', 'tools/install_runtime_truth_fix.ps1',
    'tools/patch_taptap_runtime.cs',
    'tools/check_runtime_compatibility.ps1',
    'tools/prepare_android_runtime.py', 'tools/android_runtime_device.py',
    'tools/check_android_runtime.ps1', 'ANDROID_COMPATIBILITY.md',
    'tools/install_hub_watch_join_fix.ps1', 'tools/export_source_repo.py',
    'tools/GroupTestInstaller.cs', 'tools/package_group_test.py',
]
SKIP_DIRS = {'.git', 'bin', 'obj', 'target', 'node_modules', '__pycache__', 'dist', 'release'}
SKIP_SUFFIXES = {'.dll', '.exe', '.pdb', '.bundle', '.7z', '.zip', '.rar', '.log', '.pyc', '.tsbuildinfo'}

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
    selected = {ROOT / item for item in FILES}
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
        if source.name.startswith('.env') or 'credentials' in source.name.lower():
            continue
        target = destination / relative
        if destination not in target.resolve().parents:
            raise SystemExit('Target escaped source checkout')
        target.parent.mkdir(parents=True, exist_ok=True)
        if source.suffix.lower() in {'.cs', '.rs', '.ts', '.tsx', '.json', '.css', '.ps1', '.py', '.md', '.toml', '.html'}:
            normalized = b'\n'.join(line.rstrip(b'\r') for line in source.read_bytes().split(b'\n'))
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

此目录是独立 Git 源码仓库，来自正在调试的工作区；原工作区及其 APX-Dev Git 历史保持原样。后续在原工作区修改时，运行 `python tools/export_source_repo.py <本仓库路径>` 同步源码，再审阅 Git 差异。

## 构建

Hub：进入 `AstralParty-Hub`，执行 `npm ci`、`npm run tauri -- build --no-bundle`。需要 Node.js、Rust、Windows C++ 构建工具与 WebView2。

Runtime：执行 `APX-Dev/tools/build.ps1 -Configuration Release`，需要 Visual Studio MSBuild、Python。从自己的合法游戏安装提取对应版本 `AstralParty.Runtime.dll`，放入项目文件默认的 `APX-Dev/references/updated_20261019_verified/`，或通过 `AstralPartyRuntimePath` 属性指定；其余引用位于 `APX-Dev/references/{unity,framework,thirdparty}`。图标位于 `APX-Dev/assets/vanilla-ui-r3`。Cecil 补丁工具另需 Mono.Cecil 与 Mono.Cecil.Rocks 放入 `APX-Dev/tools/lib`。本仓库不包含游戏程序集、游戏容器、账号凭据或运行日志。

部署脚本仍包含本机已验证的游戏路径，其他电脑使用前应修改并验证目标位置。跨平台启动按账号中心绑定和对应 exe 设置路由；Steam / TapTap 有本机测试环境，其他平台尚无实机验证。

## 当前功能与边界

跨局清理同时覆盖游戏挂件、手牌真值缓存与 Hub 等待页；结束时释放观战房间。观战端在每局期间持续订阅，退出后不再获得后续手牌变化。托管 Worker 的当前状态见 [本机托管说明](AstralParty-Hub/docs/local-managed-watcher.md)。

十倍思考时间修改选人及本地操作计时；不能据此承诺服务端强制截止也延长十倍。免登录全量手牌、远程独立账号池尚未实现。

自动化回归：`npm run test:resolver`、`npm run test:auto-follow`、`npm run test:runtime-truth-closure`；Rust `cargo test --lib local_watcher` 及 `cargo test --lib configured_platform_match_end_releases_only_the_spectator`。C# 独立生命周期测试位于 `APX-Dev/tools/MatchLifecycleTests`。
''')
    print(f'Exported {count} source files, {size / 1024 / 1024:.2f} MiB to {destination}')

if __name__ == '__main__':
    main()
