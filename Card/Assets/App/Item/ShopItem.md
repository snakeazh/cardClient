# ShopItem 使用文档

商店货架 / 已拥有列表单卡。展示由 `Bind` 写入，点击和拖拽通过事件抛出。

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

`BattleShopPopView` 货架和已拥有都走 `Bind(RelicConfig, icon, forSale)`：

```csharp
item.Bind(relic, ViewModel.GetRelicIcon(relic), buyPrice: session.EffectiveBuyPrice(relic.Id)); // 货架，显示折后买入价
item.Bind(relic, ViewModel.GetRelicIcon(relic), forSale: false); // 已拥有，显示 SellingPrice
```

`Bind(RelicConfig)` 会按 `relic.Type` 给底 / title / 圈上色。`relic == null` 回退普通品质。

```csharp
item.ApplyQuality(QualityType.Rare);   // 只换色
item.PlayChooseStart();
item.PlayChooseEnd();
```

点击：`BindClick` / `Clicked`。拖拽：`BindDrag` / `IBeginDragHandler` 等。

---

## 注意

- 商店商品只认 `RelicConfig`，不要再做第二套商品卡。
- 编辑器菜单 `Tools/Wire ShopItem Prefab` 会把节点挂到序列化字段。
- 图鉴遗物页用的是 `ItemCard`，不是 `ShopItem`。
