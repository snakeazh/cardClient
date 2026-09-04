# PlayerItem 使用文档

局内 / 主页 / 选角共用的人物信息卡。同一预制体里人物用 `card`、敌人用 `enemycard`，`Bind` / `ApplyTheme` 互斥切换，头像与数值写到当前可见那一套。

脚本：`Assets/App/Item/PlayerItem.cs`  
预制体：`Assets/Res/UI/Icon/PlayerItem.prefab`（资源键 `ResResourcePaths.PlayerItem` = `UI/Icon/PlayerItem`）  
色板：[`ThemeColors.md`](../ThemeColors.md)  
命名空间：`App.Game`

对局规则见 [`GameLogic.md`](../Game/GameLogic.md)。攻击力来源见下文。

---

## 节点

运行时按名字查找，Inspector 未拖引用也能工作。`cardFrame` 下两套卡面并列，默认只开人物 `card`。

| 节点 | 用途 |
|------|------|
| `cardMask` | 卡面遮罩图（`BlackBaseFrameMask`），美术摆位；人物/敌人共用，不随角色开关 |
| `IconTitleBG` | 标题底，纯色，颜色以预制体为准（代码不染色） |
| `card` | 人物卡面图，预制体默认 `OrdinaryCardFrame` |
| `card_icon` | 人物头像 |
| `card_Name` | 人物名字 |
| `card_attackValue` | 人物攻击力数字 |
| `card_attackHeart` | 人物当前血量 |
| `attack` | 人物攻击力整块；值为 0 时隐藏。底图按品质取 `Altas/ItemBg` 的 `{品质}RectangleFrame` |
| `heart` | 人物血量整块。局外 `SetHp(0)` 隐藏；局内 `Bind` 传 `hideWhenZero: false`，阵亡仍显示 0 |
| `enemycard` | 敌人卡面。`Bind` 敌人时显示，底图取 `MonsterConfig.BaseMap` |
| `enemycard_icon` | 敌人头像 |
| `enemycard_Name` | 敌人名字 |
| `enemycard_attackValue` | 敌人攻击力数字 |
| `enemycard_attackHeart` | 敌人当前血量 |
| `enemycardattack` | 敌人攻击力整块；值为 0 时隐藏。底图取 `MonsterConfig.HealthBar` |
| `enemycardheart` | 敌人血量整块。局内阵亡仍显示 0。底图与 `enemycardattack` 相同 |
| `PlayerRoot` | 位移与攻击动画根节点（`RootRect` / `RootAnimator`） |

`state` 节点已不再使用。预制体若还留着，运行时会关掉，不要再写状态/透视文案。

---

## 人物 / 敌人切换

局内敌人槽是从玩家 `PlayerItem` 克隆的，不要另做敌人预制体。

| 调用 | 显示 | 底图 |
|------|------|------|
| `ApplyTheme()`、`Bind(玩家)` | `card` 开、`enemycard` 关 | attack/heart → `{品质}RectangleFrame` |
| `Bind(敌人)` | `enemycard` 开、`card` 关 | enemycard → `BaseMap`；enemycardattack/heart → `HealthBar` |

缺配置或缺图保留当前 sprite。`CardIconRect` / `AttackValueRect` / `AttackValueAnimator` 返回当前可见套，结算抖动挂对节点。`SetSelectLift` 只动人物 `card`（选角）。

---

## 攻击力与血量

| 界面 | 攻击力 | 血量 |
|------|--------|------|
| 主页 `HomeView` | `HeroConfig.HeroDamage` | `HeroConfig.Hp` |
| 选角 `HeroItem` | 已解锁：`HeroDamage`；未解锁：0 | 已解锁：`Hp`；未解锁：0 |
| 关卡预览 `LevelUIView` | 已解锁：`HeroDamage` | 已解锁：`Hp` |
| 局内玩家 | `SeatState.Attack` ← `HeroConfig.HeroDamage` | `SeatState.Hp` |
| 局内敌人 | `SeatState.Attack` ← `MonsterConfig.MonsterDamage` | `SeatState.Hp` |

