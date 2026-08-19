#!/usr/bin/env node
// 将本地/云端复核原文合并为 A/B 盲评包，并单独保存来源映射。
'use strict';
const fs = require('fs');
const crypto = require('crypto');

const [localRoot, cloudRoot, outputRoot] = process.argv.slice(2);
if (!localRoot || !cloudRoot || !outputRoot) {
  console.error('用法: node scripts/log-analysis/build_review_packet.cjs <local_root> <cloud_root> <output_root>');
  process.exit(2);
}

function read(file) {
  if (!fs.existsSync(file)) return [];
  return fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(Boolean).map((line) => {
    try { return JSON.parse(line); } catch (_) { return null; }
  }).filter(Boolean);
}

function latestByCase(root) {
  const map = new Map();
  for (const row of read(`${root}/quality_review.jsonl`)) {
    if (row.case_id) map.set(row.case_id, row);
  }
  return map;
}

function sideFor(caseId) {
  return parseInt(crypto.createHash('sha256').update(caseId).digest('hex').slice(0, 2), 16) % 2 === 0 ? ['a', 'b'] : ['b', 'a'];
}

const local = latestByCase(localRoot);
const cloud = latestByCase(cloudRoot);
const cases = [...local.keys()].filter((id) => cloud.has(id)).sort();
fs.mkdirSync(outputRoot, { recursive: true });
const packet = [];
const key = [];
for (const caseId of cases) {
  const sides = sideFor(caseId);
  const rows = { local: local.get(caseId), cloud: cloud.get(caseId) };
  const bySide = {};
  bySide[sides[0]] = rows.local;
  bySide[sides[1]] = rows.cloud;
  packet.push({ case_id: caseId, input: bySide.a.input, answer_a: bySide.a.reply, answer_b: bySide.b.reply });
  key.push({ case_id: caseId, a_source: bySide.a.src, b_source: bySide.b.src });
}
fs.writeFileSync(`${outputRoot}/review_packet_blind.jsonl`, packet.map((row) => JSON.stringify(row)).join('\n') + (packet.length ? '\n' : ''), 'utf8');
fs.writeFileSync(`${outputRoot}/review_packet_key.jsonl`, key.map((row) => JSON.stringify(row)).join('\n') + (key.length ? '\n' : ''), 'utf8');
console.log(`盲评案例: ${packet.length}`);
console.log(`盲评包: ${outputRoot}/review_packet_blind.jsonl`);
console.log(`来源密钥: ${outputRoot}/review_packet_key.jsonl（仅用于复核后揭示，不要先看）`);
