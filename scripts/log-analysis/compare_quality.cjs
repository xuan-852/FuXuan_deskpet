#!/usr/bin/env node
// 按 task + case_id 对齐本地/云端质量日志；只输出聚合指标，不打印原文。
const fs = require('fs');

const [localPath, cloudPath] = process.argv.slice(2);
if (!localPath || !cloudPath) {
  console.error('用法: node scripts/log-analysis/compare_quality.cjs <local_quality_log.jsonl> <cloud_quality_log.jsonl>');
  process.exit(2);
}

function readJsonl(file) {
  if (!fs.existsSync(file)) throw new Error(`未找到日志：${file}`);
  return fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(Boolean).map((line) => {
    try { return JSON.parse(line); } catch (_) { return null; }
  }).filter((row) => row && row.task && row.case_id);
}

function latestByCase(rows) {
  const map = new Map();
  const excluded = [];
  const invalidReasons = new Set(['budget_blocked', 'test_blocked', 'no_api_key']);
  for (const row of rows) {
    const key = `${row.task}\t${row.case_id}`;
    if (invalidReasons.has(row.reason)) {
      excluded.push(key);
      continue;
    }
    map.set(key, row);
  }
  return { map, excluded: [...new Set(excluded)] };
  return map;
}

function ratio(rows, key) {
  return rows.length ? rows.filter((row) => row[key] === true).length / rows.length : null;
}

function average(rows, key) {
  const values = rows.map((row) => Number(row[key])).filter(Number.isFinite).filter((value) => value >= 0);
  return values.length ? values.reduce((a, b) => a + b, 0) / values.length : null;
}

function pct(value) {
  return value == null ? '-' : `${(value * 100).toFixed(1)}%`;
}

function diff(local, cloud, key, format = pct) {
  const a = format === pct ? ratio(local, key) : average(local, key);
  const b = format === pct ? ratio(cloud, key) : average(cloud, key);
  if (a == null || b == null) return '-';
  return format === pct ? `${pct(a)} vs ${pct(b)} (本地${pct(a - b)})` : `${a.toFixed(2)} vs ${b.toFixed(2)} (本地${(a - b).toFixed(2)})`;
}

const localResult = latestByCase(readJsonl(localPath));
const cloudResult = latestByCase(readJsonl(cloudPath));
const localMap = localResult.map;
const cloudMap = cloudResult.map;
const keys = [...localMap.keys()].filter((key) => cloudMap.has(key));
const missingLocal = [...cloudMap.keys()].filter((key) => !localMap.has(key));
const missingCloud = [...localMap.keys()].filter((key) => !cloudMap.has(key));
const tasks = [...new Set(keys.map((key) => key.split('\t')[0]))].sort();

console.log(`配对案例：${keys.length}；仅本地：${missingCloud.length}；仅云端：${missingLocal.length}`);
console.log(`排除无效记录：本地 ${localResult.excluded.length}；云端 ${cloudResult.excluded.length}（预算拦截/测试拦截/缺少 API Key）`);
if (missingCloud.length || missingLocal.length) console.log('注意：只有两组都出现的 task + case_id 才进入对比。');
console.log('');
console.log('task\tcases\tok(local/cloud)\taccepted(local/cloud)\tparse(local/cloud)\tavg_score(local/cloud)\tavg_latency_ms(local/cloud)');
for (const task of tasks) {
  const taskKeys = keys.filter((key) => key.startsWith(`${task}\t`));
  const local = taskKeys.map((key) => localMap.get(key));
  const cloud = taskKeys.map((key) => cloudMap.get(key));
  console.log([
    task,
    taskKeys.length,
    diff(local, cloud, 'ok'),
    diff(local, cloud, 'accepted'),
    diff(local, cloud, 'parse'),
    diff(local, cloud, 'score', average),
    diff(local, cloud, 'latency_ms', average)
  ].join('\t'));
}

const judgeKeys = keys.filter((key) => key.startsWith('chat_quality\t'));
if (judgeKeys.length) {
  const local = judgeKeys.map((key) => localMap.get(key));
  const cloud = judgeKeys.map((key) => cloudMap.get(key));
  console.log('');
  console.log('chat_quality	cases	persona(local/cloud)	memory(local/cloud)	time(local/cloud)	relevance(local/cloud)	constraint(local/cloud)	average(local/cloud)');
  console.log([
    'chat_quality',
    judgeKeys.length,
    diff(local, cloud, 'judge_persona', average),
    diff(local, cloud, 'judge_memory', average),
    diff(local, cloud, 'judge_time', average),
    diff(local, cloud, 'judge_relevance', average),
    diff(local, cloud, 'judge_constraint', average),
    diff(local, cloud, 'score', average)
  ].join('\t'));
}
