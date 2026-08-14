#!/usr/bin/env node
/**
 * 运行时冒烟测试 — 真机驱动 UI 并断言日志零异常
 *
 * 背景：EditMode（nographics）跑不到 OnGUI，RightPanel 拆分后 _starField 未实例化导致的
 * 每帧 NullReferenceException 只有真机开面板才暴露（2026-08-14 实测 36k 次 NRE）。
 * 本脚本把「建 .test_mode → 启动 exe → inbox 链路驱动 UI → 查 Player.log」固化，防回归。
 *
 * 用法:
 *   node scripts/test/runtime_smoke.cjs                # 默认路径
 *   node scripts/test/runtime_smoke.cjs --exe <path>   # 指定桌宠 exe
 *   node scripts/test/runtime_smoke.cjs --keep-alive   # 测试后保留桌宠运行（默认结束后杀）
 *   node scripts/test/runtime_smoke.cjs --verbose      # 详细输出
 *
 * 通过标准:
 *   ① 全部 @@view 命令被处理（[TestInbox] 留痕）
 *   ② 三档窗口尺寸切换出现（486x1269 / 1290x1269 / 860x900）
 *   ③ Player.log 无 NullReferenceException（其他 Exception 计为警告）
 * 前置: 已构建 Build\DesktopPet.exe；数据目录可写；测试会结束当前运行的 DesktopPet 再启动新实例
 */
