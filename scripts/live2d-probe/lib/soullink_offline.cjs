'use strict';

const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

function sha256(value) {
  const bytes = Buffer.isBuffer(value) ? value : Buffer.from(String(value));
  return crypto.createHash('sha256').update(bytes).digest('hex');
}

function readJson(file) {
  if (!file || !path.isAbsolute(file)) throw new Error(`absolute JSON path required: ${file || '<unset>'}`);
  const bytes = fs.readFileSync(file);
  return { bytes, value: JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/, '')) };
}

function writeJson(file, value) {
  if (!path.isAbsolute(file)) throw new Error(`absolute output path required: ${file}`);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', 'utf8');
}

function canonicalize(value) {
  if (Array.isArray(value)) return value.map(canonicalize);
  if (value && typeof value === 'object') {
    return Object.keys(value).sort().reduce((result, key) => {
      result[key] = canonicalize(value[key]);
      return result;
    }, {});
  }
  return value;
}

function canonicalJson(value) {
  return JSON.stringify(canonicalize(value));
}

function hashJson(value) {
  return sha256(canonicalJson(value));
}

function assertTestRoot(root) {
  if (!root || !path.isAbsolute(root)) throw new Error('outputRoot must be absolute');
  if (!fs.existsSync(root)) fs.mkdirSync(root, { recursive: true });
  const marker = path.join(root, '.test_mode');
  if (!fs.existsSync(marker)) {
    if (fs.readdirSync(root).length > 0) throw new Error(`refusing non-empty unmarked outputRoot: ${root}`);
    fs.writeFileSync(marker, '');
  }
  return root;
}

function isTemporaryRoot(root) {
  const resolved = path.resolve(root);
  const temp = path.resolve(os.tmpdir());
  return resolved === temp || resolved.startsWith(temp + path.sep);
}

function catalogParameterMap(catalog) {
  if (!catalog || catalog.schema !== 'capability-catalog/v1' || !Array.isArray(catalog.parameters)) {
    throw new Error('capability catalog must use capability-catalog/v1');
  }
  const map = new Map();
  for (const entry of catalog.parameters) {
    if (!entry || typeof entry.parameterId !== 'string' || map.has(entry.parameterId)) {
      throw new Error('capability catalog contains duplicate or missing parameterId');
    }
    map.set(entry.parameterId, entry);
  }
  return map;
}

function numberInRange(value, range) {
  return Number.isFinite(Number(value)) && range && Number(value) >= Number(range.minimum) && Number(value) <= Number(range.maximum);
}

function isMouthOrJaw(entry) {
  const text = `${entry.parameterId || ''} ${entry.channel || ''}`.toLowerCase();
  return /mouth|jaw|lip|viseme/.test(text);
}

