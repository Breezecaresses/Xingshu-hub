# 热更新资源兼容检查

资源编号只作为审计记录。电脑端的当前托管容器可以从官方 Addressables 清单中
`AstralParty.Runtime.dll` 的依赖定位，无需硬编码每次热更新的编号、容器 ID。

在已安装官方更新的开发环境运行：

```powershell
python tools/pc_resource_audit.py --out XingShu-Dev/build/resource-audit --hooks-dll <已构建的XingShu.Runtime.dll>
```

工具只读取游戏文件，在输出目录保存当前官方清单、原版容器、程序集副本与检查报告，
并生成 `platform_profiles.json` 候选。可用 `--platform STEAM_CN --platform TAPTAP`
限定渠道。流程检查渠道原生程序指纹、清单依赖、缓存唯一性、程序集格式、插件所引用的
游戏接口、反射字段与设置回调；读取期间游戏文件变化也会中止。

确认报告后更新规范配置并重新构建插件，再使用 `prepare_runtime_truth_fix.py`
从**该次官方程序集**制作补丁。制作后执行 `verify_thinking_time.ps1`，核对原有方法
保留情况；安装前正常退出游戏，通过 `install_checked_runtime.ps1` 校验目标、候选和
备份，安装后再次核对 SHA256。配置更新后必须重编译插件和 Hub。

这项工具用于自动准备新的兼容候选，尚未集成到玩家安装器中。现有玩家发布包仍只
安装包内已验证的候选，不会向未来的新资源强行写入旧游戏程序集。官方更新替换了
插件加载入口时，需要重新安装兼容版本。原生程序或接口发生变化时仍需重新适配。

2026-11-13 已用同一发现流程通过四个电脑渠道的资源 152 接口检查；启动和功能验证
分别记录，接口通过不代表实机验证完成。安卓须使用本端原始资源单独核对。
