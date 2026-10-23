# 星枢 Hub：Windows 原生迁移

版本：`0.2.0-native1`。本版为独立候选版，未替换现有 Hub，也未作为正式版本上传。

2026-10-22：按用户决定暂停原生迁移，继续使用现有 React / Tauri Hub 与 R7 游戏插件。原生源码、独立发布目录和 ZIP 均留存；下文为候选版的完成情况记录，不作为当前默认启动或发布方案。旧 Hub 入口为 `AstralParty-Hub/release/XingShu/Hub/xingshu-hub.exe`。本次验证见 [R7 验证记录](R7_VERIFICATION_20261022.md)。

## 界面和核心

界面使用 C# / WPF / .NET 10，页面、弹窗、文件选择器、表格、菜单及滚动控件均由 WPF 绘制。没有 WebView、HTML 页面、React 或前端开发服务器。发布目录自带 .NET 运行时，用户无需另装 SDK。

`xingshu-hub.exe` 启动自己持有的 `Core/xingshu-core.exe`，通过标准输入输出上的 JSON-lines 管道通信。日志写入标准错误，避免破坏消息边界。Rust 核心沿用现有数据、身份校验、游戏通信和安装引擎；`native-backend` 构建不会链接 Tauri / Wry / WebView。旧 Tauri 构建由默认的 `web-ui` feature 保留。

关闭原生界面只结束它自己启动的核心。原生版不会退出其他 Hub 或游戏。若旧 Hub 正在使用游戏通信端口，原生版会显示提示，图鉴和本地记录仍可查看。

## 已迁入的界面

- 18 个页面：总览、游戏账号、玩家资料、角色、卡牌、筹码、地图、对局、运行时、观战、游戏通信、插件、模组、导入、事务、备份、诊断、设置。
- 长卡牌、异画静态卡面、收藏、检索、前后切换；本地 MP4 使用系统媒体组件播放。
- 角色基础及合作技能、皮肤、羁绊奖励、突破材料、合作成长、潜能说明与静态表情。只有已同步的数据才显示账号进度。
- 对局按账号保存，详情包括移动步数；评分沿用原 Hub 规则。缺失统计显示 `—`，不补零或生成评分。
- 独立观战账号绑定与真实观战码请求；自动跟随会核对渠道、玩家 ID、连接身份和房间，拦截过期快照。失败和超时等待手动重试。不会自动启动游戏或切换账号。
- 插件和模组按安装位置检测；ZIP 使用已有备份、校验与回滚引擎。XSPKG 目前可检查信息，尚不能直接安装。
- 原生按钮焦点及按下反馈、深色下拉菜单和滚动条、80%–150% 界面缩放、高 DPI 与窗口尺寸恢复。

## 地图攻略

`src/shared/mapGuides.generated.json` 是供原生页读取的结构化资料，包含 19 张可展示地图、35 组难度配置、53 种独立任务（按难度共 176 条）。合作地图分别读取各自的怪物池、进度事件、任务和奖励，不复制森秋的路线。

森秋幻境增加独立的“实战速查”，根据用户提供的 `3.3.0 / 2026-10-16` 攻略图重新组织为行动顺序、进度刷怪、建筑、召唤及任务卡片。布局没有照抄原图。参考图的 49 / 44 / 42 / 35 尚未校准为游戏格子 ID，仅保留文字提示，不在真实地图上伪造落点。其他地图目前提供配置机制和预览，实战路线仍需逐图核对。

参考图中猫头鹰任务的额外奖励写作随机御灵卡，当前原始配置写作随机植物建筑卡；界面保留来源差异。进度是团队进度，不是轮数。参考图的概率和路线尚未经独立实战复核。

配置 SHA256：`674e027707b5a5890487008d1014efae11b03242213ca67d064ddad46d4c00da`。导出器拒绝与图鉴哈希不一致的配置文件，未混入哈希不同的安卓配置。游戏图像和文本仍归各自权利人所有。

## 使用和数据

完整解压原生 ZIP，保留 `Core` 和 `Assets`，运行 `xingshu-hub.exe`。需要连接游戏时，先正常退出旧 Hub，再打开原生版。只浏览本地资料可运行 `xingshu-hub.exe --offline`。

- 游戏数据库沿用 `%APPDATA%\com.xingshu.hub\xingshu_hub.db`。
- 原生偏好独立保存于 `%APPDATA%\com.xingshu.hub.native\settings.json`。
- 首次启动需在游戏账号页明确绑定主账号和独立观战账号；不会沿用历史开发者 ID 或把观战账号自动当成主账号。
- 官方客户端负责登录，原生版不保存游戏密码。手牌仍依赖已登录的官方观战客户端。
- 本 ZIP 仅包含原生 Hub，不重新安装电脑游戏插件或安卓文件。

## 构建与离线验证

需要 Windows x64、.NET 10 SDK、Rust / MSVC 工具链、Python 与 Pillow。源码已包含生成后的图鉴、地图资料及可用资源，不需要重跑游戏提取器才能构建。

```powershell
python tools/build_native_hub.py --self-contained --zip
python tools/test_native_core.py

# 不显示窗口、不接入游戏，仅输出离屏布局和隔离测试数据库：
XingShu-Dev/build/native-windows/app/xingshu-hub.exe --self-test <测试输出目录>

cd AstralParty-Hub/src-tauri
cargo test --no-default-features --features native-backend --lib game_library
cargo test --no-default-features --features native-backend --lib configured_platform_match_end_releases_only_the_spectator
cargo check --lib
```

`tools/export_map_guides.py --config-bundle <原始配置容器>` 用于更新攻略配置，额外依赖 UnityPy。它只读取经过哈希核对的任务、怪物、事件和技能表，不从未标版本的旧缓存补字段；生成结果应与修改前后配置哈希一起审阅，不分发游戏程序集或原始容器。

本轮离线验证覆盖全部页面和图鉴详情布局、地图任务对应关系、真实评分示例、缺失数据、账号身份、自动跟随及旧快照隔离；核心 IPC 验证禁止演示入口、损坏请求恢复及无效安装拒绝。测试不启动可见窗口、不操作鼠标键盘、不接入正在运行的游戏。

2026-10-22 本地结果：原生界面 83 项、核心 IPC 56 项、共享数据层 7 项、结束对局仅释放对应观战端 1 项全部通过。旧 `web-ui` 后端 `cargo check --lib` 通过。`cargo tree --no-default-features --features native-backend` 不包含 Tauri / Wry / WebView 依赖。离屏输出和隔离测试数据库只保留在本地构建目录，不附在分发 ZIP 中。

## 尚未完成的迁移和实测

- Spine 骨骼动画与 WebM 原生播放未迁入，保留静态卡面；MP4 解码仍取决于 Windows 媒体组件。
- 旧版 LocalManaged 托管 Worker 的完整管理界面尚未迁入。
- 新原生版尚未执行真实在线观战、连续换局和真实安装/还原的端到端验收。共享后端单元测试和离线 IPC 检查不能替代这些实测。
- 其他地图的路线、落点和完整攻略需要逐图核对，当前只把有依据的配置资料放入界面。
- 旧程序与原生候选都保留；本轮没有覆盖正在使用的 Hub、重启游戏或发布 Release。
