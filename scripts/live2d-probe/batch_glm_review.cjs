/* Resumable GLM-only cross review of parameters in one isolated report.
 * Usage: node scripts/live2d-probe/batch_glm_review.cjs <root> <start> <limit> [ids-file]
 * Each child reuses cloud_review.cjs cache (fingerprint includes the model),
 * so rerunning a completed range has no API call. Set FU_XUAN_GLM_MODEL to
 * select the model (default glm-4.6v-flash; glm-4.5v substitute requires
 * explicit user authorization per probe-evaluation-accuracy guidance).
 */
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const root = process.argv[2];
const start = Number.parseInt(process.argv[3] || '0', 10);
const limit = Number.parseInt(process.argv[4] || '20', 10);
const idsFile = process.argv[5] || '';
if (!root || !path.isAbsolute(root) || !fs.existsSync(path.join(root, '.test_mode'))) throw new Error('Pass an isolated root containing .test_mode.');
if (!Number.isInteger(start) || start < 0 || !Number.isInteger(limit) || limit < 1) throw new Error('start must be >= 0 and limit must be >= 1.');
const report = JSON.parse(fs.readFileSync(path.join(root, 'capability-report-probe-window.json'), 'utf8'));
const allIds = report.parameters.map(item => item.parameterId).sort((a, b) => a.localeCompare(b));
let ids = allIds;
if (idsFile) {
  const subset = JSON.parse(fs.readFileSync(idsFile, 'utf8'));
  const known = new Set(allIds);
  for (const id of subset) if (!known.has(id)) throw new Error(`Unknown parameter id in ids-file: ${id}`);
  ids = [...subset].sort((a, b) => a.localeCompare(b));
}
const selected = ids.slice(start, start + limit);
if (!selected.length) { console.log('Nothing to review: range beyond list end.'); process.exit(0); }
const stateDir = path.join(root, 'cloud_review');
fs.mkdirSync(stateDir, { recursive: true });
const scope = idsFile ? 'subset' : 'all';
const statePath = path.join(stateDir, `glm-batch-${scope}-${start}-${start + selected.length - 1}.json`);
const results = [];
let usageTotals = { prompt_tokens: 0, completion_tokens: 0, total_tokens: 0, api_calls: 0 };
for (const parameterId of selected) {
  const child = spawnSync(process.execPath, [path.join(__dirname, 'cloud_review.cjs'), root, parameterId, 'glm'], {
    encoding: 'utf8', env: process.env, timeout: 180000
  });
  const cacheFile = (child.stdout || '').split('\n').map(x => x.trim()).find(x => x.startsWith('Cloud review written:'));
  let usage = null;
  if (cacheFile) {
    const summary = JSON.parse(fs.readFileSync(cacheFile.replace('Cloud review written: ', ''), 'utf8'));
    const glm = (summary.reviews || []).find(x => x.provider === 'glm');
    if (glm && glm.status === 'ok' && glm.usage) usage = glm.usage;
  }
  if (usage) for (const k of ['prompt_tokens', 'completion_tokens', 'total_tokens']) usageTotals[k] += usage[k] || 0;
  if (usage) usageTotals.api_calls += 1;
  results.push({ parameterId, exitCode: child.status, signal: child.signal || null, status: usage ? 'ok' : 'not_ok', usage: usage || null });
  if (child.status !== 0) {
    fs.writeFileSync(statePath, JSON.stringify({ start, limit, scope, model: process.env.FU_XUAN_GLM_MODEL || 'glm-4.6v-flash', completed: results, usageTotals, nextStart: start + results.length }, null, 2), 'utf8');
    throw new Error(`Review failed at ${parameterId}; resumable state written to ${statePath}`);
  }
}
fs.writeFileSync(statePath, JSON.stringify({ start, limit, scope, model: process.env.FU_XUAN_GLM_MODEL || 'glm-4.6v-flash', completed: results, usageTotals, nextStart: start + selected.length, total: ids.length }, null, 2), 'utf8');
console.log(`Batch complete: ${selected.length}/${ids.length} (${scope}); usage: ${JSON.stringify(usageTotals)}; nextStart=${start + selected.length}`);
