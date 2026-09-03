# 炸金花闯关 · 对局逻辑

本文描述当前客户端已落地的规则，供后续改玩法、调数值、接 UI 时对照。  
实现入口：[`GameSession.md`](GameSession.md)（`GameSession.cs`），数值：`GameDefs.cs`，牌型：`CardModel.cs`，AI：`AiBrain.cs`，商店商品效果：[`RelicMechanics.md`](RelicMechanics.md)，英雄技能：[`HeroMechanics.md`](HeroMechanics.md)。  
关卡配置查询见 [`关卡模块使用文档.md`](../Level/关卡模块使用文档.md)。  
积分、血量与勇气值见 [`积分与血量模块使用文档.md`](../Score/积分与血量模块使用文档.md)。  
局内 HUD 见 [`GameUI.md`](../UI/Game/GameUI.md)。牌桌见 [`GameBoardController.md`](../UI/Game/GameBoardController.md)。人物卡见 [`PlayerItem.md`](../Item/PlayerItem.md)。攻击演出见 [`AttackCutscene.md`](../UI/Game/AttackCutscene.md)。闯关结算见 [`BattleResultPopup.md`](../UI/Popup/BattleResultPopup.md)。

玩家血量读 `HeroConfig.Hp`，怪物血量读 `MonsterConfig.MonsterHp`。攻击力读 `HeroConfig.HeroDamage` / `MonsterConfig.MonsterDamage`，在 PlayerItem 上显示。血量只在比牌后的攻击结算时扣除。玩家技能读 `HeroEntryConfig`，开局金币 / 暴击 / 搓牌可与天赋、遗物叠加。

---

## 1. 一局流程

```
发牌（玩家 5 张，存活敌人各 5 张）
  → 玩家手牌直接翻开（不显示闷牌 / 看牌）
  → WaitingOpen：点选 3 张牌（选中上移），可使用技能，或开牌
  → 开牌后用这 3 张，按上场顺序与每名存活敌人逐个比牌
      敌人从 5 张里自动选出最大的 3 张牌型，选中的牌朝玩家方向移开
  → 双方亮牌，按炸金花比大小
      赢：玩家按 (攻击力 + 牌面点数) × (牌型倍率 + 遗物加成) 打该怪物
      输：该怪物按 (攻击力 + 牌面点数) × 牌型倍率 打玩家
  → 打完所有存活敌人
  → 下一局 或 敌人全灭进商店 / 玩家阵亡失败
  → 打完该难度：BattleResultPopup 成功；阵亡：BattleResultPopup 失败（可复活）
```

主路径 UI：见 [`GameUI.md`](../UI/Game/GameUI.md) + [`GameBoardController.md`](../UI/Game/GameBoardController.md)。  
`GameTableController` 是旧场景绑法，见 [`GameTableController.md`](../UI/Game/GameTableController.md)。

---

## 2. 阶段 `GamePhase`

| 阶段 | 含义 |
|------|------|
| `Idle` | 未开局 |
| `WaitingOpen` | 发牌并看牌后：开牌或用技能 |
| `WaitingRub` | 长按手牌进入搓牌拖拽；松手成功换牌或取消 |
| `Showdown` | 当前这一对亮牌 |
| `WaitingAttack` | 播放攻击演出（玩家打怪或怪打玩家） |
| `RoundSettle` | 本手结束，点下一局 |
| `Shop` | 关卡胜利商店 |
| `StageFail` | 阵亡，可广告复活或放弃 |
| `RunComplete` | 通关该难度，弹出 [`BattleResultPopup.md`](../UI/Popup/BattleResultPopup.md) |
| `WaitingLookChoice` / `Betting` | 旧下注流程保留在代码里，当前主循环不再进入 |

---

## 3. 资源与平衡

血量与筹码分开。

