'use strict';

/*
 * 隔离 Player 驱动：验证 acknowledge_nod 的认证执行、路由门控和终态收束。
 * 用法: node scripts/test/acknowledge_nod_runtime_drive.cjs <DesktopPet.exe>
 * 仅使用 FU_XUAN_DATA 临时根与 .test_mode，不调用 OS 鼠标或生产数据。
 */
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const exe = process.argv[2];
const root = path.join(os.tmpdir(), `fuxuan_acknowledge_nod_${process.pid}_${Date.now()}`);
const inbox = path.join(root, 'inbox.txt');
const logPath = path.join(root, 'logs', 'player_log.txt');
const asset = path.resolve(__dirname, '../../code/desktop_unity/Assets/Resources/Live2D/CertifiedMotions/acknowledge_nod.json');
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

function logText() {
    try { return fs.readFileSync(logPath, 'utf8'); }
    catch { return ''; }
}
function logTail() { return logText().split(/\r?\n/).slice(-120).join('\n'); }
function assertOwnedRoot() {
    const temp = path.resolve(os.tmpdir());
    const target = path.resolve(root);
    if (!target.startsWith(temp + path.sep)
        || !path.basename(target).startsWith('fuxuan_acknowledge_nod_')
        || fs.existsSync(target) && fs.lstatSync(target).isSymbolicLink())
        throw new Error('test root is not an owned temporary directory');
}
async function waitFor(marker, after = 0, timeoutMs = 90000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (logText().slice(after).includes(marker)) return logText().slice(after);
        await sleep(100);
    }
    throw new Error(`timeout waiting for ${marker}`);
}
async function send(command, marker, timeoutMs = 30000) {
    const offset = logText().length;
    fs.writeFileSync(inbox, command, 'utf8');
    try { return await waitFor(marker, offset, timeoutMs); }
    finally { fs.writeFileSync(inbox, '', 'utf8'); }
}
async function sendQuiet(command, settleMs = 350) {
    fs.writeFileSync(inbox, command, 'utf8');
    await sleep(settleMs);
    fs.writeFileSync(inbox, '', 'utf8');
}
async function behaviorSnapshot() {
    const offset = logText().length;
    fs.writeFileSync(inbox, '@@sim:behavior-state', 'utf8');
    try {
        const text = await waitFor('[BehaviorState] snapshot count=', offset, 10000);
        const header = text.match(/\[BehaviorState\] snapshot count=(\d+)/);
        if (!header) throw new Error('behavior snapshot header missing');
        const expected = Number(header[1]);
        const deadline = Date.now() + 10000;
        while (Date.now() < deadline) {
            const output = logText().slice(offset);
            if ((output.match(/\[BehaviorState\] intent=/g) || []).length >= expected)
                return output;
            await sleep(50);
        }
        throw new Error('behavior snapshot incomplete');
    }
    finally { fs.writeFileSync(inbox, '', 'utf8'); }
}
function parseBehavior(text, status) {
    const matches = [...text.matchAll(/\[BehaviorState\] intent=([^ ]+) execution=([^ ]+) correlation=([^ ]+) status=([^ ]+) request=(\d+) requestExecution=([^ \r\n]+)/g)];
    const match = matches.reverse().find(item => Number(item[5]) > 0);
    if (!match) throw new Error(`behavior state missing: ${status}`);
    const state = {
        intentId: match[1], executionId: match[2], correlationId: match[3],
        status: match[4], requestId: Number(match[5]), requestExecutionId: match[6]
    };
    if (state.status !== status) throw new Error(`expected ${status}, got ${state.status}`);
    if (state.executionId !== state.requestExecutionId) throw new Error('request/execution association changed');
    return state;
}
function parseLife(text, expected) {
    const matches = [...text.matchAll(/\[LifeState\] snapshot version=(\d+).*?action=([^ ]+)/g)];
    const match = matches[matches.length - 1];
    if (!match) throw new Error(`life-state missing: ${expected}`);
    if (match[2] !== expected) throw new Error(`expected life ${expected}, got ${match[2]}`);
    return { version: Number(match[1]), action: match[2] };
}
async function stopPlayer(player) {
    if (!player || player.killed) return;
    try {
        const offset = logText().length;
        fs.writeFileSync(inbox, '@@test:quit', 'utf8');
        await waitFor('[TestInbox] @@test:quit', offset, 5000);
    } catch { /* child-local fallback below */ }
    await Promise.race([new Promise(resolve => player.once('exit', resolve)), sleep(8000)]);
    if (!player.killed) player.kill();
}

