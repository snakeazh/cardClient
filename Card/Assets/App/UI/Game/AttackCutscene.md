# AttackCutscene 使用文档

局内攻击演出。玩家打怪时人物卡冲向敌人；怪打玩家时敌人卡冲向人物。扣血数字由 GameUI 的 `hptext` 显示。

脚本：`Assets/App/UI/Game/AttackCutscene.cs`  
调用：`GameUIView.TryPlayAttack`  
规则：[`GameLogic.md`](../../Game/GameLogic.md)

---

## 绑定

`GameUIView.OnBind` 里把玩家卡和三名敌人卡交给演出：

```csharp
_attackFx.Bind(transform, _playerItem, _enemyItems);
```

位移根节点用各卡的 `PlayerItem.RootRect`，动画用 `RootAnimator`。

---

## 何时播放

`GameSession.AttackPlaySerial` 增加一次，播一段。结束回调 `CompletePlayerAttack()`，再结算伤害、进入下一对敌人。

| 情况 | 字段 | 方法 |
|------|------|------|
| 玩家赢，打当前怪 | `IncomingAttack == false` | `Play(visualSlot, level, …)` |
| 玩家输，当前怪打人 | `IncomingAttack == true` | `PlayIncoming(visualSlot, level, …)` |

`visualSlot` 是敌人在桌面上的视觉槽 0/1/2。`level` 为 1 低 / 2 中 / 3 高，由牌型映射。

---

## 动画片段

Animator 片段名：`ani_atk_lv{等级:D2}_{阶段}`，例如 `ani_atk_lv01_start`。

| 阶段 | 含义 |
|------|------|
| `start` | 起手 |
| `move` | 位移中 |
| `end` | 命中 |
| `hit` | 被打一方受击 |
| `back` | 退回 |
| `ani_default` | 复位 |

位移由 DOTween 驱动，不靠根节点动画位移。命中时显示 `-{AttackDamage}`：打怪贴在敌人卡上，挨打贴在玩家卡上。

---

## 伤害数字

逻辑伤害：

```
伤害 = (SeatState.Attack + HandScore.BaseChips) × HandScoreConfig.BasicMagnification × 遗物倍率
```

攻击力进关写入，玩家 `HeroConfig.HeroDamage`，怪物 `MonsterConfig.MonsterDamage`；牌面点数为亮出三张 `ChipValue` 之和（`HandScore.BaseChips`）。演出只表现已算好的 `AttackDamage`，不改公式。

---

## 注意

- 逐个比牌时不要等玩家点选敌人，结算后立刻 `BeginPlayerAttack` / `BeginIncomingAttack`。
- `Kill` 会把冲出去的卡拽回 `PlayerRoot` 父节点；换手或关界面要 `Dispose`。
- 槽位无效时跳过位移，仍走 onHit / onDone，避免卡死状态机。
