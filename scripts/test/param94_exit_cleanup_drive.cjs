'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { once } = require('events');
const { spawn } = require('child_process');

const exe = process.argv[2];
const root = path.join(os.tmpdir(), 'fuxuan_param94_exit_cleanup_20260916');
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
        await sendAndWait('@@sim:gesture:param94', 'Accepted CandidateTest/generated-motion/param94-gesture');
        console.log('candidate-accepted');
        await sleep(500); // exit during the active 2.4 s candidate sequence
        const offset = logText().length;
        fs.writeFileSync(inbox, '@@test:quit', 'utf8');
        await waitFor('Released CandidateTest/generated-motion/param94-gesture', offset, 10000);
        await waitFor('candidate-test-before-test-exit', offset, 10000);
        await waitFor('[CandidateTest] cleanup: candidate-test-before-test-exit', offset, 10000);
        console.log('exit-cleanup-released');
        await Promise.race([once(processHandle, 'exit'), sleep(8000)]);
    }
    finally {
        // If setup failed before the exit request, preserve the same normal
        // cleanup rule used by every isolated player test.
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
