# RelicMechanics · 商店商品效果

路径：`Card/Assets/App/Game/RelicMechanics.cs`  
命名空间：`App.Game`

`RelicConfig` **就是商店商品表**。货架、已拥有、装备栏、图鉴都只认这张表。  
`RelicEntryConfig` 是商品效果词条（`MechanismType` + `Value`）。一件商品可挂多条（如小精灵 `20010` + `200101`）。  
`Run.RelicConfigIds` 是已购商品 Id。锋芒禁用写在 `Run.DisabledRelicConfigId`（0 表示未禁用）。

词条数值以 `RelicEntryConfig.Value` 为准（描述文案与表不一致时不改表）。

对局规则总览：[`GameLogic.md`](GameLogic.md)。状态机：[`GameSession.md`](GameSession.md)。HUD 装备栏：[`GameUI.md`](../UI/Game/GameUI.md)。商店货架卡：[`ShopItem.md`](../Item/ShopItem.md)。品质色：[`ThemeColors.md`](../ThemeColors.md)。

---

## 1. 数据

| 表 | 路径 | 用途 |
|----|------|------|
| RelicConfig | `Res/Config/RelicConfig.json` | 商品：价格、图标、刷新权重、`MechanismId[]` |
| RelicEntryConfig | `Res/Config/RelicEntryConfig.json` | 词条：`Type` + `Value` |
| HandScoreConfig | `Res/Config/HandScoreConfig.json` | 牌型基础倍率 |

访问：`RelicConfig.Get(id)` / `RelicEntryConfig.Get(id)`。Excel 源在仓库 `Config/`。

买卖只走 `GameSession.BuyShopRelic` / `SellShopRelic`。最多 `GameBalance.MaxRelics`（3）件。没有第二套商品表。

生成枚举里残留的 `MaxHp=11` / `EveryRoundHpUp=12` / `TwoThreeFive=14` 与 `HeadCardAttack` / `HeroHpMax` / `HeroAttack` 同值，**不要用这些旧名**。血上限用 `HeroHpMax`，回合回血用 `HeroHpReplyEveryRoundEnding`，235 用 `SpecialTwoThreeFive`。

---

## 2. 伤害公式

主路径入口：`GameSession.ComputeAttackDamage` → `HandEvaluator.ComputeAttackDamage`。

本手上下文（未亮出 2 张、搓牌已用/剩余、幸运七掷骰）由 `GameSession` 建成 `RelicCombatContext` 再传进 `RelicMechanics`，不要只靠 `HandScore`。

