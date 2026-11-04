# 牌型倍率提升提示 HandTypeMagUpFx

使用消耗品把「某牌型永久倍率」拉高时的一次性提示：弹出一张牌型卡（图标 + 提升后的倍率），
复用它本来就有的表现——`cardinfoItem` 预制体上的 Animator 弹跳、`UiFlowLight` 扫光、PokerHand 粒子。

代码：`Assets/App/UI/Effects/HandTypeMagUpFx.cs`
共用特效开关：`Assets/App/UI/Effects/CardInfoFx.cs`
倍率快照/求差：`Assets/App/UI/Game/HandTypeMagDelta.cs`

## 已接入位置

| 位置 | 文件 | 触发 |
|------|------|------|
| 商店「使用」已购消耗品 | [`ShopDetailView.OnUseClicked`](../Popup/ShopDetailView.cs) | `useBtn` |
| 商店「购买并使用」 | [`ShopDetailView.BeginBuyAndUse`](../Popup/ShopDetailView.cs) | `buyUseBtn` / `buyUseVideoBtn` |

对局中途通过遗物列表（`RemainListPop` / `ItemTip`）使用消耗品暂未接。

## 时序

```
使用前后拍 Run.HandTypeMagForever 快照
  → 求差得到「哪几种牌型涨了多少」（随机 3 种也能全部抓到）
  → 每个牌型克隆一张 cardinfoItem 挂到 UILayer.TopMost 居中
  → Animator 0.3s 弹跳（playOnAwake 的粒子同时爆发）
  → 停留 0.9s
  → 0.25s 淡出并销毁
```

`SetUpdate(true)`，不受 `Time.timeScale` 影响。多牌型按 84px 行距竖排（时光宝盒一次 3 行）。

## 怎么接

```csharp
var parent = ResolveFxParent();   // UILayer.TopMost，不能挂在会关闭的详情页上
var run = ViewModel.Session?.Run; // 与 GameSession 同一个对象，使用后原地变化
HandTypeMagDelta.Capture(run, before);
Session.UseRelic(relicId);
HandTypeMagDelta.Diff(before, run, raises);
HandTypeMagUpFx.Play(parent, prefab, run, raises, atlas, Vector2.zero);
```

三个必须在「使用」之前取好的东西：**父节点、预制体、Run 引用**。
消耗品用掉后详情页会立刻 `Dismiss`，`ViewBase.ViewModel` 被置空，事后再读就全成 null。

## 参数

| 常量 | 值 | 说明 |
|------|----|------|
| `RowGap` | 84 | 多行竖排间距 |
| `Scale` | 0.9 | 加在外层 holder 上；`cardinfoItem` 的 Animator 会直接写根的 localScale，缩放只能套外面 |
| `HoldSeconds` | 0.9 | 入场后停留 |
| `FadeSeconds` | 0.25 | 淡出 |
| `FlowFrequency` | 0.8 | 扫光频率（HUD 常驻是 0.3Hz，太慢看不到完整一遍） |

## 注意

- 显示的是「基础倍率 + 永久加成」，与规则表 `WinTip` 同口径；**不要用 `GameSession.HandTypeMagnification`**，那只含基础倍率。
- 克隆体上的 `UIBind` 必须销毁，`raycastTarget` 必须关掉（否则 1 秒内挡住商店点击）。
- 特效挂在 TopMost 层根节点，不随详情页生命周期销毁。
- 预制体走资源键 `ResResourcePaths.CardInfoItem = UI/Icon/cardinfoItem`，进页预载。
- `HandType` 是 0 基枚举（`HighCard = 0`），配置表 `HandType` 是 1 基，下标一律用运行时枚举。
