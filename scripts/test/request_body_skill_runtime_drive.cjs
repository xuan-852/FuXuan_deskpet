'use strict';

/*
 * 隔离 Player 驱动：验证 request_body_skill 经 ToolRegistry 进入
 * BehaviorIntent、认证曲线执行和同一关联终态回写。
 * 用法: node scripts/test/request_body_skill_runtime_drive.cjs <DesktopPet.exe> <skill-id> <candidate.json>
 */
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const exe = process.argv[2];
const skillId = process.argv[3];
const candidateFile = process.argv[4];
const safeSkill = String(skillId || '').toLowerCase().replace(/[^a-z0-9]+/g, '_');
const root = path.join(os.tmpdir(), `fuxuan_request_body_skill_${safeSkill}_${process.pid}_${Date.now()}`);
const inbox = path.join(root, 'inbox.txt');
const logPath = path.join(root, 'logs', 'player_log.txt');
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

function assertOwnedRoot() {
    const temp = path.resolve(os.tmpdir());
    const target = path.resolve(root);
    if (!target.startsWith(temp + path.sep)
        || !path.basename(target).startsWith(`fuxuan_request_body_skill_${safeSkill}_`)
        || !/^\d+_\d+$/.test(path.basename(target).slice(`fuxuan_request_body_skill_${safeSkill}_`.length)))
        throw new Error('test root is outside the dedicated temporary directory');
    if (fs.existsSync(target) && fs.lstatSync(target).isSymbolicLink())
        throw new Error('test root must not be a symbolic link');
}

function logText() {
    try { return fs.readFileSync(logPath, 'utf8'); }
    catch { return ''; }
}

function logTail() {
    return logText().split(/\r?\n/).slice(-100).join('\n');
}

async function waitFor(marker, after = 0, timeoutMs = 90000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (logText().slice(after).includes(marker)) return;
        await sleep(80);
    }
    throw new Error(`timeout waiting for ${marker}`);
}

async function sendAndWait(command, marker, timeoutMs = 90000) {
    const offset = logText().length;
    fs.writeFileSync(inbox, command, 'utf8');
    try {
        await waitFor(marker, offset, timeoutMs);
        return logText().slice(offset);
    }
    finally {
        fs.writeFileSync(inbox, '', 'utf8');
    }
}

async function sendAndSettle(command) {
    fs.writeFileSync(inbox, command, 'utf8');
    await sleep(450);
    fs.writeFileSync(inbox, '', 'utf8');
}

async function sendBehaviorSnapshot() {
    const offset = logText().length;
    fs.writeFileSync(inbox, '@@sim:behavior-state', 'utf8');
    try {
        await waitFor('[BehaviorState] snapshot count=', offset);
        const header = logText().slice(offset).match(/\[BehaviorState\] snapshot count=(\d+)/);
        if (!header) throw new Error('behavior snapshot header missing');
        const expected = Number(header[1]);
        const deadline = Date.now() + 10000;
        while (Date.now() < deadline) {
            const output = logText().slice(offset);
            if ((output.match(/\[BehaviorState\] intent=/g) || []).length >= expected)
                return output;
            await sleep(40);
        }
        throw new Error(`incomplete behavior snapshot: expected ${expected} executions`);
    }
    finally { fs.writeFileSync(inbox, '', 'utf8'); }
}

function parseBehaviorState(text, expectedStatus) {
    const matches = [...text.matchAll(
        /\[BehaviorState\] intent=([^ ]+) execution=([^ ]+) correlation=([^ ]+) status=([^ ]+) request=(\d+) requestExecution=([^ \r\n]+)/g
    )];
    const match = matches.reverse().find(candidate => Number(candidate[5]) > 0);
    if (!match) throw new Error(`behavior state line missing for ${expectedStatus}`);
    const state = {
        intentId: match[1],
        executionId: match[2],
        correlationId: match[3],
        status: match[4],
        requestId: Number(match[5]),
        requestExecutionId: match[6]
    };
    if (state.status !== expectedStatus) {
        throw new Error(`expected behavior status ${expectedStatus}, got ${state.status}`);
    }
    if (!Number.isInteger(state.requestId) || state.requestId <= 0) {
        throw new Error(`invalid request id: ${state.requestId}`);
    }
    if (state.requestExecutionId !== state.executionId) {
        throw new Error(`request execution association mismatch: ${state.requestExecutionId} != ${state.executionId}`);
    }
    return state;
}

