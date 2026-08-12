// 临时诊断脚本：查询 push_messages 表内容（只读）
const initSqlJs = require('D:/C/小程序/server/node_modules/sql.js');
const fs = require('fs');
const path = require('path');

const DB_PATH = 'D:/C/小程序/server/data/njust.db';

(async () => {
  const SQL = await initSqlJs();
  const buf = fs.readFileSync(DB_PATH);
  const db = new SQL.Database(buf);
  const res = db.exec("SELECT id, type, title, body, payload, delivered, rowid FROM push_messages ORDER BY rowid DESC LIMIT 5");
  if (!res.length) { console.log('(empty)'); return; }
  const { columns, values } = res[0];
  console.log(JSON.stringify(values.map(v => {
    const o = {};
    columns.forEach((c, i) => o[c] = v[i]);
    return o;
  }), null, 2));
})();
