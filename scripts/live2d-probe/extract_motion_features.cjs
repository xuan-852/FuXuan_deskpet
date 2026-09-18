/*
 * Extract a model-neutral evidence packet from one local Cubism motion3 file.
 * Usage: node extract_motion_features.cjs <absolute-motion3.json> <absolute-output.json>
 * It never writes candidate curves, parameter maps, runtime data, or network requests.
 */
'use strict';
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const input = process.argv[2];
const output = process.argv[3];
if (!input || !output || !path.isAbsolute(input) || !path.isAbsolute(output))
  throw new Error('Pass absolute input and output paths.');
const bytes = fs.readFileSync(input);
const motion = JSON.parse(bytes.toString('utf8'));
const aliases = {
  'ParamAngleX': 'head.yaw', 'ParamAngleY': 'head.pitch', 'ParamAngleZ': 'head.roll',
  'ParamBodyAngleX': 'torso.pitch', 'ParamBodyAngleY': 'torso.yaw', 'ParamBodyAngleZ': 'torso.roll',
  'ParamEyeBallX': 'gaze.horizontal', 'ParamEyeBallY': 'gaze.vertical',
  'ParamEyeLOpen': 'eye.left.open', 'ParamEyeROpen': 'eye.right.open',
  'ParamArmLA': 'arm.left.upper', 'ParamArmRA': 'arm.right.upper',
  'ParamHandL': 'hand.left.pose', 'ParamHandR': 'hand.right.pose'
};

function endpoints(segments) {
  if (!Array.isArray(segments) || segments.length < 2) return [];
  const result = [[segments[0], segments[1]]];
  for (let i = 2; i < segments.length;) {
    const type = segments[i];
    let next;
    // Cubism motion3 segments encode Linear/Stepped/InverseStepped as
    // [type, endTime, endValue] and Bezier as
    // [type, c1Time, c1Value, c2Time, c2Value, endTime, endValue].
    if (type === 0 || type === 2 || type === 3) next = [segments[i + 1], segments[i + 2], 3];
    else if (type === 1) next = [segments[i + 5], segments[i + 6], 7];
    else break;
    if (!Number.isFinite(next[0]) || !Number.isFinite(next[1])) break;
    result.push([next[0], next[1]]); i += next[2];
  }
  return result;
}

const duration = Number(motion.Meta?.Duration);
if (!(duration > 0)) throw new Error('Motion has no positive duration.');
const observedDuration = Math.max(
  duration,
  ...(motion.Curves || []).map(curve => {
    const samples = endpoints(curve.Segments);
    return samples.length ? samples[samples.length - 1][0] : 0;
  })
);
const channels = (motion.Curves || []).filter(c => c.Target === 'Parameter' && aliases[c.Id]).map(c => {
  const samples = endpoints(c.Segments);
  const values = samples.map(x => x[1]);
  return {
    feature: aliases[c.Id], sourceParameterId: c.Id,
    samples: samples.map(([time, value]) => ({ phase: Number((time / observedDuration).toFixed(6)), value })),
    minimum: Math.min(...values), maximum: Math.max(...values)
  };
});
const packet = {
  schema: 'l3-motion-feature-packet/v1',
  source: { fileName: path.basename(input), sha256: crypto.createHash('sha256').update(bytes).digest('hex'), modelParameterSemanticsUnverified: true },
  timing: { declaredDurationSeconds: duration, observedDurationSeconds: observedDuration, fps: Number(motion.Meta?.Fps) || null, loop: Boolean(motion.Meta?.Loop) },
  channels,
  constraints: { evidenceOnly: true, mayNotGenerateRuntimeCurve: true, mayNotEstablishFuxuanParameterMapping: true }
};
fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, JSON.stringify(packet, null, 2) + '\n', 'utf8');
console.log(JSON.stringify({ output, channels: channels.length, sourceSha256: packet.source.sha256 }));
