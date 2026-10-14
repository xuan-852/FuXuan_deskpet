'use strict';
/*
 * Isolated runtime driver for the walking -> certified-motion handoff.
 * Usage: node scripts/test/certified_motion_handoff_drive.cjs <DesktopPet.exe> <skill-id> <candidate.json>
 *
 * skillId is used BOTH as a directory-name component and as a file name, so it is
 * validated to /^[A-Za-z0-9_-]+$/ BEFORE any directory creation, any path-derived
 * file write and any spawn. Without that check a value such as `../../evil` makes
 * the derived root escape the intended prefix entirely (path.join collapses it),
 * so the pre-launch recursive delete and the curve copy would both land outside
 * this run's own directory.
 *
 * Isolated root: every run atomically creates its own directory with
 * fs.mkdtempSync under os.tmpdir(), keeping the recognizable prefix
 * fuxuan_certified_handoff_<skillId lowercased>_ so a run never reuses - and never
 * pre-deletes - a directory another run created. The root is deliberately KEPT on
 * success (this driver leaves its evidence in place) and is also kept on failure,
 * where its path is printed so the evidence can be found.
 */
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const exe = process.argv[2];
const skillId = process.argv[3];
const candidate = process.argv[4];
if (!exe || !fs.existsSync(exe)) throw new Error('missing exe');
if (!skillId || !candidate || !fs.existsSync(candidate)) throw new Error('missing skill or candidate');
// Charset gate: ASCII letters, digits, underscore and hyphen only. This runs before
// anything derives a path from skillId.
if (!/^[A-Za-z0-9_-]+$/.test(skillId)) {
  throw new Error('invalid skill id: must match /^[A-Za-z0-9_-]+$/');
}

// Assigned only AFTER mkdtempSync succeeds, so a validation failure can never claim
// to have preserved a root that was never created.
let root = null;
let inbox = null;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const log = () => {
  if (root === null) return '';
  try { return fs.readFileSync(path.join(root, 'logs', 'player_log.txt'), 'utf8'); } catch { return ''; }
};
async function waitFor(marker, timeout = 90000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (log().includes(marker)) return;
    await sleep(120);
  }
  throw new Error(`timeout: ${marker}`);
}
async function send(command, delay = 500) {
  fs.writeFileSync(inbox, command);
  await sleep(delay);
  fs.writeFileSync(inbox, '');
}

(async () => {
  // All validation has passed; only now does this run create its own directory.
  root = fs.mkdtempSync(path.join(path.resolve(os.tmpdir()), `fuxuan_certified_handoff_${skillId.toLowerCase()}_`));
  inbox = path.join(root, 'inbox.txt');
  fs.mkdirSync(path.join(root, 'certified_motions'), { recursive: true });
  fs.writeFileSync(path.join(root, '.test_mode'), '');
  fs.copyFileSync(candidate, path.join(root, 'certified_motions', `${skillId}.json`));
  fs.writeFileSync(inbox, '');
  const player = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: root }, stdio: 'ignore' });
  try {
    await waitFor('[DesktopPet] 落地');
    await send('@@sim:idle-actions:off');
    await send('@@sim:walk:right', 900);
    const walkingOffset = log().length;
    await send('@@sim:certified-motion:' + skillId);
    await waitFor('[TestInbox] certified-motion result:');
    const rejected = log().slice(walkingOffset);
    if (rejected.includes(`[CertifiedMotion] started: ${skillId}`) || !rejected.includes('当前未处于稳定静止状态，动作已拒绝')) {
      throw new Error('walking static-gate rejection not confirmed');
    }
    await send('@@sim:walk:stop', 1800);
    const actionOffset = log().length;
    await send('@@sim:certified-motion:' + skillId);
    await waitFor(`[CertifiedMotion] started: ${skillId}`);
    await waitFor('[EmbodiedRuntimeAdmission] released: certified-motion-completed');
    const actionLog = log().slice(actionOffset);
    for (const marker of [
      `[EmbodiedRuntimeAdmission] admitted: ${skillId}`,
      '[EmbodiedSafeRecovery] pose-restored',
      '[CertifiedMotion] cleanup: certified-motion-completed'
    ]) if (!actionLog.includes(marker)) throw new Error(`missing marker: ${marker}`);
    console.log('certified-motion-handoff-passed ' + root);
  } finally {
    fs.writeFileSync(inbox, '@@test:quit');
    await sleep(1000);
    if (!player.killed) player.kill();
  }
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
