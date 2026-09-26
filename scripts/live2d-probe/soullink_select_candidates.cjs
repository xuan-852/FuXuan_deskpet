'use strict';

const fs = require('fs');
const path = require('path');
const { readJson, writeJson, sha256, hashJson, assertTestRoot } = require('./lib/soullink_offline.cjs');

function sampleChannel(channel, phase) {
  const samples = channel.samples || [];
  if (!samples.length) return null;
  if (phase <= samples[0].phase) return samples[0].value;
  if (phase >= samples[samples.length - 1].phase) return samples[samples.length - 1].value;
  for (let i = 1; i < samples.length; i++) {
    if (phase <= samples[i].phase) {
      const left = samples[i - 1], right = samples[i];
      const span = right.phase - left.phase;
      if (!(span > 0)) return right.value;
      const mix = (phase - left.phase) / span;
      return left.value + (right.value - left.value) * mix;
    }
  }
  return samples[samples.length - 1].value;
}

function vectorize(packet, gridSize = 16) {
  const channels = new Map((packet.channels || []).map(channel => [channel.feature, channel]));
  const names = [...channels.keys()].sort();
  const vector = [];
  for (const name of names) {
    const channel = channels.get(name);
    const range = Number(channel.maximum) - Number(channel.minimum);
    for (let i = 0; i < gridSize; i++) {
      const value = sampleChannel(channel, gridSize === 1 ? 0 : i / (gridSize - 1));
      vector.push(range > 0 ? (value - Number(channel.minimum)) / range : 0);
    }
  }
  return { names, vector };
}

function distance(left, right, gridSize, missingPenalty) {
  const a = vectorize(left.packet, gridSize), b = vectorize(right.packet, gridSize);
  const names = [...new Set([...a.names, ...b.names])].sort();
  let sum = 0, count = 0;
  for (const name of names) {
    const hasA = a.names.includes(name), hasB = b.names.includes(name);
    if (!hasA || !hasB) { sum += missingPenalty * missingPenalty * gridSize; count += gridSize; continue; }
    const channelIndexA = a.names.indexOf(name), channelIndexB = b.names.indexOf(name);
    for (let i = 0; i < gridSize; i++) {
      const delta = a.vector[channelIndexA * gridSize + i] - b.vector[channelIndexB * gridSize + i];
      sum += delta * delta; count += 1;
    }
  }
  const durationDelta = Math.abs(Number(left.packet.timing.observedDurationSeconds) - Number(right.packet.timing.observedDurationSeconds));
  return Math.sqrt(sum / Math.max(1, count)) + Math.min(1, durationDelta / Math.max(1, Number(left.packet.timing.observedDurationSeconds), Number(right.packet.timing.observedDurationSeconds))) * 0.1;
}

function contentFingerprint(packet) {
  return hashJson({
    timing: packet.timing,
    channels: (packet.channels || []).map(channel => ({
      feature: channel.feature,
      sourceParameterId: channel.sourceParameterId,
      minimum: channel.minimum,
      maximum: channel.maximum,
      samples: channel.samples
    })).sort((a, b) => `${a.feature}:${a.sourceParameterId}`.localeCompare(`${b.feature}:${b.sourceParameterId}`))
  });
}

