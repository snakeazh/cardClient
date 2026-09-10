# RelicMechanics · 商店商品效果

路径：`Card/Assets/App/Game/RelicMechanics.cs`  
命名空间：`App.Game`

`RelicConfig` **就是商店商品表**。货架、已拥有、装备栏、图鉴都只认这张表。  
`RelicEntryConfig` 是商品效果词条（`MechanismType` + `Value`）。一件商品可挂多条（如小精灵 `20010` + `200101`）。  
`Run.RelicConfigIds` 是已购商品 Id。收藏禁用写在 `Run.DisabledRelicIds`（每手随机，见 [`BossMechanics.md`](BossMechanics.md)）。

词条数值以 `RelicEntryConfig.Value[]` 为准：主值 `Value[0]`，第二项用 `RelicMechanics.ValueAt(entry, 1)`（缺项为 0）。描述文案与表不一致时不改表。

对局规则总览：[`GameLogic.md`](GameLogic.md)。状态机：[`GameSession.md`](GameSession.md)。HUD 装备栏：[`GameUI.md`](../UI/Game/GameUI.md)。商店货架卡：[`ShopItem.md`](../Item/ShopItem.md)。品质色：[`ThemeColors.md`](../ThemeColors.md)。BOSS 机制：[`BossMechanics.md`](BossMechanics.md)。

---

## 1. 数据

| 表 | 路径 | 用途 |
|----|------|------|
| RelicConfig | `Res/Config/RelicConfig.json` | 商品：价格、图标、刷新权重、`MechanismId[]` |
| RelicEntryConfig | `Res/Config/RelicEntryConfig.json` | 词条：`Type` + `Value` |
| HandScoreConfig | `Res/Config/HandScoreConfig.json` | 牌型 Level（比牌）与 BasicMagnification（赔率） |

访问：`RelicConfig.Get(id)` / `RelicEntryConfig.Get(id)`。Excel 源在仓库 `Config/`。

买卖只走 `GameSession.BuyShopRelic` / `SellShopRelic`。件数不设上限（同件仍不可重复购买）。没有第二套商品表。

不要用已删除的枚举残留名 `MaxHp` / `EveryRoundHpUp` / `TwoThreeFive`。血上限用 `HeroHpMax`，回合回血用 `HeroHpReplyEveryRoundEnding`，235 用 `SpecialTwoThreeFive`。

---

## 2. 伤害公式

主路径入口：`GameSession.ComputeAttackDamage` → `HandEvaluator.ComputeAttackDamage`。

本手上下文（亮出牌从左到右、未亮出 2 张、搓牌已用/剩余、幸运七掷骰）由 `GameSession` 建成 `RelicCombatContext` 再传进 `RelicMechanics`，不要只靠 `HandScore`。

```
总倍率 = (HandScoreConfig.BasicMagnification + 遗物倍率加成) × 燧石
伤害   = (攻击力 + 遗物攻击加成) × 总倍率
```

- 攻击力：`SeatState.Attack`（英雄 `HeroDamage` / 怪物 `MonsterDamage`）。
- 牌面点数不再默认进伤害。第一位/第二位/第三位/三花聚顶按亮出牌槽位把 `ChipValue` 加进遗物攻击；回旋刃为 `BaseChips × Value`。
- 遗物加成是 **加在牌型倍率上**，不是再乘一层。金花 2.5、白银法杖 +2 → `30 × (2.5+2) = 135`，不是 `30 × 2.5 × 3`。
- 燧石：总倍率 `× (1 + BossEntry Value[0])`（当前表为 ×0.5）。怪物没有遗物加成，只吃燧石。
- 结果 `Math.Round` 后至少为 1。

牌型基础倍率（`HandScoreConfig.BasicMagnification`，比牌用 `Level`）：

| 牌型 | 配置 Type | Level | 基础倍率 |
|------|-----------|-------|----------|
| 散牌 | HighCard | 1 | 1 |
| 对子 | Couplet | 2 | 2 |
| 金花 | Flush | 3 | 2.5 |
| 顺子 | Straight | 4 | 3.5 |
| 顺金 | StraightFlush | 5 | 5 |
| 豹子 | Leopard | 6 | 6 |

---

## 3. 比牌规则

