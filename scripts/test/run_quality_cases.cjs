#!/usr/bin/env node
/**
 * 质量对照案例运行器 — 自动驱动 inbox 跑完整案例集（本地或云端模式）
 *
 * 用法:
 *   node scripts/test/run_quality_cases.cjs --local   # 本地组（--ollama 模式，需已用该参数启动）
 *   node scripts/test/run_quality_cases.cjs --cloud   # 云端基线组（--cloud-baseline 模式）
 *   node scripts/test/run_quality_cases.cjs --local --cases chat,motion   # 只跑指定类型
 *   node scripts/test/run_quality_cases.cjs --local --from chat_005 --to motion_010  # 区间
 *   node scripts/test/run_quality_cases.cjs --local --cases chat --timeout-ms 30000
 *   node scripts/test/run_quality_cases.cjs --cloud --cases motion --cooldown-ms 125000
 *
 * 行为:
 *   对 chat 案例发送普通文本；对 motion 案例发送 @@motion:，直接调用指定动作。
 *   每个案例等待对应 quality_log 记录后才进入下一个，避免 case_id 串线。
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
function getArgValue(flag, fallback) {
  const idx = args.indexOf(flag);
  return idx >= 0 && args[idx + 1] ? args[idx + 1] : fallback;
}
const waitMs = parseInt(getArgValue('--wait-ms', '2000'), 10);
const timeoutMs = parseInt(getArgValue('--timeout-ms', isCloud ? '90000' : '30000'), 10);
const cooldownMs = parseInt(getArgValue('--cooldown-ms', isCloud ? '125000' : '2000'), 10);
// ★ 只有显式传 --cases 才取类型过滤（否则 args[0] 可能是 --local/--cloud）
const casesIdx = args.indexOf('--cases');
const typeFilter = (casesIdx >= 0 ? (args[casesIdx + 1] || '') : '').split(',').filter(Boolean);
const fromIdx = args.indexOf('--from');
const fromId = fromIdx >= 0 ? (args[fromIdx + 1] || null) : null;
const toIdx = args.indexOf('--to');
const toId = toIdx >= 0 ? (args[toIdx + 1] || null) : null;

function writeInbox(text) {
  fs.writeFileSync(INBOX, text, 'utf8');
}
const sleep = ms => new Promise(r => setTimeout(r, ms));

function readRows() {
  const logPath = path.join(DATA_ROOT, 'quality_log.jsonl');
  if (!fs.existsSync(logPath)) return [];
  return fs.readFileSync(logPath, 'utf8').split(/\r?\n/).filter(Boolean).map(line => {
    try { return JSON.parse(line); } catch (_) { return null; }
  }).filter(Boolean);
}

let logOffset = 0;

async function waitForCase(caseId, task, timeout) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const rows = readRows();
    const matches = rows.slice(logOffset).filter(row => row.case_id === caseId && row.task === task);
    if (matches.length) {
      logOffset = rows.length;
      return matches[matches.length - 1];
    }
    await sleep(500);
  }
  return null;
}

(async () => {
  console.log(`数据目录: ${DATA_ROOT}`);
  console.log(`模式: ${isCloud ? '云端基线组(--cloud-baseline)' : '本地组(--ollama)'}`);
  console.log(`完成等待: quality_log | 超时: ${timeoutMs}ms | 收尾: ${waitMs}ms | 动作冷却: ${cooldownMs}ms`);
  console.log(`类型过滤: ${typeFilter.length ? typeFilter.join(',') : '全部'} | 区间: ${fromId || '起始'} ~ ${toId || '末尾'}`);
  console.log(`原始案例总数: ${cases.length}`);
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
  console.log(`选中案例总数: ${filtered.length}`);

  if (!fs.existsSync(INBOX)) {
    // ★ 桌宠不主动创建 inbox，只在文件存在时轮询——运行器负责创建
    try {
      fs.writeFileSync(INBOX, '', 'utf8');
      console.log(`[init] 已创建 inbox: ${INBOX}`);
    } catch (e) {
      console.error(`[FAIL] 无法创建 inbox: ${INBOX} (${e.message})`);
      process.exit(1);
    }
  }

  // 避免复用同一数据目录时把上一次同名 case_id 当成当前结果。
  logOffset = readRows().length;
  let done = 0, fail = 0, blocked = 0;
  for (const c of filtered) {
    try {
      writeInbox(`@@case:${c.id}`);
      await sleep(500);
      writeInbox(c.type === 'motion' ? `@@motion:${c.input}` : c.input);
      const expectedTask = c.type === 'motion' ? 'motion_translation' : 'chat';
      const row = await waitForCase(c.id, expectedTask, timeoutMs);
      if (!row) {
        fail++;
        console.error(`[TIMEOUT] ${c.id}: ${expectedTask} 未写入 quality_log`);
        continue;
      }
      if (c.type === 'motion' && row.reason === 'budget_blocked') {
        blocked++;
        console.error(`[BLOCKED] ${c.id}: motion 预算闸门拦截；请等待冷却窗口后从该案例继续`);
        break;
      }
      done++;
      console.log(`[${done}/${filtered.length}] ${c.id} (${c.type}) → ${row.source || row.src}/${row.reason}`);
      await sleep(waitMs);
      if (c.type === 'motion' && isCloud && c !== filtered[filtered.length - 1]) {
        console.log(`[cooldown] ${cooldownMs}ms`);
        await sleep(cooldownMs);
      }
    } catch (e) {
      fail++;
      console.error(`[FAIL] ${c.id}: ${e.message}`);
      await sleep(1000);
    }
  }

  console.log('');
  console.log(`完成: ${done}/${filtered.length} 成功，${fail} 超时/错误，${blocked} 预算拦截`);
  console.log('下一步: 关闭桌宠后用 summarize_quality.cjs 汇总，两组都跑完后用 compare_quality.cjs 配对比较。');
  if (fail > 0) process.exitCode = 1;
})();
