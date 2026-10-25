# 非 Steam 原版立绘与卡牌核查（2026-10-25）

本文先记录后台资源核查与独立测试，后续 r8.5 人工实测及入口调整另列；不同阶段的验证范围不混用。

## r8.5 入口与行为（迁移前）

- 插件的“模组加载”页提供“原版立绘与卡牌（反和谐）”开关，不添加游戏官方设置按钮。
- 支持的渠道标识为 `TAPTAP`、`BILIBILI`、`WEGAME`、`ANDROID_CN`；Steam 和未知渠道保持游戏自身行为。
- 待官方版本初始化完成后保存当次画面状态，启用时选择客户端已有的原版资源；关闭时恢复该次保存状态。仅插件选择写入插件配置，不调用官方持久设置接口、不替换资源或解锁皮肤。
- 已打开页面通过官方 `UIManager.SwitchAdultMode` 刷新；页面正在关闭导致刷新失败时按受限间隔重试。选中的皮肤及卡牌异画不变，个别页面仍需重新打开验收。

## 资源核查

新增 `tools/check_original_artwork_assets.py`，只读取各渠道实际安装目录的 Addressables catalog 和本机 bundle 缓存。输出保存在 `XingShu-Dev/build/inspection/artwork-20261025/assets.json`，不会导出或打包游戏图片。

| 电脑渠道 | 配置中原版/SFW差异条目 | 原版地址及本地 bundle | SFW地址及本地 bundle | 解码抽样 |
| --- | ---: | ---: | ---: | --- |
| TapTap | 305 | 305 | 302 | 3 组卡牌、3 组立绘 |
| 哔哩哔哩 | 305 | 305 | 302 | 3 组卡牌、3 组立绘 |
| WeGame | 305 | 305 | 302 | 3 组卡牌、3 组立绘 |

全部 18 组抽样均实际读取到两套 `Texture2D`，每组原版与 SFW 的对象 SHA256 不同，证明所检地址不只是空声明。本次桌面目录没有发现“仅有 SFW、缺少原版”的上述配置条目，因而没有将三个桌面渠道判为缺少原版资源。

目录中缺少的三个 SFW 地址为 `UT_Hero_Bust_113_04_1_sfw`、`UT_Hero_Bust_117_04_0_sfw`、`UT_Hero_Bust_117_04_1_sfw`，其原版地址均存在。原版开关关闭后只恢复官方资源选择行为，不补造缺少的 SFW 资源。

**配置来源限制：**本机安装包随附的旧 catalog 所指 `GameData_CN` 配置 bundle 未留在缓存中，所以这次差异条目使用已解码官方参考 `XingShu-Dev/build/catalog/tables.json`，再逐条核对每个渠道的实际 catalog 和缓存。JSON 将来源明确标为 `shared_official_reference_channel_config_not_resolved`。参考配置 SHA256 为 `bfccaa2d099e72239fc69a5e402133ec5e4439b741a57326c6d69fad866b75ac`；该回退不能证明当前运行中的各渠道配置字节相同。

三份实际 catalog 的 SHA256：

- TapTap：`56ad214c8da8d47af6086c449c657ae41e2fcf620c1a70b4ff76bb7e9005267d`
- 哔哩哔哩：`13da5dfd4daac16032daf8c1709f7410a2aca16f76cdc17b33d6b86af0dee1f0`
- WeGame：`07a4e1169bb3f4a082dbc424d5b52d6dd85e4a6ac76b8ecb0f2592aef72cd782`

安卓已经核过程序集中的原版/SFW选择分支和开关行为，但本轮没有在线实机资源核查；不把桌面资源结果直接当作安卓通过。所有渠道的逐页视觉对照仍未完成，输出中的 `visualComparisonPassed` 保持 `false`。

## 官方逻辑边界

已核对各非 Steam 程序集：角色立绘、局内立绘、头像以及普通/异画卡牌的资源选择均引用现有 `GameSettings.angelMode` 分支。切换使用该既有入口，未增加外部素材加载，也不调用账号登录接口。

但不能将此描述为“账号侧完全无变化”：官方 `AccountLogic.SyncHarmonyStatus()` 在登录完成时会比较 `GameSettings.angelMode` / `HarmonyType` 与账号当前标记，必要时发送 `ClientHarmonyC2S` 画面偏好元数据。插件自身没有发送该请求或修改账号其他数据，但其改动的内存画面标记可能被后续官方登录流程上报。本开关的恢复保证针对本次进程保存的画面值和未改写的官方持久设置，不扩张为服务器偏好始终未变。

## 验证与后续

`OriginalArtworkTests` 的 **28 项全部通过**，覆盖四渠道默认选择、Steam/未知渠道隔离、初始化等待、关闭恢复、官方原本已是原版时的恢复、保存的关闭选择、UI未就绪与刷新失败重试。系统默认 .NET SDK 7 安装缺少导入 targets，故本轮使用 Visual Studio 自带 Roslyn 直接编译同一份 `Program.cs` 和 `OriginalArtworkService.cs` 到独立检查目录运行；没有构建整个 Runtime。

复现资源核查：

```powershell
python tools\check_original_artwork_assets.py --out XingShu-Dev\build\inspection\artwork-20261025\assets.json
```

尚需在各非 Steam 游戏实际打开同一角色、皮肤和卡牌，分别开关后核对画面、关闭后的恢复及重新登录行为；安卓还需核对设备 catalog / bundle。现阶段可进入本机统一候选验证，不能仅凭 `angelMode == false` 宣称全部画面恢复验收完成。

## r8.5 TapTap 人工实测与后续入口调整

- 用户在 TapTap 电脑版自行开关并重新打开同一画面，确认“能切换，关闭可恢复”。只读日志确认 22:11:42 加载 `1.0.0-game-ui-r8.5`，22:11:48 实际渠道标识为 `TAPTAP`。该结果按用户实际操作反馈记录，不冒充代理逐像素对照全部资源。
- 本次通过范围为 TapTap 的画面切换和当次关闭恢复；重新登录行为、其他两个电脑渠道和安卓实机仍未验收。
- 用户随后明确要求：将“反和谐”放到游戏原生“功能设置”中原先直播模式的位置，移除插件模组页的重复卡片。此新要求覆盖此前“不强加官方按钮”的入口选择。正在复用客户端已有控件，尚未安装或验收新的入口；不能把 r8.5 旧入口通过直接算作新入口通过。

2026-10-26 的 r8.6 已完成入口迁移与四电脑渠道候选安装；原生控件结构、开关方向、生命周期及当前实测状态见 [原生设置入口记录](NATIVE_ARTWORK_SETTINGS_20261026.md)。上述 r8.5 测试仅属于旧入口的历史证据。
