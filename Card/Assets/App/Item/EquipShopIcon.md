# EquipShopIcon 使用文档

装备格：图标 + 品质底 + 空槽。通关商店货架 / 已购已改走 [`ShopItem.md`](ShopItem.md)，本脚本留给其它仍用格子样式的界面。

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

根上有 Button。`LayoutElement` preferred 145×145。

---

## 绑法

```csharp
item.Bind(relic, sprite);
item.BindClick(OnClicked);
```

空模板保持隐藏，不要拿空槽去点。
