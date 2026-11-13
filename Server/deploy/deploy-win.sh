#!/usr/bin/env bash
# Windows（Git Bash）版日常发布，与 deploy.sh 等价：
#   dotnet publish → tar 打包（排除线上配置）→ scp 上传 → 服务器备份/解包/重启 → 探活。
# 用 rsync 的原版在 Git Bash 跑不了（无 rsync），这里用 tar + scp 代替。
# 用法: ./deploy-win.sh [user@host]     默认 root@121.43.167.98
set -euo pipefail

TARGET="${1:-root@121.43.167.98}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_DIR=/opt/card-server
PUBLISH_DIR="${REPO_ROOT}/Server/publish"
APP_TGZ="/tmp/card-app.tar.gz"
CFG_TGZ="/tmp/card-config.tar.gz"
STAMP="$(date +%Y%m%d-%H%M%S)"

echo "==> 1/5 发布 Card.Server（Release，linux-x64 前提：服务器已装 aspnetcore-runtime-8.0）"
rm -rf "${PUBLISH_DIR}"
dotnet publish "${REPO_ROOT}/Server/src/Card.Server" -c Release -o "${PUBLISH_DIR}"

echo "==> 2/5 打包应用与配表（排除线上 appsettings.Production.json，不覆盖服务器密码配置）"
tar -czf "${APP_TGZ}" -C "${PUBLISH_DIR}" \
    --exclude='appsettings.Production.json' \
    --exclude='appsettings.Development.json' .
tar -czf "${CFG_TGZ}" -C "${REPO_ROOT}/Card/Assets/Res/Config" .

echo "==> 3/5 上传到 ${TARGET}（提示输服务器密码，只需这一次）"
scp -q "${APP_TGZ}" "${CFG_TGZ}" "${TARGET}:/tmp/"

echo "==> 4/5 服务器侧：备份旧版 → 解包 → 重启（提示再输一次密码）"
ssh "${TARGET}" "
set -e
mkdir -p ${APP_DIR}/app ${APP_DIR}/Config
if [ -f ${APP_DIR}/app/Card.Server.dll ]; then
    cp -a ${APP_DIR}/app ${APP_DIR}/app.bak.${STAMP}
    echo '已备份旧版到 ${APP_DIR}/app.bak.${STAMP}'
fi
find ${APP_DIR}/app -type f ! -name 'appsettings.Production.json' -delete
tar -xzf /tmp/card-app.tar.gz -C ${APP_DIR}/app
tar -xzf /tmp/card-config.tar.gz -C ${APP_DIR}/Config
rm -f /tmp/card-app.tar.gz /tmp/card-config.tar.gz
systemctl restart card-server
sleep 2
systemctl --no-pager -l status card-server | head -12
"

echo "==> 5/5 探活"
ssh "${TARGET}" "curl -s http://127.0.0.1:5254/v1/health" \
    || echo "健康检查失败：ssh 上去执行 journalctl -u card-server -n 100 看日志"
echo
echo "完成。探活输出含 persistence/postgres/redis 即正常。"
