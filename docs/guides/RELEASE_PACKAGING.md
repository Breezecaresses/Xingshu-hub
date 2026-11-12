# 发布下载项

自 2026-10-24 起，暂按两个独立下载包发布，供玩家按设备选择：

| 下载项 | 文件名 | 内容 |
| --- | --- | --- |
| 电脑端 | `XingShu_Desktop_<版本>.zip` | 星枢 Hub、电脑安装助手和经过验证的各渠道插件；在同一助手内选择 Steam、TapTap、哔哩哔哩或 WeGame。保留 Hub 连接手机所需的安卓组件。 |
| 安卓手机 | `XingShu_Android_<版本>.zip` | 手机安装助手 APK、手动替换插件、官方原版还原文件、安装说明和校验文件。 |

不再把 Steam 和 TapTap 分成不同下载包，也不把安卓 APK 和手动替换文件分开群发。旧版本资源保留。

## 0.1.2 双端正式发布

本次电脑端和安卓端验证后一起发布，使用下方双端流程及原名 `xingshu-update.json`。Hub 和手机助手版本均为 0.1.2，构建序号为 2026111301；既有更新入口可继续使用，安卓 ZIP 内 APK 与独立更新 APK 保持一致。

## 可选的电脑独立发布

以后只有电脑完成验证时，可以单独发布；不要为凑双端清单重新广告未经验证的安卓 APK。

- 使用 `prepare_update_release.py --desktop-only --desktop <电脑.zip> --notes <说明> --out <目录>/xingshu-desktop-update.json` 生成签名清单；签名身份保持不变。
- 使用 `publish_release.py --desktop-only --stable --package <电脑.zip> --update-manifest <目录>/xingshu-desktop-update.json --source <源码仓库> --body <说明> --tag v<版本>` 上传正式版。
- 0.1.1 Hub 优先读取电脑独立清单，也兼容旧双端清单。0.1.0 及更早 Hub 首次升级电脑独立版本需手动完整解压；双端发布不受此限制。
- 电脑独立发布不提供旧名 `xingshu-update.json`，旧手机助手会跳过本次发布并继续检查此前的安卓清单，避免错误提示或下载不适用的 APK。
- 若未附带安卓组件，Hub 不提供本包的手机安装文件；以后电脑与安卓都完成验证时，仍可使用下方双端发布流程。

GitHub Releases 另外保留 `XingShu_Android_<版本>.apk`、SHA256 校验文件和 `xingshu-update.json` 作为自动更新资产。APK 与安卓 ZIP 内的 APK 完全相同，发布时标为“自动更新专用”。群发公告与手动下载入口只列两个 ZIP；现有手机助手仍按原签名清单直接下载 APK，电脑 Hub 仍下载电脑 ZIP，不需要改变客户端或让手机下载整个 ZIP。

## 打包与发布

1. 电脑端使用 `tools/package_group_test.py` 的默认 `--platform all`，合并所有已验证渠道，生成 `XingShu_Desktop_<版本>.zip`。需要电脑连接手机安装时仍传入已经验证的 `--android-bundle`。
2. 手机助手构建并验证后，使用 `tools/package_android_preview.py` 核对插件、原版及 APK 内置文件，输出安卓 ZIP、同内容的独立更新 APK 及各自的校验文件。ZIP 内的“手动替换”目录同时包含新版 `__data`、`官方原版.__data` 和 `文件校验.json`，不再另生成手动替换 ZIP 或附带 Windows EXE。
3. 保持电脑版构建标识、手机 APK 版本与签名更新清单一致。使用 `tools/prepare_update_release.py --desktop <电脑.zip> --android <手机.apk> --android-package <安卓.zip> --notes <说明文件> --out <目录>/xingshu-update.json`。先核对安卓 ZIP 内的 APK 与更新 APK 一致、手动文件与 APK 内置插件一致，再生成原格式签名清单；清单仍指向电脑 ZIP 和手机 APK。
4. 使用 `tools/publish_release.py --package <电脑.zip> --android-package <安卓.zip> --android <手机.apk> --update-manifest <清单> --source <源码仓库> --body <发布说明> --tag <标签>` 发布。签名清单为必需项，避免新发布缺少更新入口；上传前再次检查 ZIP、APK、手动文件、源码标签和签名，拒绝其他渠道分包或额外手动包。

此约定不替代各渠道兼容检查或实机验证，也不改变已经公开的历史版本。

公开正式版本发布时传入 `--stable` 并设为最新，支持相应清单的客户端无需打开“包含测试版”即可发现。版本字符串与 GitHub 的预发布标记必须同时核对；定向预览不传该参数。

两种分发包与现有更新器的兼容验证见[发布打包验证记录](../records/RELEASE_PACKAGING_20261024.md)。