function selectCandidates(entries, options = {}) {
  const gridSize = Number(options.gridSize || 16);
  const threshold = Number(options.distanceThreshold ?? 0.08);
  const missingPenalty = Number(options.missingChannelPenalty ?? 1);
  const topK = Number(options.topK || 3);
  if (!Number.isInteger(gridSize) || gridSize < 2 || gridSize > 256 || !Number.isFinite(threshold) || threshold < 0 || !Number.isFinite(missingPenalty) || missingPenalty < 0 || !Number.isInteger(topK) || topK < 1) throw new Error('invalid selection options');
  const sorted = entries.slice().sort((a, b) => String(a.candidate.candidateId).localeCompare(String(b.candidate.candidateId)));
  const survivors = [], rejected = [];
  const hashes = new Map();
  for (const entry of sorted) {
    const featureSha256 = hashJson(entry.packet);
    const contentHash = contentFingerprint(entry.packet);
    if (hashes.has(contentHash)) {
      rejected.push({ candidateId: entry.candidate.candidateId, reason: 'exact-duplicate', duplicateOf: hashes.get(contentHash), featureSha256, contentFingerprint: contentHash });
      continue;
    }
    hashes.set(contentHash, entry.candidate.candidateId);
    const neighbors = survivors.map(candidate => ({ candidateId: candidate.candidate.candidateId, distance: distance(entry, candidate, gridSize, missingPenalty) })).sort((a, b) => a.distance - b.distance || a.candidateId.localeCompare(b.candidateId)).slice(0, topK);
    const nearest = neighbors[0];
    if (nearest && nearest.distance <= threshold) {
      rejected.push({ candidateId: entry.candidate.candidateId, reason: 'near-duplicate', nearestRepresentative: nearest.candidateId, distance: nearest.distance, threshold, nearestNeighbors: neighbors, featureSha256, contentFingerprint: contentHash });
      continue;
    }
    survivors.push({ ...entry, featureSha256, contentFingerprint: contentHash, nearestNeighbors: neighbors });
  }
  return { gridSize, threshold, missingPenalty, topK, survivors, rejected };
}

function main() {
  const [outputRoot, optionsPath] = process.argv.slice(2);
  if (!outputRoot || !path.isAbsolute(outputRoot)) throw new Error('Usage: soullink_select_candidates.cjs <absolute-test-root> [absolute-options.json]');
  assertTestRoot(outputRoot);
  const options = optionsPath ? readJson(optionsPath).value : {};
  const candidateDir = path.join(outputRoot, 'candidates');
  const packetDir = path.join(outputRoot, 'feature-packets');
  const entries = fs.readdirSync(candidateDir).filter(file => file.endsWith('.json')).sort().map(file => {
    const candidatePath = path.join(candidateDir, file);
    const candidateDoc = readJson(candidatePath);
    const packetPath = path.join(packetDir, `${candidateDoc.value.candidateId}.json`);
    if (!fs.existsSync(packetPath)) throw new Error(`feature packet missing for ${candidateDoc.value.candidateId}`);
    return { candidate: candidateDoc.value, candidatePath, candidateSha256: sha256(candidateDoc.bytes), packet: readJson(packetPath).value, packetPath };
  });
  const result = selectCandidates(entries, options);
  const core = {
    schema: 'soullink-selection-report/v1',
    configuration: { gridSize: result.gridSize, distanceThreshold: result.threshold, missingChannelPenalty: result.missingPenalty, topK: result.topK },
    survivors: result.survivors.map(item => ({ candidateId: item.candidate.candidateId, candidatePath: item.candidatePath, candidateSha256: item.candidateSha256, featurePacketPath: item.packetPath, featureSha256: item.featureSha256, nearestNeighbors: item.nearestNeighbors, nextStep: 'isolated-player-capture-before-cloud-review' })),
    rejected: result.rejected,
    safety: { evidenceOnly: true, productionStatus: 'not-certified', mapWriteAllowed: false, llmExposureAllowed: false }
  };
  core.selectionSha256 = hashJson(core);
  writeJson(path.join(outputRoot, 'selection-report.json'), core);
  writeJson(path.join(outputRoot, 'cloud-review-survivors.json'), { schema: 'soullink-cloud-review-survivors/v1', selectionSha256: core.selectionSha256, requiresIsolatedPlayerCapture: true, survivors: core.survivors, cloudRequestsMade: 0 });
  console.log(JSON.stringify({ candidates: entries.length, survivors: core.survivors.length, rejected: core.rejected.length, selectionSha256: core.selectionSha256 }, null, 2));
}
if (require.main === module) { try { main(); } catch (error) { console.error(`soullink_select_candidates: ${error.message}`); process.exitCode = 1; } }
module.exports = { sampleChannel, vectorize, distance, selectCandidates };
