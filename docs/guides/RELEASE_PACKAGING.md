# 发布下载项

自 2026-10-24 起，暂按两个独立下载包发布，供玩家按设备选择：

| 下载项 | 文件名 | 内容 |
| --- | --- | --- |
| 电脑端 | `XingShu_Desktop_<版本>.zip` | 星枢 Hub、电脑安装助手和经过验证的各渠道插件；在同一助手内选择 Steam、TapTap、哔哩哔哩或 WeGame。保留 Hub 连接手机所需的安卓组件。 |
| 安卓手机 | `XingShu_Android_<版本>.apk` | 手机安装助手，内含对应手机插件与还原文件；可在手机上独立安装、更新和还原。 |

不再把 Steam 和 TapTap 分成不同下载包，也不单独发布安卓工具 ZIP、手动替换 ZIP。旧版本资源保留。SHA256 校验文件和 `xingshu-update.json` 是辅助文件，不是额外的安装包。

## 打包与发布

1. 电脑端使用 `tools/package_group_test.py` 的默认 `--platform all`，合并所有已验证渠道，生成 `XingShu_Desktop_<版本>.zip`。需要电脑连接手机安装时仍传入已经验证的 `--android-bundle`。
2. 手机助手构建并验证后，使用 `tools/package_android_preview.py` 核对插件、原版及 APK 内置文件，默认只输出一个 `XingShu_Android_<版本>.apk` 及其校验文件。`--legacy-archives` 仅用于本地排查，不作为发布入口。
3. 保持电脑版构建标识、手机 APK 版本与签名更新清单一致。使用 `tools/prepare_update_release.py --desktop <电脑.zip> --android <手机.apk> --notes <说明文件> --out <目录>/xingshu-update.json` 生成并验签。
4. 使用 `tools/publish_release.py --package <电脑.zip> --android <手机.apk> --update-manifest <清单> --source <源码仓库> --body <发布说明> --tag <标签>` 发布。上传前逐一检查哈希、源码标签和签名；只允许一个电脑 ZIP 和一个手机 APK。

此约定不替代各渠道兼容检查或实机验证，也不改变已经公开的历史版本。
