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
- 系统：Alibaba Cloud Linux 3、OpenCloudOS 9 或 Ubuntu 22.04+，`setup-ecs.sh` 自动识别 dnf/apt（含 pg_hba ident→scram、redis 配置路径差异的处理）。
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
  "ConnectionStrings": {
    "Postgres": "Host=127.0.0.1;Port=5432;Database=card;Username=card;Password=强密码",
    "Redis": "127.0.0.1:6379,password=你的Redis密码"
  },
  "GuestAuth": { "Enabled": false, "EditorEnabled": true },
  "Pvp": { "FillWithBots": false },
  "WeChat": { "AppId": "你的AppId", "AppSecret": "你的AppSecret" },
  "Douyin": { "AppId": "", "AppSecret": "" },
  "Cos": { "SecretId": "", "SecretKey": "", "Region": "ap-shanghai", "Bucket": "card-avatar2-1328828696" },
  "Game": { "ConfigPath": "/opt/card-server/Config" },
  "Admin": { "Token": "强随机管理密码32位以上" }
}
```

要点（对照仓库 `Server/README.md` 的生产清单）：

- `Urls` 锁回环地址，对外只经 Nginx。
- `GuestAuth` 关闭、`Pvp:FillWithBots` 关闭（Development 的机器人补满只是本地调试用）。
- `/debug/grant-gold` 等调试接口只在 Development 暴露，Production 自动不带。
- **`Admin:Token` 是 GM 后台总闸**：不配则 `/v1/gm` 整组接口不存在，后台页面也登不进；它就是后台登录页的密码，只发给需要用后台的人。
- `ConfigPath` 指向 `/opt/card-server/Config`（deploy.sh 会同步 `Card/Assets/Res/Config`）。

## 3. 配 Nginx + 证书（只做一次）

1. 编辑 `/etc/nginx/conf.d/card.conf`：换域名、填证书路径（证书文件放到 `/etc/nginx/cert/`）。
2. `nginx -t && systemctl restart nginx`。

WebSocket 的 `Upgrade`/`Connection` 头和长 `proxy_read_timeout` 已在配置里，不要删。

配置里另有 GM 后台与备案过渡期相关的段落（不需要可整段删）：

- `location /gm/`：静态托管 `Server/admin-web` 的构建产物（`/opt/card-server/admin/`），浏览器访问 `https://域名/gm/` 打开后台。
- `location /v1/gm/`：GM 接口反代，公网可达、靠管理密码保护。若只想允许固定 IP 访问，在此段内加 `allow <IP>; deny all;`。
- 80 端口的 `default_server`（`server_name _`）是**备案过渡期的 IP 直连站点**：域名未备案接入时腾讯云会拦截域名流量，这期间客户端和 GM 后台都走 `http://<ECS_IP>`（客户端 `GameApiSettings.DefaultBaseUrl` 即此地址）。**域名备案恢复后删除该站点**，全量切回 HTTPS。注意路径刻意用 `/gm/` 而非 `/admin/`（避开安全设备对 admin 关键字的拦截）。

微信/抖音后台把 `https://api.example.com` 配进 request 合法域名、`wss://api.example.com` 配进 socket 合法域名。

## 4. 日常发布（本地执行）

前置：本机 SSH 免密（把本机公钥写入服务器 `/root/.ssh/authorized_keys` 一次即可）。

```bash
cd Server/deploy
./deploy.sh root@<ECS_IP>
```

流程：`dotnet publish`（linux-x64，框架依赖）→ 上传应用到 `/opt/card-server/app`
（**不覆盖**线上 `appsettings.Production.json`）→ 上传配表到 `/opt/card-server/Config`
→ `npm run build` 构建 GM 后台并上传到 `/opt/card-server/admin`
→ `systemctl restart card-server` → 调 `/v1/health` 探活。

上传优先用 rsync（带 `--delete`）；Windows Git Bash 没有 rsync 时自动退化为 tar 管道
（不清理远端多余文件，功能等价）。

**发布后验证清单**（脚本末尾的探活有启动竞态，偶发误报失败，等 3~5 秒手动复查即可）：

```bash
ssh root@<ECS_IP> "
  curl -s http://127.0.0.1:5254/v1/health; echo                        # 含 postgres/redis 即正常
  curl -s -o /dev/null -w '%{http_code}\n' -X POST \
    http://127.0.0.1:5254/v1/gm/login -H 'Content-Type: application/json' \
    -d '{\"password\":\"<Admin:Token>\"}'                              # 200 = GM 接口正常
  curl -s -o /dev/null -w '%{http_code}\n' http://<ECS_IP>/v1/health   # 200 = IP 直连链路正常
  curl -s -o /dev/null -w '%{http_code}\n' http://<ECS_IP>/gm/         # 200 = 后台静态页正常
"
```

**访问入口**：

- 域名已备案：`https://域名/gm/`（GM 后台）、`https://域名`（游戏）
- 备案过渡期：`http://<ECS_IP>/gm/`（GM 后台）、`http://<ECS_IP>`（游戏，HTTP 明文仅过渡用）

**域名是否还被备案拦截的自查**：`curl -i http://域名/`，返回 302 跳
`dnspod.qcloud.com/static/webblock.html` 即仍被拦截；返回 301 跳 `https://域名`
则已恢复，可删掉 80 端口的 IP 直连站点并切换客户端 `GameApiSettings.DefaultBaseUrl`
为 `https://域名`。

## 5. 运维速查

```bash
journalctl -u card-server -f        # 看日志
systemctl restart card-server       # 重启应用
curl http://127.0.0.1:5254/v1/health
```

- **飞书告警**：`watchdog.sh` 每 2 分钟探活一次，挂了就推飞书群机器人、恢复再推一条（故障期间不刷屏）。部署方式：脚本放 `/opt/card-server/watchdog.sh`，webhook 地址写入 `/opt/card-server/feishu-webhook`（单行文本），crontab 加 `*/2 * * * * /opt/card-server/watchdog.sh`。webhook 文件不存在时脚本静默退出。

- **备份**：核心是 PG 的 `card` 库。配 crontab 每日 `pg_dump`：
  `0 4 * * * sudo -u postgres pg_dump card | gzip > /data/backup/card-$(date +\%F).sql.gz`，
  并定期拷到 OSS。Redis 里只有 token/锁/PVP 队列，丢了最多玩家重新登录，可不备。
- **配表更新**：改 xlsx 后先跑 `Config/一键导出.bat` 更新 `Card/Assets/Res/Config`，再跑 `deploy.sh`。
- **日志磁盘**：setup 脚本已把 journald 上限设为 500M（`/etc/systemd/journald.conf` 的 `SystemMaxUse`）。
- 后续量上来要拆库：PG/Redis 换 RDS/云 Redis，只改 `appsettings.Production.json` 两个连接串。
