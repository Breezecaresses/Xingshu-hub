# 局内治疗来源补链研究（2026-10-25）

## 范围与当前决定

本记录只整理已经读取的官方客户端代码、协议字段及 r8.5 自然对局证据，不新增测试、不操作游戏、不修改运行时。修复与部署总记录见 [STATS_HEALING_20261025.md](STATS_HEALING_20261025.md)。

当前候选保持“局内显示已确认部分及估算评分，结算使用官方字段校准”。本记录提出的 Buff 来源档案及额外诊断均为后续研究建议，**尚未接入候选，也未通过实战验收**。

## r8.5 样本与差额

本局合作人机样本的读取窗口为 2026-10-25 21:38:55 至 21:57:17，运行时版本为 `1.0.0-game-ui-r8.5`。原始归档见 [healing-settlement-r8.5-20261025.json](../../XingShu-Dev/build/inspection/sync-targeting-20261024/healing-settlement-r8.5-20261025.json)。该文件属于本地构建检查产物；链接供本机追溯，不代表它已随源码公开。

返回房间后接口已清除对局键，归档中的 `matchKey` 为 `null`；本样本按单次运行及时间窗口限定，不从房间截图反推对局身份。正式对局自然入库不在本样本的通过范围内。

| 席位 | 已归因正向效果量之和 | 已归因净 HP 夹限累计 | 官方最终 `TreatmentScore` |
| --- | ---: | ---: | ---: |
| P1 | 32 | 24 | 95 |
| P2 | 10 | 10 | 10 |
| P3 | 0 | 0 | 0 |
| P4 | 0 | 0 | 0 |

这里的“净 HP 夹限累计”是当前局内参考量的工程口径：只接受已确认来源，并按 `min(正向报告变化, max(0, 当前 HP - OriHp))` 累计。它不等于已证明的官方治疗公式，也没有证明所有生命上限增加或其他正向效果均应计作官方治疗。

- 全局有 65 次正向 HP 效果，45 次观察到 HP 上升。
- 未解决来源 `sourceSlot = 0` 共 50 次，正向效果量之和 182，净 HP 增量累计 113；包含 6 次由零血恢复的事件及生命上限变化，不能全部当作治疗。
- 133 次类型化英雄资料回包的治疗和其他战斗统计字段均为 0，没有覆盖当前局内累计。
- 四人官方最终治疗为 `95 / 10 / 0 / 0`；最终回包与用户上一局结算功能页的 36 项统计逐一一致，返回房间后的内存结算卡片保留正常。

P1 的净 HP 参考量 24 与最终治疗 95 相差 71。即使将 P1 已知来源事件的全部正向报告量 32 都计入，也仍与 95 相差 63。因此，**已归因事件中的夹限或溢出差异本身不足以解释全部差额**。但未知来源事件的内容尚未分解，不能据此断言差额全部来自 Buff、筹码或溢出治疗，更不能把未知 113 分给 P1。P2 的 10 恰好一致仅是单局结果，不能外推所有效果的规则。

按受益者累计也不能替代来源归因：本局 P3/P4 均有正向 HP 效果和实际 HP 上升，最终官方治疗却均为 0。不能把受益者的全部回血直接记为其治疗贡献。

## 已确认的官方字段与调用链

### 卡牌与主动技能

官方入口为：

```csharp
GameLogic.ActionLogic.OnUpdateHeroAttrS2CServerCallBack(
    UpdateHeroAttrS2C model, int errId, bool isdispatch)
```

该处理器先检查战斗场景、房间运行状态和错误码，再按 `model.Cause.S` 分派。