function auditProfile(profile, catalog, profileBytes, catalogBytes) {
  if (!profile || profile.schema !== 'soullink-fuxuan-profile/v1') throw new Error('unsupported SoulLink profile schema');
  const params = catalogParameterMap(catalog);
  const requested = Array.isArray(profile.parameters) ? profile.parameters : [];
  const catalogDigest = sha256(catalogBytes || canonicalJson(catalog));
  if (profile.model?.catalogSha256 && profile.model.catalogSha256 !== catalogDigest) {
    throw new Error(`profile/catalog hash mismatch: expected ${profile.model.catalogSha256}, got ${catalogDigest}`);
  }
  if (profile.model?.modelSha256 && catalog.source?.modelSha256 && profile.model.modelSha256 !== catalog.source.modelSha256) {
    throw new Error(`profile/model hash mismatch: expected ${profile.model.modelSha256}, got ${catalog.source.modelSha256}`);
  }
  const coverage = [];
  const missing = [];
  const rejectionReasons = [];
  const seen = new Set();
  const explicitReserved = new Set(Array.isArray(profile.reservedParameters) ? profile.reservedParameters.map(String) : []);
  for (const item of requested) {
    const id = item && String(item.parameterId || '');
    if (!id) { rejectionReasons.push({ parameterId: id, reason: 'missing-parameter-id' }); continue; }
    if (seen.has(id)) { rejectionReasons.push({ parameterId: id, reason: 'duplicate-parameter' }); continue; }
    seen.add(id);
    const catalogEntry = params.get(id);
    const requestedRange = item.range || catalogEntry?.range;
    const record = { parameterId: id, channel: item.channel || null, role: item.role || null, requestedRange: requestedRange || null, catalogRange: catalogEntry?.range || null, resetStable: Boolean(catalogEntry?.localEvidence?.resetStable), productionStatus: catalogEntry?.productionStatus || null, semanticStatus: catalogEntry?.semanticStatus || null, allowed: false, reasons: [] };
    if (!catalogEntry) { record.reasons.push('catalog-missing'); missing.push({ parameterId: id, reason: 'catalog-missing' }); }
    else {
      if (catalogEntry.productionStatus !== 'not-certified') record.reasons.push('production-status-not-allowed');
      if (!catalogEntry.localEvidence?.resetStable) record.reasons.push('reset-unstable');
      if (!requestedRange || !numberInRange(requestedRange.minimum, catalogEntry.range) || !numberInRange(requestedRange.maximum, catalogEntry.range) || Number(requestedRange.minimum) > Number(requestedRange.maximum)) record.reasons.push('range-outside-catalog');
      if (item.neutral !== undefined && !numberInRange(item.neutral, catalogEntry.range)) record.reasons.push('neutral-outside-catalog');
      if (item.default !== undefined && !numberInRange(item.default, catalogEntry.range)) record.reasons.push('default-outside-catalog');
      if (isMouthOrJaw({ ...item, parameterId: id })) record.reasons.push('mouth-jaw-reserved');
      if (explicitReserved.has(id)) record.reasons.push('explicitly-reserved');
    }
    record.allowed = record.reasons.length === 0;
    if (!record.allowed) {
      for (const reason of record.reasons) rejectionReasons.push({ parameterId: id, reason });
      missing.push(...record.reasons.map(reason => ({ parameterId: id, reason })));
    }
    coverage.push(record);
  }
  const reserved = [];
  for (const [id, entry] of params) {
    if (!seen.has(id)) reserved.push({ parameterId: id, reason: explicitReserved.has(id) ? 'explicitly-reserved' : 'not-profile-allowlisted', semanticStatus: entry.semanticStatus || null, productionStatus: entry.productionStatus || null });
  }
  for (const id of explicitReserved) if (!params.has(id)) reserved.push({ parameterId: id, reason: 'reserved-not-in-catalog' });
  const allowed = coverage.filter(item => item.allowed);
  const safeEmotionChannels = allowed.filter(item => item.role === 'emotion' || !item.role).map(item => ({ channel: item.channel, parameterId: item.parameterId }));
  const safeBodyChannels = allowed.filter(item => item.role === 'body').map(item => ({ channel: item.channel, parameterId: item.parameterId }));
  const declaredEmotionChannels = Array.isArray(profile.safeEmotionChannels) ? profile.safeEmotionChannels.map(String) : null;
  const declaredBodyChannels = Array.isArray(profile.safeBodyChannels) ? profile.safeBodyChannels.map(String) : null;
  const expectedEmotionChannels = safeEmotionChannels.map(item => String(item.channel)).sort();
  const expectedBodyChannels = safeBodyChannels.map(item => String(item.channel)).sort();
  if (!declaredEmotionChannels) rejectionReasons.push({ parameterId: null, reason: 'safeEmotionChannels-must-be-array' });
  else if (JSON.stringify([...declaredEmotionChannels].sort()) !== JSON.stringify(expectedEmotionChannels)) rejectionReasons.push({ parameterId: null, reason: 'safeEmotionChannels-do-not-match-allowlist' });
  if (!declaredBodyChannels) rejectionReasons.push({ parameterId: null, reason: 'safeBodyChannels-must-be-array' });
  else if (JSON.stringify([...declaredBodyChannels].sort()) !== JSON.stringify(expectedBodyChannels)) rejectionReasons.push({ parameterId: null, reason: 'safeBodyChannels-do-not-match-allowlist' });
  const constraints = profile.constraints || {};
  for (const key of ['freeBodyMotion', 'tts', 'lipSync', 'pixi', 'runtimeCoreSession', 'onlineUnityWrite']) {
    if (constraints[key] !== false) rejectionReasons.push({ parameterId: null, reason: `constraint-${key}-must-be-false` });
  }
  const report = {
    schema: 'soullink-profile-audit/v1',
    profileSha256: sha256(profileBytes || canonicalJson(profile)),
    catalogSha256: sha256(catalogBytes || canonicalJson(catalog)),
    catalogModelSha256: catalog.source?.modelSha256 || null,
    engine: profile.engine || null,
    model: profile.model || null,
    parameterCoverage: coverage,
    missingParameters: missing,
    reservedParameters: reserved,
    safeEmotionChannels,
    safeBodyChannels,
    constraints,
    status: rejectionReasons.length ? 'rejected' : 'audited',
    rejectionReasons,
    safety: { productionStatus: 'not-certified', mapWriteAllowed: false, llmExposureAllowed: false }
  };
  return report;
}

