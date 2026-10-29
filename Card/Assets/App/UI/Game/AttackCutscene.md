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
| 玩家输，当前怪打人 | `IncomingAttack == true` | PVE 走 `PlayIncoming(visualSlot, level, …)`；PVP 先试 `TryPlayPvpIncomingHeroSkill`（对方英雄特效，见「PVP 敌方来袭」），解析不出才回退 `PlayIncoming` |
| 怪打人且将死 | `IncomingAttackWouldKill` | 先 `OpeningCutscene.PlayLine` 敌人 BottomDialog「受死吧。」（逐字），收起后再 `PlayIncoming`。对白期间不开 mask。护身符/稻草/免疫不算将死；闪避在命中时才掷。MISS 后玩家 TopDialog 回「就这」，说完再进下一对 |

点数飞入（`SettlePointCutscene`）结束后才进攻击。将死台词插在点数飞入之后、冲锋之前。

### 闪避反击（武僧）

敌人冲过来打人、玩家闪避成功（MISS）时，如果英雄配了 `HeroConfig.MissAttackEffect`（反击特效）或 `hitAnimation`（敌人受击动画），`PlayIncoming` 里会改走 `StartDodgeCounter`（不再接原来的回位序列，避免抢 `AttackFlight`）：

1. **先把怪物退到屏幕中心 (0,0) 受击**——播 `back`，把 `AttackFlight` 补间到 `anchoredPosition = (0,0)`（`AttackFlight` 挂在 HUD 居中锚点上，所以 0,0 就是屏幕中心），清掉挂到 Flight 时留下的 local 偏移，`BackDuration` 走完；
2. **反击特效播在人物身上**（`_playerRoot`），过 `HeroEffectsConfig` 该行的 `hitTime`（武僧 114 = 0.5s）给中心的敌人播受击动画、出反击伤害字。特效走 `SpawnSkillFxAt(..., forceUiLayer: true)`，用人物卡的 UI 世界坐标；
3. 特效和动画都演完（取较长的）→ 把卡退回自己的格子（`homeAnchored`）→ `RestoreIncoming` 交回父节点 → `onCollisionDone` → `onReturned` → 伤害字停留 → `onDone`。

- 参数全部来自表：`MissAttackEffect` 取第一条 Attack 行的 `Effects` / `Time`（特效时长）/ `hitTime`（到中心后多久挨打），敌人受击动画取 `hitAnimation`（值要能在 `PlayerRoot.controller` 里找到）。
- 特效和受击动画**至少有一个真的存在**才走这套；都缺就退回普通受击流程。受击动画在控制器里找不到时只跳过动画并打 Warning，不会让 Animator 报错。
- 反击伤害本身仍由 `GameSession.ApplyDodgeCounter` 在命中时结算，这里只负责演出。
- 反击伤害字走 `onDodgeCounterHit`：走反击演出时在「敌人到中心后挨打那一帧」出，没走演出（没配或资源缺失）时就在闪避这一帧出。数值读 `GameSession.DodgeCounterDamage`（`ApplyDodgeCounter` 里遗物 `武林秘籍` + 英雄 `MissGetDamage` 实际打出的伤害之和），用 `PlaceSplashHpAt` 贴在敌人卡当前位置（中心点）。
- **PVP 同样演**：PVE 里 `GameUIView.PlayAttackCharge` 无条件 `BuildDodgeCounter(hero)`（`hero` 就是挨打方/闪避方英雄，闪避在命中时才掷）；PVP 只在 `session.LastAttackMissed` 时才建，没闪避就不传，免得多等一段反击时长。PVP 没有本地 `ApplyDodgeCounter`，`GameSession.BeginPvpAttack` 在「本刀闪避」时用 `ComputePvpDodgeCounterDamage` 按英雄 `MissGetDamage`（武僧 1 次 × 攻击 200%）+ 挨打方剩余血算出演出数字，公式与服务端 `PvpHeroRuntime.DodgeCounterDamage` 一致，**只用于出字**，真实扣血仍以服务端快照为准。对手带英雄特效的来袭走 `PlayHeroSkill` 时反击另走 `PlayPvpDodgeCounter`（见「PVP 敌方来袭」第 4 条）。
- **对方闪避我方攻击**（PVP）：`BuildPvpFoeDodgeCounter` 用对方英雄的 `MissAttackEffect` 组出 `DodgeCounterFx`；近战 `Play` / `PlayHeroSkill` / 齐射 `PlayVolleySkill` 命中帧判定 `missed` 后**接管原攻击序列**，改走 `StartEnemyDodgeCounter`（对齐 PVE `StartDodgeCounter` 的「定住」演出）：
  1. 把**被反击的我方卡**拉到屏幕中心（`AttackFlight` 补间到 `(0,0)`，近战则从冲撞位置过去）并定格，播 `back`；
  2. 在**敌方卡**位置播对方反击特效（`_flipFxY = true`，**上下翻**）；
  3. `hitTime` 后我方受击动画 + `-{反击伤害}`（`PlaceSplashHpAtPlayer`，贴在我方卡当前位置，不覆盖敌卡上的 MISS 字）；
  4. 特效演完把卡退回自己的格子 → `onCollisionDone` → `onReturned` → 伤害字停留 → `onDone`。原序列在接管时 `Kill`，不会重复收尾。

  反击伤害按对方英雄 `MissGetDamage` × 对方当前攻击算（读 `PvpFighterDto.Attack`，不能用被 `SetAttackCommand` 改写的敌卡攻击数字），按我方剩余血截断。PVP 的闪避结果开打前已知（`LastAttackMissed`），所以只在真闪避时才传 `dodgeCounter`，命中时不会白白多等一段反击时长。

