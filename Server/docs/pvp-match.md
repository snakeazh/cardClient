# PVP 对局（多轮 1v1）

产品规则见仓库根目录 [`【冒险卡】pvp玩法设计.md`](../../【冒险卡】pvp玩法设计.md)。  
排队短协议仍见 [`pvp.md`](pvp.md)。本文是第一版编排：配表驱动轮次、1v1 子桌、扣血淘汰、5 选 3 与搓/替/透。尚未做 30 秒商店、发奖。

## 配表

| 表 | 作用 |
|---|---|
| `PvpModeConfig` | 一行一个模式：人数、初始金、商店秒数、剩 2 人跳过野怪、排名奖励、轮次伤害系数 |
| `PvpRoundConfig` | 一个模式多行：`Round` + `FightKind`（Monster / Pvp）+ `MonsterGroup` + `GoldBase` |
| `PvpBotConfig` | 开发补房机器人基础数据：`Id`（100000 段）、昵称、头像、英雄；运行时 `UserId` 仍是 Guid |

野怪组走已有 `MonsterGroupConfig` → `MonsterConfig`。当前数据：模式 1「经典」共 13 轮（1/5/9/13 野怪，其余 PvP）。

加新模式只加表，不要在 C# 写 `if (round == 5)`。

技能次数读 `GameConst`：搓 `DefaultSkillShuffleNum`（默认 3）、替 `DefaultSkillReplaceNum`（默认 1）、透 `DefaultSkillPerspectiveNum`（默认 1）。**每轮开打时重置**，不是整局共用。

## 开发机器人

Development 默认 `Pvp:FillWithBots=true`：真人一 `queue` 就从 `PvpBotConfig` 取启用行补满开房。机器人没有 WebSocket，服务端会：

- 座位按非人类处理，自动选炸金花最大 3 张；英雄读表里 `HeroId`（0 则用 `DefaultHeroId`）
- 没有真人的子桌立刻摊牌（机器人打野怪、机器人打机器人）
- 有真人的桌子等双方都 `showdown`（锁定 3 张）后才比牌；对野怪/机器人时对方已锁定，你一锁就比

正式服不要开这个开关，否则一人进队就会开一局。`appsettings.json` 默认关。

## 流程

```
queue 满 4 人 → room_ready（带 modeId）
  → 服务端读 PvpModeConfig / PvpRoundConfig，用 LastHeroId 开局
  → 第 1 轮野怪：四人各打一张 1v1 子桌，发 5 张洞牌（默认亮 0/1/2）
  → 本轮可 battle：pick / rub / replace / peek；showdown 只锁定自己当前 3 张
  → 一桌双方都锁定后先广播比牌结果，再进下一轮（客户端：翻选中牌 → 胜方攻击败方 → 再刷血量/进轮）
  → 该桌比完立刻按胜者伤害 × (1 + DamageRoundScale × 轮次) 扣败者 HP；HP ≤ 0 立刻淘汰并写 Rank
  → 本轮所有子桌都比完才进入表里下一轮；技能次数重置；PvP 轮按 A-B/C-D → A-C/B-D → A-D/B-C 配对
  → 剩 2 人且模式勾了 SkipMonsterWhenTwoLeft：野怪轮改打决赛
  → 只剩 1 人或轮次打完：phase=finished，写 Rank
```

## 消息

在 [`pvp.md`](pvp.md) 的帧上增加：

| 方向 | t | 含义 |
|------|---|------|
| S | match_update | 开房、每次摊牌/技能后广播。含 round / fightKind / 四人 HP / 技能剩余 / 自己的 duel |
| S | battle_update | 仍下发，payload 是**自己那桌**的 2 座位 `BattleStateDto` |
| S | room_ready | 增加 `modeId` |
| C | battle | `pick` / `rub` / `replace` / `peek` / `showdown` |

`battle` payload：

```json
{ "action": "pick", "indexes": [0, 2, 4] }
{ "action": "rub", "index": 1 }
{ "action": "replace" }
{ "action": "peek" }
{ "action": "showdown" }
```

- `pick`：从 5 张里选 3 个不重复下标。不选则默认 `[0,1,2]`。野怪座位服务端自动取炸金花最大 3 张。
- `rub`：换掉 `index` 那张（扣 1 次搓）。
- `replace`：5 张全部重抽（扣 1 次替）。
- `peek`：只让自己看见本桌对手的 5 张（扣 1 次透）。不能看别人的桌。
- `showdown` / `open`：锁定自己当前选出的 3 张。双方都锁定后才比牌、立刻扣血。已锁定不能再 pick/rub/replace/peek。

`match_update` 要点：

```json
{
  "roomId": "...",
  "seed": 1,
  "modeId": 1,
  "modeName": "经典",
  "round": 1,
  "phase": "fight",
  "fightKind": "monster",
  "players": [ { "userId": "...", "hp": 320, "maxHp": 320, "gold": 150, "alive": true, "rank": 0, "rubLeft": 3, "replaceLeft": 1, "peekLeft": 1 } ],
  "duels": [ { "leftUserId": "...", "rightUserId": "", "monsterName": "屎莱姆", "resolved": false } ],
  "duel": { "phase": "dealt", "seats": [ { "cards": [/* 5 张 */], "selected": [0, 1, 2] }, { "cards": null } ] }
}
```

`phase` 为 `finished` 时看 `players[].rank`（1 最好）。本版不发局外金币。

## 代码

- 编排：[`PvpMatch`](../../src/Card.Battle/PvpMatch.cs)、[`PvpSchedule`](../../src/Card.Battle/PvpSchedule.cs)、[`PvpPairing`](../../src/Card.Battle/PvpPairing.cs)
- 1v1 桌：[`PvpDuelTable`](../../src/Card.Battle/PvpDuelTable.cs)
- 发 5 / 选 3 / 搓 / 替：[`BattleEngine`](../../src/Card.Battle/BattleEngine.cs) 的 `DealHole` / `Pick` / `Rub` / `Replace`（PVE 仍走 `Deal` 发 3 张 + `DrawExtra`）
- 房间宿主：[`PvpMatchHost`](../../src/Card.Server/Pvp/PvpMatchHost.cs)
