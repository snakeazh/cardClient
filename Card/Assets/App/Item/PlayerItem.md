# PlayerItem 使用文档

局内 / 主页 / 选角共用的人物信息卡。玩家与敌人同一套节点，主题色和头像在绑定时赋值。

脚本：`Assets/App/Item/PlayerItem.cs`  
预制体：`Assets/Res/UI/Icon/PlayerItem.prefab`（资源键 `ResResourcePaths.PlayerItem` = `UI/Icon/PlayerItem`）  
色板：[`ThemeColors.md`](../ThemeColors.md)  
命名空间：`App.Game`

对局规则见 [`GameLogic.md`](../Game/GameLogic.md)。攻击力来源见下文。

---

## 节点

运行时按名字查找，Inspector 未拖引用也能工作。

| 节点 | 用途 |
|------|------|
| `IconBG` | 卡片底。人物普通品质，敌人红。色值见 [`ThemeColors.md`](../ThemeColors.md) |
| `IconTitleBG` | 标题底。人物普通品质 title，敌人深红 |
| `card_Circle` | 头像圈，跟 title 同色 |
| `card_icon` | 头像 Image |
| `card_Name` | 名字 |
| `card_attackValue` | 攻击力数字 |
| `card_attackHeart` | 当前血量 |
| `attack` | 攻击力整块；值为 0 时隐藏。底图：人物 `FrameSlection1`，敌人 `FrameSlection2` |
| `heart` | 血量整块；值为 0 时隐藏。底图与 `attack` 相同 |
| `state` | 敌人状态 / 透视牌型 |
| `PlayerRoot` | 位移与攻击动画根节点（`RootRect` / `RootAnimator`） |

---

## 攻击力与血量

| 界面 | 攻击力 | 血量 |
|------|--------|------|
| 主页 `HomeView` | `HeroConfig.HeroDamage` | `HeroConfig.Hp` |
| 选角 `HeroItem` | 已解锁：`HeroDamage`；未解锁：0 | 已解锁：`Hp`；未解锁：0 |
| 关卡预览 `LevelUIView` | 已解锁：`HeroDamage` | 已解锁：`Hp` |
| 局内玩家 | `SeatState.Attack` ← `HeroConfig.HeroDamage` | `SeatState.Hp` |
| 局内敌人 | `SeatState.Attack` ← `MonsterConfig.MonsterDamage` | `SeatState.Hp` |

`SetAttack(0)` 会关掉 `attack` 节点，`SetHp(0)` 会关掉 `heart`。未解锁英雄（LevelUI 预览 / HeroItem）两边都传 0，攻击和血量都不显示。配置值大于 0 才会显示。

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
_enemyItems[slot].Bind(enemy, PortraitLoader.Get(enemy), enemy.Attack, session.ActingAiId);
```

---

## API

```csharp
item.ApplyTheme(enemy: false);          // 人物普通品质底/title；敌人红。attack/heart 底图另换
item.SetName("平凡之人");
item.SetHp(1000);
item.SetAttack(10);
item.SetState(string.Empty);
item.SetPortrait(sprite);               // null 则关掉 Image
item.SetPortrait(sprite, locked: true); // 未解锁：头像变黑
item.Bind(seat, portrait, attack, actingAiId);
```

`RootRect` / `RootAnimator` 给 [`AttackCutscene.md`](../UI/Game/AttackCutscene.md) 做冲锋演出。

---

## 注意

- 人物与敌人共用预制体，不要为敌人再做一套卡。
- 卡片底 / title / 圈的色值只改 [`ThemeColors.md`](../ThemeColors.md)，不要在本脚本写 hex。
- 攻击力读配置，不要用血量换算。
- 局内刷新走 `GameUIView.RefreshPlayerItems`，不要在别处改 `card_attackValue`。
- 局内头像按 `SeatState.Icon` + 血量分档从 `PortraitLoader` 取图，不要写死 `enemy{n}_attack`，也不要在各界面自己加载。
