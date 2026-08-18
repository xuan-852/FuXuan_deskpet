#!/usr/bin/env node
// 汇总桌宠质量遥测与 Token 用量；只输出聚合数据，不打印对话原文或动作描述。
const fs = require('fs');
const path = require('path');

const dataRoot = process.argv[2] || process.env.FU_XUAN_DATA || 'D:\\DesktopPetData';
const qualityPath = path.join(dataRoot, 'quality_log.jsonl');
const usagePath = path.join(dataRoot, 'usage_log.jsonl');

function readJsonl(file) {
  if (!fs.existsSync(file)) return [];
  return fs.readFileSync(file, 'utf8').split(/\r?\n/)
    .filter(Boolean)
    .map((line) => {
      try { return JSON.parse(line); } catch (_) { return null; }
    })
    .filter(Boolean);
}

function average(rows, key) {
  const values = rows.map((row) => Number(row[key])).filter(Number.isFinite);
  return values.length ? values.reduce((a, b) => a + b, 0) / values.length : null;
}

function percent(n, d) {
  return d ? `${(n * 100 / d).toFixed(1)}%` : '-';
}

function qualitySummary(rows) {
  const groups = new Map();
  for (const row of rows) {
    const key = `${row.task || '?'}\t${row.src || '?'}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(row);
  }

  console.log('质量遥测：');
  console.log('task\tsource\tcalls\tok\taccepted\tparse\tavg_latency_ms\tscored\tavg_score\tpass(>=3)');
  for (const [key, group] of [...groups.entries()].sort()) {
    const [task, source] = key.split('\t');
    const scored = group.filter((row) => Number(row.score) >= 0);
    const passed = scored.filter((row) => Number(row.score) >= 3).length;
    console.log([
      task,
      source,
      group.length,
      percent(group.filter((row) => row.ok === true).length, group.length),
      percent(group.filter((row) => row.accepted === true).length, group.length),
      percent(group.filter((row) => row.parse === true).length, group.length),
      average(group, 'latency_ms') == null ? '-' : average(group, 'latency_ms').toFixed(0),
      scored.length,
      scored.length ? average(scored, 'score').toFixed(2) : '-',
      scored.length ? percent(passed, scored.length) : '-'
    ].join('\t'));
  }
}

function usageSummary(rows) {
  const groups = new Map();
  for (const row of rows) {
    const key = row.src || '?';
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(row);
  }
  console.log('\nToken 用量：');
  console.log('source\tcalls\tprompt\thit\tcompletion\tcost_yuan');
  for (const [source, group] of [...groups.entries()].sort()) {
    const sum = (key) => group.reduce((total, row) => total + (Number(row[key]) || 0), 0);
    console.log([
      source,
      group.length,
      sum('prompt'),
      sum('hit'),
      sum('comp'),
      sum('cost').toFixed(4)
    ].join('\t'));
  }
}

if (!fs.existsSync(qualityPath)) {
  console.error(`未找到质量日志：${qualityPath}`);
  process.exitCode = 1;
} else {
  qualitySummary(readJsonl(qualityPath));
  usageSummary(readJsonl(usagePath));
}