| 来源 | 说明 |
|------|------|
| 玩家 HP | `HeroConfig.Hp`（选中英雄，默认 `GameConst.DefaultHeroId`） |
| 怪物 HP | `LevelConfig` → `MonsterGroupConfig` → `MonsterConfig.MonsterHp` |
| 攻击力 | 玩家 `HeroConfig.HeroDamage`，怪物 `MonsterConfig.MonsterDamage`。PlayerItem 显示该值 |
| 勇气值 | 每手仍按人物当前血量换算（旧下注用），当前主循环不再下注 |
| 积分 | 本手玩家打出的攻击数值 1:1 记分（不被剩余血量截断）。`GameConst.ChipsForPoints` 当前为 1 |
| 局内金币 | 开局 `GameConst.PlayerInitialGoldNum`（可加天赋富裕、英雄富豪）。本手攻击值按 `DamageTurnToGold`（当前 12:1）当场换金。关卡胜利发 `LevelConfig.GetGold`（可乘英雄经济教授）+ 本关击杀数 × `KillMonsterGetGold`，广告双倍再翻进店这一笔。商店刷新：`ShopRefreshFirst + min(次数, ShopRefreshGoldUpNumMax) × ShopRefreshAfter` |
| 局外货币 | 闯关结束（成功或放弃）按总积分 / `GameConst.ExchangePointsForGoldCoins`（当前 10:1）兑入钱包。见 [`BattleResultPopup.md`](../UI/Popup/BattleResultPopup.md) |

血量只在攻击结算时扣除。

三种积分：`Total` 本章节累计；`Stage` 当前关卡各回合之和；`Round` 本回合。本手没打出伤害则本轮为 0。局内金币不再由积分换算。闯关结束时 699 积分 → 69 局外货币。

- 玩家 HP 在关卡内跨局保留，进下一关时按英雄满血重开。
- 敌人每关按关卡配置血量入场，人数按该关怪物组（最多 3）。
- 换算见 `ScoreBalance.HpToCourage`。玩家阵亡（HP ≤ 0）才失败。

关卡人数与 BOSS 由 `ILevelService.Current` 决定，不再按「每 10 关 1 BOSS」硬编码。

---

## 4. 发牌、看牌、技能

- 玩家发 **5** 张，存活敌人各发 **5** 张。敌人牌默认背面；玩家发完即看牌。
- 不显示 **闷牌** / **看牌**。发牌动画结束后显示 **开牌** 和技能栏。
- 玩家点选手牌，选中的牌上移；必须选满 **3** 张才能开牌。开牌只用这 3 张，剩余 2 张不参与比牌。
- 结算时敌人从 5 张里枚举 `C(5,3)` 种组合，选出最大牌型的 3 张并翻面亮牌（不抬起）；未选中的 2 张不翻牌、不参与比牌、不播赢家结算动画。透视时该座位 5 张全部选中：先抬起，抬完再背面透视（不翻牌）；开牌结算会先清选中，再锁敌人最大的 3 张。
- `mineNode` 与敌人节点的 `cardNode` 都需要 `carpoint1` … `carpoint5`。
- `GameHud.dealpoint` 先叠 52 张 `CardIcon` 并播洗牌出现（用子节点 `Back`，不要关掉它），再飞到各座位落点。
- 技能在 `WaitingOpen` 可用。有搓牌次数时长按手牌即可拖拽搓牌（进入 `WaitingRub`）；搓完或松手取消后回到 `WaitingOpen`。点「搓牌」不进搓牌，只在按钮右侧弹出 `ItemTip`。

`GameUI` 的 `horBtns2` 上三个按钮始终显示：`PeekGood` / `ChaKanGood` / `TiHuanGood`。开牌或搓牌阶段且仍有次数时可点，否则只禁用、不隐藏。
每手发牌后次数重置为：搓牌 3 / 透视 1 / 替换 1（商店额外次数、英雄赌神加在上面）。

| 按钮 | 效果 |
|------|------|
| PeekGood | 搓牌：点按钮弹出「长按牌即可拖拽来搓牌」；有次数时长按手牌翻到背面，拖拽抖动够量后松手换牌并翻回正面 |
| ChaKanGood | 透视：点选一名敌人，5 张全部选中并先抬起，抬完再背面透视（不翻牌）；不能透视自己的牌；开牌时先重置，再锁最大 3 张 |
| TiHuanGood | 替换：自己 5 张牌全部换成牌堆新牌 |
| CompareBtn 开牌 | 用已选的 3 张与存活敌人逐个比牌 |

