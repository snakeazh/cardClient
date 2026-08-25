# RelicMechanics · 商店商品效果

路径：`Card/Assets/App/Game/RelicMechanics.cs`  
命名空间：`App.Game`

`RelicConfig` **就是商店商品表**。货架、已拥有、装备栏、图鉴都只认这张表。  
`RelicEntryConfig` 是商品效果词条（`MechanismType` + `Value`）。一件商品可挂多条（如小精灵 `10015` + `10016`）。  
`Run.RelicConfigIds` 是已购商品 Id。锋芒禁用写在 `Run.DisabledRelicConfigId`（0 表示未禁用）。

对局规则总览：[`GameLogic.md`](GameLogic.md)。状态机：[`GameSession.md`](GameSession.md)。HUD 装备栏：[`GameUI.md`](../UI/Game/GameUI.md)。

---

## 1. 数据

| 表 | 路径 | 用途 |
|----|------|------|
| RelicConfig | `Res/Config/RelicConfig.json` | 商品：价格、图标、刷新权重、`MechanismId[]` |
| RelicEntryConfig | `Res/Config/RelicEntryConfig.json` | 词条：`Type` + `Value` |
| HandScoreConfig | `Res/Config/HandScoreConfig.json` | 牌型基础倍率 |

访问：`RelicConfig.Get(id)` / `RelicEntryConfig.Get(id)`。Excel 源在仓库 `Config/`。

买卖只走 `GameSession.BuyShopRelic` / `SellShopRelic`。最多 `GameBalance.MaxRelics`（4）件。没有第二套商品表。

---

## 2. 伤害公式

主路径入口：`GameSession.ComputeAttackDamage` → `HandEvaluator.ComputeAttackDamage`。

```
总倍率 = (HandScoreConfig.BasicMagnification + 遗物倍率加成) × 燧石
伤害   = (攻击力 + BaseChips) × 总倍率
```

- 攻击力：`SeatState.Attack`（英雄 `HeroDamage` / 怪物 `MonsterDamage`）。
- `BaseChips`：亮出三张 `ChipValue` 之和（A=11，J/Q/K=10，2～10 为面值）。
- 遗物加成是 **加在牌型倍率上**，不是再乘一层。金花 3.5、白银法杖 +2 → `30 × (3.5+2) = 165`，不是 `30 × 3.5 × 3`。
- 燧石：总倍率 ×0.5。怪物没有遗物加成，只吃燧石。
- 结果 `Math.Round` 后至少为 1。

牌型基础倍率：

| 牌型 | 配置 Type | 基础倍率 |
|------|-----------|----------|
| 散牌 | HighCard | 1 |
| 对子 | Couplet | 2 |
| 顺子 | Straight | 3 |
| 金花 | Flush | 3.5 |
| 顺金 | StraightFlush | 5 |
| 豹子 | Leopard | 6 |

---

## 3. MechanismType

倍率类在 `RelicMechanics.SumMultiplierExtra` 里按当前 `HandScore` 累加 `Value`。未触发的不加。

| Type | 何时加到倍率 |
|------|----------------|
| CardMagnification | 任意牌型 |
| SquarePlate | 亮出牌含方片 |
| Spades | 含黑桃 |
| RedHeart | 含红心 |
| PlumBlossom | 含梅花 |
| Couplet | 对子 |
| Flush | 金花 |
| Straight | 顺子 |
| StraightFlush | 同花顺 |
| Leopard | 豹子（含 235 升级后的豹子） |

非倍率：

| Type | 时机 | 行为 |
|------|------|------|
| MaxHp | 购买 / 出售 / 进关 `ApplyHeroToPlayer` | 上限和当前血都加；跨关用 `英雄Hp + 已购 MaxHp 总和`；出售扣回，当前血不低于 1 |
| EveryRoundHpUp | `AfterRound`（玩家仍存活） | 回血，不超过上限 |
| BloodSucking | 玩家打出伤害后 | 回 `dealt × Value`（疯狂面具 0.01 = 1%），不超过上限 |
| TwoThreeFive | `EvaluateSeat`（仅玩家） | 散牌恰好 2+3+5 → 视为豹子且 `BeatsAll` 通杀任何牌型（含对方豹子） |

锋芒禁用的那一件整件跳过（倍率、回血、吸血、235 都不生效）。MaxHp 已经写进血量，禁用不会当场扣血。

---

## 4. 挂钩点

| 时机 | 方法 |
|------|------|
| 伤害 | `ComputeAttackDamage` |
| 买 / 卖 MaxHp | `BuyShopRelic` / `SellShopRelic` |
| 进关重算上限 | `ApplyHeroToPlayer` |
| 每手回血 | `AfterRound` → `ApplyEveryRoundHpUp` |
| 吸血 | `FinishPlayerAttack` → `ApplyBloodSucking` |
| 235 | `EvaluateSeat` → `RelicMechanics.ApplyTwoThreeFive` |
| 锋芒 | `ApplyEdgeAffix` 从 `RelicConfigIds` 随机禁一件 |

---

## 5. 伤害日志

走 `AppLog.Info(LogChannel.Game)`，不进局内 `Run.Log`。编辑器 Console 默认能看到（`LogFilter` 最低级别 Info）。频道关掉时：菜单 `Log/Channel/Game`。

开牌结算打一条公式拆解：

```
[Game] 伤害 平凡之人→敌人A | 金花 梅花10梅花7梅花6
  攻击7 + 点数23 = 30 | 牌型x3.5 + 遗物+2 (白银法杖+2) | 燧石x1 | 倍率x5.5
  30 x 5.5 = 165
```

另外只在有情况时打：

- 计算伤害大于当前 HP（截断）
- 吸血实际回了多少

生效词条文案来自 `RelicMechanics.CollectMultiplierParts`。