`SetAttack(0)` 会关掉当前可见的攻击整块。`SetHp(0)` 默认关掉血量整块（局外未解锁 / 空英雄用这个）。局内 `Bind` 走 `SetHp(..., hideWhenZero: false)`，血量为 0 仍显示框和数字 0。

---

## 头像

配置表 `Icon` 是文件名前缀，再拼血量分档后缀。路径和缓存由 [`PortraitLoader.cs`](../UI/Game/PortraitLoader.cs) 统一管：

- 启动：`PortraitLoader.PreloadAsync` 只预热全部英雄 / 怪物的 `_attack`
- 局内：`EnsureBattleStatesAsync` 对上场玩家和本关怪物补 `_damage` / `_dead`；换关遇到新怪物再补
- 取图：界面只调 `Get` / `GetRole` / `GetEnemy`，不要再各自 `LoadAsync`。受伤图还没到时先显示 `_attack`

| 角色 | 配置 | 资源目录 | 例子 |
|------|------|----------|------|
| 玩家 | `HeroConfig.Icon` | `Textures/role/` | `Adventurer_attack` |
| 怪物 | `MonsterConfig.Icon` | `Textures/enemy/` | `Monster1_attack` |

| 分档 | 条件 | 后缀 |
|------|------|------|
| 健康 | `Hp * 2 >= MaxHp`（含恰好 50%） | `_attack` |
| 受伤 | `0 < Hp` 且 `Hp * 2 < MaxHp` | `_damage` |
| 死亡 | `Hp <= 0` 或 `MaxHp <= 0` | `_dead` |

局内 `GameUIView` 在受击演出开始（`onHit`）调用 `ApplyPendingAttackHits` 扣血，随后 `RefreshPlayerItems` 按新的 `Hp / MaxHp` 取图，和卡上血量同一拍。局外（主页、选角、图鉴怪物）没有战斗血量，固定 `_attack`。图鉴收藏品仍走 `RoleIcon`（无后缀，如 `Adventurer1`），不要拼分档、也不进 `PortraitLoader`。

怪物资源将从现有的 `enemy{n}_*` 改名为配置表 `Icon`（`Monster1_attack` 等）；改名前预热会 Warn 并缓存空图，不做旧名映射。

局内绑法：

```csharp
_playerItem.Bind(session.Player, PortraitLoader.Get(session.Player), session.Player.Attack);
_enemyItems[slot].Bind(enemy, PortraitLoader.Get(enemy), enemy.Attack);
```

---

## API

```csharp
item.ApplyTheme();                      // 切到人物 card；attack/heart 切 {品质}RectangleFrame
item.SetName("平凡之人");
item.SetHp(1000);                       // 默认 hp=0 隐藏血量框
item.SetHp(0, hideWhenZero: false);     // 局内阵亡：框留下，数字 0
item.SetAttack(10);
item.SetPortrait(sprite);               // null 则关掉 Image
item.SetPortrait(sprite, locked: true); // 未解锁：头像变黑
item.Bind(seat, portrait, attack);      // 按 seat.IsPlayer 切 card / enemycard
```

`RootRect` / `RootAnimator` 给 [`AttackCutscene.md`](../UI/Game/AttackCutscene.md) 做冲锋演出。

---

## 注意

- 人物与敌人共用预制体，靠 `card` / `enemycard` 切换，不要为敌人再做一套卡。
- 卡片底 / title / 圈的色值只改 [`ThemeColors.md`](../ThemeColors.md)，不要在本脚本写 hex。`PlayerItem` 本身不再染色。
- 攻击力读配置，不要用血量换算。
- 局内刷新走 `GameUIView.RefreshPlayerItems`，不要在别处改 `card_attackValue` / `enemycard_attackValue`。
- 局内头像按 `SeatState.Icon` + 血量分档从 `PortraitLoader` 取图，不要写死 `enemy{n}_attack`，也不要在各界面自己加载。
