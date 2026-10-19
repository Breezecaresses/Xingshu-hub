# 星枢 XingShu

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
