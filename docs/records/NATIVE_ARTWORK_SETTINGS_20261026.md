# 原生功能设置中的反和谐入口（2026-10-26，r8.6 候选）

## 用户要求与范围

用户要求把“反和谐”放回游戏原生“功能设置”中原先直播模式的位置，移除星枢插件模组页的重复卡片。该要求覆盖此前保留在插件页的入口选择。

非 Steam 渠道复用游戏已经随包提供的 FairyGUI 控件；Steam 保持官方直播模式入口及原有行为。原画选择仍由 `OriginalArtworkService` 统一管理，不创建另一套配置或持续覆盖逻辑。

用户此前已在 TapTap r8.5 的旧插件入口实际操作，并反馈“能切换，关闭可恢复”。只读日志确认该次为 r8.5 / TAPTAP。这项通过不替代 r8.6 新原生入口的显示、点击及重新打开验收。

## 官方控件与显示 gear

结构为：

`UI.SettingWindow.contentPane → UI.UISettingWindow.com_LiveMode → UI.UISetting_Com_LiveMode.com_AdultSwither`

`com_AdultSwither` 是 `UISetting_Com_Toggle`，继承 `FairyGUI.GButton`。可直接使用继承的 `title`、`selected`、`onChanged`，不需要额外创建标题、按钮或白底。

直接读取 TapTap 当前 catalog 指向的实际 `Setting_fui`，解析其 FairyGUI v6 二进制结构，确认 `Setting_Com_LiveMode` 中：

| 项目 | 已确认内容 |
| --- | --- |
| 控制器 | `isAuditMode`，controller index 0 |
| 隐藏页 | selectedIndex 0，page ID `0`，名称“审核” |
| 显示页 | selectedIndex 1，page ID `1`，名称“其他” |
| 子项 2 | 白底组件 `n41`，资源 ID `o1n89`，位置 `(902,55)` |
| 子项 3 | `com_AdultSwither`，位置 `(1015,98)` |
| 子项 4 | `GGroup n26`，位置 `(902,55)`；其 GearDisplay（gear 0）绑定 controller 0，显示页列表为 `['1']` |

因此设置 `isAuditMode.selectedIndex = 1` 使用的是原生分组显示状态；仅给按钮写 `visible = true` 不能可靠越过分组的 display gear。

官方 `LiveMode_Refresh()` 会重写按钮状态：原生 `selected = GameSettings.angelMode`，即原直播模式开启对应 SFW。非 Steam 分支还会把审核控制器设为隐藏页。新入口文案为“反和谐”，所以必须按 `OriginalArtworkService.Enabled` 同步按钮：开启对应原版资源，不能把原生布尔方向原样保留。

## 事件所有权与生命周期

- 官方 `SettingWindow.OnShown()` 调用 `LiveMode_AddEvent()`，在该按钮的 `onChanged` 添加 `OnAdultSwitch(EventContext)`；`OnHide()` 通过 `LiveMode_RemoveEvent()` 移除它。
- 原官方回调会写 `GameSettings.UpdateAdultModeState`，与插件“开启为原版”的方向不同。新绑定通过已确认的私有方法 `MethodInfo` 和 `Delegate.CreateDelegate(EventCallback1, window, method)` 构造相等的原生 delegate，仅移除这一个 handler，再幂等添加自有回调；不对按钮执行整组 `Set/Clear`，保留其他监听。
- 已核对官方 IL：`GButton.get_onChanged` 构造 `new EventListener(this, "onChanged")`；`EventBridge.CallInternal` 在调用监听前写入 `context.sender = owner`。因此 `ReferenceEquals(context.sender, _toggle)` 的来源校验正确，不会把正常按钮点击拒绝为其他对象。
- `EventBridge.Remove` 使用 `System.Delegate.Remove`，按目标对象与方法匹配；重新创建的相等 delegate 可以精准移除原官方 handler。
- 程序赋值 `GButton.selected` 本身不分发 `onChanged`；实现仍保留同步 guard，防止以后边界变化导致递归。
- 宿主在 Unity 主线程受限轮询 `GRoot`，找到显示中的 `SettingWindow` 后同步按钮和原生控制器；官方重新显示或切页后重新处理其回调与刷新结果。窗口移除、销毁或宿主关闭时只移除自有监听，并按是否仍显示决定是否恢复原生 handler；恢复原文案及控制器状态。
- 不给整张官方设置页添加输入保护，不额外截断游戏原生触控事件冒泡。

## r8.6 构建与人工验收

