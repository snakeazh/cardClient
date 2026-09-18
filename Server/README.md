# Card.Server

炸牌兄弟服务端：局外 HTTP 短链管账号主档，PVP 走 WebSocket 长链。

不使用 Docker。Postgres / Redis 以系统服务跑在本机或小机器上，进程直连。

## 结构

四层，依赖只允许向下：`Server → Domain / Infrastructure`，`Infrastructure → Domain`，`Domain → Contracts`。

| 项目 | 允许 | 不允许 |
|------|------|--------|
| `src/Card.Contracts` | DTO、错误码（netstandard2.1，Unity 可引用） | 规则、仓储 |
| `src/Card.Battle` | 对局引擎、牌桌、出伤、`RelicCombat` | HTTP、档 |
| `src/Card.Domain` | 用例服务、规则、仓储接口 | EF、Redis、ASP.NET |
| `src/Card.Infrastructure` | 仓储实现、token/锁/队列、配表加载、微信/抖音/游客客户端 | HTTP 路由、用例编排 |
| `src/Card.Server` | 路由、中间件、WS 传输、组 DI、启动探活 | 改档规则 |
| `tests/Card.Domain.Tests` | 体力重置、抽天赋、结算幂等、登录弃局 | |

共享库规则：[`src/Card.Battle/README.md`](src/Card.Battle/README.md)、[`src/Card.Contracts/README.md`](src/Card.Contracts/README.md)。

Domain 用例按限界上下文拆：`AuthService`（换码/发 token）、`PlayerMetaService`（主档/天赋/体力/背包/引导）、`PveRunService`（开局/结算/商店/局内金）。登录后清未结算 run 只经过 `IPveRunService`，Auth 不依赖整份 PVE 服务。

组合根在 `Card.Server/Composition`：`AddCardInfrastructure` 只接线存适配器，`AddCardApplication` 注册用例服务。HTTP 按 `health` / `auth` / `player` / `pve` / `debug` 分文件映射，路径不变。

## 编程规则

面对问题，不要绕开。先对准真正缺的那一层再改代码，不要为了让当前请求马上变绿，换一条不该承担这件事的路径。根因在 Redis / 配置 / 协议 / 调用方，就修那一层；不要把职责塞进 Postgres、内存、客户端或别的模块顶上。当前范围做不完：留下接口、TODO、空实现或明确的未接线入口，写清以后谁来填。不要用临时存储、静默降级、假成功、复制一套逻辑来「先跑起来」。

少做防御性编程。调用方保证入参有效，被调用方直接用。空了就让它炸，方便找调用错误。

**不要：**

- `x?.Foo`、`x ?? fallback` 把 null 吞成 0 / 空串 / 空列表
- 参数写成 `T?`，函数开头 `if (x == null) return`
- `EnsureXxx`、`?? throw new ArgumentNullException` 当业务空值补丁
- 列表字段再 `??= new List<>()`（构造时就给空列表）

**可以：**

- 业务上的「没有」：空列表、0、`TryGet*` 找不到配表就跳过
- 边界一次性转正：HTTP JSON、配表反序列化、数据库缺字段 → 进 Domain / Battle 后不再判空
- 启动探活：`Database.EnsureCreated` 这类基础设施

```
// 不要
RelicIds = seat?.RelicIds ?? Array.Empty<int>();
if (tables == null || snapshot == null) return Empty;

// 要
RelicIds = seat.RelicIds;
Evaluate(tables, snapshot, score);
```

日切、补货架用业务名（`ApplyDailyReset`、`FillOffers`），不要叫 `Ensure`。

## 本地跑起来

```
cd Server
dotnet test
dotnet run --project src/Card.Server
```

Development 默认 `Persistence:Provider=Postgres`，`ConnectionStrings:Redis=127.0.0.1:6379`，`GuestAuth:Enabled=true`，`Pvp:FillWithBots=true`。监听 `http://localhost:5254`。主档进 Postgres；token / 锁 / PVP 排队进 Redis。Postgres 模式下 Redis 必填，连不上会直接退出。

第一次先装 PostgreSQL，双击 `setup-postgres.bat` 建库，再 `start-server.bat`。

也可以双击 bat（不要在 PowerShell 里粘贴运行）：

| 文件 | 作用 |
|------|------|
| `start-server.bat` | Development：Postgres 存档 + Redis token + 游客 + PVP 机器人 |
| `start-server-pg.bat` | 同上（显式 launch profile `postgres`） |
| `setup-postgres.bat` | 用 `psql` 建用户/库 `card`（需本机已装 PostgreSQL） |
| `stop-server.bat` | 结束占用 5254 的进程和 `Card.Server.exe` |

### Windows bat 两个坑（已踩过）

