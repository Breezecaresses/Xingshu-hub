# 星枢扩展包与渠道

Hub 支持给 Steam 国服、Steam 国际服、TapTap Windows、哔哩哔哩和 WeGame 分别保存一个或多个游戏目录。选择实际游戏 exe 或其所在目录，不能选择平台启动器。Steam 也可选择包含国服/国际服子目录的顶层目录。

当前群测包只附带 Steam 国服与 TapTap Windows 的运行时。其余渠道可登记目录、扫描本地扩展、安装明确声明兼容的独立包；这不表示其运行时已完成适配。

## 制作独立扩展包

`.xspkg` 和 `.zip` 均为 ZIP 格式，根目录必须有 `manifest.json`。包内路径与文件校验值须与清单一致。以下仅是格式示意，需要填入真实文件的 SHA256 和字节数：

```json
{
  "schemaVersion": 1,
  "package": {
    "id": "example.texture-pack",
    "type": "resource-pack",
    "name": "示例资源包",
    "version": "1.0.0",
    "publisher": "作者"
  },
  "compatibility": {
    "game": {
      "appId": "2622000",
      "platforms": ["STEAM_CN", "TAPTAP"]
    }
  },
  "install": {
    "requiresGameClosed": true,
    "requiresApxClosed": true,
    "backup": true
  },
  "files": [
    {
      "source": "payload/example.png",
      "target": "game/resources/example/example.png",
      "sha256": "填写真实的64位十六进制校验值",
      "size": 123
    }
  ]
}
```

`type` 可选 `apx-plugin`、`mod`、`resource-pack`。`platforms` 可填写 `STEAM_CN`、`STEAM_GLOBAL`、`TAPTAP`、`BILIBILI`、`WEGAME`；只声明实际验证兼容的渠道。省略或留空的旧清单仅按 Steam 国服处理。

逻辑路径相对于所选游戏 exe 所在目录解析：

| 清单前缀 | 实际目录 |
| --- | --- |
| `apx/plugins/` | `apx/plugins/` |
| `game/mods/` | `mods/` |
| `game/resources/` | `resources/` |
| `hub/data/` | `hub_data/` |

Hub 不会因为选了国际服而将文件写进国服子目录。同一个包在不同目录的安装记录分别保存。插件或模组必须符合游戏/加载器自己的接口，Hub 把文件装到目录并不等于该扩展已在游戏中加载。

`minVersion`、`maxVersion` 是可选的数字点分版本约束。有约束时必须先通过当前渠道的游戏版本指纹核验，未知版本会停止安装。

## 查看与卸载

插件页会单独核验本包运行时指纹。`apx/plugins`、`BepInEx/plugins`、`mods`、`resources` 和 `modimg` 中的本地文件会出现在对应列表中，手动安装项只读展示；ZIP 归档不当作已加载模组。

Hub 管理的包可按选中安装位置卸载。卸载仅删除安装记录内且校验一致的文件，若文件已被手动修改会停止。安装前备份继续保留在用户数据目录，可在安装事务/备份页查看；卸载不会自动覆盖回旧文件。
