# 单台 ECS 部署（不用 Docker）

应用、PostgreSQL、Redis、Nginx 全装在一台 ECS 上。内测/小规模够用，后续要拆只需把
PG/Redis 换成云托管版并改连接串，应用侧不用改代码。

| 文件 | 用途 |
|------|------|
| `setup-ecs.sh` | 服务器一次性初始化（装运行时/数据库、建库、装 systemd 单元） |
| `deploy.sh` | 本地发布 + 上传 + 重启（每次更新跑这个） |
| `card-server.service` | systemd 单元（被 setup 拷到 /etc/systemd/system/） |
| `nginx-card.conf` | Nginx 反代：HTTPS + WebSocket（被 setup 拷到 /etc/nginx/conf.d/） |

## 0. 买机器与前置条件

- ECS：**2C4G 起步**（PG + Redis + 应用同机，2C2G 也能跑，setup 脚本会自动加 2G swap），系统盘 40G+。
- 系统：Alibaba Cloud Linux 3 或 Ubuntu 22.04+，`setup-ecs.sh` 自动识别 dnf/apt。
- 面向微信/抖音小游戏：服务器放**中国大陆**，域名完成 **ICP 备案**，否则配不进小程序后台。
- 一个域名（如 `api.example.com`）解析到 ECS 公网 IP，并在阿里云申请免费 SSL 证书。
- 安全组：只放行 **22 / 80 / 443**。5432（PG）、6379（Redis）、5254（应用）**全部不对外**。

## 1. 初始化服务器（只做一次）

```bash
# 把 Server/ 整个目录传到 ECS（或 git clone 仓库）
scp -r Server root@<ECS_IP>:/root/

ssh root@<ECS_IP>
cd /root/Server/deploy
REDIS_PASS=你的Redis密码 bash setup-ecs.sh
```

脚本会装 dotnet 8 运行时 / PostgreSQL / Redis / Nginx，初始化 PG 数据目录并执行
`../setup-postgres.sql` 建 `card` 用户和库（表由应用启动时 `EnsureCreated` 自动建）。

**立刻改掉默认密码**：

```bash
sudo -u postgres psql -c "ALTER USER card WITH PASSWORD '强密码';"
# Redis 密码已在 setup 时写入 /etc/redis.conf
```

PG 和 Redis 默认只监听 127.0.0.1，同机访问，不要开外网。

## 2. 写线上配置（只做一次，含密码，不进 git 的那份在服务器上）

```bash
vi /opt/card-server/app/appsettings.Production.json
```

```json
{
  "Urls": "http://127.0.0.1:5254",
  "Persistence": { "Provider": "Postgres" },
  "ConnectionStrings": {
    "Postgres": "Host=127.0.0.1;Port=5432;Database=card;Username=card;Password=强密码",
    "Redis": "127.0.0.1:6379,password=你的Redis密码"
  },
  "GuestAuth": { "Enabled": false },
  "Pvp": { "FillWithBots": false },
  "WeChat": { "AppId": "你的AppId", "AppSecret": "你的AppSecret" },
  "Douyin": { "AppId": "", "AppSecret": "" },
  "Game": { "ConfigPath": "/opt/card-server/Config" }
}
```

要点（对照仓库 `Server/README.md` 的生产清单）：

- `Urls` 锁回环地址，对外只经 Nginx。
- `GuestAuth` 关闭、`Pvp:FillWithBots` 关闭（Development 的机器人补满只是本地调试用）。
- `/debug/grant-gold` 等调试接口只在 Development 暴露，Production 自动不带。
- `ConfigPath` 指向 `/opt/card-server/Config`（deploy.sh 会同步 `Card/Assets/Res/Config`）。

## 3. 配 Nginx + 证书（只做一次）

1. 编辑 `/etc/nginx/conf.d/card.conf`：换域名、填证书路径（证书文件放到 `/etc/nginx/cert/`）。
2. `nginx -t && systemctl restart nginx`。

WebSocket 的 `Upgrade`/`Connection` 头和长 `proxy_read_timeout` 已在配置里，不要删。

微信/抖音后台把 `https://api.example.com` 配进 request 合法域名、`wss://api.example.com` 配进 socket 合法域名。

## 4. 日常发布（本地执行）

```bash
cd Server/deploy
./deploy.sh root@<ECS_IP>
```

流程：`dotnet publish`（linux-x64，框架依赖）→ rsync 应用到 `/opt/card-server/app`
（**不覆盖**线上 `appsettings.Production.json`）→ rsync 配表到 `/opt/card-server/Config`
→ `systemctl restart card-server` → 调 `/v1/health` 探活。

探活返回含 `persistence`/`postgres`/`redis` 即正常。

## 5. 运维速查

```bash
journalctl -u card-server -f        # 看日志
systemctl restart card-server       # 重启应用
curl http://127.0.0.1:5254/v1/health
```

- **备份**：核心是 PG 的 `card` 库。配 crontab 每日 `pg_dump`：
  `0 4 * * * sudo -u postgres pg_dump card | gzip > /data/backup/card-$(date +\%F).sql.gz`，
  并定期拷到 OSS。Redis 里只有 token/锁/PVP 队列，丢了最多玩家重新登录，可不备。
- **配表更新**：改 xlsx 后先跑 `Config/一键导出.bat` 更新 `Card/Assets/Res/Config`，再跑 `deploy.sh`。
- **日志磁盘**：journald 默认有上限，长期运行建议在 `/etc/systemd/journald.conf` 设 `SystemMaxUse=500M`。
- 后续量上来要拆库：PG/Redis 换 RDS/云 Redis，只改 `appsettings.Production.json` 两个连接串。
