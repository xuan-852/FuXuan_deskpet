/* UTF-8-safe aggregation of cached probe visual reviews.
 * Usage: node scripts/live2d-probe/aggregate_cloud_reviews.cjs <root> [...root]
 */
const fs = require('fs');
const path = require('path');
const roots = process.argv.slice(2);
if (!roots.length) throw new Error('Pass one or more isolated probe roots.');

const entries = [];
for (const root of roots) {
  if (!path.isAbsolute(root) || !fs.existsSync(path.join(root, '.test_mode'))) throw new Error(`Unsafe probe root: ${root}`);
  const dir = path.join(root, 'cloud_review');
  if (!fs.existsSync(dir)) continue;
  for (const name of fs.readdirSync(dir).filter(x => x.endsWith('-summary.json'))) {
    const summary = JSON.parse(fs.readFileSync(path.join(dir, name), 'utf8'));
    for (const review of summary.reviews || []) {
      let verdict = null;
      let parseStatus = 'not_applicable';
      if (review.status === 'ok') {
        const normalized = String(review.content || '').replace(/^\s*```(?:json)?\s*/i, '').replace(/\s*```\s*$/, '');
        try { verdict = JSON.parse(normalized); parseStatus = 'parsed'; }
        catch { parseStatus = 'unparseable-model-content'; }
      }
      entries.push({
        parameterId: summary.parameterId, provider: review.provider, model: review.model,
        status: review.status, httpStatus: review.httpStatus, usage: review.usage,
        parseStatus, verdict, error: review.error || null, evidenceFile: path.join(dir, name)
      });
    }
  }
}
entries.sort((a, b) => a.parameterId.localeCompare(b.parameterId) || a.provider.localeCompare(b.provider));
const output = {
  schema: 'live2d-probe-cloud-review-summary/v1',
  entryCount: entries.length,
  tokenTotal: entries.reduce((sum, x) => sum + (x.usage?.total_tokens || 0), 0),
  entries
};
const destination = path.join(roots[0], 'cloud_review', 'aggregate-summary.json');
fs.writeFileSync(destination, JSON.stringify(output, null, 2), 'utf8');
console.log(`Aggregate written: ${destination}`);
for (const item of entries) console.log(`${item.parameterId}\t${item.provider}\t${item.status}\t${item.parseStatus}`);