| 协议或对象 | 已确认字段及语义 |
| --- | --- |
| `UpdateHeroAttrS2C` | `PlayerId`、`Cause`、`EffectDatas`；Card/Skill 演出将外层 `PlayerId` 用作出牌者或主动技施放者 |
| `CauseOrigin` | `S` 为来源枚举，`Id` 为具体配置或实例标识；本身没有额外玩家字段 |
| `HeroAttrEffect` | `PlayerId` 为受效者；`Hp` 与 `Buff` 属于同一 `Data` oneof 的不同分支 |
| `HeroHpChangeS2C` | `PlayerId`、`ChangeHp`、`OriHp`、`CurrHp`、`RealChangeHp`、`RealHp`、`MaxHp`、`DamageType`、`Killer` |

准确来源枚举为 `party.protocol.CauseOrigin.Types.source`：`Skill = 1`、`Card = 2`、`HeroBuff = 7`、`LandBuff = 8`、`Land = 9`、`SelectRelic = 17`。该枚举没有可直接代表通用筹码触发施放者的成员。

官方 Card/Skill 路径调用 `BattleLogic.RecordHeroAttr(model.EffectDatas)`，其保存同一效果对象；后续属性应用将同一 `HeroHpChangeS2C` 对象交给 `BattleProperty.OnLifeChanged`，最后原引用传入 `battle.signal.playerHpChange`。因此当前候选使用有界对象引用映射补全 `Killer == 0` 的 Card/Skill 来源，避免按时间邻近或当前行动玩家猜测。非零 `Killer` 不被替换，跨局、目标不符或来源冲突均不接受。

这条链确认了动作施放者与 HP 效果的关联；它仍不提供官方 `TreatmentScore` 的累计公式。

### Buff 与筹码

| 协议或对象 | 已确认信息 | 不足以证明的内容 |
| --- | --- | --- |
| `CauseOrigin.HeroBuff` | 外层 `PlayerId` 用于取该玩家的 `buffContainer`，`Cause.Id` 用于取 Buff 实例 | 外层玩家是 Buff 持有者，不能直接视作原始施加者或官方治疗得分者 |
| `HeroBuffChangeS2C` | `PlayerId`、`Buff`、`Op`、`Buffs`；包含 Insert、Delete、Update 及整表更新路径 | 没有通用显式施加者字段 |
| `party.model.Buff` | `UniqueId`、`BuffId`、`Params`、`RestoreParams`、`TargetIds`、`Chain`、`Source` 等 | `Params` 为配置相关数据，尚无已确认的通用施加者键 |
| `Buff.PlayerId` | 非 protobuf 序列化成员；教程 `InitBuffData` 设置它 | 不能把教程字段作为在线 Buff 的来源证据 |
| `party.model.buff_source` | `S` 与配置 `Id`；准确枚举成员包括 `Relic = 6` | 不带来源玩家 ID，配置来源不等于治疗计分归属 |
| `BuffTriggerNotifyS2C` | `PlayerId`、`UniqueId` | 没有 HP 载荷或与 HP 效果对应的关联 ID |
| `SkillTriggerNotifyS2C` | `PlayerId`、`SkillId`、`BuffId`、`Trigger` | 没有 HP 载荷或与 HP 效果对应的关联 ID |
| `CauseOrigin.SelectRelic` | 处理器更新属性 | 选择或升级筹码不能证明之后每次回血的来源 |
| `RelicInfoConfigure` | 包含 `BuffId`、配置参数及演出信息 | 没有通用玩家归属或治疗累计公式 |

`LandBuff` 路径根据 `Cause.Id` 查召唤物 Buff，并读取来源链安排演出，同样不能通用地将外层玩家当治疗者。`BattleRelicMessage` 是筹码信息聊天展示，不是可用于治疗归因的触发 RPC。

## 可行的后续补链

以下建议用于提高来源可解释性，不能未经验证就导入官方治疗列。

