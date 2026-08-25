# GameSession

对局状态机。规则细节见 [`GameLogic.md`](GameLogic.md)。  
主路径表现：[`GameUI.md`](../UI/Game/GameUI.md) + [`GameBoardController.md`](../UI/Game/GameBoardController.md)。  
`GameTableController` 是旧场景绑法，见 [`GameTableController.md`](../UI/Game/GameTableController.md)。

脚本：`Assets/App/Game/GameSession.cs`  
数值：`GameDefs.cs`（`GamePhase` / `SeatState` / `RunState` / `GameBalance`）

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
  → WaitingOpen：点选 3 张，或搓牌 / 透视 / 替换
  → RequestShowdown（开牌）
      先 ResetEnemyOpenSelection（清掉透视时的 5 张全选）
      再对当前敌人 LockBestOpenCardsIfEnemy（锁最大 3 张）
      RevealPlaySerial++ ，牌桌播翻牌
  → WaitingAttack → 扣血
  → 下一只敌人，或 RoundSettle / Shop / StageFail
```

发牌结束后由 `GameBoardController` 调 `GameTableViewModel.NotifyDealReady()`，HUD 才显示开牌和技能栏。

---

## 当前主路径会调用的接口

| 方法 | 谁点 | 做什么 |
|------|------|--------|
| `TogglePlayerCard(i)` | 点自己的牌 | 选中/取消，最多 3 张，选中会抬起 |
| `UsePeekGood()` | 搓牌 | 进入 `WaitingRub` |
| `RubCard(i)` / `SkipRub()` | 点牌或取消 | 随机换一张，或跳过 |
| `UseChaKanGood()` | 透视 | 开关 `SelectingXRayTarget` |
| `TryXRayPlayer()` / `TryXRayEnemySlot(slot)` | 点角色或他的牌 | 见下节 |
| `UseTiHuanGood()` | 替换 | 自己 5 张全部换成新牌 |
| `RequestShowdown()` | 开牌 | 与存活敌人逐个比牌 |
| `CompletePlayerAttack()` | 攻击演出结束 | 结算伤害，打下一个 |
| `Continue()` | 下一局 / 进商店后 | 下一手或下一关 |
| `LeaveShop()` / `BuyShopRelic` / `SellShopRelic` | 商店 | 买卖遗物 |
| `AnnounceSeatRevealed` / `FinishRevealPlay` | 牌桌动画回调 | 亮牌演出步进 |

旧下注接口（`BlindBet` / `LookCards` / `RaiseBet` / `Fold` / `AllIn`）还在，主循环不再进 `WaitingLookChoice` / `Betting`。

---

## 透视

1. 点 **透视** → `SelectingXRayTarget = true`。
2. 再点一名角色（头像或他的牌，包括自己）。
3. `TryXRaySeat`：
   - 该座位 5 张都标 `IsSpyRevealed`（背面透视用）。
   - **敌人** 5 张全部 `CardSelected = true`（先抬起）。
   - **自己** 不改你已经点选的 3 张，避免没法开牌。
   - 写入 `seat.PeekedType`（牌型名；自己没选满 3 张则是「未选定开牌」），扣 1 次。
4. 牌桌：先播抬牌（`SelectLiftDuration`），抬完再 `CardItem.SetBackSeeThrough(true)`，**不翻牌**。
5. 开牌时 `ResetEnemyOpenSelection()` 清掉 5 张全选，再 `LockBestOpenCardsIfEnemy` 只抬最大牌型的 3 张，然后才翻面亮牌。

同一座位本手只能透视一次。

---

## 注意

- 不要在透视阶段调用 `LockBestOpenCardsIfEnemy`，否则 5 张全选会被改成 3 张。
- `EvaluateSeat` 在敌人未锁 3 张时会自己枚举最大牌型，透视文案仍准确。
- 逻辑改完必须 `Notify()`（内部 `Changed`），否则牌桌和 HUD 不同步。
- 牌堆抽牌走 `Deck.TryDraw`，空堆不会把桌上的牌再发出来。
