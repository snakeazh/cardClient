# ShopItem 使用文档

带名字和价格的商店卡。通关商店货架 / 已购走这张卡，点击后买卖在 ShopDetail。

脚本：`Assets/App/Item/ShopItem.cs`  
预制体：`Assets/Res/UI/Icon/ShopItem.prefab`（资源键 `ResResourcePaths.ShopItem` = `UI/Icon/ShopItem`）  
色板：[`ThemeColors.md`](../ThemeColors.md)  
命名空间：`App.Game`

商品数据：[`RelicMechanics.md`](../Game/RelicMechanics.md)。局内已携带栏不走这张卡，见 [`GameUI.md`](../UI/Game/GameUI.md)。

---

## 节点

预制体序列化字段优先；`IconBG` / `IconTitleBG` / `card_Circle` 未拖引用时按名查找。价格文本优先认 `priceValue`，没有再找旧名 `goldNum`。

| 节点 | 用途 |
|------|------|
| `Item` | 内嵌 `ItemCard`：名字、图标、品质底 |
| `IconBG` | 卡片底，按 `RelicConfig.Type` 品质色 |
| `IconTitleBG` | 标题底，品质 title 色（可关） |
| `card_Circle` | 跟 title 同色（可关） |
| `card_icon` | 商品图标（`Altas/Relic`） |
| `card_Name` | 名字 |
| `priceframe` | 价格底图 |
| `priceValue` | 价格数字；出售绑定时写 `SellingPrice` |
| `gold` | 可选金币图标；新卡没有这个节点 |
| `ShopRoot` | Animator：`ani_shop_choose_start` / `ani_shop_choose_end` |

根上和内嵌 `Item` 都有 Button。点击以根 `Clicked` 为准，内层 `ItemCard.Clicked` 会转发出去。

品质色不要写在本脚本里，走 `ThemeColors.ApplyCard`。

---

## 绑法

```csharp
item.Bind(relic, icon, buyPrice: session.EffectiveBuyPrice(relic.Id)); // 显示折后买入价
item.Bind(relic, icon, forSale: false); // 显示 SellingPrice
item.ApplyQuality(QualityType.Rare);
item.PlayChooseStart();
item.PlayChooseEnd();
```

`Bind(RelicConfig)` 会按 `relic.Type` 给底 / title / 圈上色。`relic == null` 回退普通品质。`RelicConfigId` 从 `Data` 读。

点击：`BindClick` / `Clicked`。拖拽：`BindDrag` / `IBeginDragHandler` 等（通关商店已不再用拖拽）。

`BattleShopPopView` 用预制体上的隐藏模板 `sellItem` / `mineItem`（外壳带缩放，里面是 ShopItem），克隆到 `sellHor` 和 `MineHor` 的 Content。货架 4 格走买入价，已购走 `forSale: false`，点开 [`ShopDetail.md`](../UI/Popup/ShopDetail.md)。拖拽会转发给父级 ScrollRect，所以 MineHor 点在装备上也能滑动。

---

## 注意

- 商店商品只认 `RelicConfig`，不要再做第二套商品表。
- 通关商店列表用 `ShopItem`，详情用 [`ShopDetail.md`](../UI/Popup/ShopDetail.md) 的 `ItemCard`。出售飞币见 [`CoinFlyFx.md`](../UI/Effects/CoinFlyFx.md)。
- 编辑器菜单 `Tools/Wire ShopItem Prefab` 会把节点挂到序列化字段（价格认 `priceValue`）。
- 图鉴遗物页用的是 `ItemCard`，不是 `ShopItem`。
