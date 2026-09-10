# 纸面逐格展开入场 PaperRevealAnim

结算弹窗风格的入场动画：纸底面板自下方滑入淡现 → 标题回弹落定 → 静态块开局齐现 → 明细行逐行串行淡入、纸面随行逐格向下展开 → 行链播完一次补长露出汇总/按钮区（数字滚动同刻触发）。纯运行时代码、不改预制体，非 MonoBehaviour，节奏常量全部可调。

代码：`Assets/App/UI/Effects/PaperRevealAnim.cs`（从 BattleSettleUpPopView 抽取）

## 已接入位置

| 位置 | 文件 | 备注 |
|------|------|------|
| 关卡结算弹窗 BattleSettleUpPop | `App/UI/Popup/BattleSettleUpPopView.cs` | GrowBg=true（BG 随内容逐格长高+托底） |
| 闯关结算弹窗 BattleResultPopup | `App/UI/Popup/BattleResultPopupView.cs` | GrowBg=false（固定尺寸纸底，只做滑入/回落/淡入/行链） |

固定尺寸纸底（汇总区/按钮摆在滚动区外、BG 高度美术手摆）设 `GrowBg = false`：
组件不动 BG 的 sizeDelta、不做托底，只保留面板滑入、标题回落、块淡入与行链串行。

## 结构要求

预制体须为以下层级（Scroll View/Viewport 名字不敏感，代码只认 BG/Title/Content 三个节点）：

```
BG            九宫格纸底，居中锚点/轴心（动画只改 alpha / anchoredPosition / sizeDelta）
  Title       标题横幅（可无，传 null 即跳过回落动画）
  Scroll View 拉伸锚定在 BG 内
    Viewport  带蒙罩（RectMask2D/Mask），从上往下裁剪
      Content VerticalLayoutGroup 自上而下排列，顶锚
        …     各内容块（Content 的直接子节点 = 动画步骤）
```

## 接入方式

宿主 View 负责找节点、收集步骤、择机开播；动画本体只管播和复位：

```csharp
private PaperRevealAnim _entrance;
private readonly List<PaperRevealStep> _entranceSteps = new();

// 1) 懒建（节点齐全才建）
EnsureFitNodes();  // FindDeep 找 BG/Title/Content
if (_entrance == null && _bgRect != null && _contentRect != null)
    _entrance = new PaperRevealAnim(_bgRect, _titleRect, _contentRect, bottomEdge: 116f);

// 2) 收集步骤：按 Content 兄弟顺序（=布局显示顺序）；行块标 IsRow，其余为静态块
_entranceSteps.Clear();
foreach (Content 子节点) // activeSelf 的才收
    _entranceSteps.Add(new PaperRevealStep { Rect = child, IsRow = 名字前缀判定, OnShown = 数字滚动等 });

// 3) 开播（数据/行列表就绪后调用；Play 内部会 ForceRebuildLayoutImmediate 量高度）
_entrance.Play(_entranceSteps);

// 4) 每帧托底 BG 高度（LateUpdate；动画逐段展开期间内部自动抑制，播完恢复）
private void LateUpdate() => _entrance?.TickFitBgHeight();

// 5) 关闭/复用前必须 Kill：中断并复位到播完状态（残留半透明/错位/blocksRaycasts=false）
protected override Task OnViewClose() { _entrance?.Kill(); … }
```

节奏（滑入距离/时长、标题回落、块淡入间隔等）都是 `PaperRevealAnim` 上的 public 字段，按界面气质在构造后改。

## 规则与注意

- **淡入时刻 ≠ 空间位置**：静态块开局同帧齐现，但布局上位于行下方的静态块会被蒙罩挡到行链
  播完才真正露出；`OnShown`（如数字滚动）统一在纸面开到其区域时触发，避免"滚完了才被看见"。
- **BG 长高沿布局序累计**（蒙罩从上往下揭，可见范围只看"到此为止的块总高"）。行下方静态块的
  高度若提前计入，会把行区纸面过早掀开成大段空白——这是抽取前踩过的坑，规则已内化到实现里。
- **开播时机**：行/数据列表就绪后再 Play。若用"版本号 ObservableProperty"驱动刷新（如
  RoundRevision），订阅务必传 `emitCurrent: false`——默认回放当前值会在 OnBind 当场空播一次
  （框架时序 OnBind → VM.OnOpen），VM 建出的行接不进动画、alpha 全 1 同帧齐现。
- 行块 tween 已 `SetLink(KillOnDestroy)`：播放途中宿主重建行列表销毁行对象不会写已销毁组件。
- 播完序列会自动恢复 `BG.blocksRaycasts=true`（入场期间整棵子树对射线透明）；两条路径（播完/
  Kill）都会复位，按钮不会失联。
- 约束：BG 挂 TallScreenFitScale 类组件会写 localScale，面板不能做缩放动画；Content 由布局组
  驱动，子块只做淡入不做位移；按钮不缩放（ButtonAnim 首次按下缓存 localScale 当静止姿态）。
