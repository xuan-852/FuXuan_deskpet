/* Records an isolated, timed walk sequence for perceptibility calibration.
 * Usage: node scripts/test/walk_baseline_drive.cjs [DesktopPet.exe]
 */
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const exe = process.argv[2] || path.resolve('Build/DesktopPet.exe');
const root = path.join(os.tmpdir(), 'fuxuan_walk_baseline_20260916');
const inbox = path.join(root, 'inbox.txt');
const logPath = path.join(root, 'logs', 'player_log.txt');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
function requireTemp(target) {
  const temp = path.resolve(os.tmpdir()); const resolved = path.resolve(target);
  if (!resolved.startsWith(temp + path.sep)) throw new Error('Refusing non-temporary test root.');
}
function readLog() { try { return fs.readFileSync(logPath, 'utf8'); } catch { return ''; } }
async function waitFor(marker, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) { if (readLog().includes(marker)) return; await sleep(300); }
  throw new Error('Timed out waiting for: ' + marker);
}
async function send(command, delayMs) {
  fs.writeFileSync(inbox, command, 'utf8');
  await sleep(delayMs);
  fs.writeFileSync(inbox, '', 'utf8');
  await sleep(80);
}
async function waitForShotCount(expected, timeoutMs) {
  const shotDir = path.join(root, 'test_screenshots');
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const count = fs.existsSync(shotDir) ? fs.readdirSync(shotDir).filter(name => name.endsWith('.png')).length : 0;
    if (count >= expected) return;
    await sleep(100);
  }
  throw new Error('Timed out waiting for screenshot ' + expected + '.');
}

(async () => {
  if (!fs.existsSync(exe)) throw new Error('DesktopPet executable not found: ' + exe);
  requireTemp(root);
  fs.rmSync(root, { recursive: true, force: true });
  fs.mkdirSync(root, { recursive: true });
  fs.writeFileSync(path.join(root, '.test_mode'), '');
  fs.writeFileSync(inbox, '');
  const child = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: root }, detached: false, stdio: 'ignore' });
  try {
    await waitFor('[DesktopPet] 落地', 90000);
    await send('@@sim:idle-actions:off', 300);
    // An already-started idle effect can briefly resize the overlay RT while it
    // is reset. Do not begin a measurement until that render target has settled.
    await sleep(2500);
    await send('@@sim:walk:right', 500);
    for (let i = 0; i < 16; i++) {
      // Retain the fixed render-target canvas: cropped visual-review snapshots
      // are intentionally unsuitable for temporal measurement.
      await send('@@sim:model-measurement-snapshot', 500);
      await waitForShotCount(i + 1, 2500);
    }
    await send('@@sim:walk:stop', 400);
    await send('@@sim:model-measurement-snapshot', 500);
    await waitForShotCount(17, 2500);
    await send('@@test:quit', 1200);
    const shotDir = path.join(root, 'test_screenshots');
    const shots = fs.existsSync(shotDir) ? fs.readdirSync(shotDir).filter(name => name.endsWith('.png')).sort() : [];
    if (shots.length !== 17) throw new Error('Expected 17 walk frames, found ' + shots.length);
    console.log('Walk baseline written: ' + root);
    console.log('Frames: ' + shots.length);
  } finally {
    if (!child.killed) child.kill();
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
