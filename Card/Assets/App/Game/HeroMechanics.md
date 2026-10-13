# HeroMechanics · 英雄技能

路径：`Card/Assets/App/Game/HeroMechanics.cs`  
命名空间：`App.Game`

`HeroConfig.HeroEntryId` 指向 `HeroEntryConfig` 词条（`MechanismType` + `Value`）。数值以 `Value` 为准。数据源是开局写入的 `Run.HeroId`。收藏禁用不禁用英雄词条。

对局规则总览：[`GameLogic.md`](GameLogic.md)。状态机：[`GameSession.md`](GameSession.md)。遗物：[`RelicMechanics.md`](RelicMechanics.md)。天赋：[`天赋模块使用文档.md`](../Talent/天赋模块使用文档.md)。

PVP 不走这套多目标结算。暗灵攻击特效多段播放未接，资源也不在 `Res/Effect/Character/`。

---

## 1. 数据

| 表 | 路径 | 用途 |
|----|------|------|
| HeroConfig | `Res/Config/HeroConfig.json` | 英雄：血、攻、暴击、暴击伤害、`HeroEntryId[]`、`HeroEffects[]` |
| HeroEntryConfig | `Res/Config/HeroEntryConfig.json` | 词条：`Type` + `Value` |
| HeroEffectsConfig | `Res/Config/HeroEffectsConfig.json` | 出刀演出：`Type` + `IsProjectile` + `rolepoint` + `Effects` + `Time` |

`HeroEffects` 只驱动玩家出刀演出，不改数值。`TryResolveEffects` 取第一条 Attack 和第一条 Hit；解析不出或预制体未就绪时回退近战。

---

## 2. 当前英雄

| 英雄 | Type | 时机 | 行为 |
|------|------|------|------|
| 平凡之人 | （无词条） | 面板 | 只用 `Hp` / `HeroDamage` / `Critical` / `CriticalDamage` |
| 猎人 | HeroTakeDamagePer | 挨打 | `× (1 + Value)`，与遗物承伤相加。未闪避时至少 1 |
| 猎人 | ExtraAttackOneTime | 出手 | Value 概率对同一主目标再打一刀满伤 |
| 暗灵法师 | VersatilePerson | 出手 | 主目标满伤，其余存活敌人 `round(伤害 × Value)`。有群体时跳过 |
| 暗灵法师 | VersatilePersonUp | 击杀 | 每 `Value[0]` 次击杀（当前 1）增加分裂比例：普通 `Value[1]`，精英/领主 `Value[2]`。本局累计，下一刀生效 |
| 暗灵法师 | UnableCritical | 面板 / 出手 | 天赋和圣物暴击加完后归零。选角面板同样归零 |
| 狂战士 | GainDamageUpWhenHpDecreases | 出手 | `floor(已损失生命 / Value[0])` 层，每层 `Value[1]`，上限 `Value[2]`，加进伤害百分比 |
| 狂战士 | BloodSucking | 造成伤害后 | 与圣物吸血比例相加，按实际伤害回血。不受「无法圣物回血」影响 |
| 狂战士 | UnableReplyHp | 圣物治疗 | 圣物治疗为 0。天赋回血和英雄吸血仍生效 |
| 盗贼 | KillingGetAttack | 击杀 | `PermanentAttackBonus` 和当前攻击 +Value，本局保留 |
| 盗贼 | AoeDamageWhenAttack | 出手 | 当前攻击 `>= Value[0]` 时，本次伤害 100% 打所有存活敌人，跳过单体和分裂 |
| 盗贼 | AttackBossDamage | 出手 | 目标为精英或领主时并入伤害百分比。天赋同 Type 仍只吃领主 |
| 灰烬术士 | AoeDamage | 出手 | 对所有存活敌人各打 `round(原伤害 × Value)`，主目标不再吃 100% |
| 灰烬术士 | （面板） | 暴击 | `Critical` 15%，`CriticalDamage` 1.5 |
| 萨满祭司 | AttackBossDamage | 出手 | 同盗贼，数值为正 |
| 萨满祭司 | RelicReplyHp | 圣物治疗 | 治疗量 `× (1 + Value)` 后再回血 |
| 萨满祭司 | MonsterNumDamage | 发牌前 | 伤害 = 存活敌人数 × `Value[0]`。走承伤百分比，不走闪避和反击。致死则本手不再发牌，关卡失败 |
| 圣骑士 | HeroTakeDamagePer | 挨打 | 同猎人，数值为负 |
| 圣骑士 | HolyLightBurning | 发牌前 / 对子获胜后 | 见下文 |
| 圣骑士 | UnableMissing | 面板 / 挨打 | 天赋和圣物闪避加完后归零。选角面板同样归零 |
| 旅者 | RelicPricePer | 购买价 | `round(Price × (1 + Value))`，下限 0。不改售价 |
| 旅者 | KillingGetGold | 击杀 | 立刻 `AddGold(Value)`，不走圣物金币禁止 |
| 旅者 | UnableReplyGetGold | 圣物加金币 | 圣物效果的正数金币不加。卖圣物、关卡结算、天赋金币、击杀金币仍加 |
| 武僧 | MissDamagePer | 面板 / 挨打 | 加进闪避率 |
| 武僧 | MissGetDamage | 闪避成功 | 次数 `Value[0]`、倍率 `Value[1]`，伤害 = `round(攻击 × 倍率 × 次数)` |
| 武僧 | RubbingCardsNum | 技能次数 | 与遗物相加，`ResetSkillCharges` 夹到不低于 0 |

面板血、攻、暴击率、暴击伤害来自 `HeroConfig`，再加天赋。实战暴击率和闪避率再加圣物与层数，最后才套「无法暴击 / 无法闪避」。

圣光燃烧 `Value` = 每手开始次数、攻击力比例、每次之后的加成：

- 发牌前发动 `Value[0]` 次。把敌人清光则不再发牌，进入本关结算。
- 玩家亮牌是对子并且这场比牌获胜时，致胜攻击（含追击）打完后再发动 1 次。主目标先吃正常伤害。
- 当前这一发伤害 = `round(当前攻击 × Value[1] × (1 + 已发动次数 × Value[2]))`，然后次数 +1。
- 已发动次数 × `Value[2]` 加进后续出手的伤害百分比。

精英/领主判定读进关时写入的 `SeatState.MonsterType`。

---

## 3. 挂钩点

| 时机 | 方法 |
|------|------|
| 精英/领主加减伤、掉血加伤、圣光层数 | `ComputeAttackDamage` |
| 群体 / 分裂 / 攻击达阈值变群体 | `ApplyPlayerAttackHits` |
| 追击 | `BeginPlayerAttack` → `ShouldRollExtraAttack` |
| 承伤百分比 | `IncomingDamageAfterMitigation` |
| 无法暴击 / 无法闪避 | `ResolveLivePlayerPanel`；选角 `TalentBonusManager.Evaluate` |
| 吸血 | `ApplyBloodSucking` |
| 圣物治疗 | `HealFromRelic` |
| 圣物加金币 | `AddRelicGold` |
| 击杀金币 / 击杀加攻 / 分裂成长 | `ApplyDamage` → `ApplyHeroKillRewards` |
| 闪避反击 | `ApplyDodgeCounter` |
| 发牌前受伤 / 回合开始圣光燃烧 | `StartRound` → `ApplyRoundStartHeroMechanics` |
| 对子获胜后的圣光燃烧 | `FinishPlayerAttack` → `TryCastPendingHolyLight` |
| 购买价 | `BuyShopRelic` / `HeroMechanics.BuyPrice` |
| 搓牌次数 | `ResetSkillCharges` |
