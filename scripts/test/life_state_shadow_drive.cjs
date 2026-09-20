'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const exe = process.argv[2];
if (!exe || !fs.existsSync(exe)) throw new Error('usage: node life_state_shadow_drive.cjs <DesktopPet.exe>');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'fuxuan_life_state_'));
const inbox = path.join(root, 'inbox.txt');
const logPath = () => path.join(root, 'logs', 'player_log.txt');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const readLog = () => { try { return fs.readFileSync(logPath(), 'utf8'); } catch (_) { return ''; } };
async function send(command, wait = 1200) {
  fs.writeFileSync(inbox, command);
  await sleep(wait);
  fs.writeFileSync(inbox, '');
  await sleep(80);
}
async function waitFor(marker, timeout = 90000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    if (readLog().includes(marker)) return;
    await sleep(200);
  }
  throw new Error('timeout: ' + marker);
}
function waitForExit(child, timeout = 10000) {
  if (child.exitCode !== null) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('player did not exit after @@test:quit')), timeout);
    child.once('exit', () => {
      clearTimeout(timer);
      resolve();
    });
  });
}
function snapshot() {
  const matches = [...readLog().matchAll(/\[LifeState\] snapshot version=(\d+).*?presence=([^ ]+).*?activity=([^ ]+).*?attentionTarget=([^ ]+).*?attentionSource=([^ ]+).*?attentionConfidence=([^ ]+).*?attentionReason=([^ ]+).*?action=([^ ]+).*?events=(\d+).*?bodyVersion=(\d+).*?health=([^ ]+).*?observations=(\d+).*?faults=(\d+).*?ready=(True|False).*?progressing=(True|False)/g)];
  if (!matches.length) throw new Error('life-state snapshot missing');
  const m = matches[matches.length - 1];
  return { version: +m[1], presence: m[2], activity: m[3], attentionTarget: m[4], attentionSource: m[5], attentionConfidence: m[6], attentionReason: m[7], action: m[8], events: +m[9], bodyVersion: +m[10], health: m[11], observations: +m[12], faults: +m[13], ready: m[14], progressing: m[15] };
}
(async () => {
  fs.writeFileSync(path.join(root, '.test_mode'), '');
  fs.writeFileSync(inbox, '');
  const before = {};
  for (const name of ['pet_memory.json', 'pet_personality.json', 'motion_memory.json', 'activity.json', 'validation.json']) {
    const file = path.join(process.env.LOCALAPPDATA || '', 'FuXuan', 'DesktopPetData', name);
    before[name] = fs.existsSync(file) ? fs.readFileSync(file).toString('base64') : null;
  }
  const child = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: root }, stdio: 'ignore' });
  let passed = false;
  try {
    await waitFor('[DesktopPet] 落地');
    await sleep(2000);
    await send('@@sim:life-state');
    const first = snapshot();
    if (!['pet', 'none'].includes(first.attentionTarget) || !['test', 'none'].includes(first.attentionSource) || !['pet-click', 'none'].includes(first.attentionReason)) throw new Error('invalid attention summary');
    await send('@@sim:idle-actions:off');
    await send('@@sim:walk:stop');
    await sleep(1200);
    await send('@@sim:life-state');
    const second = snapshot();
    if (second.version < first.version || second.events < first.events || second.bodyVersion < first.bodyVersion) throw new Error('snapshot counters regressed');
    if (!['pet', 'none'].includes(second.attentionTarget) || !['test', 'none'].includes(second.attentionSource) || !['pet-click', 'none'].includes(second.attentionReason)) throw new Error('invalid attention summary after query');
    await send('@@test:quit', 1200);
    await waitForExit(child);
    const log = readLog();
    for (const bad of ['[LifeState] event rejected', 'NullReferenceException', 'AssertionException']) if (log.includes(bad)) throw new Error('unexpected log: ' + bad);
    for (const name of Object.keys(before)) {
      const file = path.join(process.env.LOCALAPPDATA || '', 'FuXuan', 'DesktopPetData', name);
      const after = fs.existsSync(file) ? fs.readFileSync(file).toString('base64') : null;
      if (after !== before[name]) throw new Error('production data changed: ' + name);
    }
    console.log(JSON.stringify({ root, first, second }));
    passed = true;
  } finally {
    if (!child.killed) child.kill();
    if (!passed) {
      try { console.error(readLog().slice(-12000)); } catch (_) { /* preserve original failure */ }
      console.error('preserved test root: ' + root);
    } else {
      fs.rmSync(root, { recursive: true, force: true });
    }
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
