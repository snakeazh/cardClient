# BattleSettleUpPop 使用文档

关卡胜利进商店前的本关结算。只统计本关：怪物数、Stage 积分、掉落金币。章节累计 `Total` 不出现在这页。

脚本：`Assets/App/UI/Popup/BattleSettleUpPopView.cs`  
视图模型：`Assets/App/UI/Popup/BattleSettleUpPopViewModel.cs`  
预制体：`Assets/Res/UI/Popup/BattleSettleUpPop.prefab`  
资源键：`ResResourcePaths.BattleSettleUpPop` = `UI/Popup/BattleSettleUpPop`  
屏幕 Id：`AppScreenIds.BattleSettleUpPop`  
层：`UILayer.Popup`

规则：[`GameLogic.md`](../../Game/GameLogic.md)  
积分：[`积分与血量模块使用文档.md`](../../Score/积分与血量模块使用文档.md)  
飞币：[`CoinFlyFx.md`](../Effects/CoinFlyFx.md)  
弹出时机：`GameTableViewModel.TryPresentShopPopup`（每段商店只弹一次）

---

## 何时弹出

`GamePhase.Shop` 且不是最后一关。先弹本页，关掉后再弹 `BattleShopPop`。最后一关直接离店，不走结算页。

进商店时 `GrantStageGold` 已经把本关金币写入 `Run.Gold`。资源栏先按 `ShopGoldGranted` 暂扣，点提现或双倍立刻关页，飞币落到金币图标时数字才涨。

---

## 按钮

| 按钮 | 行为 |
|------|------|
| `WithDrawBtn` | 按钮处散落金币飞向 `GameResourceBar`，立刻关弹窗；金币落到图标时数字才涨 |
| `DoubleBtn` | `WatchAdDoubleGold()`（每日限次、本关已双倍则不可点）。立刻关弹窗，飞币落到图标时把暂扣（基础+双倍）加回 |
| 空白 Overlay | 不播飞币，立刻释放剩余暂扣并关闭 |

点提现 / 双倍后飞币挂 `UILayer.Resource` 自己播完。关页不会提前把暂扣加回资源栏。

`DoubleBtn` 可点条件：`GameSession.CanWatchAdDoubleGold()`（商店阶段、今日广告未用完、本关尚未双倍）。

---

## 节点

预制体 `UIBind` 的 Target 可能是 `CanvasRenderer`。绑定时用 `GetGameObject(key).GetComponent<T>()`。

| 键 | 用途 |
|----|------|
| `CurScoreNum` | 本关怪物总数（关卡配置出场数） |
| `TotalScoreNum` | 本关 Stage 积分 |
| `CoinNum` | 基础奖励（`ShopGoldGranted`） |
| `Num` | 提现金额（与基础奖励相同，双倍后刷新） |
| `FormulaText` | `每N点伤害=1`，N 为 `GameConst.ExchangePointsForGoldCoins` |
| `WithDrawBtn` | 提现 |
| `DoubleBtn` | 看广告双倍 |
| `Text 1` | 回合行模板，克隆后填 `Round` / `Kills` / `Score` |

BG 高度 = Title + Content + 底部边框（`BgBottomEdge` = 116）。回合行数取积分行与击杀行的较大者，直接击杀（无牌局积分）不能按积分数截断。

---

## 金币

本关发放 = `LevelConfig.GetGold`（可乘经济教授）+ 击杀 × `KillMonsterGetGold` + 未用技能金；进店时若已双倍再 ×2。广告双倍再发一份当前 `ShopGoldGranted`。

飞币挂在 `UILayer.Resource`，与 `GameResourceBar` 同层且在其之上，关闭结算页不会带走还在飞的金币。
