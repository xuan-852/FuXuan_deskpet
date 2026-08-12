// 临时测试脚本：用 UTF-8 原生推送测试消息到桌宠
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
  console.log(`[${res.status}] ${title} -> ${text}`);
  return text;
}

(async () => {
  // T7 验证：多独立子任务（期望单次响应多个 tool_call）
  await push('测试消息A', '帮我打开浏览器搜索今天的天气，同时用另一个浏览器页面查一下我的最近考试成绩');
  // T4 验证：简单指令意图（期望首轮有 🎯 过滤日志）
  await push('测试消息B', '现在几点了');
})();