玩家 vs 敌人：`CompareTo >= 0` 算玩家赢（平局玩家赢）。敌人当开牌方时必须严格大于才算敌人赢。比牌先比 `HandScoreConfig.Level`（豹子 ＞ 顺金 ＞ 顺子 ＞ 金花 ＞ 对子 ＞ 散牌），同 Level 再比点数。

老花眼 / 错峰出行 / 235 / 近视眼只作用在玩家座位。敌人选最大 3 张时不吃这些遗物。升职改玩家实际牌型；降低改敌人实际牌型（比牌、提示、出伤），散牌为下限。

| 遗物 | Type | 行为 |
|------|------|------|
| 老花眼 | SpecialFlush | 金花/同花顺：红桃=方片、黑桃=梅花。展示跟多数花色；被改的那张真实花色和展示花色的倍率/攻击都生效，没改的只算自己的花色 |
| 错峰出行 | SpecialStraight | 三张排序后相邻点差为 1 或 2 即成顺子（2-4-6、2-3-5 算；2-5-8 不算）。A-2-3 保留 |
| 近视眼 | AllCardIsHeadCard | 不改点数/牌型；人头相关遗物把每张亮出牌都当人头 |
| 235 | SpecialTwoThreeFive | 散牌恰好 2+3+5 → 豹子且 `BeatsAll` |
| 升职 | UpLevel | 玩家牌型按 Level +Value（与变形魔方叠加）。展示名称、伤害倍率、比牌一起变，封顶豹子 |
| 降低 | ReduceLevel | 敌人牌型按 Level +Value 改写（表 Value=-1）。比牌、牌型提示、出伤倍率一起变。散牌已是最低，再降无效 |

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
| Camera | 未亮出牌里每张黑桃/梅花 |
| Cupid | 未亮出牌里每张方片/红心 |
| EveryUseRubbingNum | 本手已搓次数 |
| NoSkill | 搓牌/透视/替换剩余都为 0 |
| EveryRelic | 已拥有收藏品件数 |
| NoUseRubbingEveryRubbingNum | 剩余搓牌次数 × Value（奢侈品；不再要求本手未搓） |
| AccumulatedNumOfCardType | 该牌型本局亮出次数（含本手，整次闯关累计） |
| RubbingCardRelic | 老搓家累计倍率（卖掉仍加） |
| ProOfUpCardType | 天使已永久加上的该牌型倍率（卖掉仍加） |
| SpecialSevenCard | 幸运七本手触发次数 × Value |
| DefeatGetMagnification | 复盘笔记层数 × Value[1] |
| NoKillMonsterGetMagnification | 练习卷累计倍率（卖掉仍加） |

攻击加成 `SumAttackExtra`：

| Type | 行为 |
|------|------|
| 花色 Attack（老花眼金花被改花色的牌真实+展示都算）、牌型 Attack、SpecialEightCard、DoubleCardAttack | 与既有结算相同 |
| CardProvideAttack | `Value[]` 为 1-based 亮出牌槽位（左到右）；每张加 `ChipValueOf` + 该点数的训练永久加成（第一位/第二位/第三位/三花聚顶） |
| HeadCardAttack / ACardAttack | 人头 / 每张 A |
| TheSwordOfVictory | 未亮出 2 张里 `ChipValue` 最大的那张（表 Value=0 时按 ×1） |
| ConsumeFundsGetAttack | `floor(本局花费金币 / Value)`（Value 为每 +1 攻击所需金币；卖掉不加也不扣花费） |
| SpecialSevenCardAttack | 幸运七触发次数 × Value |
| EveryRubbingNum | 剩余搓牌次数 × Value 攻击（旗帜） |

非倍率、非当场攻击：

