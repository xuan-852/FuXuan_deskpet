'use strict';

// 普通点击本地轻回应的隔离运行时验收（2026-10-12 产品迭代）。
//
// 设计约束：
// - 只经既有 @@sim 终端链路驱动，不模拟鼠标坐标、不驱动 OS 鼠标；
// - FU_XUAN_DATA 指向临时目录 + .test_mode 双保险，生产记忆只读哈希校验；
// - 不新增 sim 命令：证据来自既有日志行与既有命令回执。
//
// 覆盖：
// 1. 普通点击播出本地短句，且点击窗口内不出现任何 ChatManager 输出（零 AI 请求）；
// 2. 冷却内再次点击：姿势照常应用，但不播轻回应（轻回应与姿势解耦）；
// 3. 执行中的动作优先：动作锁期间点击不播轻回应；
// 4. 冷却到期后恢复回应，且等待期间没有补播过期回应；
// 5. 拖拽不被误识别为点击，不产生轻回应；
// 6. 退出后正常回落：无暂停、无拖拽残留；
// 7. 生产记忆零污染。
//
// 两个既有行为决定了本脚本必须重试而不能假设时序：
// - 点击后 DragHandler 会 Pause(clickPauseDuration=1s)，暂停期间的点击被直接忽略；
// - @@sim:walk:stop 之后桌宠仍会自主重新开始行走（ForceStop 只结束当前地面任务），
//   行走会占用输入租约从而抑制点击姿势，因此"姿势应用"必须等到真正的静止窗口。
const fs = require('fs');
const os = require('os');
const path = require('path');
const { createHash } = require('crypto');
const { spawn, execFileSync } = require('child_process');

const exe = process.argv[2];
if (!exe || !fs.existsSync(exe)) throw new Error('missing DesktopPet.exe path');

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'fuxuan_click_light_'));
const inbox = path.join(root, 'inbox.txt');
const logPath = path.join(root, 'logs', 'player_log.txt');
const resultPath = path.join(root, 'click-light-response-result.json');
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

const LOCAL_RESPONSE = '[AutoChat] 本地点击轻回应:';
const CLICK_ACCEPTED = '[DragHandler] 模拟点击:';
const CLICK_IGNORED = '[DragHandler] 模拟点击忽略';
const POSE_APPLIED = '[Live2DRenderer] 点击部位:';
const POSE_SUPPRESSED = '[Live2DRenderer] 点击姿势已抑制: active-input-lease';
const CHATMANAGER = '[ChatManager]';
const COOLDOWN_WAIT_MS = 21000; // interactionCooldown 默认 20s

// ---------------- 生产记忆只读边界 ----------------

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

function isPathInside(child, parent) {
    const rel = path.relative(parent, child);
    return rel !== '' && !rel.startsWith('..' + path.sep) && rel !== '..' && !path.isAbsolute(rel);
}

const PROD_DATA_ROOT = resolveProductionDataRoot();
const PROD_FILES = ['pet_memory.json', 'pet_personality.json', 'motion_memory.json',
    'activity_log.json', 'validation_log.json', 'knowledge_base.json', 'reminders.json'];

function snapshotFile(filePath) {
    if (!fs.existsSync(filePath)) return { exists: false, hash: null, mtimeMs: null };
    return {
        exists: true,
        hash: createHash('sha256').update(fs.readFileSync(filePath)).digest('hex'),
        mtimeMs: fs.statSync(filePath).mtimeMs,
    };
}

function assertSafeTestRoot() {
    const tempRoot = path.resolve(os.tmpdir());
    if (!isPathInside(root, tempRoot)) {
        throw new Error(`隔离数据目录必须位于系统临时目录下: ${tempRoot}`);
    }
    if (root.toLowerCase() === PROD_DATA_ROOT.toLowerCase()
        || isPathInside(PROD_DATA_ROOT, root) || isPathInside(root, PROD_DATA_ROOT)) {
        throw new Error('隔离数据目录与生产数据目录重叠，拒绝启动');
    }
}

