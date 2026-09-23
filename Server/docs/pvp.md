# PVP 房间

排队、头像、短协议。多轮 1v1 编排（轮次/商店/奖励/代管/多实例）见 [`pvp-match.md`](pvp-match.md)。

## 规则

- 一局 **4 名玩家**，不多不少。
- **人齐才开房**：匹配队列凑满 4 人后才创建房间、下发 `room_ready`、允许开局。
- 1～3 人时只排队，不生成 `roomId`，不发种子，不开始比牌。队内广播当前名单（含微信/抖音头像 URL 与昵称）。
- 排队有服务端超时（`PvpRules.QueueTimeoutMs`，当前 60s）：到期踢出并给本人发 `queue_timeout`，其余排队者收 `queue_update`。`queued` / `queue_update` 都带 `timeoutMs`，客户端以它为准。
- 排队中可 `cancel` / `leave` / 断线出队。**开房后断线不是退赛**：座位由服务端代管（自动选牌锁定），重连发 `sync` 恢复，详见 pvp-match.md。
- 开房后立刻用房间 `seed` 走共享 `BattleEngine(PvpMode)` 发牌；比牌指令与单机主线同一套 `Apply`。
- 出伤与 PVE 玩家同一公式：开房读各人主档 `LastHeroId` 与天赋袋；圣物**开局不带**，由局内商店购买后下一轮生效（摊牌时 `CombatBonuses` 结算圣物本手倍率/加攻，英雄伤害%、暴击、追击照旧）。PVP 没有 BOSS、燧石；斩杀不对玩家生效。
- 头像是登录时客户端上报的社交头像 URL，不是局内英雄 `People1`。WebSocket **不传图片**，只传 URL。
- 牌面不上内部 `Card` struct，只下发 `suit`/`rank` 整数。摊牌前只能看到自己的牌。

```
queue → 队列人数 < 4 → 自己收到 queued（带 timeoutMs），其他人收到 queue_update
queue → 队列人数 = 4 → 创建房间 → 四人各收 room_ready → 各收 match_update（第 1 轮已开局）
对局内一切状态变化 → match_update 全量快照 + match_event 增量事件（见 pvp-match.md）
```

## 登录上报资料

`jscode2session` 不返回头像。客户端用微信/抖音 SDK 拿到授权后，登录带上：

```json
POST /v1/auth/login
{
  "provider": "wechat",
  "code": "...",
  "userInfo": { "nickName": "张三", "avatarUrl": "https://thirdwx.qlogo.cn/..." }
}
```

身份只信 `code`。`userInfo` 可选；无授权也能登录，排队时 `avatarUrl` 为空，客户端用默认图。`avatarUrl` 必须是 https，且主机在白名单内，否则忽略本次 URL、保留旧头像。入队时快照公开资料，排队中途改头像不影响本局队列展示。

## 协议

连接：`GET /v1/pvp/ws` 升级 WebSocket。帧：`{ "t", "seq", "payload" }`。

| 方向 | t | 含义 |
|------|---|------|
| S | hello | 连接建立 |
| C | auth | `{ "accessToken" }` |
| S | authed | `{ "userId" }` |
| C | queue | 入队 |
| S | queued | 自己入队且未满 4：`{ "players": [1～3], "timeoutMs": 60000 }` |
| S | queue_update | 有人加入/退出/超时，发给队内其他人：同 queued |
| S | queue_timeout | 排队超时（60s）被踢出 |
| S | room_ready | 四人到齐才发，见下 |
| S | match_update | 对局全量快照（含 stateVersion/events/shop），见 pvp-match.md |
| S | match_event | 阶段/结算增量事件，见 pvp-match.md |
| C | battle | `{ "action", "index"?, "indexes"? }`，动作清单见 pvp-match.md |
| C | sync | 断线重连后拉全量快照；不在对局返回 `error(not_in_battle)` |
| C | cancel / leave | 出队；对局中 leave 离开对局（断线不算 leave） |
| C / S | ping / pong | 心跳 |
| S | error | `{ "code", "message" }` |

公开结构 `PlayerPublic`：

```json
{ "userId": "...", "nickName": "张三", "avatarUrl": "https://..." }
```

`room_ready` payload：

```json
{
  "roomId": "...",
  "seed": 123,
  "modeId": 1,
  "players": [
    { "userId": "...", "nickName": "张三", "avatarUrl": "https://..." },
    { "userId": "...", "nickName": "李四", "avatarUrl": "https://..." },
    { "userId": "...", "nickName": "", "avatarUrl": "" },
    { "userId": "...", "nickName": "王五", "avatarUrl": "https://..." }
  ]
}
```

四人收到同一 `roomId` / `seed` 和完整 `players`。客户端按 `players[]` 画 4 个槽，空位占位。不要用 `opponentUserId`。

`suit`：1 红桃 2 方片 3 梅花 4 黑桃。`rank`：1=A … 13=K。对局座位/牌面结构见 pvp-match.md 的 `match_update`（`duel` 字段，摊牌后 `duel.phase` 为 `showdown`，带 `handType` / `level` / `label` / `damage` 与 `winners`）。

## 客户端画头像

- `avatarUrl` 非空：HTTPS 拉图并缓存
- 空或失败：本地默认图，不断排队
