'use strict';

// Isolated semantic regression for click visual ownership.  It uses the test
// inbox only; it never drives the OS mouse or sends raw parameter values.
const fs = require('fs');
const os = require('os');
const path = require('path');
const { once } = require('events');
const { spawn } = require('child_process');

const exe = process.argv[2];
if (!exe || !fs.existsSync(exe)) throw new Error('missing DesktopPet.exe path');

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'fuxuan_click_pose_ownership_'));
const inbox = path.join(root, 'inbox.txt');
const logPath = path.join(root, 'logs', 'player_log.txt');
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

function readLog() {
    try { return fs.readFileSync(logPath, 'utf8'); }
    catch { return ''; }
}

async function waitFor(marker, after = 0, timeoutMs = 90000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (readLog().slice(after).includes(marker)) return;
        await sleep(60);
    }
    throw new Error(`timeout waiting for ${marker}`);
}

async function sendAndWait(command, marker, timeoutMs = 90000) {
    const offset = readLog().length;
    fs.writeFileSync(inbox, command, 'utf8');
    try {
        if (marker) await waitFor(marker, offset, timeoutMs);
        else await sleep(500);
        return offset;
    }
    finally { fs.writeFileSync(inbox, '', 'utf8'); }
}

async function assertAbsent(marker, after, durationMs = 900) {
    const deadline = Date.now() + durationMs;
    while (Date.now() < deadline) {
        if (readLog().slice(after).includes(marker)) throw new Error(`unexpected marker: ${marker}`);
        await sleep(60);
    }
}

async function beginGeneratedLease() {
    for (let attempt = 0; attempt < 4; attempt++) {
        const offset = await sendAndWait('@@sim:lease:generated:begin', null);
        const delta = readLog().slice(offset);
        if (delta.includes('generated-motion lease accepted')) return;
        if (!delta.includes('generated-motion lease rejected'))
            throw new Error('generated lease command produced no result');
        await sleep(1600);
    }
    throw new Error('generated-motion lease remained unavailable after retries');
}

(async () => {
    fs.writeFileSync(path.join(root, '.test_mode'), '');
    fs.writeFileSync(inbox, '', 'utf8');
    const processHandle = spawn(exe, [], {
        env: { ...process.env, FU_XUAN_DATA: root },
        stdio: 'ignore',
    });

    try {
        await waitFor('DesktopPet] 落地');
        await sendAndWait('@@sim:idle-actions:off', null);
        await sendAndWait('@@sim:walk:stop', null);
        await sleep(1200);

        // No lease: the normal visual click path remains available.
        const idleClickOffset = await sendAndWait('@@sim:click:center', '[DragHandler] 模拟点击');
        await waitFor('[Live2DRenderer] 点击部位:', idleClickOffset);
        await sendAndWait('@@sim:life-state', 'attentionReason=direct-click');
        await sendAndWait('@@sim:life-timeline', 'state=click');

        // A generated-motion lease suppresses only the visual overlay; the
        // click event still reaches the attention/life-state chain. Reassert
        // the idle baseline because autonomous walking can reacquire its own
        // lease after the first click pause expires.
        await sleep(1300);
        await sendAndWait('@@sim:walk:stop', '[TestInbox] 已强制停止走路');
        await sleep(1600);
        await sendAndWait('@@sim:status', 'velocity=(0,0)');
        await beginGeneratedLease();
        const conflictClickOffset = await sendAndWait('@@sim:click:center', '[DragHandler] 模拟点击');
        await waitFor('[Live2DRenderer] 点击姿势已抑制: active-input-lease', conflictClickOffset);
        await sendAndWait('@@sim:life-state', 'attentionReason=direct-click');
        await sendAndWait('@@sim:life-timeline', 'state=click');
        await assertAbsent('[Live2DRenderer] 点击部位:', conflictClickOffset);

        // The lease is released before the next click; the new click may apply
        // normally and is not a replay of the suppressed click.
        await sendAndWait('@@sim:lease:generated:release', 'generated-motion lease released');
        // Releasing the test lease lets the normal walking owner reacquire its
        // lease. Stop it explicitly and wait for a stable idle window before
        // testing that a later click can apply again.
        await sendAndWait('@@sim:walk:stop', '[TestInbox] 已强制停止走路');
        await sleep(1600);
        await sendAndWait('@@sim:status', 'velocity=(0,0)');
        const recoveredClickOffset = await sendAndWait('@@sim:click:center', '[DragHandler] 模拟点击');
        await waitFor('[Live2DRenderer] 点击部位:', recoveredClickOffset);
        await assertAbsent('[Live2DRenderer] 点击姿势已抑制: active-input-lease', recoveredClickOffset);

        fs.writeFileSync(path.join(root, 'click-pose-ownership-result.json'), JSON.stringify({
            status: 'passed',
            idleClickVisualApplied: true,
            activeLeaseSuppressesClickVisual: true,
            directInteractionPreservedDuringSuppression: true,
            clickVisualAvailableAfterRelease: true,
            simulatedInputOnly: true,
        }, null, 2));

        await sendAndWait('@@test:quit', 'test-exit input cleanup completed', 15000);
        console.log(`click pose ownership passed: ${root}`);
    } finally {
        fs.writeFileSync(inbox, '@@test:quit', 'utf8');
        await Promise.race([once(processHandle, 'exit'), sleep(15000)]);
        fs.writeFileSync(inbox, '', 'utf8');
    }
})().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
});
