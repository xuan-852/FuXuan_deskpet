// 临时测试脚本：推送超长消息触发 T5 历史裁剪+摘要
const fs = require('fs');

const env = fs.readFileSync('D:/C/小程序/server/.env', 'utf8');
const m = env.match(/^MINI_TOKEN=(.+)$/m);
if (!m) { console.error('MINI_TOKEN not found'); process.exit(1); }
const TOKEN = m[1].trim();

async function push(title, body) {
  const res = await fetch('http://localhost:3000/api/push', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${TOKEN}`
    },
    body: JSON.stringify({ type: 'chat_message', title, body, payload: {} })
  });
  const text = await res.text();
  console.log(`[${res.status}] ${title} (${body.length} 字符) -> ${text}`);
}

// 超长消息：>15000 字符，触发 T5 历史字符预算裁剪 + 摘要
// 注意：TrimHistory 要求 cutEnd>1（历史中需已有 ≥2 条 user 消息），
// 且单条超长消息使总字符超预算时，裁剪点对齐后会裁掉更早的 user 消息。
const longText = '我在回忆今天的事情。'.repeat(2000); // ~20000 字符
(async () => {
  await push('测试消息C-超长', longText);
})();
