#!/usr/bin/env bash
# 本地（macOS/Linux）执行：发布应用 + 配表，rsync 到 ECS，重启服务。
# 用法：./deploy.sh [user@host]     默认 root@127.0.0.1（请改）
set -euo pipefail

TARGET="${1:?用法: ./deploy.sh user@ecs-ip}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_DIR=/opt/card-server
PUBLISH_DIR="${REPO_ROOT}/Server/publish"

echo "==> 发布 Card.Server（框架依赖，服务器需装 aspnetcore-runtime-8.0）"
rm -rf "${PUBLISH_DIR}"
dotnet publish "${REPO_ROOT}/Server/src/Card.Server" \
    -c Release -o "${PUBLISH_DIR}"

echo "==> 上传应用（不覆盖线上 appsettings.Production.json）"
rsync -az --delete \
    --exclude 'appsettings.Production.json' \
    --exclude 'appsettings.Development.json' \
    "${PUBLISH_DIR}/" "${TARGET}:${APP_DIR}/app/"

echo "==> 上传配表"
rsync -az --delete \
    "${REPO_ROOT}/Card/Assets/Res/Config/" "${TARGET}:${APP_DIR}/Config/"

echo "==> 重启服务"
ssh "${TARGET}" "systemctl restart card-server && sleep 2 && systemctl --no-pager -l status card-server | head -15"

echo "==> 探活"
ssh "${TARGET}" "curl -s http://127.0.0.1:5254/v1/health" || echo "健康检查失败，看 journalctl -u card-server -n 100"
echo
echo "完成。"
