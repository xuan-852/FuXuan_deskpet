'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { once } = require('events');
const { spawn } = require('child_process');

const exe = process.argv[2];
const root = path.join(os.tmpdir(), 'fuxuan_param94_walk_rejection_20260916');
const inbox = path.join(root, 'inbox.txt');
const logPath = path.join(root, 'logs', 'player_log.txt');
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

function logText() {
    try { return fs.readFileSync(logPath, 'utf8'); }
    catch { return ''; }
}

async function waitFor(marker, timeoutMs = 90000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (logText().includes(marker)) return;
        await sleep(150);
    }
    throw new Error(`timeout waiting for ${marker}`);
}

// RightPanel polls its test inbox every 250ms. Keep a command across several
// polls so a scheduling boundary cannot silently drop it.
async function send(command, holdMs = 900) {
    fs.writeFileSync(inbox, command, 'utf8');
    await sleep(holdMs);
    fs.writeFileSync(inbox, '', 'utf8');
    await sleep(100);
}

async function requestNormalQuit() {
    await send('@@test:quit');
}

(async () => {
    if (!exe || !fs.existsSync(exe)) throw new Error('missing DesktopPet.exe path');

    fs.rmSync(root, { recursive: true, force: true });
    fs.mkdirSync(root, { recursive: true });
    fs.writeFileSync(path.join(root, '.test_mode'), '');
    fs.writeFileSync(inbox, '');

    const processHandle = spawn(exe, [], {
        env: { ...process.env, FU_XUAN_DATA: root },
        stdio: 'ignore',
    });

    try {
        await waitFor('NativeLive2DOverlay] sync visible');
        console.log('startup-ready');
        // Overlay visibility is available before the initial gravity/drop
        // sequence ends. Movement assertions only make sense after landing.
        await sleep(3000);
        await send('@@sim:idle-actions:off');
        await send('@@sim:walk:right');
        await send('@@sim:status');
        await waitFor('velocity=(1,');
        console.log('walking-confirmed');
        await send('@@sim:gesture:param94');
        await waitFor('Param94 gate:');
        const result = logText();
        if (!result.includes('walking=True')) {
            throw new Error('candidate gate did not observe active locomotion');
        }
        if (!result.includes('[CandidateTest] rejected-static-gate')) {
            throw new Error('candidate gesture was not rejected while walking');
        }
        console.log('walking-rejection-confirmed');
    }
    finally {
        await requestNormalQuit().catch(() => {});
        await Promise.race([once(processHandle, 'exit'), sleep(8000)]);
    }

    console.log(root);
})().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
});