- r8.6 PC 完整运行时编译成功。
- 四个电脑渠道均通过 595 个游戏成员引用、9 个既有反射字段及新增原生 `OnAdultSwitch` 回调方法的兼容检查。
- 四渠道方法体审计通过：相对各自保留已有观察器的 bootstrap 输入，Steam 67,038、TapTap/哔哩各 67,024、WeGame 67,029 个方法体不变，0 个变化。新入口没有额外改写官方设置方法。
- 安卓完整运行时和资源容器构建成功，595 个成员引用、9 个反射字段及原生回调方法检查通过；66973 个官方方法体不变、16 个既有预期变化，其余 18 个 TextAsset 不变，没有 Windows 原生库，桌面网桥仍暂停。
- 只读进程检查确认游戏关闭后，安全脚本将 r8.6 安装到四个电脑渠道，逐渠道保留 r8.5 备份并核对安装后 SHA256。实际替换前脚本再次检查进程和文件身份；没有关闭、启动或操作游戏窗口。
- 用户在 TapTap 电脑版实际操作确认：原直播模式位置出现“反和谐”、开关正常切换画面、切页或关闭重开设置后状态保持，星枢模组页重复卡片已消失。此范围作为 r8.6 新入口的人工反馈通过，不借用 r8.5 旧入口结果。
- 哔哩哔哩、WeGame、安卓的实际入口与画面切换仍未验收；所有渠道的重新登录行为也未完成实测。安卓没有安装到设备；未发布上传。
- 本文结构与事件结论来自只读检查；候选安装记录位于 `XingShu-Dev/build/native-artwork-20261026/desktop/installed.json`，总体状态位于同目录上级 `candidate.json`。账号偏好上报的限制仍按原版画面专项记录说明。

画面资源、账号偏好上报边界及旧入口实测范围见 [原版画面专项记录](ORIGINAL_ARTWORK_20261025.md)。统计与结算的独立实测见 [治疗统计记录](STATS_HEALING_20261025.md)。

## r8.7 文案调整

用户随后要求将原生入口统一显示为“主播模式”。r8.7 仅调整这个可见名称及运行时版本标识，沿用同一原生位置、控件、当前已确认的开关方向和配置。星枢模组页不恢复重复入口。r8.6 的人工反馈作为行为基线保留，r8.7 的安装、实际名称和安卓候选状态另行记录，不由构建结果代替实测。

- r8.7 已构建并通过四个电脑渠道的 595 项游戏成员引用、9 个反射字段及原生回调兼容检查。在游戏关闭状态下，安全脚本已更新四渠道并核对 SHA256，r8.6 备份完整保留。
- 安卓 `1.0.0-android-preview8.7` 完整容器及手机助手 `0.1.0-test.20261026.1` 已生成。既有证书、v2/v3 签名、两份内置资源 SHA256、12 份 AndroidHub 文件和无界面自检均通过；未连接或安装手机。
- 实际电脑编译文件以及安卓 PE 的 `#US` 字符串堆均包含“主播模式”，不包含旧入口名称。r8.7 未重新要求用户打开游戏验收纯文案；此前 r8.6 的原生位置、切换和重开状态实测保留为行为基线。
- 配套 Hub 已通过 TypeScript/前端构建及 `tauri/custom-protocol` 生产构建，并逐项确认二进制内嵌四渠道 r8.7 库存 SHA。未启动或替换正在使用的 Hub；当前单独候选目录没有打入完整桌面发行安装助手。
- 当前产物及准确状态保存在 `XingShu-Dev/build/streamer-setting-20261026/` 的 `candidate.json`、`desktop/installed.json`、`Android/phone-helper-build-record.json` 和 `Hub/candidate-hub.json`。旧 r8.6 候选保留作历史，本轮没有打发布 ZIP、导出/推送源码或上传 Release，现有公开更新清单保持原状。

## r8.7 小米安装验证与发布门禁

- 用户提供当前无线调试地址后，确认设备为小米 `23116PN5BC`。手机游戏关闭时，助手由 `0.1.0-test.20261024.4` 同签名覆盖升级为 `0.1.0-test.20261026.1`，保留应用数据；插件安装前快照、官方原版备份及写入回执均已保留。
- 安装完成及用户再次启动游戏后，目标容器 SHA256 均为 `62838c13bba51920b8b35ac891e9990eb1478a80961fe0c630cad46b25d68137`，与已验证候选一致。启动日志确认 `1.0.0-android-preview8.7`，本次启动范围没有原生入口绑定错误。
- 用户实际确认“启动、开关和状态都正常”，覆盖原生“主播模式”切换画面及切页/关闭重开后的状态保持。此手机安装与原生入口发布门禁通过，不扩张为所有地图、完整局内治疗归因或服务器观战研究通过。
- 本机证据位于候选目录的 `phone-xiaomi14pro/validation.json`、`deployment.json`。该目录不随源码导出；发布包与签名清单由发布流程另行生成，最终发布结果以 Release 页面和本机回执为准。
