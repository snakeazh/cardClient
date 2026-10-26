#!/usr/bin/env python3
"""Clear GuideCompletedGroupIds in local Postgres players.profile_json."""
import json
import sys

try:
    import psycopg2
except ImportError:
    import subprocess
    subprocess.check_call([sys.executable, "-m", "pip", "install", "psycopg2-binary", "-q"])
    import psycopg2

conn = psycopg2.connect(
    host="127.0.0.1",
    port=5432,
    dbname="card",
    user="card",
    password="card",
)
cur = conn.cursor()
# EF 默认列名带大小写引号：UserId / ProfileJson
cur.execute('select "UserId", "ProfileJson" from players')
rows = cur.fetchall()
print("players", len(rows))
updated = 0
for uid, pj in rows:
    if isinstance(pj, (bytes, bytearray)):
        pj = pj.decode("utf-8")
    if isinstance(pj, str):
        data = json.loads(pj)
    else:
        data = dict(pj)
    guides = data.get("GuideCompletedGroupIds") or data.get("guideCompletedGroupIds") or []
    print(str(uid), "guides=", guides)
    data["GuideCompletedGroupIds"] = []
    if "guideCompletedGroupIds" in data:
        data["guideCompletedGroupIds"] = []
    cur.execute(
        'update players set "ProfileJson"=%s::jsonb where "UserId"=%s',
        [json.dumps(data), uid],
    )
    updated += 1
conn.commit()
print("cleared", updated)
cur.close()
conn.close()
