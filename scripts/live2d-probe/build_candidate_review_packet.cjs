/*
 * Freeze an isolated candidate-action capture into a deterministic review packet.
 * Usage: node scripts/live2d-probe/build_candidate_review_packet.cjs <isolated-root> [skill-id]
 * This utility never invokes a model or copies frames outside the isolated root.
 */
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const root = process.argv[2];
const skillId = process.argv[3] || 'screen_side_arm_raise';
if (!root || !path.isAbsolute(root)) throw new Error('Pass an absolute isolated capture root.');
if (!fs.existsSync(path.join(root, '.test_mode'))) throw new Error('Capture root must contain .test_mode.');
if (!/^[a-z0-9_\-]+$/i.test(skillId)) throw new Error('Skill ID must be a simple identifier.');

const frameDirectory = path.join(root, 'test_screenshots');
if (!fs.existsSync(frameDirectory)) throw new Error('Missing test_screenshots in isolated root.');
const frames = fs.readdirSync(frameDirectory)
  .filter(name => name.toLowerCase().endsWith('.png'))
  .map(name => {
    const file = path.join(frameDirectory, name);
    const stat = fs.statSync(file);
    return { file, name, modifiedMs: stat.mtimeMs, byteLength: stat.size };
  })
  .sort((a, b) => a.modifiedMs - b.modifiedMs || a.name.localeCompare(b.name));
if (frames.length < 5) throw new Error('Need at least five ordered PNG frames for a timed review.');

const phase = index => {
  if (index === 0) return 'baseline';
  if (index === frames.length - 1) return 'reset';
  return 'motion-' + String(Math.round(index * 100 / (frames.length - 1))).padStart(3, '0');
};
const digest = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const packetFrames = frames.map((frame, index) => ({
  order: index,
  phase: phase(index),
  file: path.relative(root, frame.file).replace(/\\/g, '/'),
  sha256: digest(frame.file),
  byteLength: frame.byteLength
}));
const packet = {
  schema: 'live2d-candidate-review-packet/v1',
  skillId,
  capture: { isolated: true, rootMarker: '.test_mode', frameCount: packetFrames.length },
  constraints: {
    evaluationMode: 'offline-only',
    requiredJudgements: ['semantic-boundary', 'sequence-continuity', 'reset-stability', 'walk-baseline-comparison'],
    prohibitedConclusion: 'certified-without-four-passed-evidence-layers'
  },
  frames: packetFrames
};
packet.packetSha256 = crypto.createHash('sha256').update(JSON.stringify(packet)).digest('hex');
const output = path.join(root, `candidate-review-packet-${skillId}.json`);
fs.writeFileSync(output, JSON.stringify(packet, null, 2) + '\n', 'utf8');
console.log(JSON.stringify({ output, packetSha256: packet.packetSha256, frameCount: packetFrames.length }, null, 2));
