# 手机安装与手动替换

适配 `com.feimo.astralparty` 官方 3.3.0 及已验证的资源缓存。普通 APK 的“所有文件访问”权限不能直接获得其他应用 `Android/data` 目录的读写权。此限制不只见于小米；不同 Android / 澎湃 OS 版本仍可能有额外限制。

## 手机安装助手（推荐）

1. 从本项目 Releases 下载并安装“星枢手机安装助手.apk”。它是安装工具，不是修改签名的游戏 APK。官方游戏保持原安装。
2. 从 [Shizuku 官方渠道](https://shizuku.rikka.app/download/) 安装 Shizuku，按其[分步骤指南](https://shizuku.rikka.app/guide/setup/)通过无线调试配对、启动服务。Android 11 以上支持直接在手机完成；首次需要手动配对与授权，手机重启后通常需重新启动服务。
3. 在官方游戏完成资源更新并进入大厅一次，然后正常退出游戏。分身、双开、其他游戏版本不在本安装器适配范围内。
4. 打开星枢安装助手，点击“连接 / 授权 Shizuku”，在系统提示中允许。再点击“检查游戏与目录权限”。
5. 检查通过后点击“安装 / 更新星枢”。完成提示必须包含“SHA256 校验通过”，再正常启动游戏。已验证的预览 4 可以直接升级；未知缓存会被拒绝。
6. 需要撤销时，正常退出游戏后点击“还原官方原版”。助手内含与适配基线完全一致的原版恢复文件；安装前缓存和原版备份另存于 `/data/local/tmp/xingshu-installer/<原版SHA256>/`。该系统暂存位置可能被清理，不能代替个人长期备份。

小米若提示系统拒绝权限，先确认 Shizuku 已运行并已给本助手授权；官方指南另提到开发者选项中的“USB 调试（安全设置）”，它不同于普通“USB 调试”。以设备实际提供的选项为准。若仍拒绝写入，改用电脑 ADB 入口；本工具不会自动更改系统安全设置，也不要求卸载系统文件管理器、解锁或 root。

安装器只提供固定游戏缓存的检查、安装和恢复接口，没有任意路径或任意命令执行入口。候选、原版、当前缓存均经过哈希核验；游戏运行时拒绝替换；写入失败时恢复安装前缓存并再次核验。普通 APK 权限或 Shizuku 不能保证所有厂商版本都可写，检查结果以设备为准。

## 手动替换

手动文件在预览 ZIP 的“手动替换”目录，含 `__data`（新版）、`官方原版.__data`（还原）及 `文件校验.json`。另提供单独的手动替换 ZIP。

目标文件为：

```text
/storage/emulated/0/Android/data/com.feimo.astralparty/files/com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/3c7700aa58877b365d45231b1225fcbb/__data
```

1. 先完成官方资源更新并进入过大厅，再正常退出游戏。
2. 使用已经得到目标目录权限的文件工具，先把现有 `__data` 复制到单独的备份目录，保留原路径与备份时间。
3. 对照 `文件校验.json` 核对游戏版本、原版或支持的旧版哈希，以及待替换文件哈希。未知文件不要覆盖，先通过游戏官方资源修复恢复对应原版。
4. 将新版 `__data` 覆盖**已有同名文件**，不要复制整个 files 目录，也不要删整个资源文件夹。替换后再次核对新版 SHA256。
5. 还原时将 `官方原版.__data` 复制到上述位置，目标文件名仍为 `__data`，并核对原版 SHA256。游戏已更新时不要强行还原旧资源。

若文件管理器无法打开、复制或覆盖目标文件，手动包本身不能改变权限，改用上面的安装助手或电脑 ADB。不要把文件写入 `/data/user/0`、分身或另一款游戏的目录。

## 电脑 ADB 备用入口

完整解压预览包，运行“星枢安卓安装助手.exe”，选择已安装的 Android platform-tools 中的 `adb.exe`，连接并授权自己的手机，刷新并选中对应设备。点击安装或还原。旧版电脑助手已有的按设备备份应保留；手机助手与电脑助手分别保存安装记录，跨工具遇到未知版本提示时可先还原官方原版再安装。

## 构建手机助手

`AndroidInstaller/` 使用 Android SDK 35、Build Tools 35、JDK 17+、Gradle 8.9、Android Gradle Plugin 8.7.3 和 Shizuku API 13.1.5。先构建运行时，再运行：

```powershell
python tools/build_android_installer.py --manifest <候选目录>/candidate.json --vanilla <已核验原版缓存> --gradle <Gradle8.9可执行文件> --out <输出APK>
```

工具在独立的 ASCII 路径编译，规避 Windows AIDL 对中文路径的限制。生成的游戏资源只进入构建目录，不放入源码仓库。签名私钥和密码保存在当前用户 `%LOCALAPPDATA%/XingShu/Signing`，不导出或上传；后续 APK 更新必须保留同一签名。

参考：[Android 官方存储限制](https://developer.android.com/about/versions/11/privacy/storage)、[Shizuku API 官方说明](https://github.com/RikkaApps/Shizuku-API)。
