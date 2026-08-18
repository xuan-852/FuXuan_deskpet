#!/usr/bin/env node
/**
 * 质量对照案例运行器 — 自动驱动 inbox 跑完整案例集（本地或云端模式）
 *
 * 用法:
 *   node scripts/test/run_quality_cases.cjs --local   # 本地组（--ollama 模式，需已用该参数启动）
 *   node scripts/test/run_quality_cases.cjs --cloud   # 云端基线组（--cloud-baseline 模式）
 *   node scripts/test/run_quality_cases.cjs --local --cases chat,motion   # 只跑指定类型
 *   node scripts/test/run_quality_cases.cjs --local --from chat_005 --to motion_010  # 区间
 *   node scripts/test/run_quality_cases.cjs --local --wait-ms 8000        # 每案例间隔（默认 6000）
 *
 * 行为:
 *   对每个案例: 写 "@@case:<id>" → 等待 500ms → 写输入文本 → 等待 wait-ms → 下一个。
 *   quality_log.jsonl 只记 case_id 不记输入；本脚本打印进度不打印原文。
 */
'use strict';
const fs = require('fs');
const path = require('path');

const cases = require('./quality_cases.js');
const DATA_ROOT = process.env.FU_XUAN_DATA || 'D:\\DesktopPetData';
const INBOX = path.join(DATA_ROOT, 'inbox.txt');

const args = process.argv.slice(2);
const isCloud = args.includes('--cloud');
const isLocal = args.includes('--local');
if (!isCloud && !isLocal) { console.error('必须指定 --local 或 --cloud'); process.exit(1); }
const waitMs = parseInt(args[args.indexOf('--wait-ms') + 1] || '6000', 10);
const typeFilter = (args[args.indexOf('--cases') + 1] || '').split(',').filter(Boolean);
const fromId = args[args.indexOf('--from') + 1] || null;
const toId = args[args.indexOf('--to') + 1] || null;

function writeInbox(text) {
  fs.writeFileSync(INBOX, text, 'utf8');
}
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  console.log(`数据目录: ${DATA_ROOT}`);
  console.log(`模式: ${isCloud ? '云端基线组(--cloud-baseline)' : '本地组(--ollama)'}`);
  console.log(`案例间隔: ${waitMs}ms | 类型过滤: ${typeFilter.length ? typeFilter.join(',') : '全部'} | 区间: ${fromId || '起始'} ~ ${toId || '末尾'}`);
  console.log(`案例总数: ${cases.length}`);
  console.log('');

  let filtered = cases;
  if (typeFilter.length) filtered = filtered.filter(c => typeFilter.includes(c.type));
  if (fromId) {
    const fromIdx = filtered.findIndex(c => c.id === fromId);
    if (fromIdx >= 0) filtered = filtered.slice(fromIdx);
  }
  if (toId) {
    const toIdx = filtered.findIndex(c => c.id === toId);
    if (toIdx >= 0) filtered = filtered.slice(0, toIdx + 1);
  }

  if (!fs.existsSync(INBOX)) {
    console.error(`[FAIL] inbox 不存在: ${INBOX}`);
    console.error('请先按指南启动桌宠（--ollama 或 --cloud-baseline）并确认 FU_XUAN_DATA 一致。');
    process.exit(1);
  }

  let done = 0, fail = 0;
  for (const c of filtered) {
    try {
      writeInbox(`@@case:${c.id}`);
      await sleep(500);
      writeInbox(c.input);
      console.log(`[${++done}/${filtered.length}] ${c.id} (${c.type}) → 已发送，等待 ${waitMs}ms...`);
      await sleep(waitMs);
    } catch (e) {
      fail++;
      console.error(`[FAIL] ${c.id}: ${e.message}`);
      await sleep(1000);
    }
  }

  console.log('');
  console.log(`完成: ${done - fail}/${filtered.length} 成功，${fail} 失败`);
  console.log('下一步: 关闭桌宠后用 summarize_quality.cjs 汇总，两组都跑完后用 compare_quality.cjs 配对比较。');
  if (fail > 0) process.exitCode = 1;
})();
