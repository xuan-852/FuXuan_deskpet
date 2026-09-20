'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const exe = process.argv[2];
if (!exe || !fs.existsSync(exe)) throw new Error('usage: node life_timeline_query_drive.cjs <DesktopPet.exe>');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'fuxuan_life_timeline_'));
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
    child.once('exit', () => { clearTimeout(timer); resolve(); });
  });
}
function latestLifeState() {
  const matches = [...readLog().matchAll(/\[LifeState\] snapshot version=(\d+).*?events=(\d+).*?bodyVersion=(\d+).*?health=([^ ]+).*?observations=(\d+).*?faults=(\d+)/g)];
  if (!matches.length) throw new Error('life-state snapshot missing');
  const m = matches[matches.length - 1];
  return { version: +m[1], events: +m[2], bodyVersion: +m[3], health: m[4], observations: +m[5], faults: +m[6] };
}
function timelineSnapshot() {
  const text = readLog();
  const matches = [...text.matchAll(/\[LifeTimeline\] snapshot count=(\d+) version=(\d+)/g)];
  if (!matches.length) throw new Error('life-timeline snapshot missing');
  const m = matches[matches.length - 1];
  const start = text.lastIndexOf(m[0]);
  const tail = text.slice(start);
  const entries = [...tail.matchAll(/\[LifeTimeline\] seq=(\d+) type=([^ ]+) source=([^ ]+) correlation=([^ ]+) state=([^ ]+) reason=([^ ]+) hash=([0-9a-f]{64}) version=(\d+)/g)];
  return { count: +m[1], version: +m[2], entries: entries.map(e => ({ sequence: +e[1], type: e[2], source: e[3], correlation: e[4], state: e[5], reason: e[6], hash: e[7], version: +e[8] })) };
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
    const beforeQuery = latestLifeState();
    await send('@@sim:life-timeline');
    const timeline = timelineSnapshot();
    await send('@@sim:life-state');
    const afterQuery = latestLifeState();
    if (afterQuery.version < beforeQuery.version || afterQuery.events < beforeQuery.events || afterQuery.bodyVersion < beforeQuery.bodyVersion || afterQuery.observations < beforeQuery.observations)
      throw new Error('timeline query regressed observation counters');
    if (timeline.count < 1 || timeline.count > 128 || timeline.entries.length < 1) throw new Error('invalid timeline count');
    for (let i = 1; i < timeline.entries.length; i++) if (timeline.entries[i].sequence <= timeline.entries[i - 1].sequence) throw new Error('timeline sequence regressed');
    if (timeline.entries.some(e => e.hash.length !== 64 || /[\r\n]/.test(e.type + e.source + e.state + e.reason))) throw new Error('unsanitized timeline entry');
    await send('@@test:quit', 1200);
    await waitForExit(child);
    const log = readLog();
    for (const bad of ['NullReferenceException', 'AssertionException']) if (log.includes(bad)) throw new Error('unexpected log: ' + bad);
    for (const name of Object.keys(before)) {
      const file = path.join(process.env.LOCALAPPDATA || '', 'FuXuan', 'DesktopPetData', name);
      const after = fs.existsSync(file) ? fs.readFileSync(file).toString('base64') : null;
      if (after !== before[name]) throw new Error('production data changed: ' + name);
    }
    console.log(JSON.stringify({ root, beforeQuery, timeline: { count: timeline.count, version: timeline.version, entries: timeline.entries.length }, afterQuery }));
    passed = true;
  } finally {
    if (!child.killed) child.kill();
    if (!passed) {
      console.error(readLog().slice(-12000));
      console.error('preserved test root: ' + root);
    } else fs.rmSync(root, { recursive: true, force: true });
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
