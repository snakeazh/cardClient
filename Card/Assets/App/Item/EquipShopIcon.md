# EquipShopIcon 使用文档

商店货架 / 已购装备格。展示由 `Bind` 写入，点击通过 `Clicked` 抛出，买卖在 [`ShopDetail`](../UI/Popup/) 里完成。

脚本：`Assets/App/Item/EquipShopIcon.cs`  
预制体：`Assets/Res/UI/Icon/EquipShopIcon.prefab`  
色板：[`ThemeColors.md`](../ThemeColors.md)  
命名空间：`App.Game`

商品数据：[`RelicMechanics.md`](../Game/RelicMechanics.md)。局内 HUD 装备栏节点结构相同，但不走这张脚本，见 [`GameUI.md`](../UI/Game/GameUI.md)。

---

## 节点

| 节点 | 用途 |
|------|------|
| `bgcolor` | 品质底，走 `ThemeColors.EquipSlot` |
| `icon` | 遗物图标（`Altas/Relic`） |
| `frame` | 外框 |
| `nohave` | 空槽文案「空」；有遗物时隐藏 |

根上有 Button。`LayoutElement` preferred 145×145，给 `sellHor` 的 HorizontalLayoutGroup 用；`MineHor` 的 GridLayout 用 `CellSize 145`。

---

## 绑法

`BattleShopPopView` 货架和已购都走 `Bind(RelicConfig, icon)`：

```csharp
item.Bind(relic, ViewModel.GetRelicIcon(relic));
item.BindClick(OnClicked);
```

点击后 `BattleShopPopViewModel.OpenDetail(relicId, buying)` 打开 ShopDetail。空模板保持隐藏，不要拿空槽去点。
