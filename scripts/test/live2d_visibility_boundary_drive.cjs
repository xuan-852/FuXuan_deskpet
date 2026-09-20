'use strict';

/*
 * Runs isolated Live2DProbe visibility-boundary sweeps. This is mechanical
 * evidence only: it never edits mappings, skills, or production data.
 * Usage: node live2d_visibility_boundary_drive.cjs <Live2DProbe.exe>
 */
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawn, execFileSync } = require('child_process');

const exe = process.argv[2];
if (!exe || !fs.existsSync(exe)) throw new Error('missing Live2DProbe.exe path');

function rejectRunningDesktopPet() {
  if (process.platform !== 'win32') return;
  let listing = '';
  try { listing = execFileSync('tasklist.exe', ['/FI', 'IMAGENAME eq DesktopPet.exe', '/FO', 'CSV', '/NH'], { encoding: 'utf8' }); }
  catch { return; }
  const instances = listing.split(/\r?\n/).filter(line => /"DesktopPet\.exe"/i.test(line));
  if (instances.length > 1) throw new Error('Multiple DesktopPet.exe instances are already running; refusing to add another test process.');
}

rejectRunningDesktopPet();
const stamp = new Date().toISOString().replace(/[-:.TZ]/g, '');
const root = path.join(os.tmpdir(), `fuxuan_visibility_boundary_${stamp}_${process.pid}`);
fs.mkdirSync(root, { recursive: true });
fs.writeFileSync(path.join(root, '.test_mode'), '');

function sha256(file) { return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'); }
function runProbe(name, variables) {
  const runRoot = path.join(root, name);
  fs.mkdirSync(runRoot, { recursive: true });
  fs.writeFileSync(path.join(runRoot, '.test_mode'), '');
  return new Promise((resolve, reject) => {
    const child = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: runRoot, ...variables }, stdio: 'ignore' });
    child.once('error', reject);
    child.once('exit', code => {
      if (code !== 0) return reject(new Error(`${name}: probe exited with ${code}`));
      resolve(runRoot);
    });
  });
}
function readNativeRange(reportRoot, id) {
  const report = JSON.parse(fs.readFileSync(path.join(reportRoot, 'capability-report-probe-window.json'), 'utf8'));
  const item = (report.parameters || []).find(entry => entry.parameterId === id);
  if (!item) throw new Error(`native range missing for ${id}`);
  return { minimum: item.minimum, maximum: item.maximum, baseline: item.baseline };
}
function fileDigestManifest(runRoot) {
  const files = [];
  function visit(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) visit(full);
      else if (entry.name.toLowerCase().endsWith('.png') || entry.name.endsWith('.json')) files.push({ file: full, sha256: sha256(full) });
    }
  }
  visit(runRoot);
  return files;
}

(async () => {
  const common = { FU_XUAN_PROBE_WRITER_MODE: 'frozen' };
  const param94Range = '-15,30';
  const native97Root = await runProbe('param97_native', {
    ...common,
    FU_XUAN_PROBE_PARAMETER: 'Param97',
  });
  const native97 = readNativeRange(native97Root, 'Param97');
  const param97Range = `${native97.minimum},${native97.maximum}`;
  const runs = [];
  runs.push(await runProbe('param94', {
    ...common, FU_XUAN_PROBE_PARAMETER: 'Param94', FU_XUAN_PROBE_VALUE_RANGE: param94Range,
    FU_XUAN_PROBE_SWEEP_STEPS: '12', FU_XUAN_PROBE_SWEEP_TARGET: 'max',
  }));
  runs.push(await runProbe('param97', {
    ...common, FU_XUAN_PROBE_PARAMETER: 'Param97', FU_XUAN_PROBE_VALUE_RANGE: param97Range,
    FU_XUAN_PROBE_SWEEP_STEPS: '12', FU_XUAN_PROBE_SWEEP_TARGET: 'max',
  }));
  runs.push(await runProbe('param94_param97', {
    ...common, FU_XUAN_PROBE_COMBINATION_SWEEP_IDS: 'Param94,Param97',
    FU_XUAN_PROBE_COMBINATION_RANGE_SCALE: '0.25', FU_XUAN_PROBE_SWEEP_STEPS: '12',
  }));
  const manifest = {
    schema: 'live2d-visibility-boundary-drive.v1',
    root,
    mapWriteAllowed: false,
    llmExposed: false,
    semanticStatus: 'unassigned',
    runs: [{ name: 'param97_native', root: native97Root, native97 }, ...runs.map(runRoot => ({ name: path.basename(runRoot), root: runRoot }))],
  };
  manifest.files = fileDigestManifest(root);
  fs.writeFileSync(path.join(root, 'visibility-boundary-input-manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
  console.log(JSON.stringify({ root, native97, runs: manifest.runs.map(run => ({ name: run.name, root: run.root })), fileCount: manifest.files.length }, null, 2));
})().catch(error => { console.error(error.stack || error.message); process.exitCode = 1; });
