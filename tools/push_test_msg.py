# -*- coding: utf-8 -*-
"""临时脚本：连续插入 3 条 chat_message，验证缓存命中率随请求增多上升"""
import sqlite3
import uuid
import json

DB = r'D:\C\小程序\server\data\njust.db'

conn = sqlite3.connect(DB)
cur = conn.cursor()

msgs = [
    ("再问一句", "今天过得怎么样？"),
    ("说个笑话", "讲个简短的笑话。"),
    ("介绍一下你自己", "用一句话介绍你是什么。"),
]

for title, body in msgs:
    msg_id = str(uuid.uuid4())
    payload = json.dumps({"autoTriggerTools": True}, ensure_ascii=False)
    cur.execute(
        "INSERT INTO push_messages (id, type, title, body, payload, created_at, delivered) "
        "VALUES (?, 'chat_message', ?, ?, ?, datetime('now'), 0)",
        (msg_id, title, body, payload)
    )
    print("插入:", title)

conn.commit()
conn.close()
print("完成")
