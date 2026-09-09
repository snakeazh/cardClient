# 商品飞入 ItemFlyFx

克隆一张 UI 卡，从详情卡飞向目标格子并缩放到目标尺寸。用于通关商店购买后飞入「我的装备」。

代码：`Assets/App/UI/Effects/ItemFlyFx.cs`

## 已接入位置

| 位置 | 文件 | 起点 | 终点 | 挂层 |
|------|------|------|------|------|
| 商店购买 | [`ShopDetail.md`](../Popup/ShopDetail.md) | 详情 `Item` / `ItemCard` | `BattleShopPop` 的 MineHor 新格 | `UILayer.TopMost` |

## 时序

```
成交 → 新格占位透明 → 克隆卡片 → 隐藏详情遮罩 → 飞入 0.45s → 显现新格 → 关详情
```

`SetUpdate(true)`，不受 timescale 影响。关闭视图时 Kill Sequence，克隆一并销毁。

## 怎么接

1. 目标列表先 `HoldIncomingMine`，再成交（`Notify` 时新格 `alpha = 0` 占位）。
2. `ItemFlyFx.Play(source, parent, destination, onArrived)`。
3. `parent` 必须不低于起点所在层。ShopDetail 在 TopMost，克隆也挂 TopMost，然后把详情 `CanvasGroup.alpha` 置 0，才能看见 Popup 层的 MineHor。
4. `onArrived` 里 `RevealIncomingMine`，再关详情。
5. 飞不起来或中途关闭必须 Reveal，避免格子永远透明。

```csharp
shop.HoldIncomingMine(relicId);
if (!vm.TryBeginBuy(watchAd: false))
{
    shop.ClearIncomingMine();
    return;
}

ItemFlyFx.Play(cardRect, topMost, shop.GetIncomingMineRect(relicId), () =>
{
    shop.RevealIncomingMine(relicId);
    vm.CompleteBuy();
});
```

实例关闭射线和 Button / Animator，避免挡住点击或播入场动画。克隆会触发 `ItemCard.Awake` 把卡面重置成普通品质，因此飞入前按源卡 `Quality` 再 `ApplyQuality` 一次。

## 参数

| 常量 | 值 | 说明 |
|------|------|------|
| `FlyDuration` | 0.45s | 位移 + 缩放到目标 |
