# Card.Server

炸牌兄弟服务端：局外 HTTP 短链管账号主档，PVP 走 WebSocket 长链。第一期不模拟发牌，客户端仍跑 `GameSession`。

不使用 Docker。Postgres / Redis 以系统服务跑在本机或小机器上，进程直连。

## 结构

| 项目 | 作用 |
|------|------|
| `src/Card.Contracts` | netstandard2.1，DTO / 错误码，以后可给 Unity 引用 |
| `src/Card.Domain` | 主档规则、PVE 开局结算、PVP 匹配 |
| `src/Card.Infrastructure` | 内存或 Postgres 仓储、可选 Redis 锁/匹配、微信/抖音/游客登录 |
| `src/Card.Server` | ASP.NET Host |
| `tests/Card.Domain.Tests` | 体力重置、抽天赋、结算幂等 |

## 本地跑起来（无需 Postgres）

```
cd Server
dotnet test
dotnet run --project src/Card.Server
```

Development 默认 `Persistence:Provider=Memory`，`GuestAuth:Enabled=true`。监听 `http://localhost:5254`。

也可以双击 bat（不要在 PowerShell 里粘贴运行）：

| 文件 | 作用 |
|------|------|
| `start-server.bat` / `开启Server.bat` | `dotnet run --project src/Card.Server` |
| `stop-server.bat` / `关闭Server.bat` | 结束占用 5254 的进程和 `Card.Server.exe` |

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

## 小机器上用 Postgres + Redis

1. 用发行版安装 PostgreSQL、Redis（systemd / Windows 服务均可），不要装 Docker。
2. 建库：`CREATE DATABASE card;` 建用户并授权。
3. 生产配置（`appsettings.Production.json` 或环境变量）：

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

- 只配 Postgres：主档和 run 落库，锁和 PVP 匹配仍用进程内存。
- 再配 Redis：单玩家锁和 PVP 队列走 Redis。
- `ConnectionStrings:Redis` 留空即不用 Redis。

微信 / 抖音未填 AppId 时，对应 `provider` 登录返回 `501` + `provider_not_configured`。

## HTTP `/v1`

局外主档走短链。每个写接口返回完整 `PlayerProfileDto`，客户端只经 `ApplyServerProfile` 灌缓存，不要本地 `Wallet.Add`。

两种金币：

- **局外 Wallet**：账号金币，在 `PlayerProfileDto.gold`。结算、广告商店、抽天赋才会改。
- **局内 `PveRun.gold`**：闯关筹码。通关商店买/卖/刷新圣物、搓牌花费走下面的 shop/run 接口；伤害换金第一期由客户端 `grant-gold` 上报。

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/health` | 探活 |
| POST | `/auth/login` | `{ provider, code, userInfo? }` |
| POST | `/auth/refresh` | `{ refreshToken }` |
| GET | `/player/profile` | 拉主档（体力已按服务器时区跨日回满） |
| POST | `/pve/start` | 扣体力，发 `runId`、初始金币与货架 |
| POST | `/pve/settle` | 幂等结算：通关、兑金、解锁进度 |
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

消息：`{ "t", "seq", "payload" }`。

客户端：`auth`（payload.accessToken）→ `queue` / `cancel` / `ping` / `leave`  
服务端：`hello` / `authed` / `queued` / `queue_update` / `room_ready` / `pong` / `error`

未满 4 人：`queued` / `queue_update` 带 `players`（userId、nickName、avatarUrl）。满 4 人：`room_ready` 带 `roomId`、`seed`、四人 `players`。比牌尚未实现。