// ---------------- 日志与命令 ----------------

function readLog() {
    try { return fs.readFileSync(logPath, 'utf8'); }
    catch { return ''; }
}

async function waitFor(marker, after = 0, timeoutMs = 90000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (readLog().slice(after).includes(marker)) return true;
        await sleep(60);
    }
    throw new Error(`timeout waiting for ${marker}`);
}

async function waitForAny(markers, after = 0, timeoutMs = 90000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        const delta = readLog().slice(after);
        for (const marker of markers) if (delta.includes(marker)) return marker;
        await sleep(60);
    }
    return null;
}

async function sendAndWait(command, marker, timeoutMs = 90000) {
    const offset = readLog().length;
    fs.writeFileSync(inbox, command, 'utf8');
    try {
        if (marker) await waitFor(marker, offset, timeoutMs);
        else await sleep(500);
        return offset;
    } finally {
        fs.writeFileSync(inbox, '', 'utf8');
    }
}

async function send(command, settleMs = 450) {
    fs.writeFileSync(inbox, command, 'utf8');
    await sleep(settleMs);
    fs.writeFileSync(inbox, '', 'utf8');
}

async function assertAbsent(markers, after, durationMs = 1500) {
    const deadline = Date.now() + durationMs;
    while (Date.now() < deadline) {
        const delta = readLog().slice(after);
        for (const marker of markers) {
            if (delta.includes(marker)) throw new Error(`unexpected marker: ${marker}`);
        }
        await sleep(60);
    }
}

/** 点击后 DragHandler 会暂停桌宠 1s，等它结束。 */
async function waitForClickPauseCleared() {
    await sleep(1800);
}

/** 等到一个真正静止的窗口（velocity=(0,0)）；行走会占用输入租约并抑制点击姿势。 */
async function waitForStationary(attempts = 15) {
    for (let attempt = 0; attempt < attempts; attempt++) {
        await send('@@sim:walk:stop', 350);
        const offset = readLog().length;
        await send('@@sim:status', 450);
        if (readLog().slice(offset).includes('velocity=(0,0)')) return true;
        await sleep(400);
    }
    return false;
}

/** 发一次点击；被 DragHandler 因暂停而忽略时返回 accepted=false，交由调用方重试。 */
async function clickOnce(timeoutMs = 8000) {
    const offset = readLog().length;
    fs.writeFileSync(inbox, '@@sim:click:center', 'utf8');
    try {
        const marker = await waitForAny([CLICK_ACCEPTED, CLICK_IGNORED], offset, timeoutMs);
        return { offset, accepted: marker === CLICK_ACCEPTED };
    } finally {
        fs.writeFileSync(inbox, '', 'utf8');
    }
}

/**
 * 点击直到播出轻回应。启动问候/主动气泡等瞬时气泡会让位（设计如此），
 * 被让位的点击不写冷却时间戳，因此重试不会污染后续冷却断言。
 */
async function clickUntilLocalResponse(attempts = 8) {
    for (let attempt = 0; attempt < attempts; attempt++) {
        const { offset, accepted } = await clickOnce();
        if (!accepted) { await sleep(1200); continue; }
        const marker = await waitForAny([LOCAL_RESPONSE, POSE_APPLIED], offset, 6000);
        if (marker === LOCAL_RESPONSE) return offset;
        await sleep(4000);
    }
    throw new Error('点击始终未播出本地轻回应');
}

/**
 * 点击并确认点击姿势真正应用（未被输入租约抑制）。
 * 自主行走会重新占用租约，因此每次尝试都重新等一个静止窗口。
 */
