# HeroMechanics · 英雄技能

路径：`Card/Assets/App/Game/HeroMechanics.cs`  
命名空间：`App.Game`

`HeroConfig.HeroEntryId` 指向 `HeroEntryConfig` 词条（`MechanismType` + `Value`）。一个英雄可挂多条（如狠人 `10002` + `100021`）。  
数据源是开局写入的 `Run.HeroId`，不要另读选角进度。锋芒只禁遗物，英雄词条始终生效。

词条数值以 `HeroEntryConfig.Value` 为准（描述文案与表不一致时不改表）。狠人 / 武林高手暴击 = `HeroConfig.Critical` + 词条 `HeroCritical`（当前 5% + 15% = 20%）。

对局规则总览：[`GameLogic.md`](GameLogic.md)。状态机：[`GameSession.md`](GameSession.md)。遗物：[`RelicMechanics.md`](RelicMechanics.md)。天赋：[`天赋模块使用文档.md`](../Talent/天赋模块使用文档.md)。

不要用枚举残留名 `MaxHp` / `EveryRoundHpUp` / `TwoThreeFive`（当前值 92–94，注释是复制错的）。

---

## 1. 数据

| 表 | 路径 | 用途 |
|----|------|------|
| HeroConfig | `Res/Config/HeroConfig.json` | 英雄：血、攻、暴击、`HeroEntryId[]` |
| HeroEntryConfig | `Res/Config/HeroEntryConfig.json` | 词条：`Type` + `Value` |

访问：`HeroConfig.Get(id)` / `HeroEntryConfig.Get(id)`。Excel 源在仓库 `Config/`。

`HeroMechanics.SumValue` / `Roll` / `BuyPrice` / `ShopWeight` 只扫当前英雄词条。与天赋、遗物同类 Type 在 `GameSession` 挂钩点相加。

---

## 2. MechanismType

| 英雄 | Type | 时机 | 行为 |
|------|------|------|------|
| 大壮 | HeroTakeDamagePer | 挨打 | 先加遗物/天赋 `HeroTakeDamage`，再 `× (1 + Value)`。怪物攻击数字仍是减伤前；飘字 `TakenDamage` 和实际扣血是减伤后；未闪避时至少 1 |
| 狠人 | Damage | `ComputeAttackDamage` | 并入 `dmgPercent`：`伤害 × (1 + 天赋百分比 + Value)` |
| 狠人 / 武林高手 | HeroCritical | `ComputeAttackDamage` | 加在 `HeroConfig.Critical` 和天赋暴击率之后 |
| 多面手 | VersatilePerson | `ApplyPendingAttackHits` | 主目标满伤，其余存活敌人 `round(伤害 × Value)`。与溅射斩比例叠加 |
| 射手 | ExtraAttackOneTime | `ApplyPendingAttackHits` | Value 概率对同一主目标再打一刀满伤。第二刀不触发溅射/AOE；目标已死扣 0 |
| 大嗓门 | AoeDamage | `ApplyPendingAttackHits` | 对所有存活敌人各打 `round(原伤害 × Value)`，主目标也不再吃 100%。有 AOE 时跳过溅射 |
| 富豪 | InitialFunds | `StartNewRun` | 与天赋富裕相加 |
| 武林高手 | MissDamagePer | `ApplyDamage` 打玩家 | Value 概率本击 0 伤（覆盖至少 1 的保底） |
| 赌神 | RubbingCardsNum | `ResetSkillCharges` | 与遗物搓牌次数相加 |
| 暴发户 | EpicLegendRelicProUp | `PickWeightedRelic` | 史诗/传说权重 `× (1 + Value)` |
| 关系户 | RelicPricePer | 购买价 | `round(Price × (1 + Value))`，下限 0。不改售价 |
| 经济教授 | GetGoldAfterLevel | `EnterShop` | `GetGold × (1 + Value)`，再加击杀数 × `KillMonsterGetGold`，然后广告双倍翻整笔 |

`Damage` 名字过泛，按枚举 Id 84 使用即可。英雄词条 Id 与遗物词条 Id 都从 10001 起，但是两张表，没有冲突。

---

## 3. 挂钩点

| 时机 | 方法 |
|------|------|
| 开局金币 | `StartNewRun` |
| 造成伤害百分比 / 暴击 | `ComputeAttackDamage` |
| AOE / 溅射 / 额外一刀 | `ApplyPendingAttackHits`（受击开始）；无演出时 `FinishPlayerAttack` 兜底 |
| 受伤百分比 | `ApplyDamage`；演出飘字读 `TakenDamage` |
| 闪避 | `ApplyDamage` |
| 搓牌次数 | `ResetSkillCharges` |
| 通关金币 | `EnterShop`（GetGold × 经济教授 + 击杀加成，再双倍） |
| 商店权重 | `PickWeightedRelic` |
| 购买价 | `BuyShopRelic` / `EffectiveBuyPrice`；货架 UI 同步显示折后价 |
