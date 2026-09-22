'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const exe = process.argv[2];
if (!exe || !fs.existsSync(exe)) throw new Error('usage: node drag_response_player_drive.cjs <DesktopPet.exe>');

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'fuxuan_drag_response_'));
const inbox = path.join(root, 'inbox.txt');
const playerLog = path.join(root, 'logs', 'player_log.txt');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const readLog = () => { try { return fs.readFileSync(playerLog, 'utf8'); } catch (_) { return ''; } };

async function waitFor(marker, timeout = 90000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (readLog().includes(marker)) return;
    await sleep(150);
  }
  throw new Error('timeout: ' + marker);
}

async function waitForSince(marker, startOffset, timeout = 90000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (readLog().slice(startOffset).includes(marker)) return;
    await sleep(150);
  }
  throw new Error('timeout since offset: ' + marker);
}

async function send(command, wait = 700) {
  fs.writeFileSync(inbox, command, 'utf8');
  // RightPanel polls every 250ms. Keep the command available for two polls;
  // clearing sooner makes a valid command indistinguishable from rejection.
  await sleep(Math.max(wait, 650));
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

function latestState() {
  const matches = [...readLog().matchAll(/\[DesktopState\] version=(\d+) mode=([^ ]+) x=(-?\d+) y=(-?\d+) velocity=\((-?\d+),(-?\d+)\) onGround=(True|False) dragging=(True|False) paused=(True|False) actionLocked=(True|False) groundTask=([^\r\n]+)/g)];
  if (!matches.length) throw new Error('desktop-state snapshot missing');
  const m = matches[matches.length - 1];
  return { version: Number(m[1]), mode: m[2], x: Number(m[3]), y: Number(m[4]), velocityX: Number(m[5]), velocityY: Number(m[6]), onGround: m[7] === 'True', dragging: m[8] === 'True', paused: m[9] === 'True', actionLocked: m[10] === 'True', groundTask: m[11].trim() };
}

function latestThrow() {
  const matches = [...readLog().matchAll(/\[DragHandler\] 抛掷: \((-?\d+), (-?\d+)\)/g)];
  if (!matches.length) throw new Error('throw evidence missing');
  const m = matches[matches.length - 1];
  return { x: Number(m[1]), y: Number(m[2]) };
}

function latestStarArmState() {
  const matches = [...readLog().matchAll(/\[StarArmState\] star-arm dragging=(True|False)([^\r\n]*)/g)];
  if (!matches.length) throw new Error('star-arm snapshot missing');
  const m = matches[matches.length - 1];
  const values = { dragging: m[1] === 'True' };
  for (const pair of m[2].trim().split(/\s+/)) {
    const [name, raw] = pair.split('=');
    if (name && raw !== undefined) values[name] = Number(raw);
  }
  return values;
}

function hasExactStarSignature(values) {
  return values.Param94 >= 9 && values.Param97 >= 3.5 && values.Param99 <= -7
    && values.Param31 >= 0.9 && values.Param32 >= 0.9 && values.Param33 >= 0.9
    && values.Param95 >= 0.9 && values.Param117 >= 0.7;
}

async function waitForStarSignature(timeout = 12000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    await sendAndWait('@@sim:star-arm-state', '[StarArmState]', 5000);
    const state = latestStarArmState();
    if (hasExactStarSignature(state)) return state;
    await sleep(300);
  }
  throw new Error('star action never reached raised-arm signature');
}

