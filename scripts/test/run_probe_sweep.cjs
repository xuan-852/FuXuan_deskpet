'use strict';

// Runs one offline Live2DProbe sweep in a fresh isolated data root.
// Usage: node scripts/test/run_probe_sweep.cjs <Live2DProbe.exe> <parameterId> <minimum,maximum> [steps] [target]

const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const [exe, parameterId, range, rawSteps = '12', target = 'max'] = process.argv.slice(2);
if (!exe || !fs.existsSync(exe)) throw new Error('missing Live2DProbe.exe path');
if (!parameterId || !/^[A-Za-z0-9_]+$/.test(parameterId)) throw new Error('invalid parameter ID');
if (!/^-?\d+(\.\d+)?,-?\d+(\.\d+)?$/.test(range || '')) throw new Error('range must be minimum,maximum');
if (!/^(min|max)$/.test(target)) throw new Error('target must be min or max');

const steps = Number(rawSteps);
if (!Number.isInteger(steps) || steps < 2 || steps > 60) throw new Error('steps must be 2..60');

const root = path.join(os.tmpdir(), `fuxuan_probe_sweep_${parameterId.toLowerCase()}_20260916`);
fs.rmSync(root, { recursive: true, force: true });
fs.mkdirSync(root, { recursive: true });
fs.writeFileSync(path.join(root, '.test_mode'), '');

const child = spawn(exe, [], {
    env: {
        ...process.env,
        FU_XUAN_DATA: root,
        FU_XUAN_PROBE_PARAMETER: parameterId,
        FU_XUAN_PROBE_VALUE_RANGE: range,
        FU_XUAN_PROBE_SWEEP_STEPS: String(steps),
        FU_XUAN_PROBE_SWEEP_TARGET: target,
    },
    stdio: 'ignore',
});

child.once('exit', code => {
    if (code !== 0) {
        process.exitCode = 1;
        console.error(`probe exited with ${code}`);
        return;
    }
    const report = path.join(root, 'parameter-sweep-report.json');
    if (!fs.existsSync(report)) {
        process.exitCode = 1;
        console.error('probe did not produce parameter-sweep-report.json');
        return;
    }
    console.log(root);
});
