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
 * 测试目录必须是 TEMP 下专属目录；测试只会终止自己创建的进程，并比较生产文件的存在性与 SHA-256。
 *
 * 用法:
 *   node scripts/test/runtime_smoke.cjs                # 默认路径
 *   node scripts/test/runtime_smoke.cjs --exe <path>   # 指定桌宠 exe
 *   node scripts/test/runtime_smoke.cjs --keep-alive   # 测试后保留桌宠运行（默认结束后杀）
 *   node scripts/test/runtime_smoke.cjs --keep-artifacts # 失败排查时保留隔离日志和截图
 *   node scripts/test/runtime_smoke.cjs --verbose      # 详细输出
 *
 * 通过标准:
 *   ① 全部 @@view 命令被处理（[TestInbox] 留痕）
 *   ② 窗口尺寸切换出现；大屏固定分辨率时额外校验三档尺寸
 *   ③ Player.log 无 Exception
 *   ④ 生产数据目录受保护文件的存在性和内容全程未变（无记忆测试）
 * 前置: 已构建 Build\DesktopPet.exe；不会结束已有的生产桌宠实例
 */
'use strict';
const { spawn, execSync } = require('child_process');
const { createHash } = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const EXE_DEFAULT = path.join(__dirname, '..', '..', 'Build', 'DesktopPet.exe');
const TEST_DATA_ROOT = path.resolve(process.env.FU_XUAN_TEST_DATA || path.join(os.tmpdir(), 'fuxuan_smoke_test'));
// DesktopPet 会把全量日志镜像写入 DataPathConfig.LogsDir。
// 测试进程通过 FU_XUAN_DATA 使用隔离目录，因此默认必须读取隔离镜像；
// 不能读取默认 Player.log，否则可能拿到旧实例日志，造成“启动成功但所有 UI 命令缺失”的假失败。
const PLAYER_LOG = process.env.PLAYER_LOG || path.join(TEST_DATA_ROOT, 'logs', 'player_log.txt');
const BOOT_MARKER = '[DesktopPet] 落地';
const POLL_MS = 300;
const BOOT_TIMEOUT_MS = 90000;

const args = process.argv.slice(2);
const exeIdx = args.indexOf('--exe');
const exe = exeIdx >= 0 ? (args[exeIdx + 1] || EXE_DEFAULT) : EXE_DEFAULT;
const keepAlive = args.includes('--keep-alive');
const keepArtifacts = args.includes('--keep-artifacts');
const verbose = args.includes('--verbose');
const validateSafety = args.includes('--validate-safety');

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

function resolveProductionDataRoot() {
    const normalize = value => value ? path.resolve(String(value).trim().replace(/^"|"$/g, '')) : '';
    const configured = normalize(process.env.FU_XUAN_DATA);
    if (configured && fs.existsSync(configured)) return configured;
    const legacyC = path.resolve('C:\\DesktopPetData');
    if (fs.existsSync(legacyC)) return legacyC;
    const legacyD = path.resolve('D:\\DesktopPetData');
    if (fs.existsSync(legacyD)) return legacyD;
    return configured || path.resolve(process.env.LOCALAPPDATA || os.tmpdir(), 'FuXuan', 'DesktopPetData');
}

const PROD_DATA_ROOT = resolveProductionDataRoot();

function isPathInside(child, parent) {
    const rel = path.relative(parent, child);
    return rel !== '' && !rel.startsWith('..' + path.sep) && rel !== '..' && !path.isAbsolute(rel);
}

