'use strict';

/*
 * Isolated runtime driver for cancellation of an active certified motion during
 * the existing test-exit flow. The supplied curve must match a registered hash.
 * Usage: node scripts/test/certified_motion_exit_cleanup_drive.cjs <DesktopPet.exe> <skill-id> <curve.json>
 *
 * Isolated root: every run atomically creates its own directory with
 * fs.mkdtempSync under os.tmpdir(), prefixed with the recognizable
 * fuxuan_certified_exit_<skillId>_ so a run never reuses (and never pre-deletes)
 * a directory another run created. Argument validation stays ahead of directory
 * creation. On failure the root of THIS run is preserved and printed; on success
 * the root is deleted only after its ownership has been proven.
 */
const fs = require('fs');
const os = require('os');
const path = require('path');
const { once } = require('events');
const { spawn } = require('child_process');

const exe = process.argv[2];
const skillId = process.argv[3];
const curveFile = process.argv[4];
// The recognizable prefix is kept. The previous fixed name was
// `fuxuan_certified_exit_<skillId sanitized>` with no trailing underscore.
const slug = String(skillId || '').replace(/[^a-z0-9]/gi, '_');
const ROOT_PREFIX = `fuxuan_certified_exit_${slug}_`;
const TMP = path.resolve(os.tmpdir());
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

// Assigned only AFTER mkdtempSync succeeds, so an argument-validation failure can
// never claim to have preserved a root that was never created.
let root = null;
let rootRealPath = null;
let inbox = null;
let logPath = null;

function logText() {
    if (!logPath) return '';
    try { return fs.readFileSync(logPath, 'utf8'); }
    catch { return ''; }
}

// Ownership verdict: true = provably the directory this run created; null = already
// gone; string = the reason it cannot be proven. Anything but true keeps the scene
// and reports non-zero instead of force-deleting.
function ownRootVerdict() {
    if (root === null) return null;
    const abs = path.resolve(root);
    // Windows: the resolved absolute path must sit inside the expected Temp dir.
    if (path.dirname(abs) !== TMP) return `absolute path is not a direct child of ${TMP}`;
    if (!path.basename(abs).startsWith(ROOT_PREFIX)) return 'name does not carry this run\'s prefix';
    let stat;
    try { stat = fs.lstatSync(abs); } catch { return null; }
    if (stat.isSymbolicLink()) return 'root is a link now, not the directory this run created';
    if (!stat.isDirectory()) return 'root is no longer a directory';
    let real;
    try { real = fs.realpathSync(abs); }
    catch (error) { return `cannot resolve the real location (${error.code || error.message})`; }
    if (path.dirname(real) !== path.dirname(rootRealPath)) {
        return `real location is not a direct child of ${path.dirname(rootRealPath)}`;
    }
    if (!path.basename(real).startsWith(ROOT_PREFIX)) return 'real name does not carry this run\'s prefix';
    if (real !== rootRealPath) return 'real location is no longer the directory this run created';
    return true;
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

    // Validation is complete; only now does this run create its own directory.
    root = fs.mkdtempSync(path.join(TMP, ROOT_PREFIX));
    rootRealPath = fs.realpathSync(root);
    inbox = path.join(root, 'inbox.txt');
    logPath = path.join(root, 'logs', 'player_log.txt');

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
    const verdict = ownRootVerdict();
    if (verdict === null) return;
    if (verdict !== true) {
        console.error('refusing to delete the test root: ' + verdict);
        console.error('preserved test root: ' + root);
        process.exitCode = 1;
        return;
    }
    fs.rmSync(root, { recursive: true, force: true });
}).catch(error => {
    console.error(error.message);
    if (root === null) {
        // Validation failed before any directory was created: this run has no root
        // to preserve, and must not pretend otherwise.
        console.error('no test root was created; nothing to preserve');
    } else {
        console.error('preserved test root: ' + root);
        console.error('player log tail:\n' + logText().split(/\r?\n/).slice(-80).join('\n'));
    }
    process.exitCode = 1;
});
