# 数字滚动 RollingText

通用数字滚动效果：文本上的数字从当前显示值平滑滚动到新值，数值增加时附带轻微弹跳放大。
基于 DOTween 插值，不依赖 MonoBehaviour，生命周期由 `BindingContext` 托管，视图关闭自动停止，无 tween 残留。

代码：`Assets/App/UI/Effects/RollingText.cs`

## 已接入位置

| 位置 | 文件 |
|------|------|
| 顶栏资源栏（局外钱包 / 体力） | `App/UI/ResourceBar/ResourceBarBinder.cs` |
| 局内 GameResource 金币（`UIRoot/Resource`，含商城/购买/结算） | `App/UI/ResourceBar/GameResourceBarBinder.cs` |

飞币到位后资源栏加金也会走本滚动，见 [`CoinFlyFx.md`](CoinFlyFx.md)。

## 接入方式

任何 `ObservableProperty` 驱动的数字文本，把 `BindText` 换成 `BindRollingText` 即可（一行替换，ViewModel 不用动）：

```csharp
// 原：Binding.BindText(text, vm.GoldText);
Binding.BindRollingText(text, vm.GoldText);

// 自定义参数
Binding.BindRollingText(text, vm.GoldText, new RollingTextOptions
{
    Duration = 0.8f,
    Ease = Ease.OutCubic,
    PopOnRoll = false,                     // 关掉弹跳，纯滚动
    Formatter = v => v.ToString("N0"),     // 千分位显示
});

// 数值型源属性（ObservableProperty<int>）也可直接绑定
Binding.BindRollingText(text, vm.GoldCount);
```

支持 `TMP_Text`（主用）与旧版 `UnityEngine.UI.Text` 两种文本组件。
字符串源按整数解析（`long.TryParse`），解析失败（如 `"--"`、空值）原样直接显示、不滚动。

不走 MVVM 绑定、需要手动驱动时：

```csharp
var roller = new RollingText(someTmpText);
roller.Set(100);        // 直接显示，不滚动
roller.RollTo(350);     // 从当前显示值滚动到 350
roller.Dispose();       // 停止并还原缩放
```

## 行为细节

- 首笔值直接显示不滚动（`Subscribe` 的 emitCurrent 不会触发动画）。
- 滚动中目标再次变化（连续加金/扣金）：终止当前 tween，从当前显示值续滚到最新值。
- 数值减少（买遗物扣金）同样滚动；弹跳仅在数值增加时触发。
- tween 使用 unscaled time（`SetUpdate(true)`），不受 timescale 影响。
- 弹跳实现为放大到 `PopScale` 再回弹到绑定时的基础缩放，重复触发会先归位再重放，不会叠加。

## 参数默认值（RollingTextOptions）

| 参数 | 默认 | 说明 |
|------|------|------|
| `Duration` | 0.6s | 单次滚动时长，≤0 时直接设值 |
| `Ease` | OutQuad | 滚动缓动 |
| `PopOnRoll` | true | 增加时是否弹跳 |
| `PopScale` | 1.2 | 弹跳放大倍数 |
| `PopDuration` | 0.3s | 弹跳总时长，放大/回弹各半 |
| `Formatter` | null | 默认裸 `ToString()`，与项目现有金币格式一致 |
