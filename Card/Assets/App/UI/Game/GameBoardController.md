# GameBoardController

局内**世界牌桌**。挂在实例化出来的 `GameHud` 上，负责发牌/翻牌/选中抬起/透视，以及点牌输入。

Canvas HUD 在 [`GameUI.md`](GameUI.md)。对局状态在 [`GameSession.md`](../../Game/GameSession.md)。

脚本：`Assets/App/UI/Game/GameBoardController.cs`  
发牌实现：`CardTableAnimator.cs`  
牌节点：[`CardItem.md`](../../Game/CardItem.md)

---

## 和 HUD 怎么分

| | 谁 |
|--|--|
| `GameUIView` | 按钮、人物卡、装备、ResourceBar、cardinfo、箭头 |
| `GameBoardController` | `GameHud` 世界空间：`dealpoint`、`mineNode`、`PlayerNode1/2/3` 上的牌 |

`GameUIView.OnViewOpen` 加载 `GameHud` 预制体，找不到组件就 `AddComponent<GameBoardController>()`，再 `Attach(ViewModel)`。

---

## 生命周期

```
Attach(viewModel)
  → BindScene：Camera.main + CardTableAnimator.Bind(GameHud)
  → Session.Changed → _cards.Sync(session)
  → DealFinished → ViewModel.NotifyDealReady()   // HUD 才显示开牌/技能

Detach / OnDestroy
  → 取消订阅，Dispose 牌动画
```

`Sync` 在发牌、选牌、透视、开牌时都会跑。发牌进行中 `IsBusy` 为 true，忽略点击。

---

## 输入（Update）

点到 UI 上（`IsPointerOverGameObject`）直接 return。否则按阶段：

| 条件 | 点击 | 调用 |
|------|------|------|
| `SelectingXRayTarget` | 自己的牌 | `TryXRayPlayer()` |
| `SelectingXRayTarget` | 敌人槽上的牌 | `TryXRayEnemySlot(slot)` |
| `WaitingOpen` | 自己的牌 | 放大镜未用则 `PeekMagnifier`，否则 `TogglePlayerCard` |
| `WaitingAttack` / `SelectingOpenTarget` | 敌人牌 | `AttackEnemyAtSlot`（当前主流程开牌后自动打，一般用不到） |
| `WaitingRub` | 自己的牌 | 点选 → 翻到背面 → 拖拽抖动达标后松手换牌并翻回 |

敌人槽 0/1/2 = `player1` / `player2` / `player3`。点空地、点人物卡不走这里；人物卡透视由 `GameUIView` 绑 `XRayPlayerCommand` / 敌人槽点击。

---

## 牌桌动画 `CardTableAnimator`

`Bind(hud, resources)` 找节点：

```
GameHud
  dealpoint          ← CardDealPoint，52 张洗牌
  mineNode           ← 玩家 carpoint1–5
  PlayerNode1/2/3    ← 敌人，对应 HUD player1/2/3
```

`Sync(session)` 顺序：

1. 座位显隐（按 `ActiveInStage`，死人仍占原槽）
2. `DealSerial` 变了 → 洗牌 + 飞牌，结束发 `DealFinished`
3. `RevealPlaySerial` 变了 → 先抬选中牌，再按座位翻牌、播结算 clip
4. 平时：同步正反面 → **先抬选中牌** → **透视的牌抬完再 `SetBackSeeThrough`**

选中位移：玩家向上，左敌向右，上敌向下，右敌向左。

透视不要在 `SyncSeatFaces` 里立刻透牌，否则会和抬牌同时播。关掉透视、真正开牌翻面时立刻 `SetBackSeeThrough(false)`。

---

## 注意

- 主路径只用这一套，不要在 `GameUIView` 里再自己 Instantiate 牌。
- `HitPlayerCard` / `HitEnemySlot` 用 `Physics2D`，牌上要有 Collider（发牌时 `EnsureCollider`）。
- 发牌没结束不要让玩家点透视/选牌：`IsBusy` 会挡住 `Update`。