| Type | 时机 | 行为 |
|------|------|------|
| HeroHpMax | 购买 / 出售 / 进关 `ApplyHeroToPlayer` | 上限和当前血都加；跨关用 `英雄Hp + 已购 HeroHpMax 总和`；出售扣回，当前血不低于 1 |
| HeroHpReplyEveryRoundEnding | `StartRound`（绷带 / 小精灵） | 回血，不超过上限 |
| HeroTakeDamage | `ApplyDamage` 打玩家 | 遗物并进承伤百分比（圆盾 -2%）；天赋仍是固定加减 |
| Liability | 买 / 付费刷新 | 允许 `Gold - 花费 >= -Value` |
| FreeShopRefresh | 进商店 | 给 Value 次免费刷新（花完再走原价，不计入付费刷新次数） |
| TakeDamageGetFunds | 玩家实际扣血后 | `Gold += Value` |
| ProOfHeadCardFunds | 玩家赢的结算 | 每张人头（近视眼则每张亮出）按 Value[0] 概率 +Value[1] 金币（缺省 +1） |
| SpecialNineCard | 玩家赢的结算 | 每张 9 +Value 金币 |
| BloodSucking | 玩家打出伤害后 | 回 `dealt × Value`，不超过上限 |
| RubbingCardsNum | `ResetSkillCharges` | 搓牌次数 `+ Value`（可减到 0） |
| KillAfterSellingPrice | 击杀敌人 | 该件售价 +Value；卖掉再买仍用累计售价 |
| EveryCardAttackForever | 本手亮牌结算后（每手一次） | 随机 Value[0] 张亮出牌的点数永久 +Value[1] 攻击；不单独出伤，等第一位/第二位/第三位/三花聚顶覆盖到该槽才把点数+历史加成一起算（卖掉训练仍保留已加部分） |
| ProOfUpCardType | 本手亮牌结算后（每手一次） | Value 概率给当前牌型永久 +1 倍率 |
| RubbingCardRelic | 成功搓牌时 | 永久倍率 +Value（卖掉仍保留已加部分） |
| SpecialSevenCardPro | 伤害结算 | 每张亮出 7 按 Value 掷一次，同时决定攻击和倍率 |
| HeroCritical / MissDamagePer | 暴击 / 闪避 | 与英雄词条相加。刺客秘籍、武林秘籍走这两条 |
| EveryRoundGetGold | `StartRound` | +Value 金币 |
| EveryRoundEndingGetGoldPer | `AfterRound` | Value[0] 概率 +Value[1] 金币 |
| EveryRoundEndingGetCritical / Evade | `AfterRound` | 叠一层，上限 Value[1]，暴击/闪避率 += 层数 × Value[0] |
| SteppingStone | 每次比牌获胜 | Value[0] 概率永久攻击 +Value[1] |
| BounceDamage | 玩家实际扣血后 | 反弹 `受到伤害 × Value`（反击拳套 1%） |
| AllPeacePer | 出伤 / 承伤各掷一次 | 该次伤害变为 0 |
| DefeatGetAttack / DefeatGetHpMax | 每次比牌战胜 | 永久攻击 / 血上限（勇气徽章 / 激励徽章） |
| DefeatAllGetGold | `AfterRound` 且本回合伤害为 0 | +Value 金币 |
| IronRiceBowl | 本回合首次亮牌 | +Value 金币 |
| EveryRoundGetHpMax | `StartRound` | 永久血上限 +Value（当前血也加） |
| LuckyFlush / LuckyStraight / LuckyCouplet | `EvaluateSeat` 敌人 | 敌人牌型严格大于目标则压到该牌型 |
| ThermosCup | `AfterRound` 且血量低于 Value[0] | 回 Value[1] 血 |
| Interest | `AfterRound` | `floor(金币 / Value[0]) × Value[1]` |
| Abacus | `AfterRound` | `floor(剩余搓牌 / Value[0]) × Value[1]` |
| DefeatAllGetGoldAndReplyHp | 本回合比过的敌人全输 | +Value[0] 金币并回 Value[1] 血 |
| MissFirstDamage | `ApplyDamage` 打玩家 | 本回合第一次承伤免疫（闪避成功不消耗） |
| ReverseResult | 玩家将输时 | 按概率翻成赢，伤害仍按玩家牌型 |
| NoKillMonsterGetMagnification | 击杀敌人 | 永久倍率 +Value[1]（缺省 Value[0]；卖掉仍加） |
| Revenge / Trap | 出伤 / 承伤 | 本局输过的 MonsterId：打这种怪加伤、被打减伤 |
| NobleBadge | `StartRound` 且血量高于 Value[0] | +Value[1] 金币 |
| DownGrade | `StartRound` | Value[0] 概率本回合敌人牌型 -Value[1] |
| AdmissionTicket | `StartRound` | 对每个存活敌人打 `Attack × Value[1]`（缺省 ×1），次数 Value[0] |
| MonsterDamage | 承伤百分比 | 敌人伤害 × (1+Value) |
| MissGetDamage | 闪避成功 | 对攻击者打自身攻击力 × Value[1] |
| DefeatGetDamage | 出伤百分比 | 小强层数 × Value[1] |
| LossRampDamage | 出伤百分比 | 复仇之刺：每输一层 +Value[0]，上限 Value[1] 层 |
| WinHeal | 比牌获胜 | 回 Value 血 |
| GoldDamageScale | 出伤百分比 | `floor(金币/Value[0]) × Value[1]`，层数上限 Value[2] |
| AstrawToClutchAt | 致命伤害 | 血量变为 1，下次造成伤害按本次伤害 × Value 回血，每关一次 |
| OneMonsterGetDamage | 场上只剩 1 名敌人 | 出伤 × (1+Value) |
| CardUpGrade | `EvaluateSeat` 玩家 | 按 Level +Value（对子→金花→顺子→顺金→豹子），封顶豹子 |
| UpLevel | `EvaluateSeat` 玩家 | 与 CardUpGrade 叠加，同样改写牌型/倍率/比牌 |
| ReduceLevel | `EvaluateSeat` 敌人 | 按 Level +Value 改写牌型/倍率/比牌/提示。散牌为下限。须在好运来等牌型改写之后 |
| MonsterHpMax | 进关刷怪（非 BOSS） | 血上限 × (1+Value) |
| CriticalAoe | 玩家暴击 | 对其他存活敌人打自身攻击力 × Value[1] |
| PerspectiveNum | `ResetSkillCharges` | 透视次数 +Value |
| DamageTurnToGold | `AfterRound` | 本回合伤害 × Value 转金币（与 GameConst 伤害换金分开） |
| GapDamage | 出伤 / 承伤 | 牌型每差 Value[0] 级，对应 ±Value[1] |
| UseDamageMul | 消耗品使用后本次比牌 | 出伤百分比 +Value |
| HealHpPercent | 消耗品使用 | 立即回复最大生命 × Value |
| HandTypeMagUp | 消耗品使用 | Value[0] 牌型永久倍率 +Value[1] |
| RandomHandTypeMagUp | 消耗品使用 | 随机 Value[1] 种牌型永久倍率 +Value[0] |
| MaxHpUpAndHeal | 消耗品使用 | 永久血上限 +Value，当前血同步加 |
| NextShopDiscount | 消耗品使用 | 下次进商店购买折扣 Value |
| RemoveBossEntry | 消耗品使用 | 移除本关 1 条 BOSS 词缀 |
| FirstLeopardGold | 消耗品使用后本局 | 每次亮出豹子 +Value 金币 |
| LevelWinDamageUp | 消耗品使用 | 本关出伤百分比 +Value |
| UseRoundNullify | 消耗品使用 | 本次比牌免疫承伤，并立即回复最大生命 × Value[1] |

