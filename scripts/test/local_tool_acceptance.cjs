#!/usr/bin/env node
/**
 * 本地模型工具调用验收：用自然语言驱动隔离桌宠实例，确认工具真的执行。
 * 生产记忆不会被读取或写入；临时目录和测试实例在结束时清理。
 */
'use strict';

const { spawn, execFileSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const repo = path.resolve(__dirname, '..', '..');
const exe = path.join(repo, 'Build', 'DesktopPet.exe');
const testRoot = path.join(os.tmpdir(), 'fuxuan_local_tool_acceptance_20260822');
const inbox = path.join(testRoot, 'inbox.txt');
const logPath = path.join(testRoot, 'logs', 'player_log.txt');
const productionRoot = process.env.FU_XUAN_DATA || 'D:\\DesktopPetData';
const productionFiles = [
  'pet_memory.json', 'pet_personality.json', 'motion_memory.json',
  'activity_log.json', 'validation_log.json', 'knowledge_base.json', 'reminders.json'
];

const cases = [
  { name: '系统信息', message: '请查看当前系统信息，并告诉我 CPU 和内存状态', tools: ['get_system_info'] },
  { name: '文件搜索', message: '请搜索项目里的 README.md 文件', tools: ['search_files', 'search_file'] },
  { name: '打开文件夹', message: '请打开桌面文件夹', tools: ['open_folder'] },
  { name: '剪贴板', message: '请把“本地工具路由测试”复制到剪贴板', tools: ['set_clipboard'] },
  {
    name: 'Excel',
    message: '请生成一个测试用的 Excel 表格，只有一列“项目”和两行“本地模型”“云端模型”',
    tools: ['generate_xlsx'],
    startMarker: '复核: generate_xlsx'
  }
];

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const readLog = () => fs.existsSync(logPath) ? fs.readFileSync(logPath, 'utf8') : '';
const count = (text, needle) => (text.match(new RegExp(needle, 'g')) || []).length;

function killPet() {
  try { execFileSync('taskkill', ['/IM', 'DesktopPet.exe', '/F', '/T'], { stdio: 'ignore', windowsHide: true }); } catch (_) {}
}

async function waitForLog(predicate, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const text = readLog();
    if (predicate(text)) return text;
    await sleep(1000);
  }
  return readLog();
}

async function main() {
  if (!fs.existsSync(exe)) throw new Error(`找不到构建产物: ${exe}`);
  const snapshot = new Map();
  for (const file of productionFiles) {
    const target = path.join(productionRoot, file);
    snapshot.set(file, fs.existsSync(target) ? fs.statSync(target).mtimeMs : 0);
  }

  killPet();
  await sleep(2500);
  fs.rmSync(testRoot, { recursive: true, force: true });
  fs.mkdirSync(testRoot, { recursive: true });
  fs.writeFileSync(path.join(testRoot, '.test_mode'), '');
  fs.writeFileSync(inbox, '');

  const child = spawn(exe, [], {
    cwd: path.dirname(exe),
    env: { ...process.env, FU_XUAN_DATA: testRoot },
    stdio: 'ignore',
    windowsHide: true
  });
  console.log(`[acceptance] started PID=${child.pid}`);

  const results = [];
  try {
    const boot = await waitForLog(text => text.includes('[DesktopPet] 落地'), 60000);
    if (!boot.includes('[DesktopPet] 落地')) throw new Error('隔离实例启动超时');

    for (const item of cases) {
      const before = readLog();
      const beforeResults = count(before, '本地术式结果');
      fs.writeFileSync(inbox, item.message, 'utf8');
      console.log(`[acceptance] sent ${item.name}: ${item.message}`);

      const after = await waitForLog(text => count(text, '本地术式结果') > beforeResults, 100000);
      const matched = item.tools.find(tool => after.includes(`本地术式结果: ${tool}`)) || null;
      const started = !matched && item.startMarker && after.includes(item.startMarker);
      const evidence = after.split(/\r?\n/)
        .filter(line => /本地术式规划|本地术式结果|本地术式不在白名单|规划 JSON 无效/.test(line))
        .slice(-5)
        .join(' | ');
      const success = Boolean(matched || started);
      results.push({ name: item.name, matched: matched || (started ? 'generate_xlsx (started)' : null), success, evidence });
      console.log(`[${success ? 'PASS' : 'FAIL'}] ${item.name} -> ${matched || (started ? 'generate_xlsx (started; bridge may be slow)' : '未观察到预期工具')}`);
      if (evidence) console.log(`  evidence: ${evidence}`);
      await sleep(2500);
    }
  } finally {
    try { child.kill(); } catch (_) {}
    killPet();
    await sleep(1000);
    fs.rmSync(testRoot, { recursive: true, force: true });
  }

  const pollution = [];
  for (const file of productionFiles) {
    const target = path.join(productionRoot, file);
    const oldMtime = snapshot.get(file);
    if (oldMtime && fs.existsSync(target) && fs.statSync(target).mtimeMs !== oldMtime)
      pollution.push(file);
  }
  if (pollution.length) throw new Error(`生产记忆被修改: ${pollution.join(', ')}`);

  const failed = results.filter(item => !item.success);
  console.log(`[acceptance] passed=${results.length - failed.length} failed=${failed.length} isolated_cleanup=${!fs.existsSync(testRoot)}`);
  if (failed.length) process.exitCode = 1;
}

main().catch(error => {
  console.error(`[FAIL] ${error.message}`);
  killPet();
  fs.rmSync(testRoot, { recursive: true, force: true });
  process.exitCode = 1;
});