```
总倍率 = (HandScoreConfig.BasicMagnification + 遗物倍率加成) × 燧石
伤害   = (攻击力 + 遗物攻击加成 + BaseChips) × 总倍率
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

## 3. 比牌规则

玩家 vs 敌人：`CompareTo >= 0` 算玩家赢（平局玩家赢）。敌人当开牌方时必须严格大于才算敌人赢。

老花眼 / 错峰出行 / 235 / 近视眼只作用在玩家座位。敌人选最大 3 张、比牌、伤害都不吃玩家遗物。

| 遗物 | Type | 行为 |
|------|------|------|
| 老花眼 | SpecialFlush | 金花/同花顺：红桃=方片、黑桃=梅花。展示跟多数花色；被改的那张真实花色和展示花色的倍率/攻击都生效，没改的只算自己的花色 |
| 错峰出行 | SpecialStraight | 三张排序后相邻点差为 1 或 2 即成顺子（2-4-6、2-3-5 算；2-5-8 不算）。A-2-3 保留 |
| 近视眼 | AllCardIsHeadCard | 不改点数/牌型；人头相关遗物把每张亮出牌都当人头 |
| 235 | SpecialTwoThreeFive | 散牌恰好 2+3+5 → 豹子且 `BeatsAll` |

---

## 4. MechanismType

倍率类在 `RelicMechanics.SumMultiplierExtra` 里按当前 `HandScore` + `RelicCombatContext` 累加 `Value`。未触发的不加。

| Type | 何时加到倍率 |
|------|----------------|
| CardMagnification | 任意牌型 |
| SquarePlate / Spades / RedHeart / PlumBlossom | 亮出牌按花色每张；老花眼金花里被改花色的牌真实+展示都算 |
| Couplet / Flush / Straight / StraightFlush / Leopard | 对应牌型 |
| EvenNumberCard / OddNumberCard | 偶数 2/4/6/8/10；奇数 A/3/5/7/9 |
| HeadCard | 人头（近视眼则每张亮出牌） |
| SpecialACard | 每张 A |
| EveryRubbingNum | 剩余搓牌次数（`PeekGoodCharges`） |
| Camera | 未亮出牌里每张黑桃/梅花 |
| Cupid | 未亮出牌里每张方片/红心 |
| EveryUseRubbingNum | 本手已搓次数 |
| NoSkill | 搓牌/透视/替换剩余都为 0 |
| EveryRelic | 已拥有收藏品件数 |
| NoUseRubbingEveryRubbingNum | 本手未搓过，剩余搓牌次数 × Value |
| AccumulatedNumOfCardType | 该牌型本局亮出次数（含本手，整次闯关累计） |
| RubbingCardRelic | 老搓家累计倍率（卖掉仍加） |
| ProOfUpCardType | 天使已永久加上的该牌型倍率（卖掉仍加） |
| SpecialSevenCard | 幸运七本手触发次数 × Value |

攻击加成 `SumAttackExtra`：

| Type | 行为 |
|------|------|
| 花色 Attack（老花眼金花被改花色的牌真实+展示都算）、牌型 Attack、SpecialEightCard、DoubleCardAttack | 与既有结算相同 |
| HeadCardAttack / ACardAttack | 人头 / 每张 A |
| TheSwordOfVictory | 未亮出 2 张里 `ChipValue` 最大的那张（表 Value=0 时按 ×1） |
| ConsumeFundsGetAttack | `floor(本局花费金币 / Value)`（Value 为每 +1 攻击所需金币；卖掉不加也不扣花费） |
| EveryCardAttackForever | 亮出每张牌读训练永久点数加成（卖掉仍加） |
| SpecialSevenCardAttack | 幸运七触发次数 × Value |

非倍率、非当场攻击：

| Type | 时机 | 行为 |
|------|------|------|
| HeroHpMax | 购买 / 出售 / 进关 `ApplyHeroToPlayer` | 上限和当前血都加；跨关用 `英雄Hp + 已购 HeroHpMax 总和`；出售扣回，当前血不低于 1 |
| HeroHpReplyEveryRoundEnding | `AfterRound`（玩家仍存活） | 回血，不超过上限 |
| HeroTakeDamage | `ApplyDamage` 打玩家 | `max(1, 伤害 + Value)`（圆盾 Value=-2） |
| Liability | 买 / 付费刷新 | 允许 `Gold - 花费 >= -Value` |
| FreeShopRefresh | 进商店 | 给 Value 次免费刷新（花完再走原价，不计入付费刷新次数） |
| TakeDamageGetFunds | 玩家实际扣血后 | `Gold += Value` |
| ProOfHeadCardFunds | 玩家赢的结算 | 每张人头（近视眼则每张亮出）按 Value 概率 +1 金币 |
| SpecialNineCard | 玩家赢的结算 | 每张 9 +Value 金币 |
| BloodSucking | 玩家打出伤害后 | 回 `dealt × Value`，不超过上限 |
| RubbingCardsNum | `ResetSkillCharges` | 搓牌次数 `+ Value`（可减到 0） |
| KillAfterSellingPrice | 击杀敌人 | 该件售价 +Value；卖掉再买仍用累计售价 |
| EveryCardAttackForever | 本手亮牌结算后（每手一次） | 亮出每张牌的点数永久 +Value 攻击 |
| ProOfUpCardType | 本手亮牌结算后（每手一次） | Value 概率给当前牌型永久 +1 倍率 |
| RubbingCardRelic | 成功搓牌时 | 永久倍率 +Value（卖掉仍保留已加部分） |
| SpecialSevenCardPro | 伤害结算 | 每张亮出 7 按 Value 掷一次，同时决定攻击和倍率 |

永久存在 `RunState`、只在 `StartNewRun` 清：训练点数攻击、天使牌型倍率、老搓家倍率、工资卡售价加成、投资累计花费、老千各牌型次数。

锋芒禁用的那一件整件跳过（倍率、回血、吸血、235 都不生效）。`HeroHpMax` 已经写进血量，禁用不会当场扣血。已叠上的永久加成不因卖掉或禁用清零。

---

## 5. 挂钩点

| 时机 | 方法 |
|------|------|
| 伤害 | `ComputeAttackDamage`（传入 `RelicCombatContext`） |
| 买 / 卖 HeroHpMax | `BuyShopRelic` / `SellShopRelic` |
| 进关重算上限 | `ApplyHeroToPlayer` |
| 每手回血 | `AfterRound` → `ApplyEveryRoundHpUp` |
| 吸血 | `FinishPlayerAttack` → `ApplyBloodSucking` |
| 235 / 老花眼 / 错峰 | `EvaluateSeat` / `SelectBestOpen` |
| 圆盾 / 补偿金 / 工资卡 | `ApplyDamage` |
| 白条 / 投资花费 | `BuyShopRelic` / `RefreshShopOffers` |
| 会员卡 | `EnterShop` 写入免费刷新，`RefreshShopOffers` 先花免费 |
| 周星星 / 双刃剑 | `ResetSkillCharges` |
| 黄金面具 / 九霄云外 | 玩家赢的 `NotifyPlayerShowdown` |
| 训练 / 天使 / 老千次数 | 本手首次亮牌结算 |
| 老搓家 | `RubCard` 成功替换时 |
| 锋芒 | `ApplyEdgeAffix` 从 `RelicConfigIds` 随机禁一件 |

---

## 6. 伤害日志

走 `AppLog.Info(LogChannel.Game)`，不进局内 `Run.Log`。编辑器 Console 默认能看到（`LogFilter` 最低级别 Info）。频道关掉时：菜单 `Log/Channel/Game`。

开牌结算打一条公式拆解，玩家还会带未亮出牌、搓牌次数、幸运七。有天赋时多一行 `天赋伤害`（相对无天赋公式的增量）：

```
[Game] 伤害 平凡之人→敌人A | 金花 梅花10梅花7梅花6
  攻击7 + 遗物攻8 (致胜之剑+8) + 天赋攻3 (2点精通+3) + 点数23 = 41 | 牌型x3.5 + 遗物+2 (白银法杖+2) 天赋+0.2 (好兆头+0.2) | 燧石x1 | 倍率x5.7
  未亮出 红桃A黑桃2 | 搓牌已用1 剩余2 透视1 替换1 | 幸运七x0
  天赋伤害+18 (2点精通+3, 好兆头+0.2)
  41 x 5.7 = 234 | 天赋伤害+18
```

另外只在有情况时打：

- 计算伤害大于当前 HP（扣血截断；积分仍按攻击数值）
- 吸血实际回了多少

生效词条文案来自 `RelicMechanics.CollectMultiplierParts` / `CollectAttackParts`。