| 例子 | 配置 |
|------|------|
| 武僧 | `MissAttackEffect = 114`（`WuSengAttack01`，Time 1.5 / hitTime 0.5），`hitAnimation = ani_ws_gj_hit` → 敌人退到屏幕中心（~0.3s）→ 人物处播 1.5s 特效，第 0.5s 起敌人受击 1s → 退回格子 |

`visualSlot` 是敌人在桌面上的视觉槽 0/1/2。`level` 为 1 低 / 2 中 / 3 高，由牌型映射。

玩家出刀若英雄配齐了攻击特效和命中特效，并且进桌时已经预载成功，走 `PlayHeroSkill`，不再冲刺。缺任一条、资源名为空、或预制体没进缓存，回退上面的 `Play`。每档 `HeroEffects*` 里必须 `Type = Attack` 和 `Type = Hit` **各一条**——只配多条 Attack（盗贼原本就是 110、111 两条 Attack）会解析失败、静默回退近战，进桌时的 `英雄特效未解析` 警告会点名。**例外：暗灵法师**（`HeroMechanics.ShadowMageHeroId`）在 `TryResolveEffects` 之前就被 `GameUIView.TryPlayHeroVolley` 截走，走「齐射」一节。**例外：圣骑士**（`PaladinHeroId`）`HeroEffects` 留空属正常：`SpecialEffect` 只给圣光燃烧；对子先圣光再通用近战。进桌预载只校验 SpecialEffect。PVE 的敌人（怪）来袭始终走 `PlayIncoming`；PVP 对手是英雄，来袭改走对方英雄的特效，见下面「PVP 敌方来袭」。配置从哪来见 [`HeroMechanics.md`](../../Game/HeroMechanics.md)。

---

## 英雄攻击特效

资源在 `Assets/Res/Effect/Character/{Effects}.prefab`，路径 `ResResourcePaths.HeroEffect`。`HeroConfig.HeroEffects` 指向 `HeroEffectsConfig`。`TryResolveEffects` 取第一条 `Type = Attack` 和第一条 `Type = Hit`。

### 普通怪 / 精英 与 弱 / 中 / 强

`HeroConfig` 里每条英雄有六组 Id，每组都是 `释放|命中` 混排，解析规则一样：取第一条 `Type = Attack` 和第一条 `Type = Hit`。

| 维度 | 组 | 选中条件 |
|------|----|----------|
| 目标类型·普通 | `HeroEffectsCommon` | 目标 `MonsterType = Normal` |
| 目标类型·精英 | `HeroEffectsElite` | 目标 `MonsterType = Elite / Boss`（领主算精英档） |
| 档位·弱 | `HeroEffectsWeak` | 档位 1 |
| 档位·中 | `HeroEffectsMid` | 档位 2 |
| 档位·强 | `HeroEffectsStrong` | 档位 3 |
| 默认 | `HeroEffects` | 兜底 |

**优先级：目标类型 → 档位 → 默认档 → 近战。** 每一层只要凑不齐 Attack + Hit 就继续往下落，所以老数据、只配一组的英雄都不用改表。