永久存在 `RunState`、只在 `StartNewRun` 清：训练点数攻击、天使牌型倍率、老搓家倍率、工资卡售价加成、投资累计花费、老千各牌型次数、暴击/闪避层数、复盘/小强/复仇之刺层数、练习卷倍率、输过的 MonsterId、永久攻击/血上限、本局是否用过技能。

收藏禁用的遗物整件跳过（倍率、回血、吸血、235 都不生效）。`HeroHpMax` 已经写进血量，禁用不会当场扣血。已叠上的永久加成不因卖掉或禁用清零。

消耗品 `RelicConfig.UseType`：0 被动，1 即时消耗，2 永久消耗。使用后从 `RelicConfigIds` 移除。`UseRelic` 可在开牌阶段或商店使用；本次比牌类必须在比牌前。

解锁：`UnlockConditionService.Report(type, amount)`。累加类（杀敌、搓牌、刷新、牌型、胜负、死亡、透视、暴击、累计金币）进度 += amount×StackedValue；取最大类（通关难度、持有金币、单次伤害）进度 = max(当前, amount)。复合条件在通关时一次记 1：奢侈品（平凡之人 + 难度≥10 + 未用技能）、天使（平凡之人 + 难度≥3）、三花聚顶（同时拥有第一位/第二位/第三位）。235 须自然 2+3+5 且战胜豹子。

