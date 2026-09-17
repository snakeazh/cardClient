# PVP 房间

## 规则

- 一局 **4 名玩家**，不多不少。
- **人齐才开房**：匹配队列凑满 4 人后才创建房间、下发 `room_ready`、允许开局。
- 1～3 人时只排队，不生成 `roomId`，不发种子，不开始比牌。队内广播当前名单（含微信/抖音头像 URL 与昵称）。
- 排队中可 `cancel` / `leave` / 断线出队，空位由后续进队的人补。
- 开房后立刻用房间 `seed` 走共享 `BattleEngine(PvpMode)` 发牌；比牌指令与单机主线同一套 `Apply`。
- 出伤与 PVE 玩家同一公式：开房读各人主档 `LastHeroId`、天赋袋和**已解锁圣物 ID**（`ShopRelicIds`）。摊牌时 `CombatBonuses` 填英雄攻击/生命、天赋加攻与倍率、圣物本手倍率/加攻、英雄伤害%、暴击、追击。PVP 没有闯关商店叠层、BOSS、燧石；未亮出牌类圣物本回合为 0；斩杀不对玩家生效。
- 头像是登录时客户端上报的社交头像 URL，不是局内英雄 `People1`。WebSocket **不传图片**，只传 URL。
- 牌面不上内部 `Card` struct，只下发 `suit`/`rank` 整数。摊牌前只能看到自己的 3 张。

```
queue → 队列人数 < 4 → 自己收到 queued，其他人收到 queue_update（players 1～3）
queue → 队列人数 = 4 → 创建房间 → 四人各收 room_ready → 服务端 Deal → 各收 battle_update
客户端 battle { action: "showdown" } → 服务端摊牌 → 四人各收 battle_update（含对手牌与胜者）
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
| S | queued | 自己入队且未满 4：`{ "players": [1～3] }` |
| S | queue_update | 有人加入/退出，发给队内其他人：`{ "players": [1～3] }` |
| S | room_ready | 四人到齐才发，见下 |
| S | battle_update | 开房发牌后立刻下发；摊牌后再发一次。按观众座位隐藏对手牌 |
| C | battle | `{ "action": "showdown" }`，任意在座玩家可摊牌；`open` 同义 |
| C | cancel / leave | 出队；已开房则离开对局 |
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
  "players": [
    { "userId": "...", "nickName": "张三", "avatarUrl": "https://..." },
    { "userId": "...", "nickName": "李四", "avatarUrl": "https://..." },
    { "userId": "...", "nickName": "", "avatarUrl": "" },
    { "userId": "...", "nickName": "王五", "avatarUrl": "https://..." }
  ]
}
```

四人收到同一 `roomId` / `seed` 和完整 `players`。客户端按 `players[]` 画 4 个槽，空位占位。不要用 `opponentUserId`。

`battle_update` payload（发牌后、对自己视角）：

```json
{
  "roomId": "...",
  "seed": 123,
  "mode": "pvp",
  "phase": "dealt",
  "viewerSeat": 0,
  "winners": [],
  "seats": [
    { "seatId": 0, "userId": "...", "nickName": "张三", "isHuman": true, "alive": true,
      "cards": [ { "suit": 1, "rank": 1 }, { "suit": 4, "rank": 13 }, { "suit": 2, "rank": 7 } ] },
    { "seatId": 1, "userId": "...", "nickName": "李四", "isHuman": true, "alive": true, "cards": null }
  ]
}
```

`suit`：1 红桃 2 方片 3 梅花 4 黑桃。`rank`：1=A … 13=K。摊牌后 `phase` 为 `showdown`，四人 `cards` 都有值，并带 `handType` / `level` / `label` / `damage` 与 `winners`（座位下标）。`damage` 是该座位按玩家出伤公式算出的伤害。

## 客户端画头像

- `avatarUrl` 非空：HTTPS 拉图并缓存
- 空或失败：本地默认图，不断排队