- 档位默认取 `AttackLevel`（1 低 / 2 中 / 3 高，由牌型映射，和卡牌动作、震屏同一套档）。想让「伤害高就出强特效」而不是按牌型，把 `AttackTuning` 的 `heroFxMidDamage` / `heroFxStrongDamage` 填上伤害阈值即可，填了以后按这一刀的 `AttackDamage` 分档。
- 选档在 `AttackTuningConfig.ResolveHeroFxLevel`，目标类型在 `GameUIView.PlayAttackCharge` 里按 `session.EnemyAtVisualSlot(AttackVisualSlot).MonsterType` 判定，日志里有 `fxLevel=` 和 `elite=`。
- 例：萨满祭司 普通怪 = `116|207`（SaManJiSiAttack03/04），精英 = `113|205`，默认也是 `113|205`。

| 字段 | 含义 |
|------|------|
| `Type` | `Attack = 2` 释放，`Hit = 1` 命中 |
| `IsProjectile` | 非 0 时从人物位置飞向目标 |
| `rolepoint` | 非投射物落点：`0`=目标，`1`=人物，`2`=屏幕中心 |
| `Effects` | 预制体名，不含目录 |
| `Time` | 特效自身时长。释放行＝这个特效演多久（投射物＝飞行时长），到点自己回池；命中行＝命中特效演多久。非投射物的释放行在特效自身动画更短时按动画收（`clampCastTimeToClip`） |
| `hitTime` | 攻击行＝**命中延迟**：释放特效开始后多久命中，0 表示释放演完那一刻命中；命中行＝命中特效显示时长覆盖（0＝按 `Time`）。投射物按飞行时间命中，不读攻击行这个值 |

萨满普通怪 `116|207` 就是这套的样板：116 演 1.5s，`hitTime = 0.3` 所以 0.3s 时命中并在目标处出 207，207 演满 1.2s，两者在 1.5s 一起收。精英 `113|205` 是 0.1 + 1.4 = 1.5。

进桌 `PreloadHeroEffectsAsync` 把默认档、普通/精英、弱/中/强里用到的预制体**全部**预载（按名字去重），每份各实例化一份放进 `HeroFxPool`（池根节点保持关闭）。出刀 `RentHeroFx` 取出，播完停粒子、清拖尾、关节点再放回。同一份同时只出一份；池里那份还在播时才再补一份。离开战斗 `Dispose` 销毁池子并释放资源句柄。WebGL 不再在出刀当帧同步加载。

一次出刀：

1. 取出释放特效。投射物从人物飞向目标，飞行时长 `Attack.Time`（＝命中延迟）；定点按 `rolepoint`（0 目标 / 1 人物 / 2 屏幕中心）放下，按 `castTime` 自己演完回池——默认就是 `Attack.Time`，但特效自身动画比它短时取动画长度（`ResolveCastDuration`）。
2. `hitDelay`（攻击行 `hitTime`，没配就是释放演完）到点结算：没命中播闪避、不放命中特效；命中才出命中特效（定点同样按 `rolepoint`，投射物再飞一段 `Hit.Time`），同时受击动画 + 伤害字。**命中可以早于释放特效演完**，释放特效不打断、继续演到自己的 `Time`。
3. 两条特效各自按自己的时长回池：释放 = `castTime`，命中 = `hitDelay + 命中行 Time`。整段动作取两者较长的那个（`actionEnd`），到点 `ClearSkillFx` 兜底清一次再 `onReturned`。
4. 伤害字仍按攻击等级停留（`HpTextHoldDuration`），然后 `onDone`。

所以萨满普通怪是：0 秒出 116 → 0.3 秒命中、出 207 并结算扣血/受击/伤害字 → 1.2 秒时 207 演完 → 1.5 秒 116 演完、一起回池 → 再停 `HpTextHoldDuration` 进下一个对手。

特效命中不播卡牌击退。溅射/群体目标：结算后凡 `LastAttackHitApplied && !Missed` 的槽都出受击特效（盗贼攻≥150 变群体、灰烬 AOE、多面手溅射等同理）；动画仍走 `ReactSlot`。命中行 `rolepoint=2` 在多人时改按各目标点出，避免全叠在屏幕中心。

### PVP 敌方来袭（上下翻转）

PVP 比牌里对手也是英雄。挨打时 `GameUIView.PlayAttackCharge` 先试 `TryPlayPvpIncomingHeroSkill`：

