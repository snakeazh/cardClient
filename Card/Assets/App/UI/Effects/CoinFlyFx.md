# 金币飞入 CoinFlyFx

按钮处散落若干 `coinitem`，停留后再飞向 `GameResourceBar` 金币图标。到位后才把暂扣的金币加回资源栏，数字走 [`RollingText.md`](RollingText.md) 滚动。

代码：`Assets/App/UI/Effects/CoinFlyFx.cs`  
预制体：`Assets/Res/UI/Icon/coinitem.prefab`  
资源键：`ResResourcePaths.CoinItem` = `UI/Icon/coinitem`

## 已接入位置

| 位置 | 文件 | 起点 | 飞币挂层 |
|------|------|------|----------|
| 关卡结算提现 / 双倍 | [`BattleSettleUpPop.md`](../Popup/BattleSettleUpPop.md) | `WithDrawBtn` / `DoubleBtn` | `UILayer.Resource`（关页后继续飞） |
| 商店出售 | [`ShopDetail.md`](../Popup/ShopDetail.md) | `SellBtn` | `UILayer.TopMost`（关页后继续飞） |

目标一律是局内 `GameResourceBar` 的 `ResourceItem/Icon`（`CoinFlyFx.FindGoldIcon()`）。

## 时序

```
散落 0.28s → 停留 0.5s → 飞入 0.5s（每枚错开 0.04s）→ onArrived（可选）
```

默认 8 枚。`SetUpdate(true)`，不受 timescale 影响。商店出售和结算提现 / 双倍关页不 Kill Sequence，金币继续飞完再销毁。

## 金币暂扣

`GameSession` 往往在飞币前就已经 `AddGold`。资源栏不能提前跳数，所以 `GameResourceViewModel` 用 `HeldGold` 把待飞入的数量扣掉再显示：

| API | 作用 |
|-----|------|
| `HoldGold(amount, refresh: false)` | 暂扣；`refresh: false` 时等随后的 `Notify` 一次刷新，避免闪一下 |
| `ReleaseHeldGold(amount)` | 飞币到位后释放；`amount ≤ 0` 全部释放 |
| 进入 `GamePhase.Shop` | 自动暂扣本关 `ShopGoldGranted`（结算提现用） |

显示值 = `max(0, Run.Gold - HeldGold)`。弹窗中途关掉必须把剩余暂扣释放掉，否则资源栏会少金。

## 新入口怎么接

1. 点击时先 `HoldGold`（若金币尚未入账则 `refresh: false` 后再 `AddGold`）。
2. `CoinFlyFx.Play(prefab, parent, fromWorld, toWorld, onArrived)`。
3. `onArrived` 里 `ReleaseHeldGold`。商店出售、结算提现 / 双倍改为飞币一开始就加金并关页，不必等飞完。
4. `parent` 必须不低于起点所在层，否则金币会被遮罩挡住。结算弹窗用 Resource；TopMost 详情用 TopMost。

```csharp
bar?.HoldGold(gold, refresh: false);
Session.SellShopRelic(relicId); // 内部 AddGold + Notify
CoinFlyFx.Play(_coinPrefab, parent, btn.position, CoinFlyFx.FindGoldIcon().position, () =>
{
    bar?.ReleaseHeldGold(gold);
});
```

预制体用 `IResourceService.LoadAsync<GameObject>(ResResourcePaths.CoinItem)`。实例关闭射线，避免挡住点击。

## 参数

| 常量 | 值 | 说明 |
|------|-----|------|
| `HoldDelay` | 0.5s | 散落后停留再飞 |
| `DefaultCount` | 8 | 枚数 |
| `ScatterRadius` | 90 | 散落半径（父节点本地像素） |
| `ScatterDuration` | 0.28s | 散落 |
| `FlyDuration` | 0.5s | 单枚飞入 |
| `FlyStagger` | 0.04s | 飞入错开 |
| `CoinScale` | 0.55 | 生成缩放；飞入时收到 0.45 倍 |
