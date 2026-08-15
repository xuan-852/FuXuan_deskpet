#!/usr/bin/env node
/**
 * 运行时冒烟测试 — 真机驱动 UI 并断言日志零异常
 *
 * 背景：EditMode（nographics）跑不到 OnGUI，RightPanel 拆分后 _starField 未实例化导致的
 * 每帧 NullReferenceException 只有真机开面板才暴露（2026-08-14 实测 36k 次 NRE）。
 *
 * 隔离设计（2026-08-15）：启动时用 FU_XUAN_DATA 指向临时目录（%TEMP%\fuxuan_smoke_test），
 * 桌宠以「无记忆」状态运行——生产 D:\DesktopPetData 的记忆/动作/活动/校验日志完全不被读写；
 * 再叠加 .test_mode 双保险（ChatManager/MotionMemory/ActivityTracker/DualModelValidator 均不落盘）。
 * 测试结束清理隔离目录，并断言生产记忆文件 mtime 未变（防污染回归）。
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
 *   ④ 生产数据目录记忆文件 mtime 全程未变（无记忆测试）
 * 前置: 已构建 Build\DesktopPet.exe；测试会结束当前运行的 DesktopPet 再启动新实例
 */
'use strict';
const { spawn, execSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const EXE_DEFAULT = path.join(__dirname, '..', '..', 'Build', 'DesktopPet.exe');
const TEST_DATA_ROOT = process.env.FU_XUAN_TEST_DATA || path.join(os.tmpdir(), 'fuxuan_smoke_test'); // 隔离数据目录（无记忆起点）
const PROD_DATA_ROOT = process.env.FU_XUAN_DATA || 'D:\\DesktopPetData'; // 生产数据目录（只读校验）
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
    ['@@view:external', '[TestInbox] @@view 命令: external'],
    // ★ 外置面板交互链路（Phase A3/A4）：会话项双击进聊天 → 工具行进设置 → ◀ 返回 → 审批注入
    ['@@view:extclick:100,200,true', '进入聊天: '],
    ['@@view:extclick:753,87', '打开子面板 → Settings'],
    ['@@view:extclick:15,15', '子面板返回 → Chat'],
    ['@@approval:smoke test cmd', '已注入测试审批'],
    ['@@view:embed', '[TestInbox] @@view 命令: embed'],
    ['@@emote:happy', '已注入表情: happy'],
    ['@@view:close', '[TestInbox] @@view 命令: close'],
    ['@@view:open', '[TestInbox] @@view 命令: open'],
];

const SIZES = ['窗口=486x1269', '窗口=1290x1269', '窗口=860x900'];
const MARKERS = ['进入聊天: ', '返回会话列表', '淡出完成，已隐藏'];
const EXT_MARKERS = [
    '[ExternalChat] 独立窗口已创建',
    '[RightPanel] ⧉ 已切换到独立面板窗口（可被其他窗口遮挡）',
    '[RightPanel] 已退出独立面板窗口',
    // Phase A3/A4 交互闭环标记
    '打开子面板 → Settings',
    '子面板返回 → Chat',
    '已注入测试审批',
];

async function main() {
    const inbox = path.join(TEST_DATA_ROOT, 'inbox.txt');
    const testMode = path.join(TEST_DATA_ROOT, '.test_mode');
    // 生产记忆文件快照（用于防污染断言）
    const PROD_FILES = ['pet_memory.json', 'pet_personality.json', 'motion_memory.json', 'activity_log.json', 'validation_log.json', 'knowledge_base.json', 'reminders.json'];
    const prodSnapshot = PROD_FILES.map(f => {
        const p = path.join(PROD_DATA_ROOT, f);
        return { f, p, mtime: fs.existsSync(p) ? fs.statSync(p).mtimeMs : 0 };
    });

    if (!fs.existsSync(exe)) { console.error(`[FAIL] 未找到桌宠 exe: ${exe}`); process.exit(1); }
    log(`[smoke] exe: ${exe}`);
    log(`[smoke] 隔离测试数据目录: ${TEST_DATA_ROOT}`);
    log(`[smoke] 生产数据目录(只读校验): ${PROD_DATA_ROOT}`);
    log(`[smoke] Player.log: ${PLAYER_LOG}`);

    // 0. 准备隔离测试目录（无记忆起点）
    fs.rmSync(TEST_DATA_ROOT, { recursive: true, force: true });
    fs.mkdirSync(TEST_DATA_ROOT, { recursive: true });

    // 1. 结束旧实例 + 清场（等 3s 让旧窗口完全释放，避免 WindowOverlay 透明窗设置失败）
    killDesktopPet();
    await sleep(3000);
    try { fs.unlinkSync(PLAYER_LOG); } catch { /* 无旧日志 */ }
    writeFileSafe(inbox, '');
    fs.writeFileSync(testMode, ''); // 开测试模式（双保险防落盘）

    // 2. 启动新实例（注入 FU_XUAN_DATA 指向隔离目录 → 无记忆运行）
    log('[smoke] 启动桌宠（隔离数据目录）...');
    const proc = spawn(exe, [], {
        stdio: 'ignore', detached: false, windowsHide: true,
        env: { ...process.env, FU_XUAN_DATA: TEST_DATA_ROOT }
    });
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
    for (const m of EXT_MARKERS) {
        if (!content.includes(m)) fails.push(`缺少独立窗口标记: ${m}`);
    }
    const nre = (content.match(/NullReferenceException/g) || []).length;
    const otherExc = (content.match(/Exception:/g) || []).length - nre;
    if (nre > 0) fails.push(`Player.log 发现 ${nre} 次 NullReferenceException（面板渲染中断，见堆栈）`);
    if (otherExc > 0) log(`[warn] 其他异常 ${otherExc} 次（不判失败，请人工确认是否良性）`);

    // 6. 清理：删测试模式 + 结束实例 + 删除隔离目录 + 断言生产记忆未变
    fs.unlinkSync(testMode);
    if (!keepAlive) { killDesktopPet(); }
    for (let i = 0; i < 10; i++) {
        try { fs.rmSync(TEST_DATA_ROOT, { recursive: true, force: true }); break; }
        catch { await sleep(300); }
    }
    // ★ 防污染断言：生产记忆文件 mtime 必须全程未变（无记忆测试的核心保证）
    const pollution = prodSnapshot.filter(x => x.mtime > 0 && fs.existsSync(x.p) && fs.statSync(x.p).mtimeMs !== x.mtime);
    if (pollution.length > 0) fails.push(`生产记忆文件被测试修改: ${pollution.map(x => x.f).join(', ')}`);
    log(`[smoke] 隔离目录已清理: ${!fs.existsSync(TEST_DATA_ROOT)}`);
    if (!keepAlive) log('[smoke] 已结束测试实例');

    if (fails.length > 0) {
        console.error(`\n[FAIL] 冒烟测试未通过（${fails.length} 项）：`);
        fails.forEach(f => console.error('  ✗ ' + f));
        process.exit(1);
    }
    console.log('\n[PASS] 运行时冒烟测试通过：全部命令处理 + 三档尺寸切换 + 零 NRE + 生产记忆零污染 ✅');
}

main().catch(e => { console.error('[FAIL] 冒烟测试异常: ' + e.message); process.exit(1); });
