'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { once } = require('events');
const { spawn } = require('child_process');

const exe = process.argv[2];
const candidateName = process.argv[3];
const candidates = {
    wave: {
        command: '@@sim:gesture:wave-candidate',
        started: '[WaveCandidateTest] started',
        cleanup: '[WaveCandidateTest] cleanup: wave-candidate-before-test-exit',
        released: 'Released CandidateTest/generated-motion/wave-candidate',
    },
    torso: {
        command: '@@sim:gesture:torso-z',
        started: '[TorsoCandidateTest] started',
        cleanup: '[TorsoCandidateTest] cleanup: torso-z-before-test-exit',
        released: 'Released CandidateTest/generated-motion/torso-z-gesture',
    },
};
const candidate = candidates[candidateName];
const root = path.join(os.tmpdir(), `fuxuan_${candidateName}_exit_cleanup_20260919`);
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
    if (!candidate) throw new Error('candidate must be wave or torso');

    fs.rmSync(root, { recursive: true, force: true });
    fs.mkdirSync(root, { recursive: true });
    fs.writeFileSync(path.join(root, '.test_mode'), '');
    fs.writeFileSync(inbox, '');
    const processHandle = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: root }, stdio: 'ignore' });

    try {
        await waitFor('NativeLive2DOverlay] sync visible');
        await sleep(3000);
        await sendForOnePoll('@@sim:idle-actions:off');
        await sendAndWait('@@sim:walk:stop', '[TestInbox]');
        await sendAndWait('@@sim:status', 'velocity=(0,0)');
        await sendAndWait(candidate.command, candidate.started);
        console.log(`${candidateName}-accepted`);
        await sleep(500);
        const offset = logText().length;
        fs.writeFileSync(inbox, '@@test:quit', 'utf8');
        await waitFor(candidate.released, offset, 10000);
        await waitFor(candidate.cleanup, offset, 10000);
        await waitFor('[EmbodiedSafeRecovery] recovered: test-exit', offset, 10000);
        console.log(`${candidateName}-exit-cleanup-released`);
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
})().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
});