1. 在明确 Card/Skill 动作的 `HeroAttrEffect.Buff` 中记录新建 Buff 的来源档案：同局代次、受效玩家、`UniqueId`、`BuffId`、明确动作施放者、来源类型与配置 ID。
2. 后续 `Cause.HeroBuff` 若命中同局、同持有者、同实例的来源档案，可将其标记为“有证据的 Buff 产生动作”。仍需区分“Buff 由谁施加”与“官方治疗记给谁”，不能直接等同。
3. 只对语义已审计的具体 Buff/筹码配置建立规则。来源链中的 `Source.Relic` 与玩家持有筹码可用于配置核对，不构成通用自疗或计分证明。
4. 删除、刷新、堆叠、实例重建、持有者变化、重连整表恢复和冲突来源必须有明确失效策略。没有完整产生过程的存量 Buff 保持未知，不补造历史。
5. 继续保持 HP 对象引用对应、同局代次及明确身份约束。不得通过前后相邻通知、动画开始时间、当前回合玩家、卡牌名称或效果描述推断来源。

在官方计分归属尚未验证前，这些信息应只进入来源诊断或单独的解释数据，不提高当前候选“已确认治疗”的权威程度。

## 建议的最小诊断

可在下一次自然对局中旁路记录以下字段，不增加游戏请求，也不改变官方处理器行为：

- 对局采集代次、局内事件序号；外层 `Cause.S/Id`、动作玩家席位、受效玩家席位。
- 原始 `Killer`、解析后来源席位、解析理由（原始来源、同对象 Card/Skill 引用或未确认）；如发生冲突则明确标记。
- 原始 HP 协议各字段、应用前后实际 HP、应用前后生命上限；分别记录报告正向量、夹限净增量、满血、由零血恢复及上限变化标记。
- 存在对应 Buff 时，记录持有者席位、`UniqueId`、`BuffId`、操作类型、`Source` 与 `Chain`；明确区分已观察产生过程和重连恢复对象。
- 官方局内回包是否具备当前模式的统计权威性，以及最终逐席位 `TreatmentScore`。最终回包独立保存，不被后续动画覆盖。

应在明确的同条协议/对象上下文中捕获所需字段，避免异步处理后读到被替换或清除的对象；观察逻辑不能改变原对象。仅保存必要的局内诊断，不包含账号登录凭据或授权令牌。

当前 r8.5 归档缺少外层 `Cause.S/Id`、Buff 来源链及变化前生命上限，无法事后把未知 113 精确分类；补字段只能帮助后续样本，不能回填本局结论。

## 过量治疗与最终权威边界

官方 [BattleProperty.OnLifeChanged](../../XingShu-Dev/build/inspection/sync-targeting-20261024/BattleProperty.cs) 在 `RealHp == 0` 时以当前 HP 加 `RealChangeHp` 形成目标 HP，否则采用 `RealHp`；再将实际 HP 夹到 `[0, MaxHp]`，最后分派同一 HP 对象。满血时 `RealChangeHp` 仍可能为正，因此不能把该字段直接命名为实际净回血。

官方客户端的 `BattlePlayerInfoWindow.RefreshHeroInfo_PVE` 使用英雄资料 RPC 更新技能冷却，PVP 才展示其战斗统计；r8.5 的 133 次全零回复进一步说明，PVE 请求成功不等于得到有效实时治疗总量。

官方最终 `PlayerFinishAchieve.TreatmentScore` 是当前可确认的整局权威值。客户端结算展示只消费该字段；目前没有取得服务端如何累计卡牌、筹码、Buff、上限增加、复活及溢出治疗的公式。

仍需后续实测确认：

- 同一明确来源效果在缺血、满血及部分溢出时，对官方最终治疗的贡献分别是什么。
- 已知施加者、持有者、受益者不同的 Buff/筹码，官方治疗最终记给谁。
- 生命上限增加、复活等事件是否进入官方治疗统计，以及具体例外配置。
- 正式对局最终结算与自然入库、安卓实机显示等独立验收事项；本次人机样本不能替代它们。

在这些问题完成验证前，`≥N` 只表示当前已确认净恢复参考量的下限，不能宣称是官方 `TreatmentScore` 的下限；`≈` 评分使用星枢自有公式，不保证接近官方游戏评分。结算到达后再使用官方整局指标精确校准。
