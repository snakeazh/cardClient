# PlayerItem 使用文档

局内 / 主页 / 选角共用的人物信息卡。玩家与敌人同一套节点，主题色和头像在绑定时赋值。

脚本：`Assets/App/Item/PlayerItem.cs`  
预制体：`Assets/Res/UI/Icon/PlayerItem.prefab`（资源键 `ResResourcePaths.PlayerItem` = `UI/Icon/PlayerItem`）  
命名空间：`App.Game`

对局规则见 [`GameLogic.md`](../Game/GameLogic.md)。攻击力来源见下文。

---

## 节点

运行时按名字查找，Inspector 未拖引用也能工作。

| 节点 | 用途 |
|------|------|
| `IconBG` | 底色。玩家橙 `#EB9852`，敌人红 `#F6393C` |
| `card_Circle` | 头像圈。玩家 `#F8AB67`，敌人 `#B20003` |
| `card_icon` | 头像 Image |
| `card_Name` | 名字 |
| `card_attackValue` | 攻击力数字 |
| `card_attackHeart` | 当前血量 |
| `attack` | 攻击力整块；值为 0 时隐藏 |
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

`SetAttack(0)` 会关掉 `attack` 节点。配置攻击力大于 0 才会显示。

局内绑法：

```csharp
_playerItem.Bind(session.Player, portrait, session.Player.Attack);
_enemyItems[slot].Bind(enemy, portrait, enemy.Attack, session.ActingAiId);
```

---

## API

```csharp
item.ApplyTheme(enemy: false);          // 玩家橙 / 敌人红
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
- 攻击力读配置，不要用血量换算。
- 局内刷新走 `GameUIView.RefreshPlayerItems`，不要在别处改 `card_attackValue`。