---

## 5. 挂钩点

| 时机 | 方法 |
|------|------|
| 伤害 | `ComputeAttackDamage`（传入 `RelicCombatContext`；贪欲之冠 / 复仇之刺 / 消耗品加伤） |
| 买 / 卖 HeroHpMax | `BuyShopRelic` / `SellShopRelic` |
| 进关重算上限 | `ApplyHeroToPlayer` |
| 每手回血 | `StartRound` → `ApplyEveryRoundHpUp` |
| 吸血 | `ApplyPendingAttackHits` → `ApplyBloodSucking` |
| 235 / 老花眼 / 错峰 | `EvaluateSeat` / `SelectBestOpen` |
| 圆盾 / 补偿金 / 工资卡 | `ApplyDamage` |
| 白条 / 投资花费 | `BuyShopRelic` / `RefreshShopOffers` |
| 会员卡 | `EnterShop` 写入免费刷新，`RefreshShopOffers` 先花免费 |
| 周星星 / 双刃剑 / 透视眼 | `ResetSkillCharges` |
| 黄金面具 / 九霄云外 / 铁饭碗 / 豹子精髓 | 玩家亮牌 `OnPlayerCardsShown` / 赢的结算 |
| 训练 / 天使 / 老千次数 | 本手首次亮牌结算（训练只写点数加成；出伤走第一位等槽位） |
| 老搓家 | `RubCard` 成功替换时 |
| 消耗品 | `UseRelic` → `ApplyConsumableEntry` |
| 收藏禁用 | `StartRound` → `ApplyRelicDisable` 从 `RelicConfigIds` 随机禁 N 件 |
| 小钱包 / 高贵徽章 / 永恒之心 / 入场券 / 好运来 / 绷带 / 小精灵 | `StartRound` |
| 记账本 / 幸运草 / 利息 / 小算盘 / 聚宝盆 / 保温杯 / 暴击拳套 / 运动鞋 | `AfterRound` |
| 变形魔方 / 升职 / 幸运牌型 / 降低 | `EvaluateSeat` |
| 逆转沙漏 / 勇气激励徽章 / 垫脚石 / 月光酒 / 后备计划 / 复盘 / 小强 / 复仇之刺 | 比牌结算 |
| 练习卷 | 击杀敌人 |
| 和平鸽 / 护身符 / 条约 / 陷阱 / 差距胶囊 / 稻草 / 反击 / 闪避反击 / 暴击溅射 | `ApplyDamage` / `ComputeAttackDamage` |
| 毒药 | `ApplyLevelEnemies` |
| 解锁进度 | `AddGold` / 亮牌 / 比牌 / 击杀 / 死亡 / 透视 / 暴击 / 通关 `ReportRunCompleteUnlocks` |

---

## 6. 伤害日志

走 `AppLog.Info(LogChannel.Game)`，不进局内 `Run.Log`。编辑器 Console 默认能看到（`LogFilter` 最低级别 Info）。频道关掉时：菜单 `Log/Channel/Game`。

开牌结算打一条公式拆解，玩家还会带未亮出牌、搓牌次数、幸运七。有天赋时多一行 `天赋伤害`（相对无天赋公式的增量）：

```
[Game] 伤害 平凡之人→敌人A | 金花 梅花10梅花7梅花6
  攻击7 + 遗物攻8 (致胜之剑+8) + 天赋攻3 (2点精通+3) = 18 | 牌型x2.5 + 遗物+2 (白银法杖+2) 天赋+0.2 (好兆头+0.2) | 燧石x1 | 倍率x4.7
  未亮出 红桃A黑桃2 | 搓牌已用1 剩余2 透视1 替换1 | 幸运七x0
  天赋伤害+17 (2点精通+3, 好兆头+0.2)
  18 x 4.7 = 85 | 天赋伤害+17
```

另外只在有情况时打：

- 计算伤害大于当前 HP（扣血截断；积分仍按攻击数值）
- 吸血实际回了多少

生效词条文案来自 `RelicMechanics.CollectMultiplierParts` / `CollectAttackParts`。
