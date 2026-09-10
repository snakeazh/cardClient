# ShopDetail 使用文档

通关商店商品详情。货架点进来买，已购点进来卖。叠在商店弹窗之上。

脚本：`Assets/App/UI/Popup/ShopDetailView.cs`  
视图模型：`Assets/App/UI/Popup/ShopDetailViewModel.cs`  
预制体：`Assets/Res/UI/Top/ShopDetail.prefab`  
资源键：`ResResourcePaths.ShopDetail` = `UI/Top/ShopDetail`  
屏幕 Id：`AppScreenIds.ShopDetail`  
层：`UILayer.TopMost`

列表卡：[`ShopItem.md`](../../Item/ShopItem.md)  
飞币：[`CoinFlyFx.md`](../Effects/CoinFlyFx.md)  
购买飞入：[`ItemFlyFx.md`](../Effects/ItemFlyFx.md)  
打开：`BattleShopPopViewModel.OpenDetail(relicId, buying)`

---

## 显示

| 模式 | `Setup(id, buying)` | 按钮 |
|------|---------------------|------|
| 买入 | `buying: true` | `BuyBtn` + `VideoBuyBtn` |
| 出售 | `buying: false` | `SellBtn` |

`Item` 上的 `ItemCard` 关阴影和入场动画，只显示名字、图标、品质。价格文本买/卖共用 `PriceText`（买入走折后价，卖出走 `EffectiveSellPrice`）。

失败改 `Tip` 文案，成功后关掉自己。点 `Mask` 关闭。

---

## 节点

| 键 | 用途 |
|----|------|
| `Mask` | 点空白关闭 |
| `Item` | `ItemCard` |
| `Detail` | 遗物描述 |
| `Tip` | 默认「点击空白处以关闭」；失败时改提示 |
| `BuyBtn` / `BuyNum` | 购买与价格 |
| `VideoBuyBtn` | 看广告免费拿（当前为模拟发放） |
| `SellBtn` / `SellNum` | 出售与卖价 |

---

## 出售飞币

点 `SellBtn`：

1. `HoldGold(卖价, refresh: false)`，再 `SellShopRelic`（内部 `AddGold` + `Notify`）。
2. 按钮处散落 `coinitem` 飞向 `GameResourceBar` 金币图标（挂 `UILayer.TopMost`，不跟详情页绑定）。
3. 飞币一开始就 `ReleaseHeldGold` 并关详情，不等金币飞完。

卖价为 0 或飞币播不出来时直接加金并关闭。出售进行中不因「已不拥有该遗物」把详情关掉。中途关闭会把暂扣金币立即加回资源栏。

---

## 购买

金币不够或遗物已满（`RelicCarryMax`）失败，满员走 Toast「遗物已满」。广告购买走 `WatchAdBuyShopRelic`。

点 `BuyBtn` / `VideoBuyBtn`：

1. `BattleShopPopView.HoldIncomingMine(relicId)`，再 `TryBeginBuy`（内部 `BuyShopRelic` / `WatchAdBuyShopRelic` + `Notify`）。
2. MineHor 新格先占位、`CanvasGroup.alpha = 0`。克隆详情 `Item` 挂 `UILayer.TopMost`，详情根节点 `CanvasGroup.alpha = 0`（否则遮罩挡住装备栏）。
3. 卡片飞向新格（约 0.45s），到位后 `RevealIncomingMine` 显现并轻微 punch，再关详情。

飞不起来时直接显现并关闭。购买进行中不因「货架已无该 Id」把详情关掉，避免动画被掐断。飞入期间购买 / 出售 / Mask 锁定。中途关闭会把占位格立刻显现。

购买不加飞币。飞入见 [`ItemFlyFx.md`](../Effects/ItemFlyFx.md)。