---

## 5. 逐个比牌

点 **开牌** 后，按敌人上场顺序（座位 1→2→3，跳过已阵亡）一对一对打：

1. 该敌人从 5 张里选出最大的 3 张并翻面亮牌（未选中的 2 张保持背面），然后双方比牌。按炸金花比大小：豹子 ＞ 顺金 ＞ 金花 ＞ 顺子 ＞ 对子 ＞ 散牌。同牌型比点数。玩家 vs 敌人时平局算玩家赢。老花眼、错峰出行、235 只改玩家牌型，敌人不吃这些遗物。细则见 [`RelicMechanics.md`](RelicMechanics.md)。
2. **玩家赢**：立刻攻击当前这只怪物，不必再点选。
3. **玩家输**：当前这只怪物立刻攻击玩家。
4. 打完当前对后进入下一只存活敌人。玩家中途阵亡则本关失败；队列打完则本手结束。

溅射斩仍在玩家打中主目标时，对其余存活敌人打 30%；多面手按词条比例溅射，两者叠加。大嗓门则把本刀变成对所有存活敌人各打原伤害的 30%，并跳过溅射。

---

## 6. 伤害

```
伤害 = (攻击力 + 牌面点数 BaseChips) × (HandScoreConfig.BasicMagnification + 遗物倍率加成)
```

攻击力进关时从配置写入 `SeatState.Attack`，本关内不随扣血变化。`BaseChips` 为亮出三张牌的 `ChipValue` 全加（A=11，J/Q/K=10，2~10 为面值）。

| 牌型 | 配置 Type | 基础倍率 |
|------|-----------|----------|
| 散牌 | HighCard | 1 |
| 对子 | Couplet | 2 |
| 顺子 | Straight | 3 |
| 金花 | Flush | 3.5 |
| 顺金 | StraightFlush | 5 |
| 豹子 | Leopard | 6 |

玩家攻击把遗物加成加进牌型倍率，再乘燧石（有 Flint 机制时 `× (1 + Value[0])`，当前表为 ×0.5）；怪物攻击只吃燧石。  
积分：本手玩家打出的**攻击数值**记入本轮（公式结果，不被怪物剩余血量截断）。扣血仍按剩余 HP 封顶。

实现：`HandEvaluator.ComputeAttackDamage`，倍率读 `HandScoreConfig`，遗物读 `RelicMechanics`，英雄技能读 `HeroMechanics`，BOSS 机制读 `BossMechanics`。细则见 [`RelicMechanics.md`](RelicMechanics.md)、[`HeroMechanics.md`](HeroMechanics.md)、[`BossMechanics.md`](BossMechanics.md)。公式拆解打在 `AppLog.Info(LogChannel.Game)`。

---

## 7. 主池 + 边池

当前主循环不再下注，奖池为 0。分层奖池代码仍保留在 `BuildPotLayers` / `AwardPots`，旧下注街不会走进来。

---

## 8. 牌型

豹子 > 顺金 > 金花 > 顺子 > 对子 > 散牌。同牌型比点数（比牌 `RankKey`：A=14）。伤害用的 `ChipValue`：A=11，J/Q/K=10，2~10 为面值。这是牌力数值，不是货币。

亮牌顺序（单挑）：先亮敌人，再亮玩家。赢家手牌高亮。

---

## 9. 商店、广告、词缀

击杀本关全部敌人 → 发本关 `LevelConfig.GetGold`（可乘经济教授）+ 击杀数 × `KillMonsterGetGold` 为**局内金币**进商店；本手伤害换金已在比牌结束时入账。`BattleSettleUpPop` 只展示本关 `Stage` 积分和本关发放的金币，不展示本章节累计。

