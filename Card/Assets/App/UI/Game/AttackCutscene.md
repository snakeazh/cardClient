# AttackCutscene 使用文档

局内攻击演出。玩家打怪时人物卡冲向敌人；怪打玩家时敌人卡冲向人物。扣血数字由 GameUI 的 `hptextdi` 显示（数字写在子节点 `hptext`，弹出动画 `ani_hptextdi`）。

脚本：`Assets/App/UI/Game/AttackCutscene.cs`  
调用：`GameUIView.TryPlayAttack`  
规则：[`GameLogic.md`](../../Game/GameLogic.md) · 遗物与伤害公式：[`RelicMechanics.md`](../../Game/RelicMechanics.md)

---

## 绑定

`GameUIView.OnBind` 里把玩家卡和三名敌人卡交给演出：

```csharp
_attackFx.Bind(transform, _playerItem, _enemyItems);
```

位移根节点用各卡的 `PlayerItem.RootRect`，动画用 `RootAnimator`。冲锋时把 `PlayerRoot` 挂到 HUD 下的 `AttackFlight`（盖住 mask，且位移不和 Animator 抢节点）。`AttackFlight` 要带上原父节点相对 HUD 的缩放，人物卡和敌人卡都是；`PlayerRoot` 保持 `localScale = 1`，否则会从 0.78 放大到 1。

---

## 何时播放

`GameSession.AttackPlaySerial` 增加一次，播一段。受击开始回调 `ApplyPendingAttackHits()` 扣血；结束回调 `CompletePlayerAttack()`，进入下一对敌人。

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
| `hit` | 被打一方受击，同时卡被击退再回位 |
| `back` | 退回 |
| `ani_default` | 复位 |

位移由 DOTween 驱动，不靠根节点动画位移。命中时显示 `-{TakenDamage}`：打怪贴在敌人卡上，挨打贴在玩家卡上。闪避成功（`LastAttackMissed`）时 `hptext` 显示 `MISS`，不播 `ani_hptextdi`。

`start` 片段、转向瞄准、后撤蓄力三者同时开始，共用 `StartDuration`；`start` 播完才接 `move` 冲撞。

受击方在命中那一刻播 `hit`，同时朝攻击方的反方向被击退，再回原位。击退位移做在受击卡的**根节点**（`PlayerItem` 的 RectTransform）上，`hit` 片段驱动的是它下面的 `PlayerRoot`，两者不抢同一个 transform，也不用挂飞行层——挪走 `PlayerRoot` 会让 `UiDissolve` 收集不到卡面图形，致死溶解就失效了。击退用 `Sequence.Insert` 插在命中时刻（`StartDuration + MoveDuration`），和定格、攻击方退回并行，不改变整段总时长。

---

## 死亡演出

这一击把血量打到 0 时，命中那一刻立刻冒致死特效，溶解则延迟 `DeathDissolveDelay`（默认 2 秒）才开始。非致死两者都不播。

判定在 `GameUIView.TryDissolveIfLethal`，在 `onHit` 里**先**按扣血前的 `Hp <= damage` 排队溶解，**再** `ApplyPendingAttackHits` 真正扣血。这样 `ShowEnemy` 翻 false 时 `_deathDissolves` 已经占位，不会被 `BindEnemyVisible` 抢着溶掉。玩家挨打和打怪两个分支各来一次，特效即时、溶解排队：

```csharp
_attackFx.PlayDeathEffect(_attackFx.HitPosition(session.AttackVisualSlot));
ScheduleDeathDissolve(AttackItemAtSlot(session.AttackVisualSlot), hideWhenDone: true);
```

`PlayDeathEffect` 把 `DeathEffect` 实例化到 HUD 末尾，位置取被打者 `RootRect` 的当前世界坐标（此刻正在击退位移中，所以落在撞击点上），按 `UiFx` 写 `sortingOrder` 并重播粒子、清拖尾。它不进攻击序列的时间轴，`DeathEffectDuration` 到点自己销毁。注意 `Kill` 会清残留特效，而下一段攻击约 1.55 秒后就开始并触发 `Kill`，所以 `DeathEffectDuration` 调超过这个数会被下一段攻击掐掉。特效 prefab 由 SO 直接引用，AssetBundle 会带上依赖，不用另外注册资源路径。

延迟到点后 `PlayDeathDissolve` 才 `PlayerItem.PlayDissolve`，`hideWhenDone` 为真（怪）溶完 `HideEnemyItem` 隐藏节点，为假（玩家）留着。

**结算节奏不跟着等。** 命中时就已经扣血，`ShowEnemy` 会立刻翻 false；命中后再过 `HitHoldDuration + BackDuration + HpTextHoldDuration`（约 1.55 秒）才 `onDone` 进下一个对手。溶解默认还要再等 2 秒。`BindEnemyVisible` 必须给延迟让位：

