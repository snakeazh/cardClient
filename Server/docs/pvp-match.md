# PVP 对局（多轮 1v1）

产品规则见仓库根目录 [`【冒险卡】pvp玩法设计.md`](../../【冒险卡】pvp玩法设计.md)。
排队短协议见 [`pvp.md`](pvp.md)。本文是当前实现：配表驱动轮次、四阶段时间轴、1v1 子桌、扣血淘汰、商店经济、断线代管与多实例路由。

## 配表

| 表 | 作用 |
|---|---|
| `PvpModeConfig` | 一行一个模式：人数、初始金、商店秒数（`ShopSeconds`）、选牌秒数（`OpenPhaseSeconds`）、剩 2 人跳过野怪、排名奖励（`RankReward`）、轮次伤害系数 |
| `PvpRoundConfig` | 一个模式多行：`Round` + `FightKind`（Monster / Pvp）+ `MonsterGroup` + 本轮基础金币 `GoldBase` |
| `PvpBotConfig` | 开发补房机器人基础数据：`Id`（100000 段）、昵称、头像、英雄；运行时 `UserId` 仍是 Guid |

野怪组走已有 `MonsterGroupConfig` → `MonsterConfig`。当前数据：模式 1「经典」共 13 轮（1/5/9/13 野怪，其余 PvP）。

加新模式只加表，不要在 C# 写 `if (round == 5)`。

技能次数基础值读 `GameConst`：搓 `DefaultSkillShuffleNum`（默认 3）、替 `DefaultSkillReplaceNum`（默认 1）、透 `DefaultSkillPerspectiveNum`（默认 1）。**每轮开打时重置**，并叠加圣物/英雄/天赋词条加成（`RubbingCardsNum`=71 搓牌、`PerspectiveNum`=128 透视；赌神/阴阳师英雄同名词条；换牌次数无对应词条，只吃基础值）。

## 回合时间轴（四阶段）

每轮由服务端 deadline 驱动，`PvpTimeoutSweeper`（500ms 一跳）推进，每次切换广播 `match_update` + 对应 `match_event`：

```
fight（选牌，deadline = 2s 发牌演出缓冲 + OpenPhaseSeconds）
  → 全部子桌结算完 → settle（纯演出窗 6s，拒绝一切 battle 操作）
  → settle 到点 → shop（deadline = ShopSeconds；配 0 跳过）
  → shop 到点 / 全员 shop_done → Round++ → 下一轮 fight
最后一轮：fight → settle → finished（settle 播完才发 finished，客户端结算弹窗不用猜演出）
```

演出时长常量在共享代码 `PvpTiming`（`Card/Assets/Shared/Battle/PvpTiming.cs`）：`DealAnimMs=2000`、`SettleAnimMs=6000`，双端同源。

## 经济

- 胜：`GoldBase + 伤害/12 + 20 × 未用技能数`（每次技能金币读 `GameConst.EverySkillProvideGold`）
- 负：胜者所得的一半；平局不结算；野怪轮玩家胜同样发金并**回复 10% 最大生命**（`max(1, floor(MaxHp×0.1))`，不超上限）
- 淘汰：HP ≤ 0 立即写 `Alive=false` 和名次；同轮多人死亡按 `|淘汰后血量|` 升序（更接近 0 名次靠前）
- 名次奖励：终局按 `RankReward`（1~4 名 200/120/60/30）填 `fighter.RewardGold`，由 `PvpRewardService` 一次性落库到局外钱包金币（`TryMarkRewardsGranted` CAS 保证整局只发一次；单人失败记 Error 日志不阻塞他人）

## 商店阶段

- 全员 30 秒（`ShopSeconds`），每人独立货架 4 件：`PvpShopRules`（Shared），池 = 本人已解锁圣物（`profile.ShopRelicIds`）− 已持有 − 在架，按 `RelicConfig.RefreshProbability` 加权随机
- 开局不带圣物（已解锁 ≠ 已携带），买到的圣物**下一轮**起进战斗座位生效
- 操作（`battle` 消息 action）：`buy` / `sell`（index=relicId）、`refresh`、`shop_done`；全员 done 立即进下一轮
- 刷新费用与 PvE 同公式（`GameConst.ShopRefreshFirst` 起递增）；机器人与断线座位进店即自动 done

## 断线 = 代管，可重连接管

- WS 断开**不再退赛**：座位标 `Disconnected`，当前桌未锁定则自动锁定结算；后续轮该座位发牌即自动锁定（机器人同款自动选牌）代打；房间内广播 `player_offline`
- 重连：重连 WS → `auth` → 发 `sync` → 服务端回 `match_update` 全量快照（按 userId 找局）并广播 `player_online`；`error(not_in_battle)` 表示对局已不在
- 死者留局观战（`duel=null`），名次奖励照发；`leave` 仍可用（对局索引清人）

