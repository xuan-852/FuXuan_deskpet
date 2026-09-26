'use strict';

const path = require('path');
const { readJson, writeJson, sha256, auditProfile, catalogParameterMap, resolveNeutral, parameterAllowlist } = require('./lib/soullink_offline.cjs');

function buildLinearSegments(points) {
  if (!points.length) throw new Error('curve has no points');
  const sorted = points.slice().sort((a, b) => a.timeSeconds - b.timeSeconds);
  const result = [0, sorted[0].value];
  let lastTime = 0;
  for (const point of sorted.slice(1)) {
    if (!(point.timeSeconds > lastTime)) continue;
    result.push(0, point.timeSeconds, point.value);
    lastTime = point.timeSeconds;
  }
  if (result.length < 5) result.push(0, sorted[0].timeSeconds + 0.000001, sorted[0].value);
  return result;
}

function buildCandidate(timeline, profile, catalog, profileBytes, catalogBytes, timelineBytes) {
  if (timeline.schema !== 'soullink-offline-timeline/v1') throw new Error('unsupported timeline schema');
  if (!Array.isArray(timeline.frames) || !timeline.frames.length) throw new Error('timeline has no frames');
  const audit = auditProfile(profile, catalog, profileBytes, catalogBytes);
  if (audit.status !== 'audited') throw new Error('profile audit rejected');
  const catalogMap = catalogParameterMap(catalog);
  const allowlist = parameterAllowlist(audit);
  const profileItems = new Map((profile.parameters || []).map(item => [String(item.parameterId), item]));
  const total = Number(timeline.timing?.durationSeconds || 0) + Number(timeline.timing?.settleSeconds || 0);
  if (!(total > 0) || !Number.isFinite(total)) throw new Error('timeline duration must be positive');
  const ids = [...new Set((timeline.parameterIds || []).map(String))].sort();
  const curves = [];
  const rejections = [];
  for (const id of ids) {
    const allow = allowlist.get(id);
    const catalogEntry = catalogMap.get(id);
    if (!allow || !catalogEntry) { rejections.push({ parameterId: id, reason: 'not-allowlisted-or-catalog-missing' }); continue; }
    const item = profileItems.get(id) || {};
    const values = [];
    for (const frame of timeline.frames) {
      if (!frame.parameters || frame.parameters[id] === undefined) continue;
      const value = Number(frame.parameters[id]);
      if (!Number.isFinite(value) || !Number.isFinite(frame.timeSeconds)) { rejections.push({ parameterId: id, reason: 'non-finite-frame-value' }); continue; }
      if (value < Number(catalogEntry.range.minimum) || value > Number(catalogEntry.range.maximum)) rejections.push({ parameterId: id, reason: 'value-outside-catalog-range', timeSeconds: frame.timeSeconds });
      values.push({ timeSeconds: Math.min(total, Math.max(0, Number(frame.timeSeconds))), value });
    }
    if (!values.length) { rejections.push({ parameterId: id, reason: 'no-timeline-values' }); continue; }
    const neutral = resolveNeutral(item, catalogEntry);
    if (neutral < Number(catalogEntry.range.minimum) || neutral > Number(catalogEntry.range.maximum)) { rejections.push({ parameterId: id, reason: 'neutral-outside-catalog-range' }); continue; }
    const deduped = [];
    for (const point of values.sort((a, b) => a.timeSeconds - b.timeSeconds)) {
      if (deduped.length && deduped[deduped.length - 1].timeSeconds === point.timeSeconds) deduped[deduped.length - 1] = point;
      else deduped.push(point);
    }
    const last = deduped[deduped.length - 1];
    if (last.timeSeconds < total) deduped.push({ timeSeconds: total, value: neutral });
    else deduped[deduped.length - 1] = { timeSeconds: total, value: neutral };
    curves.push({ parameterId: id, segments: buildLinearSegments(deduped), samples: deduped });
  }
  if (rejections.length) throw new Error(`timeline rejected: ${rejections.map(item => `${item.parameterId || '<unknown>'}:${item.reason}`).join(', ')}`);
  const candidateId = `soullink_${timeline.case?.caseId || sha256(timelineBytes).slice(0, 12)}`;
  return {
    schema: 'l3-soullink-motion-candidate/v1', candidateId, durationSeconds: total, curves,
    source: { timelineSha256: sha256(timelineBytes), profileSha256: sha256(profileBytes), catalogSha256: sha256(catalogBytes) },
    generation: { case: timeline.case || null, frameRate: timeline.timing?.frameRate || null, neutralCompletion: 'explicit-profile-neutral-or-catalog-baseline', segmentSemantics: 'cubism-linear-type-0' },
    certification: { status: 'uncertified', reviewStatus: 'needs-local-selection' },
    productionStatus: 'not-certified', mapWriteAllowed: false, llmExposureAllowed: false,
    constraints: { evidenceOnly: true, freeBodyMotion: false, tts: false, lipSync: false, pixi: false, runtimeCoreSession: false, onlineUnityWrite: false }
  };
}

function main() {
  const [timelinePath, profilePath, catalogPath, outputPath] = process.argv.slice(2);
  if (![timelinePath, profilePath, catalogPath, outputPath].every(value => value && path.isAbsolute(value))) throw new Error('Usage: soullink_timeline_to_candidate.cjs <absolute-timeline> <absolute-profile> <absolute-catalog> <absolute-output>');
  const timeline = readJson(timelinePath), profile = readJson(profilePath), catalog = readJson(catalogPath);
  const candidate = buildCandidate(timeline.value, profile.value, catalog.value, profile.bytes, catalog.bytes, timeline.bytes);
  writeJson(outputPath, candidate);
  console.log(JSON.stringify({ output: outputPath, candidateId: candidate.candidateId, curves: candidate.curves.length, status: candidate.certification.status }, null, 2));
}
if (require.main === module) { try { main(); } catch (error) { console.error(`soullink_timeline_to_candidate: ${error.message}`); process.exitCode = 1; } }
module.exports = { buildLinearSegments, buildCandidate };