function assertNoBadLogs(text) {
  for (const marker of [
    'NullReferenceException', 'MissingReferenceException', 'InvalidOperationException',
    'ArgumentException', 'AssertionException', '[LifeState] event rejected',
    'cleanup failure', 'Cleanup failed', 'Error:', 'Exception:'
  ]) {
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
    await send('@@sim:idle-actions:off');
    await sendAndWait('@@sim:walk:right', '[TestInbox] 已强制开始向右走');
    await sleep(500);
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const walkingBeforeDrag = latestState();
    if (walkingBeforeDrag.velocityX === 0) throw new Error('walking state was not active before drag');
    const initial = walkingBeforeDrag;

    const dragStart = readLog().length;
    await send('@@sim:drag:offset:220,-250,300', 100);
    await waitFor('[DragHandler] 模拟拖动开始', 10000);
    await waitFor('[DragHandler] 拖动已启动', 10000);
    const activeLog = readLog().slice(dragStart);
    if (!activeLog.includes('[DragHandoff] walking-to-drag accepted')) throw new Error('walking-to-drag handoff evidence missing');
    if (!activeLog.includes('drag-response')) throw new Error('drag-response admission evidence missing');
    await sendAndWait('@@sim:lease:generated:begin', 'generated-motion lease rejected', 10000);
    const conflictLog = readLog().slice(dragStart);
    if (!conflictLog.includes('generated-motion lease rejected')) throw new Error('generated-motion conflict rejection missing');
    await send('@@sim:lease:generated:release');
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const during = latestState();
    if (!activeLog.includes('[DragHandler] 拖动已启动')) throw new Error('dragging start evidence missing');

    await waitFor('[DragHandler] 抛掷:', 10000);
    await waitFor('drag-release', 10000);
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const thrown = latestState();
    if (thrown.dragging) throw new Error('dragging remained true after release');
    const throwVelocity = latestThrow();
    if (throwVelocity.x === 0 && throwVelocity.y === 0) throw new Error('throw velocity was zero');
    if (thrown.x === initial.x && thrown.y === initial.y) throw new Error('drag did not move pet');
    if (!readLog().includes('[DragHandler] 抛掷:')) throw new Error('throw evidence missing');

    await sleep(800);
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const airborneOrSettled = latestState();
    await waitFor('[DesktopPet] 落地', 30000);
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const landed = latestState();
    if (!landed.onGround) throw new Error('landing state not observed');

    await sendAndWait('@@sim:walk:right', '[TestInbox] 已强制开始向右走');
    await sleep(450);
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const walking = latestState();
    if (walking.dragging || walking.velocityX === 0) throw new Error('walking handoff not observed');
    await sendAndWait('@@sim:walk:stop', '[TestInbox] 已强制停止走路');

    const starStart = readLog().length;
    await sendAndWait('@@sim:idle-action:4', '[TestInbox] idle-action requested: 4');
    await sleep(1500);
    const starBaseline = await waitForStarSignature();
    if (starBaseline.dragging) throw new Error('star baseline unexpectedly reported active drag');

    await send('@@sim:drag:offset:220,-250,900', 100);
    await waitFor('[DragInterrupt] action-to-drag accepted', 10000);
    await waitFor('[DragHandler] 拖动已启动', 10000);
    const starDragStart = readLog().length;
    const starDragSnapshots = [];
    await sendAndWait('@@sim:star-arm-state', '[StarArmState]', 5000);
    const snapshot = latestStarArmState();
    if (!snapshot.dragging) throw new Error('star drag snapshot was not captured during pointer capture');
    if (hasExactStarSignature(snapshot)) throw new Error('star raised-arm signature survived during DragResponse');
    if (snapshot.Param92 !== 0 || snapshot.Param102 !== 0 || snapshot.Param103 !== 0
      || snapshot.Param105 !== 0 || snapshot.Param106 !== 0 || snapshot.Param107 !== 0
      || snapshot.Param110 !== 0 || snapshot.Param111 !== 0 || snapshot.Param112 !== 0
      || snapshot.Param113 !== 0 || snapshot.Param114 !== 0 || snapshot.Param115 !== 0) {
      throw new Error('star finger residue survived during DragResponse');
    }
    starDragSnapshots.push(snapshot);
    await waitFor('[DragHandler] 抛掷:', 15000);
    const starDragLog = readLog().slice(starDragStart);
    if (!starDragLog.includes('drag-response')) throw new Error('star action did not transfer to DragResponse');
    await waitFor('[DesktopPet] 落地', 30000);
    await sleep(900);
    await sendAndWait('@@sim:star-arm-state', '[StarArmState]', 5000);
    const starLanded = latestStarArmState();
    if (hasExactStarSignature(starLanded)) throw new Error('star raised-arm signature survived landing recovery');

    await sendAndWait('@@sim:legacy:stretch', '[TestInbox] legacy action requested: stretch');
    const actionStart = readLog().length;
    await waitFor('Accepted LegacyAction/legacy-action/stretch', 10000);
    await sendAndWait('@@sim:desktop-state', '[DesktopState]');
    const actionStateBeforeDrag = latestState();
    if (!actionStateBeforeDrag.actionLocked) throw new Error('legacy action did not hold action lock');
    const actionRect = { x: actionStateBeforeDrag.x, y: actionStateBeforeDrag.y };
    await send('@@sim:drag:offset:220,-250,300', 100);
    await waitFor('[DragInterrupt] action-to-drag accepted', 10000);
    await waitFor('[DragHandler] 拖动已启动', 10000);
    const actionDragLog = readLog().slice(actionStart);
    if (!actionDragLog.includes('[DragInterrupt] action-to-drag accepted')) throw new Error('action interruption evidence missing');
    await waitFor('[DragHandler] 抛掷:', 10000);
    await waitFor('[DesktopPet] 落地', 30000);
    await sleep(900);
    const landedAt = readLog().length;
    await send('@@sim:desktop-state', 1200);
    const actionLanded = latestState();
    if (!actionLanded.onGround) throw new Error('landing state not observed');
    if (actionLanded.actionLocked) {
      await sleep(800);
      await send('@@sim:desktop-state', 1200);
    }
    const recoveredAfterAction = latestState();
    const actionRecoveryLog = readLog();
    if (!actionRecoveryLog.slice(actionStart).includes('[DesktopPet] 落地')) throw new Error('action landing evidence missing');
    if (recoveredAfterAction.dragging || recoveredAfterAction.actionLocked) throw new Error('action state remained after action drag landing');
    if (recoveredAfterAction.x === actionRect.x && recoveredAfterAction.y === actionRect.y) throw new Error('action drag did not move pet');

    const preExit = readLog();
    await sendAndWait('@@test:quit', '[EmbodiedSafeRecovery] recovered: test-exit', 20000);
    if (child.exitCode === null) await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('player did not exit')), 10000);
      child.once('exit', () => { clearTimeout(timer); resolve(); });
    });
    const log = readLog();
    assertNoBadLogs(log);
    passed = true;
    console.log(JSON.stringify({ root, initial, during, thrown, throwVelocity, airborneOrSettled, landed, walking, starBaseline, starDragSnapshots, starLanded, evidence: { dragStarted: log.includes('[DragHandler] 拖动已启动'), throw: log.includes('[DragHandler] 抛掷:'), landing: log.includes('[DesktopPet] 落地'), generatedRejected: log.includes('generated-motion lease rejected'), starInterrupted: log.includes('[DragInterrupt] action-to-drag accepted'), safeRecovery: log.includes('[EmbodiedSafeRecovery] recovered: test-exit') } }));
  } finally {
    if (child.exitCode === null) {
      try { fs.writeFileSync(inbox, '@@test:quit', 'utf8'); } catch (_) {}
      await sleep(800);
      try { child.kill(); } catch (_) {}
    }
    if (!passed) {
      console.error(readLog().slice(-20000));
      console.error('preserved test root: ' + root);
    } else {
      fs.rmSync(root, { recursive: true, force: true });
    }
  }
})().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