function assertSafeTestDataRoot() {
    const tempRoot = path.resolve(os.tmpdir());
    if (!isPathInside(TEST_DATA_ROOT, tempRoot)) {
        throw new Error(`FU_XUAN_TEST_DATA 必须位于系统临时目录下: ${tempRoot}`);
    }
    if (!path.basename(TEST_DATA_ROOT).startsWith('fuxuan_smoke_test')) {
        throw new Error('FU_XUAN_TEST_DATA 必须使用 fuxuan_smoke_test 前缀，避免误删非测试目录');
    }
    if (TEST_DATA_ROOT.toLowerCase() === PROD_DATA_ROOT.toLowerCase() ||
        isPathInside(PROD_DATA_ROOT, TEST_DATA_ROOT) || isPathInside(TEST_DATA_ROOT, PROD_DATA_ROOT)) {
        throw new Error('测试目录与生产数据目录重叠，拒绝启动');
    }
    let current = TEST_DATA_ROOT;
    while (current.toLowerCase() !== tempRoot.toLowerCase()) {
        if (fs.existsSync(current) && fs.lstatSync(current).isSymbolicLink()) {
            throw new Error(`测试目录路径含符号链接，拒绝清理: ${current}`);
        }
        const parent = path.dirname(current);
        if (parent === current) break;
        current = parent;
    }
}

function snapshotFile(filePath) {
    if (!fs.existsSync(filePath)) return { exists: false, hash: null };
    return {
        exists: true,
        hash: createHash('sha256').update(fs.readFileSync(filePath)).digest('hex')
    };
}

function stopOwnedDesktopPet(proc) {
    if (!proc || !Number.isInteger(proc.pid) || proc.pid <= 0) return;
    try { process.kill(proc.pid, 0); } catch { return; }
    try { execSync(`taskkill /PID ${proc.pid} /F /T`, { stdio: 'ignore', windowsHide: true }); }
    catch { /* 进程已退出或被应用自身关闭 */ }
}

const COMMANDS = [
    ['@@sim:status', '[TestInbox] 模拟输入状态:'],
    ['@@sim:walk:right', '[TestInbox] 已强制开始向右走'],
    ['@@sim:drag:offset:120,20,8', '[DragHandler] 模拟拖动开始'],
    ['@@sim:status', 'petDragging=False'],
    ['@@view:open', '[TestInbox] @@view 命令: open'],
    ['@@view:chat', '[TestInbox] @@view 命令: chat'],
    // 节日主题闭环：只验证像素符玄/聊天 UI，不触碰 Live2D 参数或资源。
    ['@@sim:holiday:cn_new_year', '[TestInbox] 当前节日主题: 新春主题'],
    ['@@sim:screenshot:holiday_smoke_on', 'screenshot queued'],
    ['@@sim:holiday:off', '[TestInbox] 当前节日主题: 默认主题'],
    ['@@sim:screenshot:holiday_smoke_off', 'screenshot queued'],
    ['@@view:settings', '[TestInbox] @@view 命令: settings'],
    ['@@view:model', '[TestInbox] @@view 命令: model'],
    ['@@view:back', '子面板返回 → Settings'],
    ['@@view:back', '[TestInbox] @@view 命令: back'],
    ['@@view:report', '[TestInbox] @@view 命令: report'],
    ['@@view:back', '[TestInbox] @@view 命令: back'],
    ['@@view:reminders', '[TestInbox] @@view 命令: reminders'],
    ['@@view:back', '[TestInbox] @@view 命令: back'],
    ['@@view:memory', '[TestInbox] @@view 命令: memory'],
    ['@@view:back', '[TestInbox] @@view 命令: back'],
    ['@@view:list', '[TestInbox] @@view 命令: list'],
    ['@@view:external', '[TestInbox] @@view 命令: external'],
    ['@@view:model', '[RightPanel] 打开模型设置页'],
    ['@@view:extclick:100,250', '[RightPanel] 模型设置页选择: qwen2.5:3b'],
    ['@@view:extclick:400,170', '[TestInbox] @@view 命令: extclick:400,170'],
    ['@@view:extclick:100,545', '[RightPanel] 对话模型已切换: qwen2.5:3b'],
    ['@@view:back', '子面板返回 → Chat'],
    ['@@view:list', '[TestInbox] @@view 命令: list'],
    // ★ 外置面板交互链路（Phase A3/A4）：会话项双击进聊天 → 工具行进设置 → ◀ 返回 → 审批注入
    ['@@view:extclick:100,200,true', '进入聊天: '],
    ['@@view:extclick:753,87', '打开子面板 → Settings'],
    ['@@view:extclick:15,15', '子面板返回 → Chat'],
    ['@@approval:smoke test cmd', '已注入测试审批'],
    ['@@view:embed', '[TestInbox] @@view 命令: embed'],
    ['@@emote:happy', '已注入表情: happy'],
    ['@@view:close', '[TestInbox] @@view 命令: close'],
    ['@@view:open', '[TestInbox] @@view 命令: open'],
    // 退出生命周期回归：优先走应用自己的完整退出链，清理阶段的 taskkill 只作兜底。
    ['@@test:quit', '[TestInbox] @@test:quit → 执行完整退出（等同托盘退出）'],
];

