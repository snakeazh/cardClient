# GameUI 使用文档

局内主 HUD。发牌后直接看牌，只显示开牌和技能；比牌与攻击由 `GameSession` 驱动。

脚本：`Assets/App/UI/Game/GameUIView.cs`  
视图模型：`Assets/App/UI/Game/GameTableViewModel.cs`  
牌桌世界：`Assets/App/UI/Game/GameBoardController.cs`  
规则：[`GameLogic.md`](../../Game/GameLogic.md)

`GameTableController` 是旧场景绑法，不是主路径。

---

## 结构

```
GameUI
  PlayerItem              ← 玩家卡，显示 HeroDamage / Hp
  敌人槽（运行时克隆 PlayerItem）
  horBtns                 ← 开牌 / 取消 / 下一局（发牌动画结束才显示）
  horBtns2                ← 搓牌 / 透视 / 替换
  roundInfo               ← 本轮 / 关卡 / 总积分
  mask / hptext           ← 攻击演出用
```

发牌期间 `ShowTableButtons=false`，`horBtns`、`horBtns2`、`roundInfo` 隐藏，发完再亮。

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
| PeekGood | 进入搓牌，点选一张随机替换 |
| ChaKanGood | 点选角色翻开其手牌 |
| TiHuanGood | 自己 5 张全部换成新牌 |

每手重置：搓牌 3 / 透视 1 / 替换 1（商店加成另加）。搓完或取消回到开牌阶段。

人物卡攻击力 / 血量见 [`PlayerItem.md`](../../Item/PlayerItem.md)。  
攻击冲锋见 [`AttackCutscene.md`](AttackCutscene.md)。

---

## 积分条

`roundInfo`：`本轮{Round} 关卡{Stage} 总{Total}`。  
本轮 = 这一手对怪造成的总伤害。不再显示奖池。

---

## 注意

- 开牌前不要露出闷注 / 看牌 / 跟注 / 加注 / 弃牌。
- 玩家点桌上手牌选中/取消，选满 3 张才显示开牌。
- 开牌后不要让玩家再点选攻击目标，队列自动打当前敌人。
- 敌人槽点击只用于透视，不用于选攻击目标。
