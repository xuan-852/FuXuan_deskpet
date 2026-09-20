'use strict';

/*
 * Isolated runtime driver for cancellation of an active certified motion during
 * the existing test-exit flow. The supplied curve must match a registered hash.
 * Usage: node scripts/test/certified_motion_exit_cleanup_drive.cjs <DesktopPet.exe> <skill-id> <curve.json>
 */
const fs = require('fs');
const os = require('os');
const path = require('path');
const { once } = require('events');
const { spawn } = require('child_process');

const exe = process.argv[2];
const skillId = process.argv[3];
const curveFile = process.argv[4];
const root = path.join(os.tmpdir(), `fuxuan_certified_exit_${String(skillId || '').replace(/[^a-z0-9]/gi, '_')}`);
const inbox = path.join(root, 'inbox.txt');
const logPath = path.join(root, 'logs', 'player_log.txt');
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

function logText() {
    try { return fs.readFileSync(logPath, 'utf8'); }
    catch { return ''; }
}

async function waitFor(marker, after = 0, timeoutMs = 90000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (logText().slice(after).includes(marker)) return;
        await sleep(50);
    }
    throw new Error(`timeout waiting for ${marker}`);
}

async function sendAndWait(command, marker) {
    const offset = logText().length;
    fs.writeFileSync(inbox, command, 'utf8');
    try { await waitFor(marker, offset); }
    finally { fs.writeFileSync(inbox, '', 'utf8'); }
}

async function sendForOnePoll(command) {
    fs.writeFileSync(inbox, command, 'utf8');
    await sleep(500);
    fs.writeFileSync(inbox, '', 'utf8');
}

(async () => {
    if (!exe || !fs.existsSync(exe)) throw new Error('missing DesktopPet.exe path');
    if (!skillId || !/^[a-z0-9_-]+$/i.test(skillId)) throw new Error('invalid skill id');
    if (!curveFile || !fs.existsSync(curveFile)) throw new Error('missing registered curve json');

    fs.rmSync(root, { recursive: true, force: true });
    fs.mkdirSync(path.join(root, 'certified_motions'), { recursive: true });
    fs.writeFileSync(path.join(root, '.test_mode'), '');
    fs.copyFileSync(curveFile, path.join(root, 'certified_motions', `${skillId}.json`));
    fs.writeFileSync(inbox, '');
    const processHandle = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: root }, stdio: 'ignore' });

    try {
        await waitFor('[DesktopPet] 落地');
        await sendForOnePoll('@@sim:idle-actions:off');
        await sendAndWait('@@sim:walk:stop', '[TestInbox]');
        await sendAndWait('@@sim:status', 'velocity=(0,0)');
        await sendAndWait(`@@sim:certified-motion:${skillId}`, `[CertifiedMotion] started: ${skillId}`);
        console.log('certified-motion-accepted');

        const offset = logText().length;
        fs.writeFileSync(inbox, '@@test:quit', 'utf8');
        await waitFor('[EmbodiedSafeRecovery] pose-restored:', offset, 10000);
        await waitFor('Released GeneratedMotion/certified-motion', offset, 10000);
        await waitFor('[EmbodiedRuntimeAdmission] cancelled: certified-motion-before-test-exit', offset, 10000);
        await waitFor('[CertifiedMotion] cleanup: certified-motion-before-test-exit', offset, 10000);
        await waitFor('[EmbodiedSafeRecovery] recovered: test-exit', offset, 10000);
        console.log('certified-motion-exit-cleanup-released');
        await Promise.race([once(processHandle, 'exit'), sleep(8000)]);
    }
    finally {
        if (!logText().includes('开始退出清理')) {
            fs.writeFileSync(inbox, '@@test:quit', 'utf8');
            await sleep(900);
            fs.writeFileSync(inbox, '', 'utf8');
            await Promise.race([once(processHandle, 'exit'), sleep(8000)]);
        }
    }

    console.log(root);
})().then(() => {
    fs.rmSync(root, { recursive: true, force: true });
}).catch(error => {
    console.error(error.message);
    console.error('preserved test root: ' + root);
    console.error('player log tail:\n' + logText().split(/\r?\n/).slice(-80).join('\n'));
    process.exitCode = 1;
});
