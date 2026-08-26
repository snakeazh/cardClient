# GameSession

对局状态机。规则细节见 [`GameLogic.md`](GameLogic.md)。  
主路径表现：[`GameUI.md`](../UI/Game/GameUI.md) + [`GameBoardController.md`](../UI/Game/GameBoardController.md)。  
闯关结算：[`BattleResultPopup.md`](../UI/Popup/BattleResultPopup.md)。  
`GameTableController` 是旧场景绑法，见 [`GameTableController.md`](../UI/Game/GameTableController.md)。

脚本：`Assets/App/Game/GameSession.cs`  
数值：`GameDefs.cs`（`GamePhase` / `SeatState` / `RunState` / `GameBalance`）  
商店商品效果：[`RelicMechanics.md`](RelicMechanics.md)  
天赋局内效果：[`天赋模块使用文档.md`](../Talent/天赋模块使用文档.md)

---

## 职责

`GameSession` 只持有逻辑状态，不碰场景节点。UI 通过 `Changed` 刷新。

| 成员 | 用途 |
|------|------|
| `Changed` | 状态变化后通知 HUD / 牌桌 |
| `Player` / `Enemies[3]` | 座位。敌人数组下标 0/1/2 是逻辑座，不是屏幕槽 |
| `Run` | 整次闯关：金币、遗物、技能次数、透视标记 |
| `Phase` | 当前阶段 |
| `DealSerial` | 发牌序号，牌桌动画用来重播发牌 |
| `RevealPlaySerial` | 亮牌演出序号 |
| `SelectingXRayTarget` | 已点透视、等待点选角色 |

逻辑座 → 屏幕槽（`player1` / `player2` / `player3` = 槽 0 / 1 / 2）：1 人居中（1），2 人左右（0 和 2），3 人全亮。阵亡后仍占原槽，不把活人往中间挤。`TryXRayEnemySlot` / `AttackEnemyAtSlot` 用的是这个视觉槽。

---

## 主循环怎么驱动

```
StartNewRun / Continue
  → DealSerial++ ，牌桌播发牌
  → WaitingOpen：点选 3 张，或长按搓牌 / 透视 / 替换
  → RequestShowdown（开牌）
      先 ResetEnemyOpenSelection（清掉透视时的 5 张全选）
      再对当前敌人 LockBestOpenCardsIfEnemy（锁最大 3 张）
      RevealPlaySerial++ ，牌桌播翻牌
  → WaitingAttack → 扣血
  → 下一只敌人，或 RoundSettle / Shop / StageFail / RunComplete
```

发牌结束后由 `GameBoardController` 调 `GameTableViewModel.NotifyDealReady()`，HUD 才显示开牌和技能栏。

---

## 当前主路径会调用的接口

| 方法 | 谁点 | 做什么 |
|------|------|--------|
| `TogglePlayerCard(i)` | 点自己的牌 | 选中/取消，最多 3 张，选中会抬起 |
| `UsePeekGood()` | 搓牌按钮 | 不进搓牌，HUD 弹出长按提示 |
| `TryBeginHoldRub(i)` / `CancelHoldRub()` | 长按手牌 / 松手未达标 | 进入 `WaitingRub`，或取消回到开牌 |
| `SelectRubCard(i)` / `ClearRubSelection()` | 点选 / 松手未达标 | 记录待搓索引与提示 |
| `RubCard(i)` / `SkipRub()` | 搓牌成功或取消 | 随机换一张，或跳过 |
| `UseChaKanGood()` | 透视 | 开关 `SelectingXRayTarget` |
| `TryXRayEnemySlot(slot)` | 点敌人或他的牌 | 见下节 |
| `UseTiHuanGood()` | 替换 | 自己 5 张全部换成新牌 |
| `RequestShowdown()` | 开牌 | 与存活敌人逐个比牌 |
| `CompletePlayerAttack()` | 攻击演出结束 | 结算伤害，打下一个 |
| `Continue()` | 下一局 / 进商店后 | 下一手或下一关 |
| `LeaveShop()` / `BuyShopRelic` / `SellShopRelic` | 商店 | 买卖 RelicConfig 商品；最后一关 `LeaveShop` → `RunComplete` |
| `RestartChallenge()` | 结算页再次挑战 | 回到当前难度第 1 关并 `StartNewRun` |
| `WatchAdRevive()` | 失败弹窗再试试 | HP 回满，本关继续 |
| `AnnounceSeatRevealed` / `FinishRevealPlay` | 牌桌动画回调 | 亮牌演出步进 |

旧下注接口（`BlindBet` / `LookCards` / `RaiseBet` / `Fold` / `AllIn`）还在，主循环不再进 `WaitingLookChoice` / `Betting`。

---

## 透视

1. 点 **透视** → `SelectingXRayTarget = true`。
2. 再点一名敌人（头像或他的牌）。**不能透视自己的牌。**
3. `TryXRaySeat`：
   - 该座位 5 张都标 `IsSpyRevealed`（背面透视用）。
   - 敌人 5 张全部 `CardSelected = true`（先抬起）。
   - 写入 `seat.PeekedType`（牌型名），扣 1 次。
4. 牌桌：先播抬牌（`SelectLiftDuration`），抬完再 `CardItem.SetBackSeeThrough(true)`，**不翻牌**。
5. 开牌时 `ResetEnemyOpenSelection()` 清掉 5 张全选，再 `LockBestOpenCardsIfEnemy` 只抬最大牌型的 3 张，然后才翻面亮牌。

同一座位本手只能透视一次。

---

## 注意

- 不要在透视阶段调用 `LockBestOpenCardsIfEnemy`，否则 5 张全选会被改成 3 张。
- `EvaluateSeat` 在敌人未锁 3 张时会自己枚举最大牌型，透视文案仍准确。
- 逻辑改完必须 `Notify()`（内部 `Changed`），否则牌桌和 HUD 不同步。
- 商店商品是 `RelicConfig`；效果走 `RelicMechanics`，只读 `Run.RelicConfigIds`。见 [`RelicMechanics.md`](RelicMechanics.md)。
- 天赋养成走 `ITalentService`；局内效果走 `TalentMechanics`（`Value × 等级`）。见 [`天赋模块使用文档.md`](../Talent/天赋模块使用文档.md)。
- 打完该难度或放弃挑战走 [`BattleResultPopup.md`](../UI/Popup/BattleResultPopup.md)，不要在 HUD 上另做一套结算。
- 牌堆抽牌走 `Deck.TryDraw`，空堆不会把桌上的牌再发出来。