cmd 解析 `.bat` 很脆，下面两种写法都会把脚本拆碎，看起来像一堆「xxx 不是内部或外部命令」：

1. **UTF-8 中文 + `%变量%` + `^` 换行续写**  
   文件若是 UTF-8（尤其带中文 echo、`%PORT%`、PowerShell 多行 `^`），cmd 会按系统代码页错切字符。典型现象：`'PORT' 不是内部或外部命令`、`'-NoProfile' 不是内部或外部命令`、中文行变成乱码命令。

2. **Unix 换行（只有 LF，没有 CRLF）**  
   编辑器 / AI 写入常得到 LF。cmd 会吃掉每行开头几个字符：`setlocal` 变成 `'local'`，`cd /d` 变成 `'/d'`，`echo` 变成 `'ho'` / `'o'`，随后误报 `dotnet not found`。

正确写法：

- 编码用 **ASCII**（或系统 ANSI/GBK），不要 UTF-8 中文脚本体。
- 换行必须是 **CRLF**（`0D 0A`）。用 PowerShell 写盘时显式 `-replace "`n","`r`n"` + `Encoding.ASCII`。
- 不要用 `^` 拼多行 PowerShell；关端口用 `netstat` + `taskkill` 即可。
- 启动/关闭请 **资源管理器双击**，或 `cmd /c start-server.bat`。

配表目录默认 `../../../Card/Assets/Res/Config`（相对 `src/Card.Server`）。找不到时用内置兜底表，接口仍可测。

游客登录：

```
POST /v1/auth/login
{ "provider": "guest", "code": "editor-device-id", "userInfo": { "nickName": "测试", "avatarUrl": "" } }
```

之后 HTTP 带 `Authorization: Bearer <accessToken>`。微信/抖音登录同样带 `userInfo`（SDK 授权后的 nickName + avatarUrl）。

## 小机器 / 本机 Postgres + Redis

不使用 Docker。PostgreSQL、Redis 以系统服务跑在本机，进程直连。

**职责：**

| 组件 | 存什么 |
|------|--------|
| Postgres | 主档 `players`、登录绑定 `auth_bindings`、PVE `pve_runs` |
| Redis | access token、单玩家锁、PVP 匹配队列 |
| Memory（无 Postgres） | 全进程内存，仅测试/无库场景 |

**本机第一次：**

1. 安装 PostgreSQL（把 `psql` 加进 PATH）、Redis（监听 `127.0.0.1:6379`）。
2. 双击 `setup-postgres.bat`，或手动：

```
psql -U postgres -f setup-postgres.sql
```

默认连接串：`Host=127.0.0.1;Port=5432;Database=card;Username=card;Password=card`。
3. 双击 `start-server.bat`（或 Visual Studio 选 Development / `http`）。
4. 启动时连不上 Postgres/Redis 会直接退出。探活：`GET /v1/health`，返回 `persistence` / `postgres` / `redis`。

Editor 这条路径仍 `GuestAuth=true`。主档和 token 会跨进程重启保留。

**生产配置**（`appsettings.Production.json` 或环境变量）：

```
Persistence__Provider=Postgres
ConnectionStrings__Postgres=Host=127.0.0.1;Port=5432;Database=card;Username=card;Password=***
ConnectionStrings__Redis=127.0.0.1:6379
GuestAuth__Enabled=false
WeChat__AppId=...
WeChat__AppSecret=...
Douyin__AppId=...
Douyin__AppSecret=...
Game__ConfigPath=/path/to/Card/Assets/Res/Config
```

启动时 `EnsureCreated` 会建 `players` / `auth_bindings` / `pve_runs`。

- `Persistence:Provider=Postgres` 必须同时配 `ConnectionStrings:Redis`：主档落库，token/锁/PVP 队列走 Redis。
- `Persistence:Provider=Memory` 且 Redis 留空：全部在进程内存，仅本地测试。
- 微信 / 抖音未填 AppId 时，对应 `provider` 登录返回 `501` + `provider_not_configured`。

## HTTP `/v1`

局外主档走短链。每个写接口返回完整 `PlayerProfileDto`，客户端只经 `ApplyServerProfile` 灌缓存，不要本地 `Wallet.Add`。

两种金币：

