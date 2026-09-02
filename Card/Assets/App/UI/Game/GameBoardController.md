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
| `GameUIView` | 按钮、人物卡、遗物列表、cardinfo、BOSS 书 |
| `GameBoardController` | `GameHud` 世界空间：`dealpoint`、`mineNode`、`otherNode` 上的牌 |

`GameUIView.OnViewOpen` 加载 `GameHud` 预制体，找不到组件就 `AddComponent<GameBoardController>()`，再 `Attach(ViewModel)`。

---

## 生命周期

```
Attach(viewModel)
  → BindScene：Camera.main + CardTableAnimator.Bind(GameHud)
  → Session.Changed → _cards.Sync(session)
  → DealFinished → ViewModel.NotifyDealReady()   // HUD 才显示开战/技能

Detach / OnDestroy
  → 取消订阅，Dispose 牌动画
```

`Sync` 在发牌、选牌、透视、开牌时都会跑。发牌进行中 `IsBusy` 为 true，忽略点击。

---

## 输入（Update）

点到 UI 上（`IsPointerOverGameObject`）直接 return。否则按阶段：

| 条件 | 点击 | 调用 |
|------|------|------|
| `SelectingXRayTarget` | otherNode 上的牌 | `TryXRayEnemySlot(DisplayedEnemyVisualSlot)`（不能透视自己） |
| `WaitingOpen` | 自己的牌 | 短按：放大镜未用则 `PeekMagnifier`，否则 `TogglePlayerCard`；有搓牌次数时长按进入搓牌拖拽 |
| `WaitingAttack` / `SelectingOpenTarget` | 敌人牌 | `AttackEnemyAtSlot`（当前主流程开牌后自动打，一般用不到） |
| `WaitingRub` | 自己的牌 | 已长按：翻到背面，拖拽抖动达标后松手换牌并翻回 |

人物卡槽 `player1` / `player2` / `player3` 仍在 Canvas 上。点空地、点人物卡不走这里；敌人人物卡透视由 `GameUIView` 绑敌人槽点击（`AttackEnemyAtSlot` 在透视中会转去 `TryXRayEnemySlot`）。

---

## 牌桌动画 `CardTableAnimator`

`Bind(hud, resources)` 找节点：

```
GameHud
  dealpoint          ← CardDealPoint，52 张洗牌
  mineNode           ← 玩家 carpoint1–5
  otherNode          ← 当前 DisplayedEnemy 的 carpoint1–5
```

三人仍各有一手数据。`DisplayedEnemy`：比牌/攻击跟当前对手，透视跟已透视座位，否则第一个存活敌人。展示对象变了但未重新发牌时，把 otherNode 上 5 张换成新座位的牌，不整桌重发。

`Sync(session)` 顺序：

1. `otherNode` 有当前存活对手才显示
2. `DealSerial` 变了 → 洗牌 + 飞牌到玩家与当前敌人，结束发 `DealFinished`
3. 展示敌人变了 → `PlaceEnemyHand` 就地换牌
4. `RevealPlaySerial` 变了 → 玩家选中牌保持抬起；敌人不抬，只翻已锁定的 3 张，再播结算 clip
5. 平时：同步正反面 → **先抬选中牌** → **透视的牌抬完再 `SetBackSeeThrough`**

选中位移：玩家向上，敌人（上方 otherNode）向下。

透视不要在 `SyncSeatFaces` 里立刻透牌，否则会和抬牌同时播。关掉透视、真正开牌翻面时立刻 `SetBackSeeThrough(false)`。

---

## 注意

- 主路径只用这一套，不要在 `GameUIView` 里再自己 Instantiate 牌。
- `HitPlayerCard` / `HitEnemySlot` 用 `Physics2D`，牌上要有 Collider（发牌时 `EnsureCollider`）。点到 otherNode 返回 `DisplayedEnemyVisualSlot`。
- 发牌没结束不要让玩家点透视/选牌：`IsBusy` 会挡住 `Update`。
