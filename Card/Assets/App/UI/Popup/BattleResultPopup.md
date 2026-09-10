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
| 成功 | `GamePhase.RunComplete`（打完该难度全部关卡，商店点下一关） | `logosuccess2` |
| 失败 | `GamePhase.StageFail`（阵亡） | `logofail2` |
| 失败（无复活） | 局内 `backBtn` 经 `CommonTop` 确定退出 | `logofail2`，隐藏 `AgainBtn` |

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
| `logosuccess2` | 成功角标（挑战成功图，Title 子节点） |
| `logofail2` | 失败角标（挑战失败图，Title 子节点） |
| `coinNum` | 最底部总金币（SummeryArea 内） |
| `BackBtn` | 回到主界面 |
| `AgainBtn` | 广告复活 |
| `Scroll View` | 逐关明细滚动区（未直接使用，行克隆走 FindDeep 找 Content/Text1；View 不写它的 sizeDelta，靠拉伸锚点跟随 BG） |

行模板 `Text1` 与 `Content` 没绑 UIBind，View 用 `FindDeep` 按名字取。

---

## 逐关金币明细

Content（`BG/Scroll View/Viewport/Content`，VLG 自上而下）里克隆 `Text1` 模板逐行显示：
`LevelText` = 第N关、`DamageText` = 该关金币，`DotText` 点线装饰不动；行命名 `Stage_N`。
最底部 `SummeryArea.coinNum` 是总数（见下节）。

- 数据：`BattleResultPopupViewModel.StageRows`（`ResultStageRow{Stage,Gold}`），
  `OnOpen` 时 `RefreshStageRows()` 构建并自增 `StageRevision`，View 订阅版本号克隆行
  （**必须 `emitCurrent: false`**，否则 OnBind 时回放值 0 会空播一次入场动画）。
- 口径：每关金币 = `PointsToGold(该关积分)`，与总数同源（总积分 10:1 的分解）；
  逐关向下取整，行合计可能比总数少几枚（每关最多差 1）。
- 来源：`GameSession.StageScores`（已结束关卡，`StartStage` 里 `BeginStage()` 之前落账）
  + 当前关积分兜底行（通关最后一关不走 StartStage / 失败 / 放弃时仍在 `Score.Stage`）。
- 入场：`PaperRevealAnim` 且 **`GrowBg = false`**——本预制体 BG/滚动区固定尺寸
  （汇总区/按钮摆在滚动区外），不做 BG 逐格长高与托底，只保留滑入/标题回落/块淡入/行链串行。
- Content 的 ContentSizeFitter 由预制体自带（View 里也有运行时兜底），高度跟行数走，
  否则多出的行被滚动范围裁掉。

### BG 高度自适应

View 每帧 `FitBgHeight()`（**宽度固定不变**），线性模型（基准与预制体手调值一致）：

```
extra   = (行数 − 1) × 行高        // 行高实测 Text1 模板 ≈ 59.5
BG 高度 = 439 + extra              // BgBaseHeight（代码只写 BG 这一处）
Scroll View 高度 = BG 高度 − 240   // 拉伸锚点自动跟随 = 199 + extra
```

第 1 关直接退出行数=1 → BG 正好 439；每多一行 BG 加一个行高，滚动区经拉伸锚点自动
同步加高，1 行时纸面/滚动区的富余量在任意行数下保持不变（全部行直接可见，无需滚动）。
子节点锚点配合：Title 挂 BG 顶边（长高时顶边上移、跟随），SummeryArea/两按钮挂 BG
底边（底边下移、跟随）；**Scroll View 是拉伸锚点（(0,0)-(1,1)+负 sizeDelta −240），
代码不写它的 sizeDelta**——拉伸锚点下 sizeDelta 是相对 BG 的增量，按绝对高度写会
算成 BG高+目标高 的双重叠加，滚动区溢出纸面。BG 居中锚点/轴心 +
TallScreenFitScale（只写 localScale，不冲突）。行数极多时纸面会超出屏幕，未做上限。

---

## 局外货币

`coinNum` = `ScoreBalance.PointsToGold(Total)` = 总积分 / `GameConst.ExchangePointsForGoldCoins`（当前 10:1，向下取整）。

打开弹窗时先显示 `coinNum`。成功（`RunComplete`）当场兑入；失败点 `BackBtn` 放弃时才兑入。复活不兑入。

```csharp
var funds = ScoreBalance.PointsToGold(session.Score.Total);
wallet.Add(funds);
```