1. 从 `ViewModel.PvpState` 取对手 `HeroId`（野怪轮 `IsMonsterRound` 直接跳过），按 `ResolveHeroFxLevel` 选档，`HeroMechanics.TryResolveEffects(foeHero, level, eliteTarget)` 解析对方英雄的 Attack + Hit；
2. 解析不到成套特效 / 预制体没预载（平凡之人、圣骑士近战、武僧、旅者、以及野怪轮）→ 返回 false，仍走 `PlayIncoming` 敌人卡冲刺；
3. 能播时调 `PlayHeroSkill(..., fromEnemy: true, mirrorY: true)`：释放点是对手卡、命中点是我方卡（`fromEnemy`），整段特效上下翻转（`mirrorY`），命中回调补一次震屏，伤害字出在我方卡上（`ShowAttackHpTexts` 的 `IncomingAttack` 分支）；
4. 这一刀被闪避（MISS）且我方英雄配了 `MissAttackEffect` 时，`PlayPvpDodgeCounter` 在人物卡处补反击特效（**不**跟着来袭上下翻），过 `hitTime` 给攻击方卡播受击动画并出 `-{DodgeCounterDamage}` 反击伤害字；反击时长算进 `PlayHeroSkill` 的 `tail`，不会提前收场。反向（**对方闪避我方攻击**）走 `StartEnemyDodgeCounter` 的「定住」演出（我方卡拉到中心、敌卡播翻过的反击特效），见「闪避反击」一节第 8 条。

翻转只作用于英雄特效实例：`SpawnSkillFxAt` 在算完 `ResolveFxScale` / `scaleMultiplier` 后把 `localScale.y` 取负（`_flipFxY`），所以尾焰、命中特效都朝下打；`SpawnSkillFxAt` 里的 `position` / `rotation` 不变。**投射物不翻**（`SpawnSkillFx(..., applyMirror: false)`）：它靠 `FaceToward` 按飞行方向绕 Z 转，再叠负缩放会和旋转打架、看起来像被拧反。`_flipFxY` 在 `PlayHeroSkill` 里按 `mirrorY` 设置、`Kill()` 里清零，不会串到下一段演出。

PVP 圣光燃烧（`HolyLightCommand`）走的是另一条既有路径（`TryPlayHolyLight` → `PlayHeroSkill(fromEnemy: true)`），**不带** `mirrorY`，保持原样。对手是圣骑士但没配 `HeroEffects` 时普通攻击仍回退近战。

### 齐射（暗灵法师，代码特判）

`HeroMechanics.ShadowMageHeroId`（= 3）不走上面的"一条释放 + 一条命中"，改用 `AttackCutscene.PlayVolleySkill`。解析见 `HeroMechanics.TryResolveVolleyEffects`，表字段分工：

| 字段 | 内容 |
|------|------|
| `CastEffect` | 01 法阵（非投射物 Attack，`Aoshufeidan01`） |
| `HeroEffects` | 02 主弹 + 04 受击（`Aoshufeidan02` + `Aoshufeidan04`） |
| `SpecialEffect` | 03 溅射弹（可再带一份 04；`Aoshufeidan03`） |

兼容旧数据：四个 Id 全塞在 `HeroEffects` 里也能凑齐。档位（弱/中/强）对这套不生效，`hitAnimation` 照样对主目标生效。

时间与抛物线参数在 `Assets/Res/SO/AttackTuning.asset` 的「暗灵法师齐射」段（`AttackTuningConfig`），**不读特效表的 `Time`**：

| 时刻 | 内容 |
|------|------|
| 0s | `Aoshufeidan01` 在人物处持续播放 1.5s；`Aoshufeidan02` 在人物处蓄力 |
| 1.0s | 02 直线飞向主目标（0.3s）；同时两侧每个存活敌人各起飞一发 03（0.4s：先反向再回零再飞向怪） |
| 1.3s | 主弹落地：统一结算扣血/受击动画/伤害字，主目标出 04 |
| 1.4s | 每发 03 各自落地：只在该槽位补 04（伤害已在 1.3s 结算） |
| 1.85s | 最后一发 04（1.4 + 命中行 `Time`）播完 → `ClearSkillFx` + `onReturned` → 再停 `HpTextHoldDuration` → `onDone` |

