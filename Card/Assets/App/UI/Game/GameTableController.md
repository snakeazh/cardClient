# GameTableController

旧的场景绑法。在 SampleScene 里把已放好的 `GameHud` / `GameUI` 接到 `GameSession`，并**运行时拼一套操作条**。

**当前主路径不是它。** 正式局内是 [`GameUI.md`](GameUI.md) + [`GameBoardController.md`](GameBoardController.md)。

脚本：`Assets/App/UI/Game/GameTableController.cs`

---

## 什么时候会碰到

场景里如果还挂着这个脚本，它会：

1. `GameObject.Find("GameHud")`，用 `CardTableAnimator` 发牌（和主路径同一套动画）。
2. `Find("Canvas/GameUI")`，在下面 **CreateButton / CreateText** 生成闷注、看牌、加注、开牌、商店等。
3. `Session.Changed` 时 `ViewModel.Refresh()` + 刷新简易牌面/商店列表。

这套按钮文案和布局是旧下注流程（闷注 / 看牌 / x2 / x4），和现在 `GameUI` 预制体上的「开牌 + 搓牌/透视/替换」不是同一套。

---

## 输入

`Update` 只处理：

- `WaitingOpen` + 放大镜：点自己的牌 `PeekMagnifier`
- `WaitingRub`：按住拖动累计距离后 `RubCard`

**没有**透视点选（`SelectingXRayTarget` / `TryXRayEnemySlot`）。透视、开牌后的攻击、装备栏都只接在主路径 `GameBoardController` + `GameUIView` 上。

也没有 `DealFinished` → `NotifyDealReady`，发牌结束不会自动亮出当前 HUD 那套 `horBtns`。

---

## 不要做什么

- 不要把新功能接到这里来「顺便兼容」。新输入、新按钮、透视抬牌、ItemTip 都加在 `GameUIView` / `GameBoardController`。
- 不要以为场景里的 `GameTableController` 和预制体 `GameUI` 会抢同一套按钮；它是另造 ActionBar。
- 主路径打开 `GameUI` 时不会 `Attach` 这个脚本。

对局规则和 API 仍以 [`GameSession.md`](../../Game/GameSession.md) 为准。
