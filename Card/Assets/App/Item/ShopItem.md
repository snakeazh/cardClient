# ShopItem 使用文档

带名字和价格的商店卡。当前通关商店货架 / 已购改走 [`EquipShopIcon.md`](EquipShopIcon.md)，买卖在 ShopDetail；本卡脚本保留，不要再接到 BattleShopPop。

脚本：`Assets/App/Item/ShopItem.cs`  
预制体：`Assets/Res/UI/Icon/ShopItem.prefab`（资源键 `ResResourcePaths.ShopItem` = `UI/Icon/ShopItem`）  
色板：[`ThemeColors.md`](../ThemeColors.md)  
命名空间：`App.Game`

商品数据：[`RelicMechanics.md`](../Game/RelicMechanics.md)。局内已携带栏不走这张卡，见 [`GameUI.md`](../UI/Game/GameUI.md)。

---

## 节点

预制体序列化字段优先；`IconBG` / `IconTitleBG` / `card_Circle` 未拖引用时按名查找。

| 节点 | 用途 |
|------|------|
| `IconBG` | 卡片底，按 `RelicConfig.Type` 品质色 |
| `IconTitleBG` | 标题底，品质 title 色 |
| `card_Circle` | 跟 title 同色 |
| `card_icon` | 商品图标（`Altas/Relic`） |
| `card_Name` | 名字 |
| `gold` / `goldNum` | 金币图标与价格；出售绑定时写 `SellingPrice` |
| `ShopRoot` | Animator：`ani_shop_choose_start` / `ani_shop_choose_end` |

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

`Bind(RelicConfig)` 会按 `relic.Type` 给底 / title / 圈上色。`relic == null` 回退普通品质。

点击：`BindClick` / `Clicked`。拖拽：`BindDrag` / `IBeginDragHandler` 等（通关商店已不再用拖拽）。

---

## 注意

- 商店商品只认 `RelicConfig`，不要再做第二套商品表。
- 通关商店列表用 `EquipShopIcon`，详情用 `ShopDetail` 的 `ItemCard`。
- 编辑器菜单 `Tools/Wire ShopItem Prefab` 会把节点挂到序列化字段。
- 图鉴遗物页用的是 `ItemCard`，不是 `ShopItem`。