async function clickExpectingPose(attempts = 10) {
    for (let attempt = 0; attempt < attempts; attempt++) {
        await waitForStationary();
        const { offset, accepted } = await clickOnce();
        if (!accepted) { await sleep(1200); continue; }
        const marker = await waitForAny([POSE_APPLIED, POSE_SUPPRESSED], offset, 6000);
        if (marker === POSE_APPLIED) return offset;
        await sleep(1200);
    }
    throw new Error('点击姿势始终被输入租约抑制，无法验证姿势应用');
}

// ---------------- 主流程 ----------------

(async () => {
    assertSafeTestRoot();
    const prodSnapshot = PROD_FILES.map(f => {
        const p = path.join(PROD_DATA_ROOT, f);
        return { f, p, ...snapshotFile(p) };
    });

    fs.writeFileSync(path.join(root, '.test_mode'), '');
    fs.writeFileSync(inbox, '', 'utf8');
    fs.writeFileSync(path.join(root, 'pet_config.json'), JSON.stringify({
        motionAgentEnabled: false, activityCategoryTrackingEnabled: false,
        localModel: 'fuxuan-click-test-unavailable', chatLocalModel: 'fuxuan-click-test-unavailable',
    }));

    const processHandle = spawn(exe, ['--no-cloud'], {
        env: { ...process.env, FU_XUAN_DATA: root, FU_XUAN_NO_CLOUD: '1', DESKTOP_TOKEN: '',
            FU_XUAN_LOCAL_MODEL: 'fuxuan-click-test-unavailable',
            FU_XUAN_LOCAL_CHAT_MODEL: 'fuxuan-click-test-unavailable' },
        stdio: 'ignore',
    });
    let exited = false;
    let spawnError = null;
    processHandle.on('exit', () => { exited = true; });
    processHandle.on('error', error => { spawnError = error; exited = true; });

    const evidence = {
        status: 'passed',
        prodDataRoot: PROD_DATA_ROOT,
        isolatedDataDir: root,
    };

    try {
        await waitFor('DesktopPet] 落地');
        await sendAndWait('@@sim:idle-actions:off', null);
        await waitForStationary();

        // 启动问候（firstGreetingDelay=3s）是 Low 优先级气泡，会让位。
        // 留足它完整淡出的时间，避免把"让位"误判成"不回应"。
        await sleep(11000);

        // ---- 1. 普通点击：本地轻回应 + 零 AI 请求 ----
        const firstClickOffset = await clickUntilLocalResponse();
        if (readLog().slice(firstClickOffset).includes(CHATMANAGER)) {
            throw new Error('普通点击窗口内出现了 ChatManager 输出，疑似发起了 AI 请求');
        }
        evidence.clickEmitsLocalResponse = true;
        evidence.clickIssuesNoAiRequest = true;
        await sendAndWait('@@sim:life-state', 'attentionReason=direct-click');
        evidence.clickKeepsAttentionEvent = true;

        // ---- 2. 冷却内再次点击：姿势照常应用，但不播轻回应 ----
        const cooldownWindowOffset = readLog().length;
        await waitForClickPauseCleared();
        await clickExpectingPose();
        await assertAbsent([LOCAL_RESPONSE], cooldownWindowOffset, 2000);
        evidence.cooldownSuppressesLightResponse = true;
        evidence.poseStillAppliesDuringCooldown = true;

        // ---- 3. 执行中的动作优先：动作锁期间不播轻回应 ----
        let actionLockObserved = false;
        for (const actionId of [3, 5, 2, 1, 4]) {
            await send(`@@sim:idle-action:${actionId}`, 250);
            const stateOffset = readLog().length;
            await send('@@sim:desktop-state', 450);
            const stateLine = readLog().slice(stateOffset).split('\n')
                .find(line => line.includes('[DesktopState]'));
            if (!stateLine || !stateLine.includes('actionLocked=True')) continue;

            const lockedClickOffset = readLog().length;
            const { accepted } = await clickOnce();
            if (!accepted) continue; // 暂停期被忽略，换一个动作再试
            actionLockObserved = true;
            await assertAbsent([LOCAL_RESPONSE], lockedClickOffset, 2500);
            break;
        }
        evidence.actionLockSuppressionVerified = actionLockObserved;
        if (!actionLockObserved) {
            evidence.actionLockSuppressionNote =
                '未在轮询窗口内观测到 actionLocked=True 的可点击窗口，该项未验证（不得计为通过）';
        }

        // ---- 4. 冷却到期恢复，且等待期间不补播过期回应 ----
        await waitForStationary();
        await send('@@sim:resume', 450);
        const waitOffset = readLog().length;
        await sleep(COOLDOWN_WAIT_MS);
        if (readLog().slice(waitOffset).includes(LOCAL_RESPONSE)) {
            throw new Error('冷却等待期间出现了补播的过期轻回应');
        }
        await waitForStationary();
        await clickUntilLocalResponse(6);
        evidence.cooldownReleaseRecoversResponse = true;
        evidence.noStaleReplayWhileIdle = true;

        // ---- 5. 无锁与姿态残留：点击姿势仍能正常应用 ----
        await waitForClickPauseCleared();
        await clickExpectingPose();
        await sendAndWait('@@sim:desktop-state', 'actionLocked=False');
        evidence.noResidualLockOrPose = true;

        // ---- 6. 拖拽不得被误识别为点击 ----
        // 放在最后：拖拽结束会走既有的拖拽自动聊天策略（本轮未改造），
        // 它可能让 ChatManager 忙碌，不应影响前面的轻回应断言。
        await waitForClickPauseCleared();
        const dragOffset = readLog().length;
        await sendAndWait('@@sim:drag:offset:120,20,8', '[DragHandler] 模拟拖动开始');
        await sleep(3000);
        await assertAbsent([LOCAL_RESPONSE], dragOffset, 2000);
        await sendAndWait('@@sim:status', 'dragging=False');
        evidence.dragDoesNotEmitLocalResponse = true;

        // ---- 7. 退出与回落 ----
        await sendAndWait('@@test:quit', 'test-exit input cleanup completed', 20000);
        evidence.exitRecovered = true;
    } finally {
        fs.writeFileSync(inbox, '@@test:quit', 'utf8');
        const deadline = Date.now() + 20000;
        while (!exited && Date.now() < deadline) await sleep(100);
        fs.writeFileSync(inbox, '', 'utf8');
        if (!exited && Number.isInteger(processHandle.pid)) {
            try { execFileSync('taskkill', ['/PID', String(processHandle.pid), '/T', '/F'], { stdio: 'ignore' }); }
            catch { /* Owned process may have exited between the check and taskkill. */ }
            throw new Error('测试实例未自行退出，已尝试清理本次 PID: ' + processHandle.pid);
        }
        if (spawnError) throw spawnError;
        const polluted = prodSnapshot.filter(before => {
            const after = snapshotFile(before.p);
            return before.exists !== after.exists || before.hash !== after.hash || before.mtimeMs !== after.mtimeMs;
        });
        if (polluted.length) throw new Error('生产记忆被修改: ' + polluted.map(x => x.f).join(', '));
    }

    // ---- 生产记忆零污染 ----
    const polluted = prodSnapshot.filter(before => {
        const after = snapshotFile(before.p);
        return before.exists !== after.exists || before.hash !== after.hash || before.mtimeMs !== after.mtimeMs;
    });
    if (polluted.length > 0) {
        throw new Error('生产记忆文件被测试修改: ' + polluted.map(x => x.f).join(', '));
    }
    evidence.productionMemoryUntouched = true;
    evidence.status = evidence.actionLockSuppressionVerified ? 'passed' : 'partial';

    fs.writeFileSync(resultPath, JSON.stringify(evidence, null, 2));
    console.log('click light response ' + evidence.status + ': ' + root);
    if (evidence.status !== 'passed') process.exitCode = 2;
})().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
});
