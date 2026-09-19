'use strict';

// Isolated runtime acceptance test for AC-INPUT-04.  The inbox commands are
// accepted only with .test_mode and exercise semantic leases, never raw params.
const fs = require('fs');
const os = require('os');
const path = require('path');
const { once } = require('events');
const { spawn } = require('child_process');

const exe = process.argv[2];
if (!exe || !fs.existsSync(exe)) throw new Error('missing DesktopPet.exe path');

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'fuxuan_live2d_input_conflict_'));
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

async function sendAndWait(command, marker, timeoutMs) {
    const offset = readLog().length;
    fs.writeFileSync(inbox, command, 'utf8');
    try {
        if (marker) await waitFor(marker, offset, timeoutMs);
        else await sleep(500);
    }
    finally { fs.writeFileSync(inbox, '', 'utf8'); }
}

async function assertAbsent(marker, after, durationMs = 700) {
    const deadline = Date.now() + durationMs;
    while (Date.now() < deadline) {
        if (readLog().slice(after).includes(marker)) throw new Error(`unexpected marker: ${marker}`);
        await sleep(60);
    }
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

        await sendAndWait('@@sim:expression:happy', '[TestInbox] expression accepted: happy');
        await sendAndWait('@@sim:expression:missing', '[TestInbox] expression rejected: missing');
        await sendAndWait('@@sim:expression:stop', '[TestInbox] expression stop requested');
        await sendAndWait('@@sim:expression:sad', '[TestInbox] expression accepted: sad');
        await sendAndWait('@@sim:expression:stop', '[TestInbox] expression stop requested');

        await sendAndWait('@@sim:lease:generated:begin', 'generated-motion lease accepted');
        const generatedExpressionOffset = readLog().length;
        await sendAndWait('@@sim:expression:happy', '[TestInbox] expression rejected: happy');
        await assertAbsent('[TestInbox] expression accepted: happy', generatedExpressionOffset);
        await sendAndWait('@@sim:lease:generated:release', 'generated-motion lease released');

        await sendAndWait('@@sim:expression:happy', '[TestInbox] expression accepted: happy');
        const expressionLegacyOffset = readLog().length;
        await sendAndWait('@@sim:legacy:stretch', 'legacy action requested: stretch');
        await sendAndWait('@@sim:expression:stop', '[TestInbox] expression stop requested');
        const recoveredLegacyOffset = readLog().length;
        await sendAndWait('@@sim:legacy:stretch', 'Accepted LegacyAction/legacy-action/stretch');
        await waitFor('Released LegacyAction/legacy-action/stretch', recoveredLegacyOffset, 15000);

        await sendAndWait('@@sim:lease:generated:begin', 'generated-motion lease accepted');
        const generatedLegacyOffset = readLog().length;
        await sendAndWait('@@sim:legacy:stretch', 'legacy action requested: stretch');
        await assertAbsent('Accepted LegacyAction/legacy-action/stretch', generatedLegacyOffset);
        await sendAndWait('@@sim:lease:generated:release', 'generated-motion lease released');

        await sendAndWait('@@sim:legacy:stretch', 'Accepted LegacyAction/legacy-action/stretch');
        const activeLegacyOffset = readLog().length;
        await sendAndWait('@@sim:lease:generated:begin', 'generated-motion lease rejected');
        await waitFor('Released LegacyAction/legacy-action/stretch', activeLegacyOffset, 15000);

        fs.writeFileSync(path.join(root, 'ac-input-04-result.json'), JSON.stringify({
            acceptanceId: 'AC-INPUT-04',
            status: 'passed',
            generatedThenLegacy: 'legacy request did not acquire LegacyAction lease',
            legacyThenGenerated: 'generated request was rejected while LegacyAction lease was active',
        }, null, 2));

        await sendAndWait('@@sim:expression:happy', '[TestInbox] expression accepted: happy');
        await sendAndWait('@@test:quit', 'test-exit input cleanup completed', 15000);
        fs.writeFileSync(path.join(root, 'ac-expression-lifecycle-result.json'), JSON.stringify({
            acceptanceId: 'AC-EXPRESSION-LIFECYCLE',
            status: 'passed',
            unknownExpressionRejected: true,
            stopThenReuse: true,
            generatedConflictRejected: true,
            legacyConflictRecovered: true,
            testExitCleanup: true,
        }, null, 2));
        console.log(`AC-INPUT-04 and AC-EXPRESSION-LIFECYCLE passed: ${root}`);
    } finally {
        fs.writeFileSync(inbox, '@@test:quit', 'utf8');
        await Promise.race([once(processHandle, 'exit'), sleep(15000)]);
        fs.writeFileSync(inbox, '', 'utf8');
    }
})().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
});
