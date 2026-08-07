// 列出 push_messages 全部记录（含被消费的），用于统计测试消息总数
const initSqlJs = require('D:/C/小程序/server/node_modules/sql.js');
const fs = require('fs');

initSqlJs().then(SQL => {
    const db = new SQL.Database(fs.readFileSync('D:/C/小程序/server/data/njust.db'));
    const r = db.exec(`SELECT rowid, type, title, substr(body,1,40) AS b, delivered FROM push_messages ORDER BY rowid DESC LIMIT 40`);
    if (!r[0]) { console.log('(空)'); return; }
    const cols = r[0].columns;
    console.log(cols.join(' | '));
    r[0].values.forEach(v => console.log(v.join(' | ')));
});
