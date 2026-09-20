'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { once } = require('events');
const { spawn } = require('child_process');

const exe = process.argv[2];
const root = path.join(os.tmpdir(), 'fuxuan_wave_matrix_cycle_20260919');
const inbox = path.join(root, 'inbox.txt');
const logPath = path.join(root, 'logs', 'player_log.txt');
const presets = ['arm94', 'arm97', 'arm118', 'body-head'];
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

    const processHandle = spawn(exe, [], {
        env: { ...process.env, FU_XUAN_DATA: root },
        stdio: 'ignore',
    });

    try {
        await waitFor('NativeLive2DOverlay] sync visible');
        await sleep(3000);
        await sendForOnePoll('@@sim:idle-actions:off');
        await sendForOnePoll('@@sim:walk:stop');
        await sendForOnePoll('@@sim:status');
        await waitFor('velocity=(0,0)');

        const startOffset = logText().length;
        await sendForOnePoll('@@sim:gesture:wave-matrix:cycle');
        await waitFor('[WaveMatrixTest] cycle-command-accepted', startOffset);

        let offset = startOffset;
        for (const preset of presets) {
            await waitFor(`[WaveMatrixTest] cycle-preset: ${preset}`, offset, 30000);
            offset = logText().length;
            await waitFor(`[WaveMatrixTest] preset-completed: cycle-${preset}`, offset, 30000);
            offset = logText().length;
        }
        console.log('wave-matrix-cycle-completed-one-round');

        const cancelOffset = logText().length;
        await sendForOnePoll('@@sim:gesture:wave-matrix:cancel');
        await waitFor('[WaveMatrixTest] cancel-command-accepted', cancelOffset, 10000);
        await waitFor('[WaveMatrixTest] cleanup: wave-matrix-cancelled', cancelOffset, 10000);
        console.log('wave-matrix-cycle-cancel-cleanup');

        const quitOffset = logText().length;
        await sendForOnePoll('@@test:quit');
        await waitFor('[EmbodiedSafeRecovery] recovered: test-exit', quitOffset, 10000);
        await waitFor('[Live2DRenderer] test-exit input cleanup completed', quitOffset, 10000);
        console.log('wave-matrix-cycle-exit-recovery');

        await Promise.race([once(processHandle, 'exit'), sleep(8000)]);
    } finally {
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
