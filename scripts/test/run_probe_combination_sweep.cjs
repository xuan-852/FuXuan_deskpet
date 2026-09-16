'use strict';

// Runs an offline, three-repeat synchronized sweep for an arbitrary Live2D
// parameter group. It is probe evidence only, never a runtime action.
// Usage: node scripts/test/run_probe_combination_sweep.cjs <Live2DProbe.exe> <id1,id2[,idN]> [rangeScale] [steps] [frozen|physics]

const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const [exe, ids, rawScale = '0.25', rawSteps = '12', writerMode = 'frozen'] = process.argv.slice(2);
if (!exe || !fs.existsSync(exe)) throw new Error('missing Live2DProbe.exe path');
if (!ids || !ids.split(',').every(id => /^[A-Za-z0-9_]+$/.test(id))) throw new Error('invalid parameter IDs');
if (new Set(ids.split(',')).size < 2) throw new Error('at least two distinct parameter IDs are required');
const scale = Number(rawScale);
const steps = Number(rawSteps);
if (!(scale > 0 && scale <= 1)) throw new Error('rangeScale must be in (0, 1]');
if (!Number.isInteger(steps) || steps < 2 || steps > 60) throw new Error('steps must be 2..60');
if (!/^(frozen|physics)$/.test(writerMode)) throw new Error('writerMode must be frozen or physics');

const slug = ids.toLowerCase().replace(/[^a-z0-9]+/g, '_');
const root = path.join(os.tmpdir(), `fuxuan_probe_combination_sweep_${slug}_20260916`);
fs.rmSync(root, { recursive: true, force: true });
fs.mkdirSync(root, { recursive: true });
fs.writeFileSync(path.join(root, '.test_mode'), '');

const child = spawn(exe, [], {
    env: {
        ...process.env,
        FU_XUAN_DATA: root,
        FU_XUAN_PROBE_COMBINATION_SWEEP_IDS: ids,
        FU_XUAN_PROBE_COMBINATION_RANGE_SCALE: String(scale),
        FU_XUAN_PROBE_SWEEP_STEPS: String(steps),
        FU_XUAN_PROBE_WRITER_MODE: writerMode,
    },
    stdio: 'ignore',
});

child.once('exit', code => {
    if (code !== 0) {
        process.exitCode = 1;
        console.error(`probe exited with ${code}`);
        return;
    }
    const report = path.join(root, 'custom-combination-sweep-report.json');
    if (!fs.existsSync(report)) {
        process.exitCode = 1;
        console.error('probe did not produce custom-combination-sweep-report.json');
        return;
    }
    console.log(root);
});
