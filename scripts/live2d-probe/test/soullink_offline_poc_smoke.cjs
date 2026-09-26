'use strict';

const assert = require('assert');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');

const repo = path.resolve(__dirname, '../../..');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'soullink-offline-poc-'));
fs.writeFileSync(path.join(root, '.test_mode'), '');
const catalogPath = path.join(root, 'capability-catalog.json');
const profilePath = path.join(root, 'soullink.profile.json');
const auditPath = path.join(root, 'profile-audit.json');
const manifestPath = path.join(root, 'manifest.json');
const adapterPath = path.join(repo, 'scripts/live2d-probe/fixtures/soullink_engine_fixture.cjs');
const write = (file, value) => fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', 'utf8');
const run = (script, args) => {
  const result = spawnSync(process.execPath, [path.join(repo, script), ...args], { cwd: repo, encoding: 'utf8' });
  if (result.error) throw result.error;
  return result;
};

const catalog = {
  schema: 'capability-catalog/v1',
  source: { modelSha256: 'model-fixture', reportSha256: 'report-fixture', parameterCount: 2 },
  safety: { productionStatus: 'not-certified', mapWriteAllowed: false, llmExposureAllowed: false },
  parameters: [
    { capabilityId: 'parameter.ParamAngleX', parameterId: 'ParamAngleX', range: { minimum: -30, maximum: 30, baseline: 0 }, semanticStatus: 'unassigned', productionStatus: 'not-certified', localEvidence: { resetStable: true } },
    { capabilityId: 'parameter.ParamAngleY', parameterId: 'ParamAngleY', range: { minimum: -20, maximum: 20, baseline: 1 }, semanticStatus: 'unassigned', productionStatus: 'not-certified', localEvidence: { resetStable: true } },
    { capabilityId: 'parameter.ParamMouthOpen', parameterId: 'ParamMouthOpen', range: { minimum: 0, maximum: 1, baseline: 0 }, semanticStatus: 'unassigned', productionStatus: 'not-certified', localEvidence: { resetStable: true } },
    { capabilityId: 'parameter.ParamUnstable', parameterId: 'ParamUnstable', range: { minimum: -1, maximum: 1, baseline: 0 }, semanticStatus: 'unassigned', productionStatus: 'not-certified', localEvidence: { resetStable: false } }
  ]
};
const profile = {
  schema: 'soullink-fuxuan-profile/v1',
  engine: { package: 'soullink-fixture-engine', version: 'fixture-1' },
  model: { modelId: 'fixture-fuxuan' },
  parameters: [
    { parameterId: 'ParamAngleX', channel: 'head.yaw', role: 'emotion', neutral: 0, range: { minimum: -12, maximum: 12 } },
    { parameterId: 'ParamAngleY', channel: 'head.pitch', role: 'emotion', neutral: 1, range: { minimum: -8, maximum: 8 } }
  ],
  safeEmotionChannels: ['head.yaw', 'head.pitch'], safeBodyChannels: [], reservedParameters: ['ParamMouthOpen', 'ParamUnstable'],
  constraints: { freeBodyMotion: false, tts: false, lipSync: false, pixi: false, runtimeCoreSession: false, onlineUnityWrite: false, neutralCompletion: 'explicit' }
};
write(catalogPath, catalog); write(profilePath, profile);
let result = run('scripts/live2d-probe/soullink_profile_check.cjs', [profilePath, catalogPath, auditPath]);
assert.strictEqual(result.status, 0, result.stderr);
const audit = JSON.parse(fs.readFileSync(auditPath, 'utf8'));
for (const key of ['parameterCoverage', 'missingParameters', 'reservedParameters', 'safeEmotionChannels', 'safeBodyChannels']) assert.ok(Object.prototype.hasOwnProperty.call(audit, key), key);
assert.strictEqual(audit.status, 'audited');