'use strict';
const { spawn, execSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const EXE_DEFAULT = path.join(__dirname, '..', '..', 'Build', 'DesktopPet.exe');
const DATA_ROOT = process.env.FU_XUAN_DATA || 'D:\\DesktopPetData';
const PLAYER_LOG = process.env.PLAYER_LOG || path.join(os.homedir(), 'AppData', 'LocalLow', 'DefaultCompany', 'desktop pet', 'Player.log');
const BOOT_MARKER = '[DesktopPet] 落地';
const POLL_MS = 300;
const BOOT_TIMEOUT_MS = 90000;

const args = process.argv.slice(2);
const exeIdx = args.indexOf('--exe');
const exe = exeIdx >= 0 ? (args[exeIdx + 1] || EXE_DEFAULT) : EXE_DEFAULT;
const keepAlive = args.includes('--keep-alive');
const verbose = args.includes('--verbose');

const sleep = ms => new Promise(r => setTimeout(r, ms));
const log = m => console.log(m);
const vlog = m => { if (verbose) console.log('[v] ' + m); };

// Player.log 被应用持续写入，读时可能短暂占用 → 重试
function readLogSafe() {
    for (let i = 0; i < 20; i++) {
        try { return fs.readFileSync(PLAYER_LOG, 'utf8'); } catch { sleep(150); }
    }
    return '';
}
function writeFileSafe(p, content) {
    for (let i = 0; i < 20; i++) {
        try { fs.writeFileSync(p, content); return; } catch { sleep(150); }
    }
    throw new Error('无法写入 ' + p);
}

function killDesktopPet() {
    try { execSync('taskkill /IM DesktopPet.exe /F /T', { stdio: 'ignore', windowsHide: true }); } catch { /* 没有在运行则忽略 */ }
}

const COMMANDS = [
    ['@@view:open', '[TestInbox] @@view 命令: open'],
    ['@@view:chat', '[TestInbox] @@view 命令: chat'],
    ['@@view:settings', '[TestInbox] @@view 命令: settings'],
    ['@@view:back', '[TestInbox] @@view 命令: back'],
    ['@@view:report', '[TestInbox] @@view 命令: report'],
    ['@@view:back', '[TestInbox] @@view 命令: back'],
    ['@@view:reminders', '[TestInbox] @@view 命令: reminders'],
    ['@@view:back', '[TestInbox] @@view 命令: back'],
    ['@@view:list', '[TestInbox] @@view 命令: list'],
    ['@@emote:happy', '已注入表情: happy'],
    ['@@view:close', '[TestInbox] @@view 命令: close'],
    ['@@view:open', '[TestInbox] @@view 命令: open'],
];

const SIZES = ['窗口=486x1269', '窗口=1290x1269', '窗口=860x900'];
const MARKERS = ['进入聊天: ', '返回会话列表', '淡出完成，已隐藏'];

async function main() {
    const inbox = path.join(DATA_ROOT, 'inbox.txt');
    const testMode = path.join(DATA_ROOT, '.test_mode');

    if (!fs.existsSync(exe)) { console.error(`[FAIL] 未找到桌宠 exe: ${exe}`); process.exit(1); }
    log(`[smoke] exe: ${exe}`);
    log(`[smoke] 数据目录: ${DATA_ROOT}`);
    log(`[smoke] Player.log: ${PLAYER_LOG}`);

    // 1. 结束旧实例 + 清场
    killDesktopPet();
    await sleep(1200);
    try { fs.unlinkSync(PLAYER_LOG); } catch { /* 无旧日志 */ }
    writeFileSafe(inbox, '');
    fs.writeFileSync(testMode, ''); // 开测试模式（防污染记忆）

    // 2. 启动新实例
    log('[smoke] 启动桌宠...');
    const proc = spawn(exe, [], { stdio: 'ignore', detached: false, windowsHide: true });
    proc.unref(); // 不 hold 父进程事件循环（否则 --keep-alive 时本脚本无法退出）

    // 3. 等待启动完成（Player.log 出现落地标记）
    let booted = false;
    const t0 = Date.now();
    while (Date.now() - t0 < BOOT_TIMEOUT_MS) {
        if (readLogSafe().includes(BOOT_MARKER)) { booted = true; break; }
        await sleep(POLL_MS);
    }
    if (!booted) {
        console.error('[FAIL] 启动超时，未看到落地标记 ' + BOOT_MARKER);
        fs.unlinkSync(testMode); killDesktopPet(); process.exit(1);
    }
    log('[smoke] 启动完成，开始驱动 UI...');

    // 4. inbox 终端链路驱动 UI（每命令间隔留足 0.25s 轮询 + 处理）
    for (const [cmd] of COMMANDS) {
        writeFileSafe(inbox, cmd);
        vlog('-> ' + cmd);
        await sleep(1600);
    }
    await sleep(2000);

    // 5. 读取日志断言
    const content = readLogSafe();
    const fails = [];

    for (const [cmd, expect] of COMMANDS) {
        if (!content.includes(expect)) fails.push(`缺少命令留痕: ${cmd}（期望 ${expect}）`);
    }
    for (const s of SIZES) {
        if (!content.includes(s)) fails.push(`缺少窗口尺寸切换: ${s}`);
    }
    for (const m of MARKERS) {
        if (!content.includes(m)) fails.push(`缺少行为标记: ${m}`);
    }
    const nre = (content.match(/NullReferenceException/g) || []).length;
    const otherExc = (content.match(/Exception:/g) || []).length - nre;
    if (nre > 0) fails.push(`Player.log 发现 ${nre} 次 NullReferenceException（面板渲染中断，见堆栈）`);
    if (otherExc > 0) log(`[warn] 其他异常 ${otherExc} 次（不判失败，请人工确认是否良性）`);

    // 6. 清理
    fs.unlinkSync(testMode);
    if (!keepAlive) { killDesktopPet(); log('[smoke] 已结束测试实例'); }

    if (fails.length > 0) {
        console.error(`\n[FAIL] 冒烟测试未通过（${fails.length} 项）：`);
        fails.forEach(f => console.error('  ✗ ' + f));
        process.exit(1);
    }
    console.log('\n[PASS] 运行时冒烟测试通过：全部命令处理 + 三档尺寸切换 + 零 NRE ✅');
}

main().catch(e => { console.error('[FAIL] 冒烟测试异常: ' + e.message); process.exit(1); });
