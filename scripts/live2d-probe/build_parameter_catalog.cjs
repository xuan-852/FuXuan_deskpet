/* Builds a read-only parameter capability catalog from isolated local + DeepSeek evidence.
 * Usage: node scripts/live2d-probe/build_parameter_catalog.cjs <isolated-probe-root>
 */
const fs = require('fs');
const path = require('path');
const root = process.argv[2];
if (!root || !path.isAbsolute(root) || !fs.existsSync(path.join(root, '.test_mode'))) throw new Error('Pass an isolated root containing .test_mode.');
const report = JSON.parse(fs.readFileSync(path.join(root, 'capability-report-probe-window.json'), 'utf8'));
const aggregate = JSON.parse(fs.readFileSync(path.join(root, 'cloud_review', 'aggregate-summary.json'), 'utf8'));
const deepseek = new Map(aggregate.entries.filter(x => x.provider === 'deepseek' && x.status === 'ok' && x.parseStatus === 'parsed').map(x => [x.parameterId, x]));
function role(verdict) {
  if (!verdict?.visible_change) return 'no-visible-evidence';
  const guess = String(verdict.semantic_guess || '').toLowerCase();
  const summary = `${verdict.change_summary || ''} ${verdict.min_vs_max || ''}`;
  if (guess === 'unknown') return /显示|装饰|开关|特效/.test(summary) ? 'effect-only' : 'unclassified-visible';
  return /(头|脸|眼|眉|口|嘴|手|臂|身体|躯干|姿态|倾斜|旋转)/.test(guess) ? 'pose-candidate' : 'unclassified-visible';
}
const parameters = report.parameters.map(local => {
  const review = deepseek.get(local.parameterId);
  const verdict = review?.verdict || null;
  return {
    parameterId: local.parameterId,
    range: { minimum: local.minimum, maximum: local.maximum, baseline: local.baseline },
    local: {
      peakMeanPixelDifference: Math.max(local.minMeanDifference, local.maxMeanDifference),
      resetStable: local.resetStable,
      frameCount: local.frames.length
    },
    deepseek: verdict ? {
      visibleChange: verdict.visible_change,
      resetMatchesBaseline: verdict.reset_matches_baseline,
      semanticGuess: verdict.semantic_guess || 'unknown',
      semanticConfidence: verdict.semantic_confidence || 'unspecified',
      summary: verdict.change_summary || '',
      evidenceFile: review.evidenceFile
    } : { status: 'missing' },
    provisionalRole: role(verdict),
    certification: 'not-certified',
    mapWriteAllowed: false
  };
});
const catalog = {
  schema: 'live2d-parameter-capability-catalog/v1',
  source: { writerMode: report.writerMode, repeats: report.repeats, deepseekOnly: true, aggregateTokenTotal: aggregate.tokenTotal },
  safety: { mapWriteAllowed: false, semanticMappingsAuthoritative: false },
  counts: parameters.reduce((out, x) => { out[x.provisionalRole] = (out[x.provisionalRole] || 0) + 1; return out; }, {}),
  parameters
};
const output = path.join(root, 'parameter-capability-catalog.json');
fs.writeFileSync(output, JSON.stringify(catalog, null, 2), 'utf8');
console.log(`Catalog written: ${output}`);
console.log(JSON.stringify(catalog.counts));
