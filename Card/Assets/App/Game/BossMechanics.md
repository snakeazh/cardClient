# BossMechanics · 关卡机制

路径：`Card/Assets/App/Game/BossMechanics.cs`  
命名空间：`App.Game`

每关从 `BossEntryConfig.All` 随机抽 `LevelConfig.LevelEntryNum` 条（`StartStage` → `PickLevelEntries`），写入 `Run.LevelEntryIds`。数量为 0 时本关无机制。同 `Type` 不重复抽取。  
不读 `MonsterConfig.MonsterEntry`。表里没有行的 `Edge` / `AllIn` 不会被抽到。

数值一律读对应 `BossEntryConfig.Value[]`：主值 `Value[0]`，第二项 `BossMechanics.ValueAt(run, type, 1)`。  
HUD：[`GameUI.md`](../UI/Game/GameUI.md) 的 `roundbuffGrid` 下每条机制一个 `roundbuff`，点击 `ItemTip` 出该条 `Name` / `Desc`。有机制时开局弹 [`GamePopupInfo`](../UI/Popup/GamePopupInfoView.cs)，`stageinfo` 列出每条「名称：描述」，点空白关闭。

对局规则总览：[`GameLogic.md`](GameLogic.md)。状态机：[`GameSession.md`](GameSession.md)。遗物：[`RelicMechanics.md`](RelicMechanics.md)。

---

## 1. 数据

| 表 | 路径 | 用途 |
|----|------|------|
| BossEntryConfig | `Res/Config/BossEntryConfig.json` | 机制：`Name`、`Type`（`BossEntryType`）、`Value[]`、`Desc` |
| LevelConfig | `Res/Config/LevelConfig.json` | `LevelEntryNum`：本关抽几条 |

访问：`BossEntryConfig.Get(id)` / `BossMechanics.Find(run, type)` / `BossMechanics.ResolveAll(run)`。Excel 源在仓库 `Config/`。

`Run` 关卡临时：`BossShieldHitsLeft`、`StolenAttack`、`HandBrandIndex`、`DisabledRelicIds`。进下一关在 `StartStage` 清。  
敌人座位临时：`PhaseRageTriggered`、`SecondWindUsed`。进关 `ApplyLevelEnemies` 清。

---

## 2. BossEntryType