03 用**三次贝塞尔**（先反向勾再拐向怪，像 S/J）：P0 起点 → P1 反方向勾住（可略下沉）→ P2 抬起拐向怪 → P3 怪物。右怪先往左、左怪先往右。勾的大小看 `volleyJumpReverseXRatio/Min`，下沉看 `volleyJumpReverseXAt`，拐弯点看 `volleyJumpXZeroAt`。

"两侧" = 主目标之外的存活敌人槽，有几个飞几发；没有别的敌人时只打主目标。弹丸落地计时用独立的 `DelayedCall`（带 `_playToken` 校验），不挂在弹丸自己的 tween 上——弹丸回池时 tween 会被清掉。进桌预载会把 `CastEffect` / `SpecialEffect` 一并收进池子。

**齐射的四个特效强制走 UI 层**（`SpawnSkillFxAt(forceUiLayer: true)`）：`Aoshufeidan01` 曾经是 Default 层，那样会被主相机画在 UI 后面、法阵等于看不见，位置还落在主相机平面 `y≈-5.8`；现在整棵子树 `UiFx.ApplyUiLayer` + UI 世界坐标 + `sortingOrder = HeroFxSortingOrder(230)`，和卡面同一空间。受击 04 走 `SpawnVolleyHitFx`（也是强制 UI + 齐射缩放），不吃普通路径里 `SpawnHitFx` 的投射物分支。可调字段（Inspector → AttackTuning →「暗灵法师齐射」）：

| 字段 | 含义 |
|------|------|
| `volleyFxScale` | 在 UI 层基准缩放（约 0.52）之上再乘的倍率，1 = 跟萨满/圣骑士那套一样大 |
| `volleySpawnOffsetY` | 01/02/03 相对人物卡纵向偏移，正数往上 |
| `volleyJumpHeightRatio` / `volleyMinJumpPower` | 溅射弹弧高 |
| `volleyJumpReverseXAt` | P1 下沉比例（0~1），越大反向勾越往下 |
| `volleyJumpXZeroAt` | P2 沿起点→怪的位置（0~1），推荐 0.55~0.7 |
| `volleyJumpReverseXRatio` / `Min` | 反向勾横向幅度，越大先甩得越开 |
| `volleySpawnDelay` | 03 比主弹晚多少秒生成，默认 0.1 |

弹丸**不做朝向旋转**（保持预制体原始朝向）：尾焰/剑光是按固定朝向做的，按飞行方向转会把它们拧歪。

看不见特效时看这条自检日志（生成后 0.12s 打一次）：

```
[HeroVolley] 法阵01 自检 layer=5 active=True pos=... scale=0.52 renderers=6 boundsCenter=... boundsSize=... particles=3
```

`particles=0` = 预制体没发射（美术问题）；`boundsSize` 接近 0 或 `pos` 在屏幕外 = 位置/尺寸问题；都正常还看不见才是排序/遮挡。

摆放看生成时用的那个 `SpawnSkillFxAt(...)`：它按 `forceUiLayer` 和实例 Layer 决定走哪套坐标与缩放。

| 情况 | 坐标 | 缩放 | 排序 |
|------|------|------|------|
| UI 层（预制体根节点 Layer = UI，或 `forceUiLayer: true`） | 直接用卡牌的 UI 世界坐标 | `UI 相机正交尺寸 ÷ 主相机正交尺寸`（当前约 5 / 9.6 ≈ 0.52），再乘 `scaleMultiplier` | 见下 |
| Default 等其它 Layer 且没强制 | 换成主相机前方 `Z = 0` 平面的世界坐标（`UiToFxWorld`） | 保持 1 | 不动（`sortingOrder` 还是预制体原值） |

排序两条路，**不要叠用**：

| 预制体 | 生成时 | 怎么压到人物下面 |
|--------|--------|------------------|
| 挂了 `EffectSortingInherit` | 挂到 HUD 下，**不**再 `ApplySorting(230)`，按「原值 + 最近的 Canvas + `OrderOffset`」 | `OrderOffset` 填负数（如 `-10`） |
| 没挂 | `UiFx.ApplySorting(go, 230)`，一律压在卡面之上 | 改不了相对层级；要自己控就挂 Inherit |

`SpawnSkillFx(prefab, pos, life)` 是「不强制层、倍率 1、无 Inherit 时排序 230」的简写；齐射走 `SpawnSkillFxAt(..., forceUiLayer: true, scaleMultiplier: tuning.VolleyFxScale, sortingOrder: HeroFxSortingOrder)`。Default 层的特效会被主相机画在 UI 后面，法阵那种贴在人物卡上的东西会直接看不见——所以齐射一律强制 UI 层。

