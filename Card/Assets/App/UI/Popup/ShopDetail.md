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
2. 按钮处散落 `coinitem` → 停 0.5s → 飞向 `GameResourceBar` 金币图标。
3. 到位后 `ReleaseHeldGold(卖价)`，资源栏滚动加金，再关详情。

卖价为 0 或飞币播不出来时直接加金并关闭。出售进行中不因「已不拥有该遗物」把详情关掉，避免动画被掐断。飞币期间购买 / 出售 / Mask 锁定。中途关闭会把暂扣金币立即加回资源栏。

飞币挂在 `UILayer.TopMost`。挂 Resource 会被本页遮罩挡住。

---

## 购买

金币不够或遗物已满（`RelicCarryMax`）失败，满员走 Toast「遗物已满」。广告购买走 `WatchAdBuyShopRelic`。买成功后货架列表不再含该 Id，`OnSessionChanged` 会关掉详情。购买不加飞币。
