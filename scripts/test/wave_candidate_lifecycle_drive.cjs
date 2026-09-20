'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { once } = require('events');
const { spawn } = require('child_process');

const exe = process.argv[2];
const root = path.join(os.tmpdir(), `fuxuan_wave_candidate_lifecycle_${Date.now()}`);
const inbox = path.join(root, 'inbox.txt');
const logPath = path.join(root, 'logs', 'player_log.txt');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const readLog = () => { try { return fs.readFileSync(logPath, 'utf8'); } catch { return ''; } };

async function waitFor(marker, offset = 0, timeoutMs = 90000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (readLog().slice(offset).includes(marker)) return;
        await sleep(100);
    }
    throw new Error(`timeout waiting for ${marker}`);
}

async function command(text, marker, timeoutMs = 90000) {
    const offset = readLog().length;
    fs.writeFileSync(inbox, text, 'utf8');
    try { await waitFor(marker, offset, timeoutMs); }
    finally { fs.writeFileSync(inbox, '', 'utf8'); }
}

(async () => {
    if (!exe || !fs.existsSync(exe)) throw new Error('missing DesktopPet.exe path');
    fs.rmSync(root, { recursive: true, force: true });
    fs.mkdirSync(root, { recursive: true });
    fs.mkdirSync(path.dirname(logPath), { recursive: true });
    fs.writeFileSync(path.join(root, '.test_mode'), '');
    fs.writeFileSync(inbox, '');
    const child = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: root }, stdio: 'ignore' });
    let exited = false;
    child.once('exit', () => { exited = true; });
    try {
        await waitFor('NativeLive2DOverlay] sync visible');
        await command('@@sim:idle-actions:off', '[Live2DRenderer] 测试隔离：已暂停空闲动作调度');
        await command('@@sim:walk:stop', '[TestInbox] 已强制停止走路');
        await command('@@sim:gesture:wave-candidate', '[WaveCandidateTest] started');
        await sleep(11500);
        const activeLog = readLog();
        if ((activeLog.match(/Accepted CandidateTest\/generated-motion\/wave-candidate/g) || []).length !== 1)
            throw new Error('wave candidate acquired more than one lease');
        await command('@@sim:gesture:wave-candidate:cancel', '[WaveCandidateTest] cancel-command-accepted');
        await waitFor('[WaveCandidateTest] cleanup: wave-candidate-cancelled');
        await command('@@sim:gesture:wave-candidate', '[WaveCandidateTest] started');
        await sleep(700);
        await command('@@test:quit', '[EmbodiedSafeRecovery] recovered: test-exit', 20000);
        await Promise.race([once(child, 'exit'), sleep(10000)]);
        if (!exited) throw new Error('isolated player did not exit');
        const finalLog = readLog();
        if (finalLog.includes('NullReferenceException') || finalLog.includes('AssertionException'))
            throw new Error('runtime exception found in isolated player log');
        console.log(JSON.stringify({ ok: true, root, exited, productionIsolation: true }));
    } finally {
        if (!exited) {
            fs.writeFileSync(inbox, '@@test:quit', 'utf8');
            await sleep(1000);
            if (!exited) child.kill();
        }
    }
})().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
});
