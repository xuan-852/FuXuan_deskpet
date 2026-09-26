'use strict';

const path = require('path');
const {
  readJson, writeJson, sha256, hashJson, assertTestRoot, isTemporaryRoot,
  auditProfile, normalizeMatrix
} = require('./lib/soullink_offline.cjs');

function loadAdapter(adapterPath) {
  if (adapterPath) {
    if (!path.isAbsolute(adapterPath)) throw new Error('--adapter must be an absolute module path');
    return require(adapterPath);
  }
  let engine;
  try { engine = require('@soullink-emotion/engine'); }
  catch (error) { throw new Error(`SoulLink engine unavailable; pass --adapter <absolute-module>: ${error.message}`); }
  const version = engine.version || engine.VERSION || null;
  return {
    packageName: '@soullink-emotion/engine', version,
    createManualClock: engine.createManualClock,
    createCase({ profile, testCase, matrix, clock }) {
      if (typeof engine.SoullinkRuntime !== 'function' || typeof clock?.set !== 'function') throw new Error('engine must export SoullinkRuntime and a settable createManualClock');
      const runtime = new engine.SoullinkRuntime({ profile: profile.engineProfile || profile, motionStyle: { ...(profile.motionStyle || {}), intensity: testCase.intensity, seed: testCase.seed } });
      return {
        step(timeSeconds, deltaSeconds) {
          clock.set(timeSeconds * 1000);
          return runtime.update(timeSeconds, deltaSeconds);
        }
      };
    }
  };
}

function extractParameterValues(snapshot) {
  const values = snapshot?.live2dParams || snapshot?.parameters || snapshot?.parameterValues;
  if (!values || typeof values !== 'object' || Array.isArray(values)) throw new Error('engine snapshot has no parameter value object');
  return values;
}

function runCase(adapter, profile, testCase, matrix) {
  if (typeof adapter.createManualClock !== 'function') throw new Error('engine adapter must provide createManualClock()');
  const clock = adapter.createManualClock();
  const instance = adapter.createCase({ profile, testCase, matrix, clock });
  if (!instance || typeof instance.step !== 'function') throw new Error('engine adapter must return step(timeSeconds, deltaSeconds)');
  const total = matrix.durationSeconds + matrix.settleSeconds;
  const frameCount = Math.ceil(total * matrix.frameRate);
  const frames = [];
  const observed = new Set();
  for (let index = 0; index <= frameCount; index++) {
    const time = Math.min(total, index / matrix.frameRate);
    const delta = index === 0 ? 0 : time - Math.min(total, (index - 1) / matrix.frameRate);
    const snapshot = instance.step(time, delta);
    const raw = extractParameterValues(snapshot);
    const parameters = {};
    for (const id of Object.keys(raw).sort()) {
      const value = Number(raw[id]);
      if (!Number.isFinite(value)) throw new Error(`non-finite parameter value: ${id}`);
      observed.add(id);
      parameters[id] = value;
    }
    frames.push({ timeSeconds: Number(time.toFixed(9)), parameters });
  }
  return { frames, parameterIds: [...observed].sort() };
}

function verifyAllowed(timeline, audit) {
  const allowed = new Set(audit.parameterCoverage.filter(item => item.allowed).map(item => item.parameterId));
  const forbidden = timeline.parameterIds.filter(id => !allowed.has(id));
  if (forbidden.length) throw new Error(`engine produced undeclared or reserved parameters: ${forbidden.join(', ')}`);
}

