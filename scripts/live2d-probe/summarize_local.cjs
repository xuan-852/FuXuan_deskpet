/* Conservative local classifier for an isolated ProbeWindow report.
 * It does not assign semantic names or modify any parameter map.
 * Usage: node scripts/live2d-probe/summarize_local.cjs <isolated-probe-root>
 */
const fs = require('fs');
const path = require('path');

const root = process.argv[2];
if (!root || !path.isAbsolute(root)) throw new Error('Pass an absolute isolated probe root.');
if (!fs.existsSync(path.join(root, '.test_mode'))) throw new Error('Refusing to classify a non-test root.');
const reportPath = path.join(root, 'capability-report-probe-window.json');
if (!fs.existsSync(reportPath)) throw new Error('Probe report is missing.');
const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));

function classify(parameter) {
  const peakDifference = Math.max(parameter.minMeanDifference, parameter.maxMeanDifference);
  let localVisualTier = 'near-zero';
  if (peakDifference >= 1) localVisualTier = 'strong';
  else if (peakDifference >= 0.25) localVisualTier = 'weak';
  const physicsSensitive = /^ParamBodyAngle[XYZ]$/.test(parameter.parameterId);
  const state = !parameter.resetStable ? 'unreliable-reset'
    : physicsSensitive && localVisualTier === 'near-zero' ? 'requires-physics-recheck'
    : localVisualTier === 'strong' ? 'visual-review-candidate'
    : localVisualTier === 'weak' ? 'weak-visual-candidate'
    : 'no-local-visible-evidence';
  return {
    parameterId: parameter.parameterId,
    peakMeanPixelDifference: Number(peakDifference.toFixed(6)),
    resetStable: parameter.resetStable,
    localVisualTier,
    state,
    semanticStatus: 'unassigned',
    mapWriteAllowed: false
  };
}

const parameters = report.parameters.map(classify);
const summary = {
  schema: 'live2d-probe-local-summary/v1',
  sourceReport: reportPath,
  repeats: report.repeats,
  thresholds: { strong: 1, weak: 0.25, unit: 'mean RGB channel difference (0-255)' },
  safety: { semanticStatus: 'unassigned for every result', mapWriteAllowed: false },
  parameters
};
const output = path.join(root, 'capability-report-probe-window.local-summary.json');
fs.writeFileSync(output, JSON.stringify(summary, null, 2), 'utf8');
console.log(`Local summary written: ${output}`);
for (const item of parameters) console.log(`${item.parameterId}\t${item.state}\t${item.peakMeanPixelDifference}`);