## 协议增量（在 [`pvp.md`](pvp.md) 基础上）

双端协议常量集中在 `Card/Assets/Shared/Contracts/PvpProtocol.cs`（`PvpActions` / `PvpEventKinds` / `PvpPhases`）和 `BattleDtos.cs`（`BattlePhaseNames`）——**改协议只改这两处**，双端编译期对齐。

| 方向 | t | 含义 |
|------|---|------|
| S | match_update | 一切状态变化广播**全量快照**（逐人视角）。含 `stateVersion`（每次变更单调递增，客户端丢弃 ≤ 上次版本的快照）、`events`（本批增量事件）、`shop`（shop 阶段且本人存活时带货架） |
| S | match_event | 紧跟 match_update 逐条下发：`round_start` / `settle_start` / `shop_start` / `duel_resolved`（每个真人参与者一条，value=扣血）/ `player_eliminated`（value=名次）/ `player_offline` / `player_online` / `match_finished` |
| C | sync | 重连后拉全量快照；未在对局返回 `error(not_in_battle)` |
| C | battle | action：`pick` / `rub` / `replace` / `peek` / `showdown`（`open` 同义）；shop 阶段：`buy` / `sell` / `refresh` / `shop_done` |
| S | ~~battle_update~~ | **已停发**（信息全在 match_update.duel 里） |

`match_update` 要点：

```json
{
  "roomId": "...", "seed": 1, "modeId": 1, "modeName": "经典",
  "round": 2, "phase": "fight", "fightKind": "monster",
  "stateVersion": 17,
  "phaseDeadlineUtcMs": 1700000000000, "serverNowUtcMs": 1699999990000,
  "players": [ { "userId": "...", "hp": 320, "maxHp": 320, "gold": 150, "alive": true,
                 "rank": 0, "rubLeft": 3, "replaceLeft": 1, "peekLeft": 1,
                 "disconnected": false, "rewardGold": 0, "relicIds": [101] } ],
  "duels": [ { "leftUserId": "...", "rightUserId": "", "monsterName": "屎莱姆", "resolved": false } ],
  "duel": { "phase": "dealt", "seats": [ { "cards": [/* 5 张 */], "selected": [0,1,2] }, { "cards": null } ] },
  "shop": null,
  "events": [ { "version": 17, "kind": "round_start", "round": 2, "userId": "", "value": 2 } ]
}
```

- 倒计时一律用 `phaseDeadlineUtcMs - serverNowUtcMs` 折算（客户端不要拿本地时钟直接比 deadline）
- `phase=finished` 时看 `players[].rank`（1 最好）和 `rewardGold`
- 摊牌前 `duel.seats[对方].cards = null`；`peek` 后仅自己视角可见对方 5 张

## 多实例（Redis pub/sub）

- 队列：`pvp:queue` 是 Redis **ZSET**（score=入队毫秒），入队/取消/超时扫描三条 Lua 脚本原子执行（从 List 迁移，部署时先 `DEL pvp:queue`）
- 路由：对局由开房实例（房主）独占托管；`PvpMessageRouter` 发送时本地有 socket 直发、否则发 `pvp:msg` 频道；非房主收到 battle/sync 经 `pvp:cmd` 频道转发房主处理。无 Redis 时 `NullPvpBus` 全 no-op，单实例行为不变
- 已知限制：房主单点，房主实例宕机 = 该房间对局丢失（客户端 sync 8s 超时退出），不做房间迁移

## 代码

- 编排（状态机/经济/商店/代管/事件）：`Card/Assets/Shared/Battle/PvpMatch.cs`（链接进 `src/Card.Battle`）
- 轮次/配对/商店规则：`PvpSchedule.cs`、`PvpPairing.cs`、`PvpShopRules.cs`（同目录）
- 1v1 桌：`PvpDuelTable.cs`（`Compare` 前回写两座当前技能剩余，供"技能全空加倍率"类圣物判定）
- 房间宿主/连接/路由：`Server/src/Card.Server/Pvp/`（`PvpMatchHost`、`PvpWebSocketHost`、`PvpMessageRouter`、`PvpBusSubscriber`、`PvpTimeoutSweeper`、`PvpRewardService`、`PvpCombatSeats`）
- Redis 队列/总线：`Server/src/Card.Infrastructure/Redis/RedisServices.cs`、`RedisPvpBus.cs`
- 测试：`Server/tests/Card.Domain.Tests/PvpMatchTests.cs`、`PvpQueueTests.cs`