function normalizeMatrix(matrix) {
  if (!matrix || !Array.isArray(matrix.emotions) || !Array.isArray(matrix.intensities) || !Array.isArray(matrix.seeds)) throw new Error('matrix emotions/intensities/seeds arrays are required');
  if (!matrix.emotions.length || !matrix.intensities.length || !matrix.seeds.length) throw new Error('matrix must not be empty');
  const emotions = matrix.emotions.map(String);
  const intensities = matrix.intensities.map(Number);
  const seeds = matrix.seeds.map(Number);
  if (emotions.some(value => !value || value.length > 80) || intensities.some(value => !Number.isFinite(value) || value < 0 || value > 1) || seeds.some(value => !Number.isInteger(value))) throw new Error('matrix contains invalid emotion, intensity, or seed');
  const unique = new Set();
  const cases = [];
  for (const emotion of emotions) for (const intensity of intensities) for (const seed of seeds) {
    const key = `${emotion}\u0000${intensity}\u0000${seed}`;
    if (unique.has(key)) throw new Error(`duplicate matrix case: ${key}`);
    unique.add(key);
    const safeEmotion = /^[A-Za-z0-9_-]+$/.test(emotion) ? emotion : `u${Buffer.from(emotion, 'utf8').toString('hex')}`;
    const safeIntensity = String(intensity).replace('.', '_');
    const caseId = `${safeEmotion}-${safeIntensity}-${seed}`;
    if (caseId.includes('..') || caseId.startsWith('.') || caseId.endsWith('.')) throw new Error(`emotion cannot safely form a caseId: ${emotion}`);
    cases.push({ emotion, intensity, seed, caseId });
  }
  if (new Set(cases.map(item => item.caseId)).size !== cases.length) throw new Error('matrix caseId collision');
  cases.sort((a, b) => a.emotion.localeCompare(b.emotion) || a.intensity - b.intensity || a.seed - b.seed);
  const frameRate = Number(matrix.frameRate || 30);
  const durationSeconds = Number(matrix.durationSeconds || 2);
  const settleSeconds = Number(matrix.settleSeconds || 0);
  if (!Number.isInteger(frameRate) || frameRate <= 0 || !Number.isFinite(durationSeconds) || durationSeconds <= 0 || !Number.isFinite(settleSeconds) || settleSeconds < 0) throw new Error('matrix timing is invalid');
  return { cases, frameRate, durationSeconds, settleSeconds, maxCases: matrix.maxCases || null };
}

function resolveNeutral(item, catalogEntry) {
  if (item.neutral !== undefined) return Number(item.neutral);
  if (item.default !== undefined) return Number(item.default);
  if (catalogEntry?.range?.baseline !== undefined) return Number(catalogEntry.range.baseline);
  throw new Error(`no neutral/default/baseline for ${item.parameterId}`);
}

function parameterAllowlist(audit) {
  return new Map([...audit.safeEmotionChannels, ...audit.safeBodyChannels].map(item => [item.parameterId, item]));
}

module.exports = { sha256, readJson, writeJson, canonicalize, canonicalJson, hashJson, assertTestRoot, isTemporaryRoot, catalogParameterMap, auditProfile, normalizeMatrix, resolveNeutral, parameterAllowlist };