旋转：生成时 `rotation = Quaternion.identity`，即保持预制体原始朝向；只有旧的投射物路径（`FaceToward`）会按飞行方向绕 Z 转，齐射不再转。

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

英雄配了 `HeroConfig.hitAnimation` 时，**释放特效开播那一帧**就播（替代命中瞬间的 `ani_atk_lvXX_hit`）。群体出手（`AoeDamage` / `AoeDamageWhenAttack`）时 `PlayHeroSkill` 传入全部存活敌人槽一起飞，与扣血范围一致；命中帧只结算/出受击特效/伤害字，`ReactSlot` 不再把已开播的动画从头重开；MISS 仍打断成闪避。齐射仍只给主目标提前开播。值填 `PlayerRoot.controller` 里的状态名，例如圣骑士的 `ani_sqs_gj_hit`；留空或控制器里找不到，就还是命中帧播通用 `hit`。

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
| `clampCastTimeToClip` | 释放特效（非投射物）按自身动画时长收手，`Time` 只当上限。默认开 |
| `heroFxMidDamage` | 英雄特效分档：这一刀伤害 ≥ 此值用「中」档。填 0 = 按牌型档位 |
| `heroFxStrongDamage` | 英雄特效分档：这一刀伤害 ≥ 此值用「强」档，要连「中」档一起填 |
| `deathDissolveDelay` | 致死后等多久才开始溶解和播致死特效 |
| `deathEffect` | 致死时在被打者位置播的特效 prefab，留空不播 |
| `deathEffectDuration` | 致死特效的存活时长，到点销毁 |
| `volley*`（暗灵法师齐射） | 法阵/蓄力/飞行时长、溅射抛物线、生成高度、缩放，见「齐射」一节 |

前八项低 / 中 / 高三档各一组，对应 `AttackLevel` 的 1 / 2 / 3；其余在“通用 / 齐射”段，不分档。

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
- 粒子特效挂到 UI 下必须走 `UiFx.ApplySorting`，否则会被 UI 盖掉；同时**不要依赖预制体自带 Layer**——Default 层会被主相机画在 UI 后面（法阵就是这么"消失"的）。要么把预制体整棵改成 UI 层，要么像齐射那样用 `SpawnSkillFxAt(forceUiLayer: true)`。
- 释放特效按自己的 `Time` 回池，命中特效按 `hitDelay + Time` 回池，两者互不打断；整段动作取较长的那个（`actionEnd`）再 `onReturned` → 伤害字停留 → `onDone`。别再改回「命中当帧掐断释放特效」或「把两条特效都绑到命中特效的显示时长上」。
- PVP 对手来袭的上下翻转只由 `mirrorY: true` 打开（`_flipFxY`，`Kill()` 清零）；圣光燃烧等其它 `fromEnemy` 演出不带 mirror。别把 `scale.y` 取负写进 `SpawnSkillFxAt` 的默认路径，否则自己出刀和齐射都会反。
- 命中延迟只认攻击行的 `hitTime`，且会被夹在 `[0, castTime]` 之间（投射物不读，直接按飞行时间命中）。想让命中晚于释放演完，得加大攻击行的 `Time`，不是加大 `hitTime`。
- 齐射的时间、抛物线、缩放、生成高度都在 `AttackTuning.asset` 的「暗灵法师齐射」段；改完进 Play 即生效（编辑器下改完重进游戏）。
- `HeroConfig` 每组特效 Id（默认档 / 普通 / 精英 / 弱 / 中 / 强）里 Attack / Hit 两种 `Type` 各要一条，缺一种就整组回退（目标类型 → 档位 → 默认档 → 近战）。同一组里多条 Attack 只有第一条生效。**暗灵法师例外**：走 `CastEffect` + `HeroEffects` + `SpecialEffect` 齐射（见「齐射」），缺 `Aoshufeidan01`（Id 102）会整段解析失败、静默回退近战。
- 新加一个英雄特效：先在 `HeroEffectsConfig.xlsx` 加 Attack + Hit 两行，再把 Id 填进 `HeroConfig.xlsx` 对应列（`释放|命中`）：普通怪 `HeroEffectsCommon`、精英/领主 `HeroEffectsElite`、牌型档 `HeroEffectsWeak/Mid/Strong`。不用改代码，进桌会一起预载。
