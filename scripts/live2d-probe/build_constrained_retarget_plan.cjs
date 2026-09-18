/*
 * Turn a reviewed, local MotionFeaturePacket into an evidence-only retarget plan.
 * Usage: node build_constrained_retarget_plan.cjs <absolute-manifest.json> <absolute-feature-packet.json> <absolute-output.json>
 * This tool intentionally never emits Live2D parameter IDs, curve points, runtime assets, or certification outcomes.
 */
'use strict';
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const [manifestPath, packetPath, outputPath] = process.argv.slice(2);
if (![manifestPath, packetPath, outputPath].every(path.isAbsolute))
  throw new Error('Pass absolute manifest, feature-packet, and output paths.');

function readJson(file) {
  const bytes = fs.readFileSync(file);
  return { bytes, value: JSON.parse(bytes.toString('utf8')) };
}
function sha256(bytes) { return crypto.createHash('sha256').update(bytes).digest('hex'); }
function activity(channel) {
  if (!Array.isArray(channel.samples) || channel.samples.length < 2) return -1;
  let total = 0;
  for (let i = 1; i < channel.samples.length; i++) total += Math.abs(channel.samples[i].value - channel.samples[i - 1].value);
  return total;
}
function landmark(samples, compare) {
  let best = samples[0];
  for (const sample of samples) if (compare(sample.value, best.value)) best = sample;
  return best.phase;
}

const manifest = readJson(manifestPath);
const packet = readJson(packetPath);
if (manifest.value.schema !== 'l3-reference-manifest/v1') throw new Error('Unsupported reference manifest schema.');
if (packet.value.schema !== 'l3-motion-feature-packet/v1') throw new Error('Unsupported motion feature packet schema.');
if (manifest.value.sourceSha256 !== packet.value.source.sha256) throw new Error('Manifest/source feature hash mismatch.');
if (manifest.value.licenseStatus !== 'reviewed-local-reference-only') throw new Error('Reference is not approved for local-reference-only use.');

const armChannels = (packet.value.channels || []).filter(channel => channel.feature === 'arm.left.upper' || channel.feature === 'arm.right.upper');
if (!armChannels.length) throw new Error('Feature packet contains no eligible abstract arm channel.');
const referenceChannel = armChannels.slice().sort((a, b) => activity(b) - activity(a))[0];
const peakPhase = landmark(referenceChannel.samples, (value, current) => Math.abs(value) > Math.abs(current));
const plan = {
  schema: 'l3-constrained-retarget-plan/v1',
  status: 'NeedsEvidence',
  source: {
    manifestSha256: sha256(manifest.bytes),
    featurePacketSha256: sha256(packet.bytes),
    sourceSha256: packet.value.source.sha256
  },
  intendedSemanticBoundary: 'single screen-side arm lift, brief hold, and return; not a wave, greeting, anatomical-side claim, or dance',
  targetCapability: {
    abstractCapabilityId: 'screen-side-arm-raise',
    resourceSlot: 'RightArm',
    targetParameterMapping: 'unassigned',
    certificationStatus: 'not-established-by-this-plan'
  },
  timingReference: {
    sourceFeature: referenceChannel.feature,
    sourceParameterSemanticsUnverified: true,
    startPhase: referenceChannel.samples[0].phase,
    peakPhase,
    returnPhase: referenceChannel.samples[referenceChannel.samples.length - 1].phase,
    normalizedOnly: true
  },
  constraints: {
    mayNotGenerateCurve: true,
    mayNotContainLive2DParameterIds: true,
    mayNotEstablishScreenOrAnatomicalSide: true,
    requiresStationaryRuntimeGate: true,
    requiresIndependentMechanicalVisualSemanticNaturalnessReview: true,
    requiresHumanSignatureBeforeCertification: true
  },
  unsupportedOrUnproven: [
    'source action semantic label', 'source-to-Fuxuan side correspondence',
    'source-to-Fuxuan parameter mapping', 'amplitude, speed, acceleration, and jerk limits',
    'runtime playback safety', 'visual naturalness', 'production certification'
  ]
};
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, JSON.stringify(plan, null, 2) + '\n', 'utf8');
console.log(JSON.stringify({ output: outputPath, status: plan.status, sourceFeature: referenceChannel.feature, peakPhase }));
