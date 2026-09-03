#!/usr/bin/env node
/**
 * 节日皮肤逐主题截图驱动（评审用，隔离数据目录，生产记忆零污染）
 *
 * 用法:
 *   node scripts/test/holiday_eval_drive.cjs            # 默认路径 + 生成 screenshots
 *   node scripts/test/holiday_eval_drive.cjs --verbose  # 详细输出
 *   node scripts/test/holiday_eval_drive.cjs --theme=lantern_festival --verbose  # 单主题评审
 *
 * 行为:
 *   ① 用 FU_XUAN_DATA 指向临时目录 + .test_mode（双保险，生产 D:\DesktopPetData 不被读写）
 *   ② 只启动并管理本脚本自己的 DesktopPet 实例，不触碰其他播放器
 *   ③ 启动桌宠 → @@view:open → 逐主题 @@sim:holiday:<id> → static/小界面/motion 截图 → off → recovery 截图
 *   ④ 截图保存到隔离目录 test_screenshots/，脚本不删除，供评审
 *   ⑤ 断言 Player.log 记录主题切换，输出截图文件清单
 */
'use strict';
const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const EXE = path.join(__dirname, '..', '..', 'Build', 'DesktopPet.exe');
const DATA_ROOT = path.resolve(process.env.FU_XUAN_EVAL_DATA || path.join(os.tmpdir(), 'fuxuan_festival_eval'));
const TEMP_ROOT = path.resolve(os.tmpdir());
if (!(DATA_ROOT === TEMP_ROOT || DATA_ROOT.startsWith(TEMP_ROOT + path.sep))) {
  throw new Error('FU_XUAN_EVAL_DATA 必须位于系统临时目录内，拒绝删除非临时路径: ' + DATA_ROOT);
}
const PLAYER_LOG = path.join(DATA_ROOT, 'logs', 'player_log.txt');
const BOOT_MARKER = '[DesktopPet] 落地';
const POLL_MS = 300;
const BOOT_TIMEOUT_MS = 120000;
const verbose = process.argv.includes('--verbose');
const trial = process.argv.includes('--trial'); // 只跑 cn_new_year 一张做试探
const themeArg = process.argv.find(arg => arg.startsWith('--theme='));
const requestedTheme = themeArg ? themeArg.slice('--theme='.length) : '';

// 正式主题（与 HolidayThemeRuntime.Themes 表一致，含 default）
const THEMES = [
  ['cn_new_year', '新春'],
  ['lantern_festival', '元宵'],
  ['dragon_boat', '端午'],
  ['qixi', '七夕'],
  ['mid_autumn', '中秋'],
];

const sleep = ms => new Promise(r => setTimeout(r, ms));
const log = m => console.log(m);
const vlog = m => { if (verbose) console.log('[v] ' + m); };