- **局外 Wallet**：账号金币，在 `PlayerProfileDto.gold`。结算、广告商店、抽天赋才会改。
- **局内 `PveRun.gold`**：闯关筹码。通关商店买/卖/刷新圣物、搓牌花费走下面的 shop/run 接口；伤害换金第一期仍由客户端 `grant-gold` 上报金额。局外结算金不再信客户端分数。

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/v1/health` | 探活：`persistence` / `postgres` / `redis`，后端挂了返回 503 |
| POST | `/auth/login` | `{ provider, code, userInfo?, pendingSettle? }`。登录时处理未结算 run |
| GET | `/player/profile` | 拉主档。跨日才写回体力/广告计数，同一天的 GET 不落库 |
| POST | `/pve/start` | 扣体力，发 `runId`、初始金币与货架。已有未结算 run 时返回 `active_run_exists` |
| GET | `/pve/run/active` | 当前未结算 run；没有则为 `{ "run": null }` |
| POST | `/pve/run/progress` | `{ runId, clearedStage, score }` 上报本关进度。服务端钳制分数、累计积分、推进关卡 |
| POST | `/pve/settle` | `{ runId, cleared, forfeit? }`。奖励由服务端按进度判定，忽略客户端 `totalScore` / `stats` / `levelId` |
| POST | `/pve/shop/enter` | 进店：免费重滚货架，写入免费刷新次数 |
| POST | `/pve/shop/buy` | `{ runId, relicId }` 扣局内金、拿圣物 |
| POST | `/pve/shop/sell` | `{ runId, relicId }` 卖圣物 |
| POST | `/pve/shop/refresh` | `{ runId }` 刷新货架 |
| POST | `/pve/run/grant-gold` | `{ runId, amount, reason }` 局内入账（客户端上报） |
| POST | `/pve/run/spend` | `{ runId, amount, kind }` 局内扣费 |
| POST | `/talent/draw` | 服务端随机 + 扣金 + DrawCount |
| POST | `/energy/refill-ad` | 广告回满体力 |
| POST | `/adshop/claim` | `{ kind: stamina\|gold }` |
| POST | `/bag/grant` | `{ itemId, amount }` |
| POST | `/bag/consume` | `{ itemId, amount }` |
| POST | `/guide/complete` | `{ groupId }` 幂等 |
| POST | `/debug/grant-gold` | Development 调试发局外金 |

错误体：`{ "code", "message" }`。

## WebSocket `/v1/pvp/ws`

规则见 [`docs/pvp.md`](docs/pvp.md)。**产品规则：4 人一房，人齐才开房。** 未满 4 人只处于排队，不发 `room_ready`、不开局。

多轮 1v1（配表轮次、扣血、淘汰）见 [`docs/pvp-match.md`](docs/pvp-match.md)。开房后按 `PvpModeConfig` 第 1 轮开 1v1 子桌，不再四人同桌摊一张。

Development 默认 `Pvp:FillWithBots=true`：一人 `queue` 即用 3 个机器人补满开房，没有真人的子桌服务端自动摊牌。正式服保持关闭。

消息：`{ "t", "seq", "payload" }`。

客户端：`auth`（payload.accessToken）→ `queue` / `cancel` / `ping` / `leave` / `battle`（`pick` / `rub` / `replace` / `peek` / `showdown`）  
服务端：`hello` / `authed` / `queued` / `queue_update` / `room_ready` / `match_update` / `battle_update` / `pong` / `error`

未满 4 人：`queued` / `queue_update` 带 `players`（userId、nickName、avatarUrl）。满 4 人：`room_ready` 带 `roomId`、`seed`、`modeId`、四人 `players`，随即 `match_update`。第一轮按配表开 1v1 子桌（经典模式为野怪热身）。摊牌只结算自己那桌；出伤仍走 `CombatDamage` / `CombatBonuses`，再乘模式表的轮次系数后扣败者 HP。无局内商店叠层、BOSS、燧石；对玩家不斩杀。

## 登录与未结算对局

客户端启动若已有 access token，先 `GET /player/profile`；401 再游客登录。连不上弹窗重试，不会静默进主页。

**未结算 run 在 `POST /auth/login` 里判定并处理。**

1. 请求可带 `pendingSettle`（`runId` + `cleared` + `forfeit`）。有则先按挂单结算，**奖励按服务端已记录的关卡进度和积分**，不信客户端 `totalScore` / `stats`。
2. 通关（`cleared=true`）要求本局已 `POST /pve/run/progress` 且至少清过一关；否则视为无效挂单，再按放弃处理。
3. 若之后仍有 active run：服务端按失败结算（与局内放弃相同）。积分金、已推进最高关、商店/局内金相关解锁照发；不发整章通关金。开局已扣体力不退。
4. 返回的 `PlayerProfileDto` 已是处理后的主档。客户端只 `ApplyServerProfile`。
5. 选关若仍碰到 `active_run_exists`（同一次启动里未结算就再开一局）：客户端再按失败结算并提示。
6. Memory 存储下重启 Server 会清空 run。挂单补报得到 `run_not_found` / `conflict` / `invalid_request` 即忽略，视为对局已失效。
