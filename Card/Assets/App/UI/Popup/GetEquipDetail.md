# GetEquipDetail 使用文档

局内新解锁遗物的恭喜获得弹窗。局内只积压 Id，回主页后再弹，不再走 Toast。

脚本：`Assets/App/UI/Popup/GetEquipDetailView.cs`  
视图模型：`Assets/App/UI/Popup/GetEquipDetailViewModel.cs`  
预制体：`Assets/Res/UI/Popup/GetEquipDetail.prefab`  
资源键：`ResResourcePaths.GetEquipDetail` = `UI/Popup/GetEquipDetail`  
屏幕 Id：`AppScreenIds.GetEquipDetail`  
层：`UILayer.TopMost`

解锁服务：[`遗物解锁.md`](../../Unlock/遗物解锁.md)  
卡面：`ItemCard`（`Assets/App/Item/ItemCard.cs`）  
打开：`HomeViewModel.PresentPendingUnlocksAsync`

---

## 何时弹出

1. 局内 `UnlockConditionService.Report` 达标 → 新遗物 Id 写入积压列表，**不弹 UI**。
2. 结算后回主页，`HomeViewModel.OnOpen` 调用 `ConsumePendingUnlockRelicIds()`。
3. 有积压则 `Setup(ids)` 后 `Open` 本页；无积压跳过。

同一局里解锁多件，按解锁顺序一次带入，页内上下切换浏览。

---

## 显示

| 内容 | 来源 |
|------|------|
| 名字 / 描述 / 品质 | `RelicConfig` |
| 图标 | `Altas/Relic`（`RelicConfig.Icon`） |
| 获取方式 | `UnlockConditionConfig.Desc`；无条件兜底「商店购买获得」 |
| 个数 | `equipNum`：`(当前/总数)`，仅一件也显示 `(1/1)` |

`CongratulationsImg`、`Tip`（「点击空白处以关闭」）用预制体静态展示，代码不改。

---

## 交互

| 控件 | 行为 |
|------|------|
| `LeftBtn` | 语义为**上**：上一条（节点名仍为 LeftBtn） |
| `RightBtn` | 语义为**下**：下一条（节点名仍为 RightBtn） |
| `get` | 确定，关闭 |
| `Mask` | 点空白关闭 |

件数 ≤ 1 时隐藏上下按钮。切换只换文案/图标，不重播翻卡。

---

## 翻卡演出

首次打开对 `Item` 调 `ItemCard.PlayRewardReveal(quality, showChoukaEffect: false)`：

- 播 `ItemRoot` 翻面动画
- 按品质点亮天赋色特效（红/紫/蓝；普通无额外特效）
- **不**点亮 `ChoukaEffect01`（解锁入口与抽卡入口区分）

---

## 节点

| 键 | 用途 |
|----|------|
| `Mask` | 点空白关闭 |
| `Item` | `ItemCard` 遗物卡 |
| `Detail` | 遗物描述 |
| `AcquireMethod` | 获取方式文案 |
| `equipNum` | `(当前/总数)` |
| `LeftBtn` / `RightBtn` | 上 / 下切换（多件时显示） |
| `get` | 确定关闭 |
| `CongratulationsImg` | 恭喜获得图（常开） |
| `Tip` | 固定关闭提示（未进 UIReference，静态） |

绑定时用 `GetGameObject(key).GetComponent<T>()`。

---

## 打开示例

```csharp
var ids = _unlock.ConsumePendingUnlockRelicIds();
if (ids.Count == 0)
{
    return;
}

var registration = _ui.Registry.GetByViewModelType(typeof(GetEquipDetailViewModel));
var vm = (GetEquipDetailViewModel)_ui.Registry.CreateViewModel(registration);
vm.Setup(ids);
await _ui.Open(vm);
```

`Setup` 会过滤无效 Id（≤0 或配置不存在）。全部无效时列表为空，界面无内容，调用方应先判断 `Count`。