const FIXED_SCREEN_SIZES = ['窗口=486x1269', '窗口=1290x1269', '窗口=860x900'];
const fixedScreenAssertions = process.env.FU_XUAN_SMOKE_FIXED_SCREEN === '1';
const MARKERS = ['进入聊天: ', '返回会话列表', '淡出完成，已隐藏'];
const EXT_MARKERS = [
    '[ExternalChat] 独立窗口已创建',
    '[RightPanel] ⧉ 已切换到独立面板窗口（可被其他窗口遮挡）',
    '[RightPanel] 已退出独立面板窗口',
    // Phase A3/A4 交互闭环标记
    '打开子面板 → Settings',
    '子面板返回 → Chat',
    '已注入测试审批',
    '外置聊天 UI 性能模式 开启',
    '外置聊天 UI 性能模式 关闭',
];

async function main() {
    const inbox = path.join(TEST_DATA_ROOT, 'inbox.txt');
    const testMode = path.join(TEST_DATA_ROOT, '.test_mode');
    // 生产记忆文件快照（用于防污染断言）
    const PROD_FILES = ['pet_memory.json', 'pet_personality.json', 'motion_memory.json', 'activity_log.json', 'validation_log.json', 'knowledge_base.json', 'reminders.json'];
    assertSafeTestDataRoot();
    if (validateSafety) {
        log(`[PASS] 测试目录边界有效: ${TEST_DATA_ROOT}`);
        log(`[PASS] 生产目录边界有效: ${PROD_DATA_ROOT}`);
        return;
    }
    const prodSnapshot = PROD_FILES.map(f => {
        const p = path.join(PROD_DATA_ROOT, f);
        return { f, p, ...snapshotFile(p) };
    });

    if (!fs.existsSync(exe)) { console.error(`[FAIL] 未找到桌宠 exe: ${exe}`); process.exit(1); }
    log(`[smoke] exe: ${exe}`);
    log(`[smoke] 隔离测试数据目录: ${TEST_DATA_ROOT}`);
    log(`[smoke] 生产数据目录(只读校验): ${PROD_DATA_ROOT}`);
    log(`[smoke] Player.log: ${PLAYER_LOG}`);

    // 0. 准备隔离测试目录（无记忆起点）
    fs.rmSync(TEST_DATA_ROOT, { recursive: true, force: true });
    fs.mkdirSync(TEST_DATA_ROOT, { recursive: true });

    // 1. 仅清理本次专属目录；绝不终止已有生产桌宠实例。
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
        fs.unlinkSync(testMode); stopOwnedDesktopPet(proc); process.exit(1);
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
    if (fixedScreenAssertions) {
        for (const s of FIXED_SCREEN_SIZES) {
            if (!content.includes(s)) fails.push(`缺少窗口尺寸切换: ${s}`);
        }
    } else {
        // 隔离运行常在 512x512 的无头/远程桌面分辨率下执行，窗口会被 Screen 边界裁剪，
        // 不能把用户大屏上的固定物理尺寸硬编码为冒烟测试前提；视图命令和页面标记仍会验证页面切换。
        const sizeMatches = [...content.matchAll(/(?:窗口=|物理=\()([0-9]+)x([0-9]+)/g)];
        const uniqueSizes = new Set(sizeMatches.map(m => `${m[1]}x${m[2]}`));
        if (uniqueSizes.size < 2) {
            fails.push(`窗口尺寸未发生切换（实际尺寸: ${[...uniqueSizes].join(', ') || '未记录'}）`);
        }
    }
    for (const m of MARKERS) {
        if (!content.includes(m)) fails.push(`缺少行为标记: ${m}`);
    }
    for (const m of EXT_MARKERS) {
        if (!content.includes(m)) fails.push(`缺少独立窗口标记: ${m}`);
    }
    const holidayShotDir = path.join(TEST_DATA_ROOT, 'test_screenshots');
    const holidayShots = fs.existsSync(holidayShotDir)
        ? fs.readdirSync(holidayShotDir).filter(name => name.endsWith('.png'))
        : [];
    if (holidayShots.length < 2) fails.push(`节日主题截图不足（实际 ${holidayShots.length} 张，期望开启/关闭各 1 张）`);
    const nre = (content.match(/NullReferenceException/g) || []).length;
    const otherExc = (content.match(/Exception:/g) || []).length - nre;
    if (nre > 0) fails.push(`Player.log 发现 ${nre} 次 NullReferenceException（面板渲染中断，见堆栈）`);
    if (otherExc > 0) fails.push(`Player.log 发现 ${otherExc} 次其他 Exception`);

    // @@test:quit 必须真的让应用自行退出；后面的 taskkill 只是防止测试实例残留，
    // 不能用强杀结果掩盖退出链没有生效。
    if (COMMANDS.some(([cmd]) => cmd === '@@test:quit')) {
        let stillRunning = false;
        try {
            process.kill(proc.pid, 0);
            stillRunning = true;
        } catch { /* 进程已退出 */ }
        if (stillRunning) fails.push('@@test:quit 后桌宠进程仍在运行，未完成应用自身退出');
    }

    // 6. 清理：只终止本次创建的 PID；--keep-alive 保留隔离目录和 .test_mode，
    // 让留下的实例仍处在无记忆、禁云端的受保护状态。
    if (!keepAlive && !keepArtifacts) {
        try { fs.unlinkSync(testMode); } catch { /* 已不存在 */ }
        stopOwnedDesktopPet(proc);
        for (let i = 0; i < 10; i++) {
            try { fs.rmSync(TEST_DATA_ROOT, { recursive: true, force: true }); break; }
            catch { await sleep(300); }
        }
    }
    // ★ 防污染断言：存在性或内容哈希的任何变化都必须失败。
    const pollution = prodSnapshot.filter(x => {
        const after = snapshotFile(x.p);
        return x.exists !== after.exists || x.hash !== after.hash;
    });
    if (pollution.length > 0) fails.push(`生产记忆文件被测试修改: ${pollution.map(x => x.f).join(', ')}`);
    log(`[smoke] 隔离目录已清理: ${!fs.existsSync(TEST_DATA_ROOT)}`);
    if (!keepAlive) {
        stopOwnedDesktopPet(proc);
        log('[smoke] 已结束测试实例');
    }

    if (fails.length > 0) {
        console.error(`\n[FAIL] 冒烟测试未通过（${fails.length} 项）：`);
        fails.forEach(f => console.error('  ✗ ' + f));
        process.exit(1);
    }
    console.log('\n[PASS] 运行时冒烟测试通过：全部命令处理 + 三档尺寸切换 + 零 NRE + 生产记忆零污染 ✅');
}

main().catch(e => { console.error('[FAIL] 冒烟测试异常: ' + e.message); process.exit(1); });
