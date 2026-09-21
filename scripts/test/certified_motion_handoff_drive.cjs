'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const exe = process.argv[2];
const skillId = process.argv[3];
const candidate = process.argv[4];
if (!exe || !fs.existsSync(exe)) throw new Error('missing exe');
if (!skillId || !candidate || !fs.existsSync(candidate)) throw new Error('missing skill or candidate');

const root = path.join(os.tmpdir(), `fuxuan_certified_handoff_${skillId.toLowerCase()}`);
const inbox = path.join(root, 'inbox.txt');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const log = () => { try { return fs.readFileSync(path.join(root, 'logs', 'player_log.txt'), 'utf8'); } catch { return ''; } };
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
  fs.rmSync(root, { recursive: true, force: true });
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
})().catch(error => { console.error(error.message); process.exitCode = 1; });
