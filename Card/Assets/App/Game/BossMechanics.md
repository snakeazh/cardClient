# BossMechanics · BOSS 机制

路径：`Card/Assets/App/Game/BossMechanics.cs`  
命名空间：`App.Game`

BOSS 关从 `BossEntryConfig.All` 随机抽一条（`StartStage` → `PickBossEntry`），写入 `Run.BossEntryId`。非 BOSS 关为 0。  
不读 `MonsterConfig.MonsterEntry`。表里没有行的 `Edge` / `AllIn` 不会被抽到。

数值一律读 `BossEntryConfig.Value[]`：主值 `Value[0]`，第二项 `BossMechanics.ValueAt(run, 1)`。  
HUD：[`GameUI.md`](../UI/Game/GameUI.md) 的 `roundbuff` 显示 `Name`，点击 `ItemTip` 出 `Desc`。

对局规则总览：[`GameLogic.md`](GameLogic.md)。状态机：[`GameSession.md`](GameSession.md)。遗物：[`RelicMechanics.md`](RelicMechanics.md)。

---

## 1. 数据

| 表 | 路径 | 用途 |
|----|------|------|
| BossEntryConfig | `Res/Config/BossEntryConfig.json` | 机制：`Name`、`Type`（`BossEntryType`）、`Value[]`、`Desc` |

访问：`BossEntryConfig.Get(id)` / `BossMechanics.Resolve(run)`。Excel 源在仓库 `Config/`。

`Run` 关卡临时：`BossShieldHitsLeft`、`StolenAttack`、`HandBrandIndex`、`DisabledRelicIds`。进下一关在 `StartStage` 清。

---

## 2. BossEntryType

| Type | 时机 | 行为 |
|------|------|------|
| DisableHeart / Spade / Diamond / PlumBlossom | `DrawRubCard` | 搓出牌禁该花色 |
| DisableHead | `DrawRubCard` | 搓出牌禁 J/Q/K |
| Flint | `ComputeAttackDamage` | 总倍率 `× (1 + Value[0])`。只改倍率，不改 `BaseChips` |
| FlushDamage / FlushStraightDamage / StraightDamage | 玩家出伤 | 对应牌型 `× (1 + Value[0])` |
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
| BossRage | BOSS 扣血后 | `floor(已损失HP比例 / Value[0]) × Value[1]` 加在 `SeatState.Attack` 上，人物卡立刻显示。层数用进关基础攻 + 窃取，不叠乘已加成的 Attack |
| BossShield | `ApplyDamage` 目标是 BOSS | 前 `Value[0]` 次伤害（含溅射/AOE）免疫 |
| RoundLimit | `StartRound` 递增手数后 | 超过 `Value[0]` 手且 BOSS 仍在，玩家 HP 清 0 |
| AttackSteal | `StartRound` | 每手开始把玩家当前攻击 `× Value[0]` 转给 BOSS |
| BossTimid | `ApplyDamage` 目标是 BOSS | 场上还有非 BOSS 存活时 BOSS 免疫 |
| DisableRedSuit / DisableBlackSuit | `EvaluateSeat` 玩家 | 红（方片+红桃）或黑（黑桃+梅花）不参与牌型、不计入点数 |

打敌人时的免疫顺序：胆小首领 → 灵活身姿闪避 → 黑暗护盾。

---

## 3. 挂钩点

| 时机 | 方法 |
|------|------|
| 抽机制 | `StartStage` → `PickBossEntry` |
| 血上限 | `ApplyHeroToPlayer` |
| 每手开始 | `StartRound`：回合制约、收藏禁用、窃取指环 |
| 搓牌 | `DrawRubCard` |
| 发牌张数 | `DealAll` / `PlayerDealCount` |
| 开牌烙印 | `StartSequentialCompare` → `PickHandBrand` |
| 比牌过滤 | `EvaluateSeat` / `GetScoreBan` |
| 出伤 | `ComputeAttackDamage` |
| 入伤 | `IncomingDamageAfterMitigation` / `ApplyDamage` |
| 狂暴写回攻击 | `ApplyDamage` → `RefreshBossRageAttack`（窃取指环也会刷新） |
| 回血 | `HealPlayer` |
