# 软件更新入口与验证记录

本轮待发布版本为 `0.1.0-test.20261023.4` / `2026102304`。新增电脑渠道、真机覆盖安装与更新入口实测见 [渠道与升级记录](CHANNEL_COMPATIBILITY_20261023.md)。以下 `.3` 内容是首次本地候选阶段的历史记录；线上升级结果另行追加，未记录的步骤不视为通过。

## 玩家操作

- 电脑版：Hub 设置 → 检查更新 → 下载更新 → 更新并重启 Hub。
- 手机版：安装助手首页 → 检查助手更新 → 下载新版 APK → 在系统页面确认覆盖安装。首次可能需允许“安装未知应用”，无需卸载旧助手。
- 默认包含测试版，可关闭以只检查标记为正式版的 Release。
- 更新助手会保留应用数据；游戏内插件仍需退出游戏，在包管理器或手机助手中另行安装/更新。手机写入游戏文件仍需要本机无线调试或已运行的 Shizuku 授权。
- 已发布的旧 Hub、旧 APK 没有此入口，需要先手动安装支持更新的新版本一次。

## 当前状态

- Hub 与 Android 助手构建身份：`0.1.0-test.20261023.3` / `2026102303`。
- 2026-10-23 核对当前官方 Release：`v0.1.0-test.20261022.2`，尚无 `xingshu-update.json`。检查时应提示“当前发布尚未提供签名更新清单”，不会把它误报为最新版本或执行旧包。
- 本轮只生成本地候选，没有上传、重写旧 Release 或推送 Git。
- 软件更新的下载、签名、文件事务已做隔离验证；实际线上版本升级、手机系统安装器覆盖确认尚需下一次签名发布后实测。

## 更新协议与边界

- 固定仓库 `Breezecaresses/Xingshu-hub`，公开 GitHub Releases API，无需用户令牌。只接受 GitHub 的 HTTPS API、Release 地址及固定资产 CDN 域名。
- 各 Release 的 `xingshu-update.json` 是 base64 原始 JSON 与 `SHA256withRSA` / PKCS#1 v1.5 签名的封装。两端内置相同证书公钥；私钥沿用本机已有的 APK 签名身份，位于用户 Signing 目录，不进入工作区、源码仓库或发布包。
- 签名内容含协议版本、仓库、tag、版本、单调递增 sequence、更新说明、电脑版 ZIP 和手机 APK 的文件名/大小/SHA256。客户端取所选发布通道中最大 sequence；不通过字符串排序，拒绝降级。
- 签名覆盖的大小和 SHA256 校验通过前，不解压或安装。Android 还核对包名、versionCode 与已安装助手的签名，再用只读 ContentProvider 将 APK 交给系统安装器。
- Hub 内嵌本地编译的更新工具，不从 PATH 查找或运行网上下载的更新脚本。下载与准备不退出 Hub；点击重启更新后，工具核对父进程及解压目录，在原 Hub 退出后才写入。另有同目录运行程序时拒绝覆盖。
- 便携包更新只写本包的 Hub、Runtime、Android 配套及固定根文档/助手，不写任何游戏安装目录。ZIP 路径逃逸、大小超限、大小写重复、链接、额外可执行文件、本地模组候选包均拒绝。
- 全量预检并备份到本包 `.xingshu-updates`，然后逐文件原子替换；普通写入失败时恢复已经替换的文件并校验。保留未受管理文件与系统用户目录下的数据库/设置。断电、强行终止更新进程等异常仍可能需要重新解压完整包；原文件备份保留用于恢复，不能宣称对所有系统故障自动恢复。
- Windows EXE 目前仍没有 Authenticode 签名；更新清单的 RSA 签名用于验证本项目更新来源，不等于微软 SmartScreen 信誉认证。

## 下一次发布步骤

