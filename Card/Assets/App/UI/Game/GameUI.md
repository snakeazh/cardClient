# GameUI 使用文档

局内主 HUD。发牌后直接看牌，只显示开牌和技能；比牌与攻击由 `GameSession` 驱动。

脚本：`Assets/App/UI/Game/GameUIView.cs`  
视图模型：`Assets/App/UI/Game/GameTableViewModel.cs`  
牌桌世界：[`GameBoardController.md`](GameBoardController.md)  
规则：[`GameLogic.md`](../../Game/GameLogic.md) · 状态机：[`GameSession.md`](../../Game/GameSession.md)

`GameTableController` 是旧场景绑法，见 [`GameTableController.md`](GameTableController.md)。

---

## 结构

```
GameUI
  backBtn                 ← 返回主页
  ResourceBar             ← 资源栏，当前只显示金币
  PlayerItem              ← 玩家卡，显示 HeroDamage / Hp
  敌人槽（运行时克隆 PlayerItem）
  horBtns                 ← 开牌 / 取消 / 下一局（发牌动画结束才显示）
  horBtns2                ← 搓牌 / 透视 / 替换
  horEquipBtns2           ← 已携带遗物（equip1/2/3）
  cardinfoItem            ← 结算时显示玩家牌型；敌人克隆到 cardInfoParent
  roundInfo               ← 第几轮
  arrow                   ← 当前行动对象指示（玩家 / player1/2/3 各一），上下缓动
  mask / hptext           ← 攻击演出用
```

发牌期间 `ShowTableButtons=false`，`horBtns`、`horBtns2`、`horEquipBtns2` 隐藏，发完再亮。`roundInfo` 局内一直显示第几轮。

`cardinfoItem` 只在结算（开牌比牌 / 攻击演出 / 本手结束）时显示：玩家用 HUD 上的原节点，敌人把同一套克隆到各自 `player1/2/3.cardInfoParent` 下。

| 节点 | 用途 |
|------|------|
| `cardtype` | 牌型图标，读 `Altas/CardType`（`HighCardIcon` / `PairIcon` / `StraightIcon` / `SameSuitIcon` / `FlushIcon` / `LeopardIcon`） |
| `cardtype2` | 牌型名称图（`HighCard` / `Pair` / `Straight` / `SameSuit` / `Flush` / `Leopard`） |
| `cardtypeNum` | 倍率文案，格式 `xn`，n 来自 `HandScoreConfig.BasicMagnification` |

`horEquipBtns2` 按 `Run.RelicConfigIds` 把遗物图标填进 `equip1`～`equip3` 的 `icon`（`Altas/Relic`）。空槽保留底框，并显示子节点 `nohave`；有装备则隐藏 `nohave`，点击在槽左侧弹出 `ItemTip`。

**不要再创建或绑定 `duelHint`。** 中间提示条已去掉。

---

## 当前可见按钮

发牌结束、`WaitingOpen`：

| 按钮 | 文案 | 何时显示 |
|------|------|----------|
| `CompareBtn` | 开牌 | `WaitingOpen`，且已选 3 张 |
| `PeekGood` / `ChaKanGood` / `TiHuanGood` | 搓牌 n / 透视 n / 替换 n | 始终在 `horBtns2`，没次数则禁用 |
| `CancelBtn` | 取消 | 搓牌中，跳过搓牌 |
| `NextRoundBtn` | 下一局 | `RoundSettle` |

以下节点仍在预制体里，当前流程**隐藏**：

`BlindBtn`（闷注）、`LookBtn`（看牌）、`RaiseBtn` / `RaiseHighBtn`、`AllInBtn`、`FoldBtn`。

---

## 一局操作

```
发牌动画结束
  → 玩家 5 张手牌已翻开
  → 点选 3 张（选中上移），可点技能，或点「开牌」
  → 用这 3 张与每名存活敌人逐个亮牌、打伤害（敌人从 5 张里自动选出最大 3 张，朝玩家方向移开）
  → 全灭：进商店；否则点「下一局」
```

技能：

| 按钮 | 效果 |
|------|------|
| PeekGood | 进入搓牌；点选一张翻到背面，拖拽抖动后松手换牌 |
| ChaKanGood | 点选角色：5 张先全部抬起，再 `SetBackSeeThrough` 透出牌面；开牌时改成最大 3 张 |
| TiHuanGood | 自己 5 张全部换成新牌 |

每手重置：搓牌 3 / 透视 1 / 替换 1（商店加成另加）。搓完或取消回到开牌阶段。

人物卡攻击力 / 血量见 [`PlayerItem.md`](../../Item/PlayerItem.md)。  
攻击冲锋见 [`AttackCutscene.md`](AttackCutscene.md)。

---

## 积分条

`roundInfo`：`第{n}轮`，n 为本关第几手（点「下一局」后递增，进下一关从 1 重计）。

HUD 根节点 `arrow` 指向玩家；`player1/2/3` 下各有一个 `arrow`。同一时间只亮当前对象：选牌/技能时是玩家，比牌和攻击时是当前敌人。亮起后沿 Y 轴上下缓动。

---

## 注意

- 开牌前不要露出闷注 / 看牌 / 跟注 / 加注 / 弃牌。
- 玩家点桌上手牌选中/取消，选满 3 张才显示开牌。
- 开牌后不要让玩家再点选攻击目标，队列自动打当前敌人。
- 敌人槽点击只用于透视，不用于选攻击目标。
