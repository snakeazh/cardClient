# CardDealPoint 使用文档

发牌点叠牌。一局开始时在 `dealpoint` 上叠满 52 张背面牌，只把本局座位需要的牌飞出去，剩余留在牌堆。

脚本：`Assets/App/Game/CardDealPoint.cs`  
动画：`Assets/App/UI/Game/CardTableAnimator.cs`  
命名空间：`App.Game`

在场景节点上挂脚本即可，**不需要 PrefabBuilder**。

---

## 场景准备

1. `GameHud` 下要有名为 `dealpoint`（或 `DealPoint`）的节点。
2. 在该节点上挂 `CardDealPoint`。没挂的话，运行时 `CardTableAnimator.Bind` 会自动补上（偏移用默认值）。
3. 各座位节点（`mineNode` / 敌人节点）的 `cardNode` 下要有落点：玩家 `carpoint1`–`carpoint5`，敌人 `carpoint1`–`carpoint3`（也认 `cardpoint` / `CardPoint`）。
4. 单张牌外观用现有资源 `Resources/Game/icon/CardIcon`，运行时实例化，不用再生成预制体。

---

## Inspector

| 字段 | 默认 | 说明 |
|------|------|------|
| Offset X | `0.04` | 每张牌相对下一张的水平偏移 |
| Offset Y | `-0.03` | 每张牌相对下一张的垂直偏移 |
| Sorting Order Start | `1` | 最底下那张的 sortingOrder |
| Sorting Order Step | `1` | 往上每张加多少 |

index `0` 在发牌点原点，index `n` 的本地坐标是 `(OffsetX * n, OffsetY * n, 0)`。后放上去的在最上面。

编辑器里改偏移时，已作为子节点的 `CardItem` 会跟着重排，方便预览。

运行时：

```csharp
dealPoint.SetOffset(0.06f, -0.04f);
dealPoint.OffsetX = 0.02f;
dealPoint.OffsetY = -0.02f;
```

---

## 发牌流程

由 `CardTableAnimator` 在 `DealSerial` 变化时播放：

1. 清掉上一局的牌。
2. 在发牌点叠 **52** 张背面牌（`Deck.Size`），先全部隐藏。
3. 洗牌动画：每张间隔 **0.05s** 依次显示；前 51 张播 `aini_card_appear01`（0.1s），堆顶最后一张播 `aini_card_appear02`（0.2s）。最后一张播完再发牌。
4. 按座位轮发：玩家 5 张、每个还在局中的敌人 3 张，从牌堆**最上面**抽出，飞到对应 `carpoint`。抽出时堆顶播 `aini_card_deal`（约 0.23s，与飞牌同时），露出的下一张播 `aini_card_back` 高亮。
5. 发够本局所需张数后停止。剩下的牌留在 `dealpoint`，不再飞出。

例如 1 名玩家 + 3 名敌人 = 飞出 14 张，牌堆留 38 张。敌人减少则飞出更少。

下一局开始会清空牌堆，重新叠 52 张。

逻辑层 `GameSession` 每局 `new Deck()`（52 张洗牌），给座位发 3 张；表现层这 52 张是牌堆外观，飞出的那几张再 `SetCard` 成该座位手里的牌。

---

## API

```csharp
dealPoint.Attach(item);          // 放到堆顶并按偏移排列
var top = dealPoint.Peek();      // 看堆顶，不取出
var item = dealPoint.GetCard(i); // 按叠放顺序取牌，0 在底
var item = dealPoint.Pop();      // 取出堆顶（解除父节点，保留世界坐标）
dealPoint.Relayout();            // 按当前偏移重排
dealPoint.Clear();               // 销毁堆里所有牌
dealPoint.Count;                 // 剩余张数
dealPoint.GetWorldPosition(i);   // 第 i 张应在的世界坐标
dealPoint.GetSortingOrder(i);    // 第 i 张的 sortingOrder
```

`Pop` 之后**不会**把剩下的牌重新挤回原点，看起来就是从堆顶揭走一张。

---

## 注意

- 牌堆上的牌不挂点击碰撞；飞到座位后才加 `BoxCollider2D`（搓牌/点敌人用）。
- 飞出中的牌 sortingOrder 会高于剩余牌堆，避免钻到堆下面。
- `_deck` 目前仍是 `GameSession` 私有字段，搓牌续抽不会自动减少牌堆上的剩余张数。
