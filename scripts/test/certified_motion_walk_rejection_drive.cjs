'use strict';
/*
 * Isolated runtime driver for the certified-motion walking / airborne rejection gates.
 * Usage: node scripts/test/certified_motion_walk_rejection_drive.cjs <DesktopPet.exe> <skill-id> <candidate.json>
 *
 * skillId is used BOTH as a directory-name component and as the curve copy's file
 * name, so it is validated to /^[A-Za-z0-9_-]+$/ BEFORE any directory creation, any
 * copy and any spawn. Without that check the raw skillId reaches
 * path.join(root, 'certified_motions', `${skillId}.json`), where path.join collapses
 * `..`: skillId=../../evil lands the copy at %TEMP%\evil.json (outside this run's
 * root) and ../../../evil lands it outside %TEMP% altogether.
 *
 * Isolated root: the directory is now allocated atomically with fs.mkdtempSync under
 * os.tmpdir(), keeping the recognizable prefix
 * fuxuan_certified_motion_rejection_<skillId lowercased>_ . Uniqueness comes from
 * mkdtemp's own atomic retry loop, not from a pid/timestamp name, and mkdtemp never
 * returns an existing directory - so a run never reuses and never pre-deletes one.
 * The root is deliberately KEPT on success (this driver leaves its evidence in place)
 * and is also kept on failure, where its path is printed so the evidence can be found.
 */

const fs = require('fs');
const os = require('os');
const path = require('path');
const { once } = require('events');
const { spawn } = require('child_process');

const [exe, skillId, candidateFile] = process.argv.slice(2);
if (!exe || !fs.existsSync(exe)) throw new Error('missing DesktopPet.exe path');
if (!skillId || !candidateFile || !fs.existsSync(candidateFile)) throw new Error('missing skill id or candidate json');
// Charset gate: ASCII letters, digits, underscore and hyphen only. This runs before
// anything derives a path from skillId.
if (!/^[A-Za-z0-9_-]+$/.test(skillId)) {
    throw new Error('invalid skill id: must match /^[A-Za-z0-9_-]+$/');
}

// Assigned only AFTER mkdtempSync succeeds, so a failure before creation can never
// claim to have preserved a root that was never created.
let root = null;
let inbox = null;
let logPath = null;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const log = () => {
    if (logPath === null) return '';
    try { return fs.readFileSync(logPath, 'utf8'); } catch { return ''; }
};

async function waitFor(marker, after = 0, timeoutMs = 90000) {
    for (const end = Date.now() + timeoutMs; Date.now() < end; await sleep(100))
        if (log().slice(after).includes(marker)) return;
    throw new Error(`timeout waiting for ${marker}`);
}

async function send(command, holdMs = 500) {
    fs.writeFileSync(inbox, command, 'utf8');
    await sleep(holdMs);
    fs.writeFileSync(inbox, '', 'utf8');
    await sleep(60);
}

(async () => {
    // All validation has passed; only now does this run create its own directory.
    root = fs.mkdtempSync(path.join(path.resolve(os.tmpdir()), `fuxuan_certified_motion_rejection_${skillId.toLowerCase()}_`));
    inbox = path.join(root, 'inbox.txt');
    logPath = path.join(root, 'logs', 'player_log.txt');
    fs.mkdirSync(path.join(root, 'certified_motions'), { recursive: true });
    fs.writeFileSync(path.join(root, '.test_mode'), '');
    fs.copyFileSync(candidateFile, path.join(root, 'certified_motions', `${skillId}.json`));
    fs.writeFileSync(inbox, '');
    const processHandle = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: root }, stdio: 'ignore' });
    try {
        await waitFor('NativeLive2DOverlay] sync visible');
        // The overlay becomes visible before the inbox poller is fully ready.
        await sleep(2500);
        await send('@@sim:idle-actions:off');

        const airborneOffset = log().length;
        await send(`@@sim:airborne-zero-velocity:${skillId}`);
        await waitFor('[TestInbox] airborne-zero-velocity result:', airborneOffset);
        const airborneLog = log().slice(airborneOffset);
        if (!airborneLog.includes('velocity=(0,0) onGround=false'))
            throw new Error('airborne zero-velocity fixture was not confirmed');
        if (!airborneLog.includes('当前未处于稳定静止状态，动作已拒绝'))
            throw new Error('certified motion did not reject airborne zero-velocity state');
        if (airborneLog.includes(`[CertifiedMotion] started: ${skillId}`))
            throw new Error('certified motion started while airborne');
        if (airborneLog.includes(`[EmbodiedRuntimeAdmission] admitted: ${skillId}`))
            throw new Error('airborne request reached embodied runtime admission');
        if (airborneLog.includes(`[Live2DInputCoordinator] Accepted GeneratedMotion/generated-motion/certified-motion:${skillId}`))
            throw new Error('airborne request acquired the certified-motion lease');
        console.log('airborne-zero-velocity-rejection-confirmed');
        await waitFor('[DesktopPet] 落地', airborneOffset);
        await send('@@sim:walk:stop');
        await sleep(500);

        const groundedOffset = log().length;
        await send(`@@sim:certified-motion:${skillId}`);
        await waitFor(`[CertifiedMotion] started: ${skillId}`, groundedOffset);
        await waitFor('[CertifiedMotion] cleanup: certified-motion-completed', groundedOffset);
        console.log('post-landing-motion-accepted');

        await send('@@sim:walk:right');
        await send('@@sim:status');
        await waitFor('velocity=(1,');
        const offset = log().length;
        await send(`@@sim:certified-motion:${skillId}`);
        await waitFor('[TestInbox] certified-motion result:', offset);
        await sleep(800);
        const result = log().slice(offset);
        if (result.includes(`[CertifiedMotion] started: ${skillId}`))
            throw new Error('certified motion started while walking');
        if (!result.includes('当前未处于稳定静止状态，动作已拒绝'))
            throw new Error('certified motion did not report static-gate rejection');
        console.log('walking-rejection-confirmed');
    } finally {
        fs.writeFileSync(inbox, '@@test:quit', 'utf8');
        await Promise.race([once(processHandle, 'exit'), sleep(8000)]);
    }
    console.log(root);
})().catch(error => {
    console.error(error.message);
    if (root === null) {
        // Validation failed before any directory was created: there is no root of this
        // run to preserve, and this must not pretend otherwise.
        console.error('no test root was created; nothing to preserve');
    } else {
        // The root is intentionally kept on failure so the evidence stays retrievable.
        console.error('preserved test root: ' + root);
    }
    process.exitCode = 1;
});
