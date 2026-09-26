'use strict';

const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const { readJson, writeJson, sha256, assertTestRoot } = require('./lib/soullink_offline.cjs');

function candidateToMotion3(candidate) {
  if (candidate.schema !== 'l3-soullink-motion-candidate/v1' || !Array.isArray(candidate.curves)) throw new Error('unsupported SoulLink candidate schema');
  return {
    Version: 3,
    Meta: { Duration: Number(candidate.durationSeconds), Fps: 30, Loop: false, AreBeziersRestricted: false },
    Curves: candidate.curves.map(curve => ({ Target: 'Parameter', Id: curve.parameterId, Segments: curve.segments }))
  };
}

function extractFeatures(candidatePath, outputRoot, outputPath) {
  assertTestRoot(outputRoot);
  if (!path.isAbsolute(candidatePath) || !path.isAbsolute(outputRoot) || !path.isAbsolute(outputPath)) throw new Error('candidate, outputRoot, and output paths must be absolute');
  const candidateDoc = readJson(candidatePath);
  const envelope = candidateToMotion3(candidateDoc.value);
  const envelopePath = path.join(outputRoot, 'motion-envelopes', `${candidateDoc.value.candidateId}.motion3.json`);
  const extractorOutput = path.join(outputRoot, 'feature-packets', `${candidateDoc.value.candidateId}.base.json`);
  writeJson(envelopePath, envelope);
  const result = spawnSync(process.execPath, [path.join(__dirname, 'extract_motion_features.cjs'), envelopePath, extractorOutput], { encoding: 'utf8' });
  if (result.status !== 0) throw new Error(`feature extractor failed: ${(result.stderr || result.stdout || '').trim()}`);
  const packetDoc = readJson(extractorOutput);
  const packet = packetDoc.value;
  packet.source = { ...packet.source, candidateSha256: sha256(candidateDoc.bytes), motionEnvelopeSha256: sha256(fs.readFileSync(envelopePath)), timelineSha256: candidateDoc.value.source?.timelineSha256 || null };
  packet.constraints = { ...packet.constraints, evidenceOnly: true, mayNotGenerateRuntimeCurve: true, mayNotEstablishFuxuanParameterMapping: true };
  writeJson(outputPath, packet);
  return { packet, envelopePath, extractorOutput, candidateSha256: sha256(candidateDoc.bytes), motionEnvelopeSha256: sha256(fs.readFileSync(envelopePath)), featurePacketSha256: sha256(Buffer.from(JSON.stringify(packet))) };
}

function main() {
  const [candidatePath, outputRoot, outputPath] = process.argv.slice(2);
  if (![candidatePath, outputRoot, outputPath].every(Boolean)) throw new Error('Usage: soullink_extract_features.cjs <absolute-candidate> <absolute-test-root> <absolute-feature-output>');
  const result = extractFeatures(candidatePath, outputRoot, outputPath);
  console.log(JSON.stringify({ output: outputPath, channels: result.packet.channels.length, candidateSha256: result.candidateSha256, featurePacketSha256: result.featurePacketSha256 }, null, 2));
}
if (require.main === module) { try { main(); } catch (error) { console.error(`soullink_extract_features: ${error.message}`); process.exitCode = 1; } }
module.exports = { candidateToMotion3, extractFeatures };
