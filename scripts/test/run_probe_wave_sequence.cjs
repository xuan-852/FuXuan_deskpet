'use strict';

// Runs one isolated offline wave-candidate sequence. Evidence only; never writes mappings.
// Usage: node scripts/test/run_probe_wave_sequence.cjs <Live2DProbe.exe> <id1,id2,id3[,idN]> [wristScale] [steps] [repeats] [frozen|physics]

const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawn } = require('child_process');

const [exe, ids, rawScale = '0.25', rawSteps = '4', rawRepeats = '3', writerMode = 'frozen'] = process.argv.slice(2);
if (!exe || !fs.existsSync(exe)) throw new Error('missing Live2DProbe.exe path');
if (!ids || !ids.split(',').every(id => /^[A-Za-z0-9_]+$/.test(id))) throw new Error('invalid parameter IDs');
const parsedIds = ids.split(',');
if (!parsedIds.includes('Param94') || !parsedIds.includes('Param99') || !parsedIds.includes('Param92')) throw new Error('wave sequence requires Param94,Param99,Param92');
const scale = Number(rawScale), steps = Number(rawSteps), repeats = Number(rawRepeats);
if (!(scale > 0 && scale <= 1)) throw new Error('wristScale must be in (0, 1]');
if (!Number.isInteger(steps) || steps < 2 || steps > 20) throw new Error('steps must be 2..20');
if (!Number.isInteger(repeats) || repeats < 1 || repeats > 10) throw new Error('repeats must be 1..10');
if (!/^(frozen|physics)$/.test(writerMode)) throw new Error('writerMode must be frozen or physics');

const slug = parsedIds.join('_').toLowerCase().replace(/[^a-z0-9]+/g, '_');
const root = path.join(os.tmpdir(), `fuxuan_probe_wave_sequence_${slug}_${crypto.createHash('sha1').update(Date.now().toString()).digest('hex').slice(0, 8)}`);
fs.mkdirSync(root, { recursive: true });
fs.writeFileSync(path.join(root, '.test_mode'), '');
const logPath = path.join(root, 'probe-process.log');
const log = fs.createWriteStream(logPath);
const child = spawn(path.resolve(exe), [], {
  env: { ...process.env, FU_XUAN_DATA: root, FU_XUAN_PROBE_WAVE_SEQUENCE_IDS: ids,
    FU_XUAN_PROBE_WAVE_WRIST_SCALE: String(scale), FU_XUAN_PROBE_WAVE_STEPS: String(steps),
    FU_XUAN_PROBE_WAVE_REPEATS: String(repeats), FU_XUAN_PROBE_WRITER_MODE: writerMode },
  stdio: ['ignore', 'pipe', 'pipe']
});
child.stdout.pipe(log); child.stderr.pipe(log);
const timer = setTimeout(() => { try { child.kill(); } catch {} }, 180000);
child.once('exit', (code, signal) => {
  clearTimeout(timer); log.end();
  if (code !== 0) {
    const failure = path.join(root, 'capability-report-probe-window.failure.txt');
    if (fs.existsSync(failure)) process.stderr.write(fs.readFileSync(failure, 'utf8'));
    throw new Error(`probe exited with ${code}${signal ? ` (${signal})` : ''}; root=${root}`);
  }
  const report = path.join(root, 'wave-sequence-report.json');
  if (!fs.existsSync(report)) throw new Error(`probe did not produce wave-sequence-report.json; root=${root}`);
  const parsed = JSON.parse(fs.readFileSync(report, 'utf8'));
  console.log(JSON.stringify({ root, report, frames: parsed.repeatsData?.reduce((sum, item) => sum + (item.frames?.length || 0), 0), peak: parsed.peakMeanDifference, reset: parsed.maxResetMeanDifference, resetStable: parsed.resetStable }, null, 2));
});
