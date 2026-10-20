# 安卓兼容预览

当前候选版本 `1.0.0-android-preview2` 是游戏内托管插件预览，桌面 Hub 与安卓设备配对尚未实现。它不是安卓 Hub APK，也不包含 Windows 原生插件。

## 已确认环境

2026-10-20 在 TapTap 内置 MuMu 的 5 号实例验证：Android 12、`com.feimo.astralparty` 3.3.0、arm64-v8a 游戏。原版程序集包 SHA-256 为 `01ee8b4332fdb024fd4c5b9bfdb38ce25f9d42e0b07941765bc7cd06efdaaaa4`。

已确认插件程序集加载、运行时初始化、14 个模块装配和悬浮球显示。触屏完整功能与安卓真机仍需验证。Live2D、Spine 等可选驱动没有随本预览安装，不能将它们的缺失日志解释为已支持动态驱动。

参考本地 APMM 0.3.35 的安卓缓存路径与官方还原包，核对后从设备原版程序集构建。APMM 目录是已编译发行包，不应称为已审阅其完整源码。游戏数据与素材归游戏原作者所有；星枢源码来源为 [Breezecaresses/Xingshu-hub](https://github.com/Breezecaresses/Xingshu-hub)。

预览 2 已离线编译与引用校验通过，沿用同一安卓基线并加入游戏设置风格界面；当前模拟器未连接，尚未部署或完成触控复测。上面的实机结论来自预览 1。

## 修复内容

- 安卓编译不引用被游戏裁剪的 `System.Diagnostics.Process` 与 `System.Console`，进程标识改从自身 `/proc` 读取。
- 使用独立安卓版本标记与 `ANDROID_CN` 平台标识。
- 安卓悬浮球默认尺寸从 64 调整为 44 个界面单位；旧默认值只迁移一次，自定义大小保留。菜单与文字缩放独立保留。
- 未连接桌面 Hub 时，身份检查限制为每秒一次，仅在身份变化时记录日志，避免每帧写入。
- 构建工具保留 Android BuildTarget 13，校验 527 个游戏成员引用；除引导入口、三个计时方法与新增插件外，不修改其他游戏程序集。

## 构建与设备部署

需要本地 Unity 引用程序集、Mono.Cecil、MSBuild、Python 的 UnityPy 和 Android SDK `adb`。这些引用及游戏二进制未放入源码仓库。

1. 从明确选择的 ADB 设备读取原版缓存，不要将 PC 缓存写入安卓。
2. 构建：

```powershell
python tools/prepare_android_runtime.py --bundle <原版程序集缓存> --out <独立输出目录>
```

3. 在游戏大厅或关闭游戏后安装：

```powershell
python tools/android_runtime_device.py install --serial <ADB设备标识> --manifest <输出目录>/candidate.json --backup-dir <此设备的备份目录>
```

安装器先核对应用版本、当前缓存哈希和原版备份，再停止指定游戏并写入既有缓存文件；不清除游戏数据。Android 12 的缓存目录可能禁止创建新文件，所以暂存于 `/data/local/tmp`，再覆盖已有文件并校验。失败会尝试恢复并校验原版。完成后正常启动游戏即可。

4. 还原：

```powershell
python tools/android_runtime_device.py restore --serial <同一ADB设备标识> --manifest <输出目录>/candidate.json --backup-dir <同一备份目录>
```

不要在游戏更新后强行覆盖新缓存。未知哈希会停止安装，需重新核对基线。真机必须由用户自行连接并授权 ADB，缓存访问权限以实际设备为准；工具不会自动开启 root 或修改安全设置。

## 后续边界

Windows Hub 当前用 Windows 进程与连接来源校验会话，单独执行 `adb reverse` 无法把安卓游戏会话变成已验证的桌面游戏进程。后续需要明确的安卓配对入口与设备会话校验，不能放宽现有桌面身份验证来冒充兼容。
