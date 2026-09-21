'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const exe = process.argv[2];
if (!exe || !fs.existsSync(exe)) throw new Error('usage: node desktop_physics_lifecycle_drive.cjs <DesktopPet.exe>');

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'fuxuan_desktop_physics_'));
const inbox = path.join(root, 'inbox.txt');
const playerLog = path.join(root, 'logs', 'player_log.txt');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const readLog = () => { try { return fs.readFileSync(playerLog, 'utf8'); } catch (_) { return ''; } };

async function waitFor(marker, timeout = 90000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (readLog().includes(marker)) return;
    await sleep(200);
  }
  throw new Error('timeout: ' + marker);
}

async function send(command, wait = 900) {
  fs.writeFileSync(inbox, command, 'utf8');
  await sleep(wait);
  fs.writeFileSync(inbox, '', 'utf8');
  await sleep(80);
}

async function sendAndWait(command, marker, timeout = 15000) {
  const before = readLog().length;
  await send(command);
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const text = readLog();
    if (text.slice(before).includes(marker)) return text.slice(before);
    await sleep(150);
  }
  throw new Error('timeout: ' + marker + ' after ' + command);
}

function latestDesktopState() {
  const matches = [...readLog().matchAll(/\[DesktopState\] version=(\d+) mode=([^ ]+) x=(-?\d+) y=(-?\d+) velocity=\((-?\d+),(-?\d+)\) onGround=(True|False) dragging=(True|False) paused=(True|False) actionLocked=(True|False) groundTask=([^\r\n]+)/g)];
  if (!matches.length) throw new Error('desktop-state snapshot missing');
  const m = matches[matches.length - 1];
  return {
    version: Number(m[1]), mode: m[2], x: Number(m[3]), y: Number(m[4]),
    velocityX: Number(m[5]), velocityY: Number(m[6]), onGround: m[7] === 'True',
    dragging: m[8] === 'True', paused: m[9] === 'True', actionLocked: m[10] === 'True',
    groundTask: m[11].trim()
  };
}

function assertNoBadLogs(text) {
  for (const marker of ['NullReferenceException', 'AssertionException', '[LifeState] event rejected']) {
    if (text.includes(marker)) throw new Error('unexpected log: ' + marker);
  }
}

(async () => {
  fs.writeFileSync(path.join(root, '.test_mode'), '');
  fs.writeFileSync(inbox, '');
  const child = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: root }, stdio: 'ignore' });
  let passed = false;
  try {
    await waitFor('[DesktopPet] 落地');
    await send('@@sim:idle-actions:off', 900);
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const initial = latestDesktopState();

    await sendAndWait('@@sim:walk:right', '[TestInbox] 已强制开始向右走');
    await sleep(500);
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const walking = latestDesktopState();
    if (walking.paused || walking.actionLocked || walking.velocityX === 0) throw new Error('walking state not observed');

    await sendAndWait('@@sim:pause:0', '[TestInbox] pause accepted');
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const paused = latestDesktopState();
    if (!paused.paused || paused.mode !== 'Paused') throw new Error('pause state not observed');
    const pausedVersion = paused.version;
    await sleep(600);
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const pausedAgain = latestDesktopState();
    if (!pausedAgain.paused || pausedAgain.version !== pausedVersion) throw new Error('paused physics state changed');

    await sendAndWait('@@sim:resume', '[TestInbox] resume accepted');
    await sleep(500);
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const resumed = latestDesktopState();
    if (resumed.paused) throw new Error('resume state remained paused');

    const actionOffset = readLog().length;
    await sendAndWait('@@sim:walk:stop', '[TestInbox] 已强制停止走路');
    await send('@@sim:idle-action:1', 700);
    const actionLog = readLog().slice(actionOffset);
    if (!actionLog.includes('[TestInbox] idle-action requested: 1')) throw new Error('idle action request not observed');

    await sendAndWait('@@sim:life-state', '[LifeState]', 15000);
    await sendAndWait('@@sim:life-timeline', '[LifeTimeline]', 15000);
    const preExitLog = readLog();
    await sendAndWait('@@test:quit', '[EmbodiedSafeRecovery] recovered: test-exit', 20000);
    if (child.exitCode === null) {
      await new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('player did not exit after @@test:quit')), 10000);
        child.once('exit', () => { clearTimeout(timer); resolve(); });
      });
    }

    const log = readLog();
    assertNoBadLogs(log);
    const lifeStates = [...log.matchAll(/\[LifeState\] snapshot version=(\d+).*?events=(\d+).*?bodyVersion=(\d+).*?observations=(\d+).*?faults=(\d+)/g)]
      .map(m => ({ version: Number(m[1]), events: Number(m[2]), bodyVersion: Number(m[3]), observations: Number(m[4]), faults: Number(m[5]) }));
    for (let i = 1; i < lifeStates.length; i++) {
      if (lifeStates[i].version < lifeStates[i - 1].version || lifeStates[i].events < lifeStates[i - 1].events || lifeStates[i].bodyVersion < lifeStates[i - 1].bodyVersion) {
        throw new Error('life-state counters regressed');
      }
    }
    if (!preExitLog.includes('[DesktopState]') || !preExitLog.includes('[LifeTimeline]')) throw new Error('read-only evidence markers missing');
    passed = true;
    console.log(JSON.stringify({ root, initial, walking, paused, resumed, lifeStateSamples: lifeStates.length }));
  } finally {
    if (child.exitCode === null) {
      try { fs.writeFileSync(inbox, '@@test:quit', 'utf8'); } catch (_) { /* best effort */ }
      await sleep(800);
      try { child.kill(); } catch (_) { /* owned test process only */ }
    }
    if (!passed) {
      console.error(readLog().slice(-16000));
      console.error('preserved test root: ' + root);
    } else {
      fs.rmSync(root, { recursive: true, force: true });
    }
  }
})().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