1. 同时更新 `AstralParty-Hub/src-tauri/src/update_build.json` 和 `AndroidInstaller/app/build.gradle` 的版本、sequence/versionCode；sequence 必须大于所有已发布值。正式版/测试版由 GitHub Release 的 prerelease 标记控制。
2. 构建、签名 Android 助手，验证旧签名一致；准备 Android 配套。构建 Hub，然后用同版本打包电脑版 ZIP。带 `LocalWorktreeCandidate` 的包及未确认授权的第三方模组库不能用于在线更新。
3. 用 `tools/prepare_update_release.py --desktop <电脑版.zip> --android <英文名.apk> --notes <更新说明.txt> --out <目录>/xingshu-update.json` 生成清单。工具校验 APK 身份、版本与两个资产哈希后才签名，不上传。
4. 发布时将电脑版 ZIP、手机 APK 与签名清单放在同一个 Release 的 Assets；`tools/publish_release.py --update-manifest <清单路径>` 会在发布前重新验签、核对 tag 和待上传资产，拒绝不匹配。
5. 至少用旧版 Hub 和小米实机分别检查、下载并升级一次；确认用户设置/配对身份保留，且游戏插件版本没有被软件更新冒充。新助手内置插件需要单独操作安装。

## 验证记录

- 新增隔离测试 `python tools/test_app_updates.py`：8 项通过，覆盖签名篡改、仓库/tag/版本身份、ZIP 损坏、路径逃逸/重复/不支持文件、未发布候选拒绝、备份/保留无关文件、部分写入失败回滚、暂存文件被篡改及 GUI 无控制台父进程的 UTF-8 双向通信。
- `npm run test:all`：28/28 套件通过；`cargo test --lib`：164/164 通过；TypeScript/Vite 构建通过。
- 新 APK Release 构建和 lint 通过，v2/v3 签名通过，仍为旧证书 `8eff0ea3ea6153f8d3dfa3531b9c43e71e6f4b123ae32da27ec6383c84733c65`。
- 本轮没有在线手机或已发布的新签名更新资产，未将真机覆盖安装及正式线上升级标记为完成。
- 生产 Hub 构建完成；修复后的本地完整候选 `发布包/XingShu_0.1.0-test.20261023.3.zip`（237.24 MiB），SHA256 `3f390d6d8f4e965406404ad9a3de911c477bc67370950a1338352fe132b855b8`。含未实测第三方纹理库，仅供本地验证，不作为在线更新资产。
- 常用 Hub 配套 161 个文件已逐一核对，本次变更 8 个文件，原文件均备份至 `XingShu-Dev/build/hub-update-20261023/hub-before`；收据 `hub-installed.json`。新 APK SHA256 `150ab9ebd88d624ce5e40b60658d9fd266d2466b49b35cad6999a84e01826cea`，已合入电脑版 Android 配套。
- 本地更新工具实际访问官方 API 的返回已记录在 `XingShu-Dev/build/app-update-20261023/live-feed-check.json`，正确区分“无签名更新资产”和“已是最新版”。
- 首次实际按钮点验发现 GUI 父进程下 .NET 默认以 GBK 返回中文，Rust 的 UTF-8 JSON 解码失败；前端结构化错误还误显示为 `[object Object]`。已在工具入口固定双向 UTF-8，并提取结构化错误 message。首个 `.3` 中间构建已归档至 `XingShu-Dev/build/app-update-20261023/before-utf8-fix`，修复后重新生成同一未发布版本。
- 修复后重打包的 Steam 国服、TapTap 隔离安装/重复安装/还原/重装/旧版升级还原、未知版本和损坏文件拒绝均通过，完整包校验通过。
- 最终常用 Hub 配套 161 个文件逐一校验，本次替换 4 个文件并保留备份；收据 `XingShu-Dev/build/hub-update-utf8-20261023/hub-installed.json`。最终 Hub SHA256 `b728475f0755b289301a199e30ef2ef47b98e0afdf8d7b54910d820a25906cf1`。
- 最终版已重开并确认设置页显示 `.3` 软件更新入口。实际隐藏进程访问官方 API 的 UTF-8 返回已通过；最后一轮界面点击因窗口几何与用户输入干扰未取得明确返回，未将最终按钮结果标记为通过。