const outputRoot = path.join(root, 'run');
const manifest = { schema: 'soullink-offline-manifest/v1', profile: profilePath, capabilityCatalog: catalogPath, outputRoot, adapter: adapterPath, engine: { version: 'fixture-1' }, matrix: { emotions: ['calm', 'warm'], intensities: [0.5], seeds: [11, 22], frameRate: 10, durationSeconds: 2, settleSeconds: 0.4 } };
write(manifestPath, manifest);
result = run('scripts/live2d-probe/soullink_engine_scan.cjs', [manifestPath]);
assert.strictEqual(result.status, 0, result.stderr + result.stdout);
const batch = JSON.parse(fs.readFileSync(path.join(outputRoot, 'batch-report.json'), 'utf8'));
assert.strictEqual(batch.completedCases, 4); assert.strictEqual(batch.rejectedCases, 0);
const timelinePath = path.join(outputRoot, 'timelines', 'calm-0_5-11.json');
assert.ok(fs.existsSync(timelinePath));

const candidateDir = path.join(outputRoot, 'candidates'); fs.mkdirSync(candidateDir, { recursive: true });
const featureDir = path.join(outputRoot, 'feature-packets'); fs.mkdirSync(featureDir, { recursive: true });
const convert = run('scripts/live2d-probe/soullink_timeline_to_candidate.cjs', [timelinePath, profilePath, catalogPath, path.join(candidateDir, 'soullink_calm_0_5_11.json')]);
assert.strictEqual(convert.status, 0, convert.stderr);
const candidatePath = path.join(candidateDir, 'soullink_calm_0_5_11.json');
const candidate = JSON.parse(fs.readFileSync(candidatePath, 'utf8'));
assert.strictEqual(candidate.certification.status, 'uncertified'); assert.strictEqual(candidate.mapWriteAllowed, false);
for (const curve of candidate.curves) {
  const last = curve.samples[curve.samples.length - 1];
  const profileItem = profile.parameters.find(item => item.parameterId === curve.parameterId);
  assert.strictEqual(last.timeSeconds, candidate.durationSeconds);
  assert.strictEqual(last.value, profileItem.neutral);
  assert.strictEqual(curve.segments[2], 0);
}
const extract = run('scripts/live2d-probe/soullink_extract_features.cjs', [candidatePath, outputRoot, path.join(featureDir, `${candidate.candidateId}.json`)]);
assert.strictEqual(extract.status, 0, extract.stderr);
const packetPath = path.join(featureDir, `${candidate.candidateId}.json`); const packet = JSON.parse(fs.readFileSync(packetPath, 'utf8'));
assert.strictEqual(packet.constraints.mayNotGenerateRuntimeCurve, true); assert.strictEqual(packet.constraints.mayNotEstablishFuxuanParameterMapping, true);

const duplicate = JSON.parse(JSON.stringify(candidate)); duplicate.candidateId = 'soullink_duplicate';
const duplicatePath = path.join(candidateDir, 'soullink_duplicate.json'); write(duplicatePath, duplicate);
const duplicateExtract = run('scripts/live2d-probe/soullink_extract_features.cjs', [duplicatePath, outputRoot, path.join(featureDir, 'soullink_duplicate.json')]);
assert.strictEqual(duplicateExtract.status, 0, duplicateExtract.stderr);
const select = run('scripts/live2d-probe/soullink_select_candidates.cjs', [outputRoot]);
assert.strictEqual(select.status, 0, select.stderr);
const selection = JSON.parse(fs.readFileSync(path.join(outputRoot, 'selection-report.json'), 'utf8'));
assert.ok(selection.survivors.length >= 1); assert.ok(selection.rejected.some(item => item.reason === 'exact-duplicate'));
const survivors = JSON.parse(fs.readFileSync(path.join(outputRoot, 'cloud-review-survivors.json'), 'utf8'));
assert.strictEqual(survivors.cloudRequestsMade, 0); assert.strictEqual(survivors.requiresIsolatedPlayerCapture, true);

const badRoot = path.join(root, 'unmarked'); fs.mkdirSync(badRoot); fs.writeFileSync(path.join(badRoot, 'keep.txt'), 'keep');
const badManifest = { ...manifest, outputRoot: badRoot }; const badManifestPath = path.join(root, 'bad-manifest.json'); write(badManifestPath, badManifest);
result = run('scripts/live2d-probe/soullink_engine_scan.cjs', [badManifestPath]); assert.notStrictEqual(result.status, 0);

console.log(`soullink-offline-poc-smoke: pass (${crypto.createHash('sha256').update(JSON.stringify(selection)).digest('hex').slice(0, 12)})`);