function readLogSafe() {
  for (let i = 0; i < 20; i++) {
    try { return fs.readFileSync(PLAYER_LOG, 'utf8'); } catch { sleep(150); }
  }
  return '';
}
function writeFileSafe(p, c) {
  for (let i = 0; i < 20; i++) {
    try { fs.writeFileSync(p, c); return; } catch { sleep(150); }
  }
  throw new Error('无法写入 ' + p);
}
async function main() {
  fs.rmSync(DATA_ROOT, { recursive: true, force: true });
  fs.mkdirSync(DATA_ROOT, { recursive: true });
  fs.writeFileSync(path.join(DATA_ROOT, '.test_mode'), ''); // 第二道防落盘
  try { fs.unlinkSync(PLAYER_LOG); } catch { /* 无旧日志 */ }
  const inbox = path.join(DATA_ROOT, 'inbox.txt');
  writeFileSafe(inbox, '');

  log(`[eval] exe: ${EXE}`);
  log(`[eval] 隔离数据目录: ${DATA_ROOT}`);
  log(`[eval] Player.log: ${PLAYER_LOG}`);

  if (!fs.existsSync(EXE)) { console.error(`[FAIL] 未找到 exe: ${EXE}`); process.exit(1); }

  const proc = spawn(EXE, [], {
    stdio: 'ignore', detached: false, windowsHide: true,
    env: { ...process.env, FU_XUAN_DATA: DATA_ROOT }
  });
  proc.unref();

  let booted = false;
  const t0 = Date.now();
  while (Date.now() - t0 < BOOT_TIMEOUT_MS) {
    if (readLogSafe().includes(BOOT_MARKER)) { booted = true; break; }
    await sleep(POLL_MS);
  }
  if (!booted) {
    console.error('[FAIL] 启动超时，未看到落地标记 ' + BOOT_MARKER);
    try { proc.kill(); } catch { /* 仅结束本脚本自己启动的进程 */ }
    process.exit(1);
  }
  log('[eval] 启动完成，开始驱动 UI...');

  async function send(cmd, delay) {
    writeFileSafe(inbox, cmd);
    vlog('-> ' + cmd);
    await sleep(delay);
  }

  // 打开面板（截图前置条件：面板必须可见）
  await send('@@view:open', 1600);
  // 节日配饰位于聊天主视图的标题头像、气泡头像和右下角动态小人；先进入聊天视图再取证。
  await send('@@view:chat', 1600);

  const activeThemes = requestedTheme
    ? THEMES.filter(([id]) => id === requestedTheme)
    : (trial ? THEMES.slice(0, 1) : THEMES); // 单主题或试探只跑 cn_new_year
  if (requestedTheme && activeThemes.length === 0) {
    console.error('[FAIL] 未知主题: ' + requestedTheme);
    try { proc.kill(); } catch { /* 仅结束本脚本自己启动的进程 */ }
    process.exit(1);
  }

  // 逐主题：static / motion / 关
  for (const [id, name] of activeThemes) {
    await send('@@sim:holiday:' + id, 1100);
    await send('@@sim:holiday:list', 700);
    await send('@@sim:holiday:status', 700);
    vlog(`  [${name}] 已切换，抓 static`);
    await send('@@sim:screenshot:' + id + '_static', 2200);
    await send('@@view:list', 250);
    await send('@@sim:screenshot:' + id + '_small', 900);
    await send('@@view:chat', 1200);
    vlog(`  [${name}] 抓 motion`);
    await send('@@sim:screenshot:' + id + '_motion', 800);
    await send('@@view:list', 250);
    await send('@@view:chat', 1200);
    await send('@@sim:holiday:off', 900); // 恢复默认，为下一主题做干净起点
  }
  await send('@@sim:screenshot:default_recovery', 1400);
  await send('@@view:list', 250);
  await send('@@view:chat', 900);
  await send('@@test:quit', 1800);

  const content = readLogSafe();
  const shotDir = path.join(DATA_ROOT, 'test_screenshots');
  const shots = fs.existsSync(shotDir) ? fs.readdirSync(shotDir).filter(n => n.endsWith('.png')).sort() : [];
  const themeLogs = (content.match(/\[TestInbox\] 当前节日主题/g) || []).length;
  const listLogs = (content.match(/\[TestInbox\] 可用节日主题/g) || []).length;
  const statusLogs = activeThemes.reduce((count, [, name]) =>
    count + (content.match(new RegExp('\\[TestInbox\\] 当前节日主题: ' + name, 'g')) || []).length, 0);
  const recoveryLogs = (content.match(/\[TestInbox\] 当前节日主题: 默认主题/g) || []).length;
  const quitLogs = (content.match(/@@test:quit → 执行完整退出/g) || []).length;
  const requiredLabels = activeThemes.flatMap(([id]) => [
    `${id}_static`, `${id}_small`, `${id}_motion`
  ]).concat('default_recovery');
  const missingLabels = requiredLabels.filter(label => !shots.some(name => name.startsWith(label + '_')));

  log('');
  log('[eval] 主题切换日志条数: ' + themeLogs + '（本轮期望至少 ' + activeThemes.length + ' 次主题切换，另含 off）');
  log('[eval] list 日志: ' + listLogs + '，status 日志: ' + statusLogs + '，默认恢复日志: ' + recoveryLogs + '，完整退出日志: ' + quitLogs);
  log('[eval] 截图数量: ' + shots.length);
  shots.forEach(s => log('   ' + s));
  const nonEmpty = shots.filter(s => fs.statSync(path.join(shotDir, s)).size > 4000);
  log('[eval] 非空白截图(>4KB): ' + nonEmpty.length + '/' + shots.length);
  const nre = (content.match(/NullReferenceException/g) || []).length;
  if (nre > 0) log('[warn] Player.log 发现 ' + nre + ' 次 NullReferenceException');
  log('[eval] 截图目录(未清理): ' + shotDir);

  if (listLogs < activeThemes.length || statusLogs < activeThemes.length
    || recoveryLogs < 1 || quitLogs < 1 || missingLabels.length > 0 || nre > 0) {
    throw new Error('逐主题证据不完整: ' + JSON.stringify({
      listLogs, statusLogs, recoveryLogs, quitLogs, missingLabels, nre
    }));
  }

  // 不杀进程（保留窗口供人工查看）；如需退出可手动关闭。
  // 结束进程，避免常驻测试实例。
  // killPet();
}

main().catch(e => { console.error('[FAIL] ' + e.message); process.exit(1); });
