'use strict';

// Conservative local summary for an isolated wave-sequence report.
// It never assigns semantic names and never writes mappings.
// Usage: node scripts/live2d-probe/summarize_wave_sequence.cjs <isolated-probe-root>

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const root = process.argv[2];
if (!root || !path.isAbsolute(root)) throw new Error('Pass an absolute isolated probe root.');
if (!fs.existsSync(path.join(root, '.test_mode'))) throw new Error('Refusing to summarize a non-test root.');
const reportPath = path.join(root, 'wave-sequence-report.json');
if (!fs.existsSync(reportPath)) throw new Error('Wave sequence report is missing.');

const reportBytes = fs.readFileSync(reportPath);
const report = JSON.parse(reportBytes.toString('utf8'));
const requiredStages = ['baseline', 'raise_arm', 'set_hand', 'wrist_swing', 'return_arm', 'reset_hand', 'reset'];
const auxiliaryStages = ['set_auxiliary', 'reset_auxiliary'];
const sha256 = value => crypto.createHash('sha256').update(value).digest('hex');
const finite = value => Number.isFinite(value);

if (report.mapWriteAllowed !== false) throw new Error('Refusing report with mapWriteAllowed=true.');
if (!Array.isArray(report.repeatsData) || report.repeatsData.length !== report.repeats) throw new Error('Invalid repeatsData.');
if (!Array.isArray(report.parameterIds) || report.parameterIds.length === 0) throw new Error('Missing parameter IDs.');

const parameterRanges = report.parameterIds.map((parameterId, index) => ({
  parameterId,
  baseline: report.baselines[index],
  minimum: report.minimums[index],
  maximum: report.maximums[index],
  baselineWithinNativeRange: finite(report.baselines[index]) && finite(report.minimums[index]) && finite(report.maximums[index])
    && report.baselines[index] >= report.minimums[index] && report.baselines[index] <= report.maximums[index]
}));

const repeats = report.repeatsData.map(item => {
  const frames = Array.isArray(item.frames) ? item.frames : [];
  const stages = frames.map(frame => frame.stage);
  const uniqueStages = [...new Set(stages)];
  const missingRequiredStages = requiredStages.filter(stage => !uniqueStages.includes(stage));
  const hasAuxiliary = auxiliaryStages.some(stage => uniqueStages.includes(stage));
  const expectedStageSet = hasAuxiliary ? [...requiredStages.slice(0, 3), 'set_auxiliary', 'wrist_swing', 'return_arm', 'reset_hand', 'reset_auxiliary', 'reset'] : requiredStages;
  const stageOrderValid = expectedStageSet.every((stage, index) => {
    const first = stages.indexOf(stage);
    return first >= 0 && (index === 0 || first > stages.indexOf(expectedStageSet[index - 1]));
  });
  const valuesWithinRange = frames.every(frame => Array.isArray(frame.parameterValues)
    && frame.parameterValues.length === report.parameterIds.length
    && frame.parameterValues.every((value, index) => finite(value) && value >= report.minimums[index] - 1e-4 && value <= report.maximums[index] + 1e-4));
  return {
    repeat: item.repeat,
    frameCount: frames.length,
    uniqueStages,
    missingRequiredStages,
    stageOrderValid,
    valuesWithinNativeRanges: valuesWithinRange,
    peakMeanDifference: item.peakMeanDifference,
    maxAdjacentMeanDifference: item.maxAdjacentMeanDifference,
    resetMeanDifference: item.resetMeanDifference,
    resetStable: item.resetStable,
    hasAuxiliary
  };
});

const summary = {
  schema: 'live2d-wave-sequence-local-summary/v1',
  sourceReport: reportPath,
  reportSha256: sha256(reportBytes),
  candidateId: report.candidateId,
  writerMode: report.writerMode,
  parameterIds: report.parameterIds,
  parameterRanges,
  repeats: report.repeats,
  framesPerRepeat: report.framesPerRepeat,
  stageContract: { requiredStages, auxiliaryStages, semanticStatus: 'unassigned' },
  repeatsData: repeats,
  peakMeanDifference: report.peakMeanDifference,
  maxResetMeanDifference: report.maxResetMeanDifference,
  resetStable: report.resetStable,
  safety: { mapWriteAllowed: false, semanticStatus: 'unassigned', cloudReview: false },
  mechanicalChecks: {
    allStagesPresent: repeats.every(item => item.missingRequiredStages.length === 0),
    stageOrderValid: repeats.every(item => item.stageOrderValid),
    valuesWithinNativeRanges: repeats.every(item => item.valuesWithinNativeRanges),
    resetStable: report.resetStable === true && repeats.every(item => item.resetStable === true)
  }
};

const output = path.join(root, 'wave-sequence-report.local-summary.json');
fs.writeFileSync(output, JSON.stringify(summary, null, 2), 'utf8');
console.log(JSON.stringify({ output, reportSha256: summary.reportSha256, mechanicalChecks: summary.mechanicalChecks }, null, 2));