(async () => {
    if (!exe || !fs.existsSync(exe)) throw new Error('missing DesktopPet.exe path');
    if (!fs.existsSync(asset)) throw new Error('missing acknowledge_nod asset');
    assertOwnedRoot();
    fs.mkdirSync(path.join(root, 'certified_motions'), { recursive: true });
    fs.writeFileSync(path.join(root, '.test_mode'), '');
    fs.copyFileSync(asset, path.join(root, 'certified_motions', 'acknowledge_nod.json'));
    fs.writeFileSync(inbox, '', 'utf8');

    const player = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: root }, stdio: 'ignore', windowsHide: true });
    try {
        await waitFor('[DesktopPet] 落地');
        await send('@@sim:idle-actions:off', '测试隔离：已暂停空闲动作调度');
        await send('@@sim:walk:stop', '已强制停止走路');
        await sleep(1600);
        await send('@@sim:status', 'velocity=(0,0)');

        const firstOffset = logText().length;
        const startedLog = await send('@@sim:skill:acknowledge', '[CertifiedMotion] started: acknowledge_nod');
        if (!startedLog.includes('[CertifiedMotion] candidate loaded: acknowledge_nod, source=data-root'))
            throw new Error('data-root candidate source marker missing');
        if (!startedLog.includes('[EmbodiedRuntimeAdmission] admitted: acknowledge_nod'))
            throw new Error('admission marker missing');
        const activeBehavior = parseBehavior(await behaviorSnapshot(), 'Executing');
        const activeLife = parseLife(await send('@@sim:life-state', '[LifeState] snapshot'), 'Active');
        await waitFor('[CertifiedMotion] cleanup: certified-motion-completed', firstOffset);
        await waitFor(`[BodySkillUI] terminal=Completed execution=${activeBehavior.executionId}`, firstOffset);
        const completedBehavior = parseBehavior(await behaviorSnapshot(), 'Completed');
        const completedLife = parseLife(await send('@@sim:life-state', '[LifeState] snapshot'), 'Completed');
        for (const key of ['intentId', 'executionId', 'correlationId', 'requestId', 'requestExecutionId'])
            if (activeBehavior[key] !== completedBehavior[key]) throw new Error(`identity changed for ${key}`);
        if (!logText().slice(firstOffset).includes('[EmbodiedSafeRecovery] pose-restored'))
            throw new Error('pose restoration marker missing');

        // The natural-language route is intentionally closed while LlmExposed=false.
        const hiddenOffset = logText().length;
        await send('点点头给我看', '[ChatManager] 确定性身体请求回执已发布');
        const hiddenLog = logText().slice(hiddenOffset);
        if (hiddenLog.includes('[CertifiedMotion] started: acknowledge_nod'))
            throw new Error('hidden acknowledge_nod was exposed to natural language');
        if (!hiddenLog.includes('[ChatManager] 确定性身体请求回执已发布'))
            throw new Error('hidden route did not publish a deterministic refusal receipt');

        const cancelOffset = logText().length;
        await send('@@sim:skill:acknowledge', '[CertifiedMotion] started: acknowledge_nod');
        const cancelActive = parseBehavior(await behaviorSnapshot(), 'Executing');
        await send('@@sim:stop-body-skill', '[TestInbox] stop-body-skill result:');
        await waitFor(`[BodySkillUI] terminal=Cancelled execution=${cancelActive.executionId}`, cancelOffset);
        const cancelled = parseBehavior(await behaviorSnapshot(), 'Cancelled');
        if (cancelled.executionId !== cancelActive.executionId) throw new Error('cancel identity changed');

        const timeoutOffset = logText().length;
        await send('@@sim:skill:acknowledge', '[CertifiedMotion] started: acknowledge_nod');
        const timeoutActive = parseBehavior(await behaviorSnapshot(), 'Executing');
        await send('@@sim:expire-body-skill', '[TestInbox] expire-body-skill result:');
        await waitFor(`[BodySkillUI] terminal=Expired execution=${timeoutActive.executionId}`, timeoutOffset);
        const expired = parseBehavior(await behaviorSnapshot(), 'Expired');
        if (expired.executionId !== timeoutActive.executionId) throw new Error('expiry identity changed');

        const conflictOffset = logText().length;
        await send('@@sim:walk:left', '[TestInbox] 已强制开始向左走');
        await sleep(250);
        await sendQuiet('@@sim:skill:acknowledge');
        await sleep(700);
        const conflictLog = logText().slice(conflictOffset);
        if (conflictLog.includes('[CertifiedMotion] started: acknowledge_nod'))
            throw new Error('acknowledge_nod started while locomotion was active');
        if (!conflictLog.includes('当前未处于稳定静止状态') && !conflictLog.includes('动作通道被占用'))
            throw new Error('conflict refusal marker missing');
        await send('@@sim:walk:stop', '已强制停止走路');

        console.log(JSON.stringify({ activeBehavior, completedBehavior, activeLife, completedLife, cancelled, expired, root }));
    } finally {
        await stopPlayer(player);
    }
})().then(() => {
    assertOwnedRoot();
    fs.rmSync(root, { recursive: true, force: true });
}).catch(error => {
    console.error(error.message);
    console.error('preserved test root: ' + root);
    console.error('player log tail:\n' + logTail());
    process.exitCode = 1;
});