function main() {
  const args = process.argv.slice(2);
  const manifestPath = args[0];
  if (!manifestPath || !path.isAbsolute(manifestPath)) throw new Error('Usage: soullink_engine_scan.cjs <absolute-manifest.json> [--adapter <absolute-module>]');
  const getFlag = name => { const i = args.indexOf(name); return i < 0 ? null : args[i + 1]; };
  const { value: manifest, bytes: manifestBytes } = readJson(manifestPath);
  if (manifest.schema !== 'soullink-offline-manifest/v1') throw new Error('unsupported offline manifest schema');
  for (const key of ['profile', 'capabilityCatalog', 'outputRoot']) if (!path.isAbsolute(manifest[key] || '')) throw new Error(`${key} must be an absolute path`);
  if (!isTemporaryRoot(manifest.outputRoot)) throw new Error('outputRoot must be inside the system temporary directory');
  assertTestRoot(manifest.outputRoot);
  process.env.FU_XUAN_DATA = manifest.outputRoot;
  const profileDoc = readJson(manifest.profile);
  const catalogDoc = readJson(manifest.capabilityCatalog);
  const auditPath = path.join(manifest.outputRoot, 'profile-audit.json');
  const audit = auditProfile(profileDoc.value, catalogDoc.value, profileDoc.bytes, catalogDoc.bytes);
  writeJson(auditPath, audit);
  if (audit.status !== 'audited') throw new Error(`profile audit rejected: ${audit.rejectionReasons.map(item => item.reason).join(', ')}`);
  const matrix = normalizeMatrix(manifest.matrix);
  if (matrix.cases.length > Number(manifest.maxCases || 1000)) throw new Error('matrix exceeds maxCases limit');
  const adapterPath = getFlag('--adapter') || manifest.adapter;
  const adapter = loadAdapter(adapterPath);
  const engineIdentity = { packageName: adapter.packageName || 'injected-adapter', version: adapter.version || 'unknown' };
  if (manifest.engine?.version && engineIdentity.version !== manifest.engine.version) throw new Error(`engine version drift: expected ${manifest.engine.version}, got ${engineIdentity.version}`);
  const report = { schema: 'soullink-offline-batch-report/v1', manifestSha256: sha256(manifestBytes), profileSha256: audit.profileSha256, catalogSha256: audit.catalogSha256, engine: engineIdentity, deterministic: true, cases: [], safety: { evidenceOnly: true, productionStatus: 'not-certified', mapWriteAllowed: false, llmExposureAllowed: false } };
  const timelineDir = path.join(manifest.outputRoot, 'timelines');
  for (const testCase of matrix.cases) {
    const item = { ...testCase, status: 'rejected', error: null, timelineFile: null, timelineSha256: null };
    try {
      const first = runCase(adapter, profileDoc.value, testCase, matrix);
      const second = runCase(adapter, profileDoc.value, testCase, matrix);
      if (hashJson(first) !== hashJson(second)) throw new Error('nondeterministic');
      const timeline = {
        schema: 'soullink-offline-timeline/v1', case: testCase,
        source: { manifestSha256: report.manifestSha256, profileSha256: audit.profileSha256, catalogSha256: audit.catalogSha256, engine: engineIdentity },
        timing: { frameRate: matrix.frameRate, durationSeconds: matrix.durationSeconds, settleSeconds: matrix.settleSeconds },
        frames: first.frames, parameterIds: first.parameterIds,
        constraints: { evidenceOnly: true, uncertified: true, productionStatus: 'not-certified', mapWriteAllowed: false, llmExposureAllowed: false }
      };
      verifyAllowed(timeline, audit);
      const file = path.join(timelineDir, `${testCase.caseId}.json`);
      writeJson(file, timeline);
      item.status = 'completed'; item.timelineFile = file; item.timelineSha256 = sha256(require('fs').readFileSync(file));
    } catch (error) { item.error = error.message; }
    report.cases.push(item);
  }
  report.completedCases = report.cases.filter(item => item.status === 'completed').length;
  report.rejectedCases = report.cases.length - report.completedCases;
  writeJson(path.join(manifest.outputRoot, 'batch-report.json'), report);
  console.log(JSON.stringify({ outputRoot: manifest.outputRoot, completedCases: report.completedCases, rejectedCases: report.rejectedCases, engine: engineIdentity }, null, 2));
  if (report.rejectedCases) process.exitCode = 2;
}

if (require.main === module) {
  try { main(); } catch (error) { console.error(`soullink_engine_scan: ${error.message}`); process.exitCode = 1; }
}
module.exports = { runCase, extractParameterValues, loadAdapter };