```csharp
if (_deathDissolves.ContainsKey(item))
{
    return;
}
```

漏了这一步，卡会在 1.55 秒被抢着溶掉，延迟等于没有。`_deathDissolves` 记 item 到延迟 tween 的映射，`ShowEnemy` 翻回 true（换关、重开）时 `CancelDeathDissolve` 撤掉，避免延迟落到复用后的新怪身上；`OnViewClose` 走 `CancelAllDeathDissolves`。延迟 tween 还 `SetLink(item.gameObject)` 兜一层。

代价是延迟这 2 秒里下一段攻击已经开始，死掉的卡冒完烟还会站着不动一会儿才化；玩家死亡时战斗失败弹窗也可能先盖上来。要改成“等溶解播完再结算”得把 `onDone` 推后，那是另一套节奏。

---

## 参数配置

时长和后撤距离在 `Assets/Res/SO/AttackTuning.asset`（`AttackTuningConfig`），策划直接在 Inspector 改，每个字段带中文 Tooltip。

| 字段 | 含义 |
|------|------|
| `StartDuration` | 起手阶段时长，起手片段 / 瞄准 / 后撤共用 |
| `MoveDuration` | 冲撞到目标的时长 |
| `HitHoldDuration` | 命中后定格停留 |
| `BackDuration` | 退回原位时长 |
| `RetreatDistance` | 后撤蓄力距离（UI 像素） |
| `HitKnockbackDistance` | 受击方被击退的距离（UI 像素） |
| `HitKnockbackDuration` | 受击方被击退的时长 |
| `HitRecoverDuration` | 受击方从击退位置回原位的时长 |
| `HpTextHoldDuration` | 退回后伤害数字继续停留，过完才扣血 |
| `deathDissolveDelay` | 致死后等多久才开始溶解和播致死特效 |
| `deathEffect` | 致死时在被打者位置播的特效 prefab，留空不播 |
| `deathEffectDuration` | 致死特效的存活时长，到点销毁 |

前八项低 / 中 / 高三档各一组，对应 `AttackLevel` 的 1 / 2 / 3；后四项在“通用”段，不分档。

加载：`AppBootstrap` 启动时 `await AttackTuningConfig.PreloadAsync(resources)` 预热一次，和 `CardShadowPool.PreloadAsync` 同级；`AttackCutscene` 直接读 `AttackTuningConfig.Instance`，不再逐次开界面加载。编辑器下走 AssetDatabase，改完重进游戏生效；出包要先跑一次 `Res/Build AssetBundles`。资产丢了或预热失败只打 Warning，`Instance` 退回字段默认值继续演出。

`MoveDuration` / `BackDuration` 是固定时长而非固定速度，三个敌人槽位距离不同，远的槽位飞得更快。

---

## 伤害数字

逻辑伤害：

```
伤害 = (SeatState.Attack + 遗物攻击加成) × (HandScoreConfig.BasicMagnification + 遗物倍率加成)
```

细则见 [`RelicMechanics.md`](../../Game/RelicMechanics.md)。

攻击力进关写入，玩家 `HeroConfig.HeroDamage`，怪物 `MonsterConfig.MonsterDamage`；牌面点数不再默认进伤害，由指定圣物加成。怪物卡上的攻击数字读减伤前的 `AttackDamage`；命中飘字读减伤后的 `TakenDamage`（打出多少显示多少，不按受击者剩余血量截断）。积分也按该攻击数值记，不按实际扣血。

---

## 注意

- 逐个比牌时不要等玩家点选敌人，结算后立刻 `BeginPlayerAttack` / `BeginIncomingAttack`。
- `Kill` 会把冲出去的卡拽回 `PlayerRoot` 父节点；换手或关界面要 `Dispose`。
- 不要把 `AttackFlight` 的缩放写成 1 后直接挂 `PlayerRoot`；飞行层必须跟原父节点（`PlayerItem` 的 0.78）对齐。
- 槽位无效时跳过位移，仍走 onHit / onDone，避免卡死状态机。
- 时长不要写回代码常量，一律加到 `AttackTuningConfig` 让策划调。
- 受击击退不要挂飞行层、不要动 `PlayerRoot` 的父子关系，否则致死溶解会失效。
- 击退 + 回位的总时长若超过 `HitHoldDuration + BackDuration + HpTextHoldDuration`，整段会被拉长，`onDone` 跟着延后。
- 致死特效不要 `Insert` 进攻击序列：致死是 `onHit` 运行时才知道的，序列早已建好；独立计时销毁即可。
- 改致死溶解的时机时，`BindEnemyVisible` 那条数据驱动的溶解路径要一起看，它比延迟更早触发。
- 粒子特效挂到 UI 下必须走 `UiFx.ApplySorting`，否则会被 UI 盖掉。
