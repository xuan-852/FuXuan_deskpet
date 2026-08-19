#!/usr/bin/env node
// 从隔离质量复核原文中输出回复结构聚合指标；不打印用户输入或完整回复。
const fs = require('fs');
const path = require('path');

const dataRoot = process.argv[2] || process.env.FU_XUAN_DATA || 'D:\\DesktopPetData';
const file = path.join(dataRoot, 'quality_review.jsonl');

if (!fs.existsSync(file)) {
  console.error(`未找到质量复核文件：${file}`);
  process.exit(1);
}

function readJsonl(filePath) {
  return fs.readFileSync(filePath, 'utf8').split(/\r?\n/)
    .filter(Boolean).map((line) => {
      try { return JSON.parse(line); } catch (_) { return null; }
    }).filter(Boolean);
}

function splitSentences(text) {
  return String(text || '').split(/[。！？!?\n]+/u)
    .map((part) => part.replace(/^\s*[【\[].*?[】\]]\s*/u, '').trim())
    .filter(Boolean);
}

const rows = readJsonl(file);
const groups = new Map();
for (const row of rows) {
  const key = `${row.src || '?'}\t${row.model || '?'}`;
  if (!groups.has(key)) groups.set(key, []);
  groups.get(key).push(row);
}

console.log('回复结构聚合（原文不输出）：');
console.log('source\tmodel\tcases\tavg_reply_chars\tavg_sentences\tavg_sentence_chars\tmax_sentence_chars\t2plus_sentences\t本座\t我\t主人\t你\t将军');
for (const [key, group] of [...groups.entries()].sort()) {
  const sentenceGroups = group.map((row) => splitSentences(row.reply));
  const allSentences = sentenceGroups.flat();
  const avg = (values) => values.length ? values.reduce((a, b) => a + b, 0) / values.length : 0;
  const chars = group.map((row) => String(row.reply || '').length);
  const [source, model] = key.split('\t');
  console.log([
    source,
    model,
    group.length,
    avg(chars).toFixed(2),
    avg(sentenceGroups.map((parts) => parts.length)).toFixed(2),
    avg(allSentences.map((part) => part.length)).toFixed(2),
    allSentences.length ? Math.max(...allSentences.map((part) => part.length)) : 0,
    `${sentenceGroups.filter((parts) => parts.length >= 2).length}/${group.length}`,
    group.filter((row) => String(row.reply || '').includes('本座')).length,
    group.filter((row) => String(row.reply || '').includes('我')).length,
    group.filter((row) => String(row.reply || '').includes('主人')).length,
    group.filter((row) => String(row.reply || '').includes('你')).length,
    group.filter((row) => String(row.reply || '').includes('将军')).length
  ].join('\t'));
}
