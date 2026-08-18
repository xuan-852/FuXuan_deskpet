#!/usr/bin/env node
// 校验质量采样是否完整；只输出案例编号和聚合状态，不打印输入原文。
const fs = require('fs');
const path = require('path');
const cases = require('../test/quality_cases.js');

const dataRoot = process.argv[2];
if (!dataRoot) {
  console.error('用法: node scripts/log-analysis/validate_quality_run.cjs <data-root> [chat|motion]');
  process.exit(2);
}

const filter = process.argv[3] ? new Set(process.argv[3].split(',').filter(Boolean)) : null;
const expected = cases.filter(c => !filter || filter.has(c.type));
const qualityPath = path.join(dataRoot, 'quality_log.jsonl');
if (!fs.existsSync(qualityPath)) {
  console.error(`未找到质量日志：${qualityPath}`);
  process.exit(1);
}

const rows = fs.readFileSync(qualityPath, 'utf8').split(/\r?\n/).filter(Boolean).map(line => {
  try { return JSON.parse(line); } catch (_) { return null; }
}).filter(Boolean);

const taskFor = type => type === 'motion' ? 'motion_translation' : 'chat';
const invalidReasons = new Set(['budget_blocked', 'test_blocked', 'no_api_key']);
let missing = 0;
let invalid = 0;
const sourceCounts = new Map();

for (const item of expected) {
  const task = taskFor(item.type);
  const matching = rows.filter(row => row.case_id === item.id && row.task === task);
  const row = matching[matching.length - 1];
  if (!row) {
    missing++;
    console.log(`MISSING\t${item.id}\t${task}`);
    continue;
  }
  if (invalidReasons.has(row.reason)) {
    invalid++;
    console.log(`INVALID\t${item.id}\t${row.src || '?'}\t${row.reason}`);
    continue;
  }
  const key = `${task}\t${row.src || '?'}`;
  sourceCounts.set(key, (sourceCounts.get(key) || 0) + 1);
}

console.log(`案例总数：${expected.length}`);
console.log(`完整：${expected.length - missing - invalid}`);
console.log(`缺失：${missing}`);
console.log(`无效：${invalid}`);
console.log('来源统计：');
for (const [key, count] of [...sourceCounts.entries()].sort()) console.log(`${key}\t${count}`);

if (missing || invalid) process.exitCode = 1;