商店商品来自 `RelicConfig`（`RelicEntryConfig` 为效果词条）。已购 Id 存在 `Run.RelicConfigIds`，件数不设上限。效果见 [`RelicMechanics.md`](RelicMechanics.md)。

打完该难度全部关卡（`LeaveShop` → `RunComplete`）或关卡失败（`StageFail`）→ [`BattleResultPopup.md`](../UI/Popup/BattleResultPopup.md)：

- 成功：只显示 BackBtn
- 失败：BackBtn 放弃；AgainBtn 广告复活（每关 1 次，用完后隐藏）
- `coinNum`：局外货币 = 总积分 / 10

广告（按钮模拟）：

- 借贷：补当前基础注（旧流程）
- 复活：HP 回满
- 额外搓牌：本关最多 2 次
- 双倍金币：每天最多 3 次

BOSS 关从 `BossEntryConfig` 随机一条机制（禁搓花色、燧石、收藏禁用、手牌压缩等）。HUD `roundbuff` 显示名称。详见 [`BossMechanics.md`](BossMechanics.md)。

---

## 10. 关键代码

| 文件 | 职责 |
|------|------|
| `App/Game/GameSession.cs` | 状态机、开牌队列、逐个比牌、攻击 |
| `App/Game/GameDefs.cs` | 阶段、平衡、座位 |
| `App/Game/RelicMechanics.cs` | RelicConfig 商品效果，说明见 [`RelicMechanics.md`](RelicMechanics.md) |
| `App/Game/BossMechanics.cs` | BOSS 机制，说明见 [`BossMechanics.md`](BossMechanics.md) |
| `App/Game/CardModel.cs` | 牌、牌型、伤害公式 |
| `App/UI/Game/GameUIView.cs` | HUD 绑定，说明见 [`GameUI.md`](../UI/Game/GameUI.md) |
| `App/UI/Game/GameTableViewModel.cs` | 文案、按钮、弹出失败/商店/结算 |
| `App/UI/Popup/BattleResultPopupView.cs` | 闯关结算，说明见 [`BattleResultPopup.md`](../UI/Popup/BattleResultPopup.md) |
| `App/UI/Game/CardTableAnimator.cs` | 发牌、洗牌出现、看牌/摊牌翻面 |
| `App/UI/Game/AttackCutscene.cs` | 玩家打怪 / 怪打玩家，说明见 [`AttackCutscene.md`](../UI/Game/AttackCutscene.md) |
| `App/Item/PlayerItem.cs` | 人物/敌人卡，说明见 [`PlayerItem.md`](../Item/PlayerItem.md) |
| `App/Game/CardItem.cs` | 单张牌贴图与 `FlipTo` |
| `App/Game/CardDealPoint.cs` | dealpoint 叠牌 |

座位字段：

- `Hp` / `MaxHp`：生命。攻击结算才扣除
- `Attack`：攻击力。玩家 `HeroConfig.HeroDamage`，怪物 `MonsterConfig.MonsterDamage`
- `Courage`：勇气值（筹码）。每手由人物当前 HP 换算
- `Folded` / `Looked` / `ShowCards`

---

## 11. 改规则时注意

1. HP 只在攻击结算时扣除。
2. 发牌后直接看牌，不要再露出闷牌 / 看牌。
3. 开牌前点选 3 张牌（或用技能），不要走跟注 / 加注 / 弃牌。
4. 开牌后必须按敌人顺序逐个比，不要全员一起摊牌后点选。
5. 伤害用 `(攻击力 + BaseChips) × (HandScoreConfig 倍率 + 遗物加成)`，遗物是加在倍率上，不要再乘一层。细则见 [`RelicMechanics.md`](RelicMechanics.md)。
6. 玩家 HP / 攻击力读 `HeroConfig`，怪物 HP / 攻击力读 `MonsterConfig`。
7. `HandScore.BaseChips` / `Card.ChipValue` 是牌力，不是货币。
8. 玩家和敌人都发 5 张；开牌各用 3 张（玩家点选，敌人自动选最大牌型）。
