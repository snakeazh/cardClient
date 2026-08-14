# CardItem 使用文档

世界空间卡牌节点。负责贴图（正/背面）、世界变换，以及移动、翻面动画。

脚本：`Assets/App/Game/CardItem.cs`  
命名空间：`App.Game`

---

## 场景准备

1. 节点上挂 `CardItem`。
2. 自身或子节点需要有 `SpriteRenderer`。可在 Inspector 指定 `CurrentRenderer`；留空则运行时自动查找。
3. 正面贴图按 `Card.ResourceId` 从图集 `Altas/Card` 取（如 `101` = 红心 A）；背面为 `CardBack`。启动时由 `IAtlasService` 预加载。见 `CardSpriteLibrary`。

```csharp
using App.Game;
using DG.Tweening;
using UnityEngine;
```

---

## 朝向状态

```csharp
public enum CardFaceState
{
    Front, // 正面
    Back   // 背面
}
```

当前朝向：`item.FaceState`（只读，由 `Initialize` / `SetFace` / `Flip` / `FlipTo` 更新）。

---

## 初始化

必须先 `Initialize`，再移动或翻面。

```csharp
var card = new Card(Suit.Heart, Rank.Ace);
var worldPos = new Vector3(0f, 0f, 0f);
var worldRot = Quaternion.identity;
var scale = Vector3.one;

item.Initialize(card, CardFaceState.Back, worldPos, worldRot, scale);
```

欧拉角重载：

```csharp
item.Initialize(card, CardFaceState.Front, worldPos, new Vector3(0f, 0f, 15f), scale);
```

| 参数 | 说明 |
|------|------|
| `card` | 牌数据，用 `ResourceId` 取正面图 |
| `faceState` | 初始朝向 |
| `worldPosition` | 世界坐标 |
| `worldRotation` / `worldEulerAngles` | 世界旋转 |
| `scale` | **本地**缩放 |

初始化会立刻改 `transform` 并换贴图，**没有动画**。

---

## 立刻换面（不改旋转）

```csharp
item.SetFace(CardFaceState.Front);
item.SetFace(CardFaceState.Back);
```

只改 `FaceState` 和 Sprite，**不会**改旋转。搓牌过程中途停动画时，不要指望 `SetFace` 把侧立的牌转回来。

---

## 翻面（绕本地 Y 轴）

转到 90°（侧立）时换贴图，再转回原朝向，避免背面被镜像。

```csharp
item.Flip();                              // 翻到另一面，默认 0.35s
item.FlipTo(CardFaceState.Front);         // 翻到正面
item.FlipTo(CardFaceState.Back, 0.4f, Ease.InOutSine);

item.Flip().OnComplete(() =>
{
    Debug.Log("翻面结束: " + item.FaceState);
});
```

| 方法 | 行为 |
|------|------|
| `Flip(duration, ease)` | 正 ↔ 背 |
| `FlipTo(faceState, duration, ease)` | 翻到指定面 |

- 默认时长 `0.35`，缓动 `Ease.InOutSine`。
- `duration <= 0` 时等同 `SetFace`，返回 `null`。
- 再次调用会先停掉上一次翻面。

---

## 移动到目标位置

世界坐标平移，使用 DOTween。

```csharp
item.MoveTo(new Vector3(1.2f, 0f, 0f), 0.35f);
item.MoveTo(target.position, 0.5f, Ease.InOutCubic).OnComplete(() => { });
```

默认缓动 `Ease.OutQuad`。重复调用会先停掉上一次移动。移动和翻面互不影响，可以同时播。

---

## 换牌数据 / 缩放抖动

```csharp
item.SetCard(new Card(Suit.Spade, Rank.King)); // 只换数据与贴图，不改位置/旋转
item.PunchScale(0.22f, 0.32f);                 // DOTween 缩放抖动
```

---

## 发牌点

叠牌、52 张牌堆、X/Y 偏移见 [CardDealPoint.md](CardDealPoint.md)。在场景 `dealpoint` 节点上挂 `CardDealPoint` 即可，不需要 PrefabBuilder。

---

## 组合示例

发牌：背面落到桌面，再翻开。

```csharp
var start = deckAnchor.position;
var end = slotAnchor.position;

item.Initialize(card, CardFaceState.Back, start, Quaternion.identity, Vector3.one);
item.MoveTo(end, 0.4f).OnComplete(() =>
{
    item.FlipTo(CardFaceState.Front, 0.3f);
});
```

盖牌：

```csharp
if (item.FaceState == CardFaceState.Front)
{
    item.FlipTo(CardFaceState.Back);
}
```

---

## 注意

- 节点销毁时会自动 `Kill` 移动和翻面 Tween。
- 翻面中途 `Kill` 可能停在 Y≈90° 侧立；需要复位请重新 `Initialize` 或自己改 `transform.rotation`。
- `Card.IsFace` 表示 J/Q/K（人头牌），与 `CardFaceState`（正反面）不是一回事。
