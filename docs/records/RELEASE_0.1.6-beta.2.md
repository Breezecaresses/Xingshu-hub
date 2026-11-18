# 星枢 0.1.6-beta.2 测试版

本次为预发布测试版，正式版仍为 `0.1.5`，代码许可保持 MIT。本版全面适配《吉星派对》`3.3.0 / 资源 160`（电脑四渠道与安卓国服），并重点修复切场黑屏、点赞与手牌 UI 层级遮挡及云端／人机（Bot）观战手牌同步问题。

## 版本信息与下载

- **适用游戏版本**：`3.3.0 / 资源 160`（Steam 国服、TapTap Windows、哔哩哔哩、WeGame、安卓国服）
- **版本号**：Hub 与手机助手 `0.1.6-beta.2`（更新序号 `2026111801`），电脑端插件 `1.0.0-game-ui-r8.33`，安卓端插件 `1.0.0-android-preview8.34`
- [电脑端 ZIP](https://github.com/Breezecaresses/AstralParty-XingShu/releases/download/v0.1.6-beta.2/XingShu_Desktop_0.1.6-beta.2.zip)：含 Hub、安装助手、四渠道插件与安卓配套连接组件。
- [安卓手机 ZIP](https://github.com/Breezecaresses/AstralParty-XingShu/releases/download/v0.1.6-beta.2/XingShu_Android_0.1.6-beta.2.zip)：含手机助手 APK、手动替换与官方原版还原文件。
- [直接安装安卓 APK](https://github.com/Breezecaresses/AstralParty-XingShu/releases/download/v0.1.6-beta.2/XingShu_Android_0.1.6-beta.2.apk)：与安卓 ZIP 内 APK 一致，可覆盖更新手机助手。

## 核心修复与改动

- **官方资源 160 全端适配**：更新电脑四渠道（Steam 国服／TapTap／哔哩哔哩／WeGame）与安卓国服的渠道配置、资源指纹、卡牌注册表及运行时清单。
- **场景切换／进入对局黑屏修复**：移除运行时中剩余的 `UIManager` 懒加载窗口属性访问，补全单例与当前场景守卫，避免在未打开目标窗口时意外实例化原生界面；为全量运行时方法建立 IL 静态回归门禁。
- **点赞图标大小调节与 UI 层级对齐**：
  - 手机端头像点赞按钮默认明显放大，保持原版蓝／彩图标；在「功能中心 → 自动化与社交 → 头像点赞」新增「点赞图标大小」（`60%–200%`，5% 一档）及「恢复默认」，调整即时保存并在关闭设置后生效，保留头像锚点与边界防越界限制。
  - 将头像旁手牌挂件挂载至 `UIBattleInfoPanel.com_AttrInfos` 底层，并将点赞与手牌挂件的战斗显隐及模态弹窗穿透逻辑统一对齐至怪物实时推演（`MonsterTargetRuntime`）与击飞测算（`MonsterKillProbability`），不再遮挡推演／击飞面板或浮于信息弹窗之上。
- **云端／人机（Bot）观战手牌同步修复**：
  - 修复非己方及 PVE 人机玩家在 `HandTruthSource` 与 `HandCardDisplayManager` 中优先读取云端／观战镜像，并在本地 `_HandCards` 与实时 `CardCount` 不一致时拒绝沿用首局过期快照，解决人机出牌后 HUD 仍停留初始手牌的问题。
  - 取消跨模式错误的 `RetiredMirrorEpoch` 拦截；修复 Hub `cloud_hand_sync.rs` 遇到房间名单外或自身条目时整包丢弃的问题（改为过滤跳过并保留有效队友手牌）。
  - 修复安卓云端手牌（`AndroidCloudHands`）在返回 `401` 吊销会话、切换未授权账号及 App 回调失败时的状态清理与提示覆盖问题，并补全手机端「退出同步」入口。
- **工具与部署兼容性修复**：
  - `android_runtime_device.py` 新增 `ro.serialno` 硬件序列号与同 IP 匹配，支持安卓无线调试端口变更后的继续安装与还原，同时保持目标版本与文件 SHA-256 严格校验。
  - `pc_resource_audit.py` 与 `install_checked_runtime.ps1` 修复无 `pwsh` 时的 Windows PowerShell 5.1 回退、UTF-8 清单读取与单元素数组兼容。

## 安装与更新说明

- **电脑端**：完整解压 ZIP 后运行「星枢安装助手.exe」，先正常退出游戏，再按渠道安装／更新对应插件，不要在压缩包内直接运行。
- **安卓端**：安装新版 APK 后需正常退出游戏，在手机助手内重新点击安装／更新游戏内插件再启动游戏；也可通过电脑端 Hub 的无线／USB ADB 一键更新。
- 在应用内开启「接收测试版更新」后可检查本版；未开启测试版的用户将继续接收正式版 `0.1.5`。

## 验证范围与待反馈项

- **已验证**：Steam 国服与安卓实机（`3.3.0 / 资源 160`）已实测资源 160 适配、切场黑屏修复、人机手牌镜像同步及推演／击飞层级对齐；自动化验证覆盖双端构建、`XingShu.Runtime.dll` 全量窗口观测 IL 与生命周期检查、点赞布局回归、对局生命周期与云端镜像单测、Hub 云端手牌同步单测、四渠道资源与安装校验及安卓 APK 签名／包一致性检查。
- **仍待广泛反馈**：更多手机机型与分辨率下的点赞滑块触摸表现、各渠道同房重开与换房连续观战、多人自然结算完整点赞流程，以及不同技能／地图场景下的 UI 叠层表现。反馈请注明渠道、应用与资源版本、复现步骤及截图，并隐藏个人账号信息。