| Type | 时机 | 行为 |
|------|------|------|
| DisableHeart / Spade / Diamond / PlumBlossom | `DrawRubCard` | 搓出牌禁该花色 |
| DisableHead | `DrawRubCard` | 搓出牌禁 J/Q/K |
| Flint | `ComputeAttackDamage` | 总倍率 `× (1 + Value[0])`。只改倍率 |
| FlushDamage / CoupletDamage / StraightDamage | 玩家出伤 | 对应牌型 `× (1 + Value[0])` |
| CurseBody | 入伤 + 对怪主刀后 | 玩家受伤 `× (1 + Value[0])`；每次对怪主刀再扣自身当前 HP 的 `\|Value[1]\|` |
| PlayerDamageDown | 玩家出伤 | `× (1 + Value[0])` |
| SkillDisable | 技能按钮 / 长按搓牌 | 搓牌、透视、替换全部禁用 |
| HandBrand | 开牌 | 已选 3 张里随机 1 张不进牌型与点数，UI 仍显示选中 |
| MonsterEvade | `ApplyDamage` 打敌人 | `Value[0]` 闪避；成功则对玩家打 `round(自身攻击 × Value[1])` |
| MonsterDamageUp | 敌人出伤 | `× (1 + Value[0])` |
| RelicDisable | `StartRound` | 每手从已持有遗物随机失效 `Value[0]` 件；不禁消耗品 |
| GoldThorn | 敌人出伤 | 额外 `round(GoldSpentThisRun × Value[0])` |
| FragileBody | `ApplyHeroToPlayer` / `HealPlayer` | 血上限 `× (1 + Value[0])`；局内回血全拦。广告复活仍回满 |
| HandCompress | 发牌 / 替换 / 动画 | 玩家发 `Value[0]` 张（默认 4），仍选 3 张开牌 |
| MonsterRage | 敌人扣血后 | `floor(已损失HP比例 / Value[0]) × Value[1]` 加在 `SeatState.Attack` 上，人物卡立刻显示。层数用进关基础攻 + 窃取，不叠乘已加成的 Attack |
| MonsterShield | `ApplyDamage` 目标是 BOSS | 前 `Value[0]` 次伤害（含溅射/AOE）免疫 |
| RoundLimit | `StartRound` 递增手数后 | 超过 `Value[0]` 手且 BOSS 仍在，玩家 HP 清 0 |
| AttackSteal | `StartRound` | 每手开始把玩家当前攻击 `× Value[0]` 转给 BOSS |
| BossTimid | `ApplyDamage` 目标是 BOSS | 场上还有非 BOSS 存活时 BOSS 免疫 |
| DisableRedSuit / DisableBlackSuit | `EvaluateSeat` 玩家 | 红（方片+红桃）或黑（黑桃+梅花）不参与牌型、不计入点数 |
| FaceDevalue | `EvaluateSeat` | 人头牌伤害点数改为 `Value[0]`（默认 5），比牌 Keys/Level 不变 |
| AceDown | `EvaluateSeat` | A 伤害点数改为 `Value[0]`（默认 1），比牌不变 |
| RubDown | `ResetSkillCharges` | 本关搓牌次数 `-Value[0]` |
| SwapLock | 替换按钮 | 禁用替换技能 |
| RubFee | `ApplyRubReplace` | 每次搓牌扣 `Value[0]` 金币，不够不能搓 |
| MonsterRegen | `StartRound` | 活着的敌人回复最大生命 `× Value[0]` |
| MonsterGrow | `AfterRound` | 活着的敌人 `BaseAttack` `+ Value[0]`，避免被狂暴刷新冲掉 |
| ThornShell | `ApplyDamage` 玩家主刀打中敌人 | 自损该次实际伤害 `× Value[0]`（直扣 HP，不走 `ApplyDamage`） |
| VengefulSoul | 击杀敌人 | 玩家 `PermanentAttackBonus + Value[0]`（可为负，跨关永久） |
| HealBan | `HealPlayer` | 治疗 `× (1 + Value[0])`。`FragileBody` 仍全拦 |
| TieLose | `OpenerWinsCompare` | 双方 `CompareLevel` 相同则敌人胜，不再比 Keys |
| PhaseRage | `ApplyDamage` 敌人扣血后 | 每个敌人首次 HP 低于 `Value[0]` 时，`BaseAttack × (1 + Value[1])`。不加额外免疫 |
| SecondWind | `ApplyDamage` 敌人将死 | 每个敌人首次将死时以最大生命 `× Value[0]` 复活，不触发击杀 |
| LifeSiphon | `ApplyDamage` 敌人打到玩家 | 攻击者按实际伤害 `× Value[0]` 回血 |
| DisableHeartUesd / SpadeUesd / DiamonUesdd / PlumBlossomUesd | `EvaluateSeat` 玩家 | 该花色不参与牌型、不计入伤害点数 |
| DisableHeadUesd | `EvaluateSeat` 玩家 | 人头牌不参与牌型、不计入伤害点数 |

打敌人时的结算顺序：胆小首领 → 灵活身姿闪避 → 黑暗护盾 → 不灭传说（将死复活）→ 扣血 → 背水一战 → 击杀/怨恨之灵 → 诅咒之躯/尖刺外壳。敌人打玩家后生命虹吸。

---

## 3. 挂钩点

| 时机 | 方法 |
|------|------|
| 抽机制 | `StartStage` → `PickLevelEntries`（条数 = `LevelEntryNum`） |
| 血上限 | `ApplyHeroToPlayer` |
| 每手开始 | `StartRound`：回合制约、收藏禁用、窃取指环、巫术灵体回血 |
| 搓牌 | `DrawRubCard` / `ApplyRubReplace`（有偿服务扣金） |
| 搓牌次数 | `ResetSkillCharges`（生锈拇指） |
| 替换 | `PlayerMayUseTiHuanGood`（宿命之手） |
| 发牌张数 | `DealAll` / `PlayerDealCount` |
| 开牌烙印 | `StartSequentialCompare` → `PickHandBrand` |
| 比牌过滤 | `EvaluateSeat` / `GetScoreBan`（含禁用花色/人头、王权旁落、折翼之A） |
| 比牌胜负 | `OpenerWinsCompare`（苛刻裁判） |
| 出伤 | `ComputeAttackDamage` |
| 入伤 | `IncomingDamageAfterMitigation` / `ApplyDamage`（复活、背水、虹吸、尖刺、怨恨） |
| 狂暴写回攻击 | `ApplyDamage` → `RefreshBossRageAttack`（窃取指环也会刷新） |
| 回血 | `HealPlayer`（枯竭之泉削弱；脆弱身躯全拦） |
| 每手结束 | `AfterRound`：磨刀霍霍 |