function parseLifeState(text, expectedAction) {
    const matches = [...text.matchAll(
        /\[LifeState\] snapshot version=(\d+).*?action=([^ ]+)/g
    )];
    const match = matches[matches.length - 1];
    if (!match) throw new Error(`life-state snapshot missing for ${expectedAction}`);
    const state = { version: Number(match[1]), action: match[2] };
    if (state.action !== expectedAction) {
        throw new Error(`expected life action ${expectedAction}, got ${state.action}`);
    }
    return state;
}

async function stopOwnedPlayer(player) {
    if (!player || player.killed) return;
    try {
        fs.writeFileSync(inbox, '@@test:quit', 'utf8');
        await waitFor('[TestInbox] @@test:quit', logText().length, 5000);
    }
    catch { /* fallback below only targets this child */ }
    await Promise.race([
        new Promise(resolve => player.once('exit', resolve)),
        sleep(8000)
    ]);
    if (!player.killed) player.kill();
}

(async () => {
    if (!exe || !fs.existsSync(exe)) throw new Error('missing DesktopPet.exe path');
    if (!skillId || !/^[a-z0-9_-]+$/i.test(skillId)) throw new Error('invalid skill id');
    if (!candidateFile || !fs.existsSync(candidateFile)) throw new Error('missing candidate json');

    assertOwnedRoot();
    if (fs.existsSync(root)) throw new Error(`test root already exists: ${root}`);
    fs.mkdirSync(path.join(root, 'certified_motions'), { recursive: true });
    fs.writeFileSync(path.join(root, '.test_mode'), '');
    fs.copyFileSync(candidateFile, path.join(root, 'certified_motions', `${skillId}.json`));
    fs.writeFileSync(inbox, '', 'utf8');

    const player = spawn(exe, [], {
        env: { ...process.env, FU_XUAN_DATA: root },
        stdio: 'ignore',
        windowsHide: true
    });

    try {
        await waitFor('[DesktopPet] 落地');
        await sendAndWait('@@privacy:activity:status', '[Privacy] activity-category-tracking=off');
        await sendAndWait('@@privacy:activity:on', '[Privacy] activity-category-tracking=on');
        await sendAndWait('@@privacy:activity:off', '[Privacy] activity-category-tracking=off');
        await sendAndSettle('@@sim:idle-actions:off');
        await sendAndWait('@@sim:walk:stop', '[TestInbox] 已强制停止走路');
        await sleep(1600);
        await sendAndWait('@@sim:status', 'velocity=(0,0)');

        await sendAndWait('@@sim:expression:happy', '[TestInbox] expression accepted: happy');
        const expressionActiveLog = await sendBehaviorSnapshot();
        const expressionActive = expressionActiveLog.match(/execution=(input-expression-\d+).*status=Executing/);
        if (!expressionActive) throw new Error('expression did not register an executing input intent');
        await sendAndWait('@@sim:expression:stop', '[TestInbox] expression stop requested');
        const expressionDoneLog = await sendBehaviorSnapshot();
        if (!expressionDoneLog.includes(`execution=${expressionActive[1]}`)
            || !expressionDoneLog.includes('status=Cancelled'))
            throw new Error('expression stop did not record a cancelled terminal');

        const requestLog = await sendAndWait(
            `@@sim:request-body-skill:${skillId}`,
            `[TestInbox] request-body-skill result: ✅ 已开始执行身体技能「${skillId}」`
        );
        if (!requestLog.includes(`[EmbodiedRuntimeAdmission] admitted: ${skillId}`)) {
            throw new Error('request did not reach runtime admission');
        }
        if (!requestLog.includes(`[CertifiedMotion] started: ${skillId}`)) {
            throw new Error('request did not start certified motion');
        }

        const activeBehaviorLog = await sendBehaviorSnapshot();
        const activeBehavior = parseBehaviorState(activeBehaviorLog, 'Executing');
        const activeLifeLog = await sendAndWait(
            '@@sim:life-state',
            '[LifeState] snapshot'
        );
        const activeLife = parseLifeState(activeLifeLog, 'Active');

        await waitFor('[EmbodiedRuntimeAdmission] released: certified-motion-completed');
        await waitFor(`[CertifiedMotion] cleanup: certified-motion-completed`);
        await waitFor(`[BodySkillUI] terminal=Completed execution=${activeBehavior.executionId}`);

        const completedBehaviorLog = await sendBehaviorSnapshot();
        const completedBehavior = parseBehaviorState(completedBehaviorLog, 'Completed');
        const completedLifeLog = await sendAndWait(
            '@@sim:life-state',
            '[LifeState] snapshot'
        );
        const completedLife = parseLifeState(completedLifeLog, 'Completed');

        for (const key of ['intentId', 'executionId', 'correlationId', 'requestId', 'requestExecutionId']) {
            if (activeBehavior[key] !== completedBehavior[key]) {
                throw new Error(`behavior association changed for ${key}: ${activeBehavior[key]} != ${completedBehavior[key]}`);
            }
        }
        const fullLog = logText();
        if ((fullLog.match(new RegExp(`\\[EmbodiedRuntimeAdmission\\] admitted: ${skillId}`, 'g')) || []).length !== 1) {
            throw new Error('expected exactly one runtime admission');
        }
        if ((fullLog.match(new RegExp(`\\[CertifiedMotion\\] started: ${skillId}`, 'g')) || []).length !== 1) {
            throw new Error('expected exactly one certified-motion start');
        }
        if (!fullLog.includes('[EmbodiedSafeRecovery] pose-restored')) {
            throw new Error('pose restoration marker missing');
        }

        // 完整自然语言入口：Ollama 可用性不应挡住明确的认证身体请求。
        const naturalOffset = logText().length;
        await sendAndWait('歪歪头给我看', '[TestInbox] 已注入消息: 歪歪头给我看');
        await waitFor('[ChatManager] 确定性身体请求回执已发布', naturalOffset);
        await waitFor(`[CertifiedMotion] started: ${skillId}`, naturalOffset);
        const naturalActive = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Executing');
        if (naturalActive.executionId === activeBehavior.executionId)
            throw new Error('natural-language request reused an old execution id');

        await sendAndWait('@@sim:stop-body-skill', '[TestInbox] stop-body-skill result: ✅ 已归元');
        await waitFor(`[BodySkillUI] terminal=Cancelled execution=${naturalActive.executionId}`);
        const naturalCancelled = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Cancelled');
        for (const key of ['intentId', 'executionId', 'correlationId', 'requestId', 'requestExecutionId']) {
            if (naturalActive[key] !== naturalCancelled[key])
                throw new Error(`cancel association changed for ${key}`);
        }

        const recoveryOffset = logText().length;
        await sendAndWait('歪歪头给我看', '[TestInbox] 已注入消息: 歪歪头给我看');
        await waitFor(`[CertifiedMotion] started: ${skillId}`, recoveryOffset);
        const naturalRecoveredActive = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Executing');
        if (naturalRecoveredActive.executionId === naturalActive.executionId)
            throw new Error('recovery request reused cancelled execution id');
        await waitFor('[EmbodiedRuntimeAdmission] released: certified-motion-completed', recoveryOffset);
        await waitFor(`[BodySkillUI] terminal=Completed execution=${naturalRecoveredActive.executionId}`);
        const naturalRecovered = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Completed');
        if (naturalRecovered.executionId !== naturalRecoveredActive.executionId)
            throw new Error('recovery completion changed execution id');

        const unsupportedOffset = logText().length;
        await sendAndWait('挥挥手给我看', '[ChatManager] 确定性身体请求回执已发布');
        await sleep(350);
        if (logText().slice(unsupportedOffset).includes('[CertifiedMotion] started:'))
            throw new Error('unsupported body request started an uncertified motion');

        const timeoutOffset = logText().length;
        await sendAndWait('歪歪头给我看', '[ChatManager] 确定性身体请求回执已发布');
        await waitFor(`[CertifiedMotion] started: ${skillId}`, timeoutOffset);
        const timeoutActive = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Executing');
        await sendAndWait('@@sim:expire-body-skill', '[TestInbox] expire-body-skill result: True');
        await waitFor(`[BodySkillUI] terminal=Expired execution=${timeoutActive.executionId}`);
        const timeoutDone = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Expired');
        if (timeoutDone.executionId !== timeoutActive.executionId)
            throw new Error('timeout changed execution identity');

        const reenableOffset = logText().length;
        await sendAndWait('歪歪头给我看', '[ChatManager] 确定性身体请求回执已发布');
        await waitFor(`[CertifiedMotion] started: ${skillId}`, reenableOffset);
        const reenableActive = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Executing');
        await sendAndWait('@@sim:disable-enable-renderer', '[TestInbox] disable-enable-renderer result: true');
        await waitFor(`[BodySkillUI] terminal=Cancelled execution=${reenableActive.executionId}`);
        const reenableDone = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Cancelled');
        if (reenableDone.executionId !== reenableActive.executionId)
            throw new Error('renderer re-enable changed execution identity');

        const afterReenableOffset = logText().length;
        await sendAndWait('歪歪头给我看', '[ChatManager] 确定性身体请求回执已发布');
        await waitFor(`[CertifiedMotion] started: ${skillId}`, afterReenableOffset);
        const afterReenableActive = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Executing');
        await waitFor(`[BodySkillUI] terminal=Completed execution=${afterReenableActive.executionId}`);

        const modelLossOffset = logText().length;
        await sendAndWait('歪歪头给我看', '[ChatManager] 确定性身体请求回执已发布');
        await waitFor(`[CertifiedMotion] started: ${skillId}`, modelLossOffset);
        const modelLossActive = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Executing');
        await sendAndWait('@@sim:destroy-body-model', '[TestInbox] destroy-body-model result: True');
        await waitFor(`[BodySkillUI] terminal=RecoveryFailed execution=${modelLossActive.executionId}`);
        const modelLossDone = parseBehaviorState(
            await sendBehaviorSnapshot(), 'RecoveryFailed');
        if (modelLossDone.executionId !== modelLossActive.executionId)
            throw new Error('model loss changed execution identity');
        if (!logText().slice(modelLossOffset).includes('[EmbodiedSafeRecovery] cleanup-failed:'))
            throw new Error('model loss did not report recovery failure');
        await waitFor('[Live2DRenderer] model rebuilt after model-unavailable', modelLossOffset, 30000);
        await sendAndWait('@@sim:body-model-ready', '[TestInbox] body-model-ready result: True');

        const rebuiltOffset = logText().length;
        await sendAndWait('歪歪头给我看', '[ChatManager] 确定性身体请求回执已发布');
        await waitFor(`[CertifiedMotion] started: ${skillId}`, rebuiltOffset);
        const rebuiltActive = parseBehaviorState(
            await sendBehaviorSnapshot(), 'Executing');
        await waitFor(`[BodySkillUI] terminal=Completed execution=${rebuiltActive.executionId}`);

        for (const [status, executionId] of [
            ['Completed', activeBehavior.executionId],
            ['Cancelled', naturalActive.executionId],
            ['Completed', naturalRecovered.executionId],
            ['Expired', timeoutActive.executionId],
            ['Cancelled', reenableActive.executionId],
            ['Completed', afterReenableActive.executionId],
            ['RecoveryFailed', modelLossActive.executionId],
            ['Completed', rebuiltActive.executionId]
        ]) {
            const marker = `[BodySkillUI] terminal=${status} execution=${executionId}`;
            if (logText().split(marker).length - 1 !== 1)
                throw new Error(`expected one terminal UI marker: ${marker}`);
        }

        console.log(JSON.stringify({
            skillId,
            activeBehavior,
            completedBehavior,
            naturalActive,
            naturalCancelled,
            naturalRecovered,
            timeoutDone,
            reenableDone,
            modelLossDone,
            rebuiltActive,
            activeLife,
            completedLife,
            root
        }));
    }
    finally {
        await stopOwnedPlayer(player);
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
