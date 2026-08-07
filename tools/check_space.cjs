// 检查某条消息 body 是否含意外空格
const initSqlJs = require('D:/C/小程序/server/node_modules/sql.js');
const fs = require('fs');
(async () => {
  const SQL = await initSqlJs();
  const db = new SQL.Database(fs.readFileSync('D:/C/小程序/server/data/njust.db'));
  const res = db.exec("SELECT id, body FROM push_messages WHERE id='5586c607-fcac-4db2-ac5b-b4512b4c1975'");
  const body = res[0].values[0][1];
  console.log('LEN:', body.length);
  console.log('CHARS:', [...body].map(c => c.codePointAt(0).toString(16)).join(' '));
  console.log('HAS_SPACE:', body.includes(' '));
})();
