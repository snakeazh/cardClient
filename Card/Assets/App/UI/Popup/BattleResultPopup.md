# BattleResultPopup 使用文档

闯关结算页。成功 / 失败两套节点；`coinNum` 是局外货币。

脚本：`Assets/App/UI/Popup/BattleResultPopupView.cs`  
视图模型：`Assets/App/UI/Popup/BattleResultPopupViewModel.cs`  
预制体：`Assets/Res/UI/Popup/BattleResultPopup.prefab`  
资源键：`ResResourcePaths.BattleResultPopup` = `UI/Popup/BattleResultPopup`  
屏幕 Id：`AppScreenIds.BattleResultPopup`

规则：[`GameLogic.md`](../../Game/GameLogic.md)  
积分换算：[`积分与血量模块使用文档.md`](../../Score/积分与血量模块使用文档.md)  
弹出时机：`GameTableViewModel`

---

## 何时弹出

| 结果 | 时机 | 显示 |
|------|------|------|
| 成功 | `GamePhase.RunComplete`（打完该难度全部关卡，商店点下一关） | `logosuccess` / `logosuccess2` |
| 失败 | `GamePhase.StageFail`（阵亡） | `logofail` / `logofail2` |
| 失败（无复活） | 局内 `backBtn` 经 `CommonTop` 确定退出 | `logofail` / `logofail2`，隐藏 `AgainBtn` |

不再弹出独立的 `BattleFailPopup`（预制体与脚本保留，暂不使用）。阵亡直接出本页。主动退出先弹 [`CommonTop`](CommonTopView.cs)，确定后再以 `forfeitNoRevive` 打开本页。

---

## 按钮

| 按钮 | 成功 | 失败 | 行为 |
|------|------|------|------|
| `BackBtn` | 显示 | 显示 | 回主界面；失败时放弃本局并兑入局外货币 |
| `AgainBtn` | 隐藏 | 本关尚未复活时显示；主动退出隐藏 | `WatchAdRevive()`：HP 回满，本关继续。每关最多 1 次 |

---

## 节点

预制体 `UIBind` 的 Target 可能是 `CanvasRenderer`。绑定时用 `GetGameObject(key).GetComponent<T>()`，不要 `UI.Get<TMP_Text>` / `UI.Get<Button>`。

| 键 | 用途 |
|----|------|
| `logosuccess` | 成功立绘 |
| `logofail` | 失败立绘 |
| `logosuccess2` | 成功角标（挑战成功图） |
| `logofail2` | 失败角标（挑战失败图） |
| `coinNum` | 本局兑入的局外货币 |
| `BackBtn` | 回到主界面 |
| `AgainBtn` | 广告复活 |

---

## 局外货币

`coinNum` = `ScoreBalance.PointsToGold(Total)` = 总积分 / `GameConst.ExchangePointsForGoldCoins`（当前 10:1，向下取整）。

打开弹窗时先显示 `coinNum`。成功（`RunComplete`）当场兑入；失败点 `BackBtn` 放弃时才兑入。复活不兑入。

```csharp
var funds = ScoreBalance.PointsToGold(session.Score.Total);
wallet.Add(funds);
```
