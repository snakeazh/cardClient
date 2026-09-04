# ThemeColors · 全局 UI 色板

路径：`Card/Assets/App/ThemeColors.cs`  
命名空间：`App`

卡片底、标题底、头像圈、装备槽底色都从这里取。**不要在界面脚本里再写 hex。**

品质枚举：`QualityType`（`Enums.g.cs`，来自 EnumConfig.xlsx）。  
人物卡：[`PlayerItem.md`](Item/PlayerItem.md) · 商店装备格：[`EquipShopIcon.md`](Item/EquipShopIcon.md) · 商店卡：[`ShopItem.md`](Item/ShopItem.md) · HUD 装备栏：[`GameUI.md`](UI/Game/GameUI.md)

---

## 品质色

`IconBG` 用底色，`IconTitleBG` 用 title。`card_Circle` 跟 title 同色。

| `QualityType` | 值 | IconBG（底） | IconTitleBG / card_Circle |
|---------------|----|--------------|---------------------------|
| Ordinary 普通 | 1 | `#FBF5DF` | `#F5E8B1` |
| Rare 稀有 | 2 | `#DFE8FB` | `#B1D1F5` |
| Epic 史诗 | 3 | `#F4DFFB` | `#CB91FF` |
| Legend 传说 | 4 | `#FFCD7D` | `#FF9243` |

未知品质回退普通。

```csharp
ThemeColors.ApplyCard(relic.Type, iconBg, iconTitleBg, cardCircle);
var bg = ThemeColors.ForQuality(type);
var title = ThemeColors.ForQualityTitle(type);
```

---

## 人物 / 敌人

| 用途 | 底 `IconBG` | Title / Circle |
|------|-------------|----------------|
| 人物 | `Player` = 普通底 `#FBF5DF` | `PlayerTitle` `#F5E8B1` |
| 敌人 | `Enemy` `#F6393C` | `EnemyTitle` `#B20003` |

```csharp
ThemeColors.ApplyCard(QualityType.Ordinary, iconBg, titleBg, circle);     // 人物
ThemeColors.ApplyCard(ThemeColors.Enemy, ThemeColors.EnemyTitle, ...);   // 敌人
```

`PlayerItem` 不再走这套染色：人物/敌人分 `card` / `enemycard` 节点，底图见图集与 `MonsterConfig`，见 [`PlayerItem.md`](Item/PlayerItem.md)。其它卡片仍可用上面的 `ApplyCard`。

---

## 装备槽

局内 `equip1`～`equip3` 的 `bgcolor`：

| 状态 | 颜色 |
|------|------|
| 有装备 | `ForQuality(RelicConfig.Type)`（只用底色，没有 title） |
| 空槽 | `EquipEmpty` 黑 |

```csharp
bgcolor.color = ThemeColors.EquipSlot(relic != null, relic != null ? relic.Type : QualityType.Ordinary);
```

---

## 谁在用

| 界面 | 节点 | 取色 |
|------|------|------|
| `EquipShopIcon` | `bgcolor` | `EquipSlot`（品质底 / 空槽黑） |
| `ShopItem` | `IconBG` / `IconTitleBG` / `card_Circle` | `RelicConfig.Type` |
| `PlayerItem` | 不染色 | 人物 `card` + 品质框；敌人 `enemycard` + BaseMap/HealthBar |
| `GameUIView` 装备栏 | `equipN/bgcolor` | 品质底 / 空槽黑 |

图鉴 `ItemCard` 目前还用选中橙，没有接这张表。

---

## 改色

只改 `ThemeColors.cs` 里的 hex。卡片三件套走 `ApplyCard`，不要只改其中一个 Image。
