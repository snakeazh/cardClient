#!/usr/bin/env bash
# Card.Server 探活看门狗：挂了发飞书告警，恢复发恢复通知。
# webhook 地址写在 /opt/card-server/feishu-webhook（单行文本，权限 600）。
set -u

WEBHOOK_FILE=/opt/card-server/feishu-webhook
STATE_FILE=/tmp/card-watchdog.state
HEALTH_URL=http://127.0.0.1:5254/v1/health
HOST_TAG="card-server 49.235.187.59"

[ -f "$WEBHOOK_FILE" ] || exit 0
WEBHOOK=$(tr -d '[:space:]' < "$WEBHOOK_FILE")
[ -n "$WEBHOOK" ] || exit 0

BODY=$(curl -s -m 5 "$HEALTH_URL")
NOW=$(date '+%F %T')

if echo "$BODY" | grep -q '"status":"ok"'; then
    if [ -f "$STATE_FILE" ]; then
        # 从故障恢复
        curl -s -m 5 -X POST "$WEBHOOK" -H 'Content-Type: application/json' \
            -d "{\"msg_type\":\"text\",\"content\":{\"text\":\"✅ [已恢复] $HOST_TAG 服务恢复正常\n时间: $NOW\n探活: $BODY\"}}" >/dev/null
        rm -f "$STATE_FILE"
    fi
    exit 0
fi

# 故障：只在首次进入故障状态时告警，避免每 2 分钟刷屏
if [ ! -f "$STATE_FILE" ]; then
    touch "$STATE_FILE"
    DETAIL=$(systemctl --no-pager -l status card-server 2>/dev/null | head -5 | sed 's/"/\\"/g' | tr '\n' '|')
    curl -s -m 5 -X POST "$WEBHOOK" -H 'Content-Type: application/json' \
        -d "{\"msg_type\":\"text\",\"content\":{\"text\":\"🚨 [告警] $HOST_TAG 服务异常\n时间: $NOW\n探活返回: ${BODY:-（无响应/连接失败）}\n服务状态: $DETAIL\n请上机查看: journalctl -u card-server -n 100\"}}" >/dev/null
fi
