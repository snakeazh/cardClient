#!/usr/bin/env bash
# 一次性初始化 ECS：装 ASP.NET Core 8 运行时 / PostgreSQL / Redis / Nginx，建库，装 systemd 单元。
# 支持 Alibaba Cloud Linux 3 (dnf) 和 Ubuntu 22.04+ (apt)。root 或 sudo 执行。
# 用法: REDIS_PASS=xxx PG_PASS=xxx bash setup-ecs.sh
set -euo pipefail

REDIS_PASS="${REDIS_PASS:?请设置 REDIS_PASS}"
PG_PASS="${PG_PASS:?请设置 PG_PASS}"
APP_DIR=/opt/card-server
DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"

if command -v dnf >/dev/null 2>&1; then
    PM=dnf
    REDIS_CONF=/etc/redis.conf
    REDIS_SVC=redis
elif command -v apt-get >/dev/null 2>&1; then
    PM=apt
    REDIS_CONF=/etc/redis/redis.conf
    REDIS_SVC=redis-server
else
    echo "不支持的系统：找不到 dnf / apt-get"; exit 1
fi
echo "==> 包管理器: ${PM}"

echo "==> 安装软件包"
if [ "${PM}" = dnf ]; then
    dnf install -y aspnetcore-runtime-8.0 postgresql-server postgresql redis nginx
else
    apt-get update
    apt-get install -y aspnetcore-runtime-8.0 postgresql redis-server nginx
fi

echo "==> 初始化并启动 PostgreSQL"
if [ "${PM}" = dnf ] && [ ! -d /var/lib/pgsql/data/base ]; then
    postgresql-setup --initdb
fi
systemctl enable --now postgresql

echo "==> 建库（已存在会报错，可忽略）"
# postgres 用户读不到 /root，先拷到 /tmp
cp "${DEPLOY_DIR}/../setup-postgres.sql" /tmp/setup-postgres.sql
chmod 644 /tmp/setup-postgres.sql
(cd /tmp && sudo -u postgres psql -f /tmp/setup-postgres.sql) || true
rm -f /tmp/setup-postgres.sql
sudo -u postgres psql -c "ALTER USER card WITH PASSWORD '${PG_PASS}';"

echo "==> 配置 Redis（仅本机 + 密码）"
sed -i -E "s/^#? *requirepass .*/requirepass ${REDIS_PASS}/" "${REDIS_CONF}"
grep -q '^requirepass' "${REDIS_CONF}" || echo "requirepass ${REDIS_PASS}" >> "${REDIS_CONF}"
systemctl enable --now "${REDIS_SVC}"
systemctl restart "${REDIS_SVC}"

echo "==> 2G swap（小内存机器防 OOM）"
if ! swapon --show | grep -q .; then
    fallocate -l 2G /swapfile || dd if=/dev/zero of=/swapfile bs=1M count=2048
    chmod 600 /swapfile
    mkswap /swapfile
    swapon /swapfile
    grep -q '/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

echo "==> 部署目录"
mkdir -p ${APP_DIR}/app ${APP_DIR}/Config

echo "==> systemd 单元"
cp "${DEPLOY_DIR}/card-server.service" /etc/systemd/system/card-server.service
systemctl daemon-reload
systemctl enable card-server   # 首次部署完应用后再 systemctl start

echo "==> Nginx"
cp "${DEPLOY_DIR}/nginx-card.conf" /etc/nginx/conf.d/card.conf
echo "    域名备案好后编辑 /etc/nginx/conf.d/card.conf 填域名和证书路径，然后："
echo "    nginx -t && systemctl restart nginx"

echo "==> 完成。接下来："
echo "    1. 写 ${APP_DIR}/app/appsettings.Production.json（见 deploy/README.md 第 2 步）"
echo "    2. 本地执行 ./deploy.sh root@<ECS_IP> 上传应用"
echo "    3. systemctl restart card-server && curl http://127.0.0.1:5254/v1/health"
