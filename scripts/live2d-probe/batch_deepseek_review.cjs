/* Resumable DeepSeek-only review of every parameter in one isolated report.
 * Usage: node scripts/live2d-probe/batch_deepseek_review.cjs <root> [start] [limit]
 * Each child reuses cloud_review.cjs cache, so rerunning a completed range has no API call.
 */
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const root = process.argv[2];
const start = Number.parseInt(process.argv[3] || '0', 10);
const limit = Number.parseInt(process.argv[4] || '20', 10);
if (!root || !path.isAbsolute(root) || !fs.existsSync(path.join(root, '.test_mode'))) throw new Error('Pass an isolated root containing .test_mode.');
if (!Number.isInteger(start) || start < 0 || !Number.isInteger(limit) || limit < 1) throw new Error('start must be >= 0 and limit must be >= 1.');
const report = JSON.parse(fs.readFileSync(path.join(root, 'capability-report-probe-window.json'), 'utf8'));
const ids = report.parameters.map(item => item.parameterId).sort((a, b) => a.localeCompare(b));
const selected = ids.slice(start, start + limit);
const stateDir = path.join(root, 'cloud_review');
fs.mkdirSync(stateDir, { recursive: true });
const statePath = path.join(stateDir, `deepseek-batch-${start}-${start + selected.length - 1}.json`);
const results = [];
for (const parameterId of selected) {
  const child = spawnSync(process.execPath, [path.join(__dirname, 'cloud_review.cjs'), root, parameterId, 'deepseek'], {
    encoding: 'utf8', env: process.env, timeout: 180000
  });
  results.push({ parameterId, exitCode: child.status, signal: child.signal || null, output: (child.stdout || '').trim().split('\n').slice(-2) });
  if (child.status !== 0) {
    fs.writeFileSync(statePath, JSON.stringify({ start, limit, completed: results, nextStart: start + results.length }, null, 2), 'utf8');
    throw new Error(`Review failed at ${parameterId}; resumable state written to ${statePath}`);
  }
}
fs.writeFileSync(statePath, JSON.stringify({ start, limit, completed: results, nextStart: start + selected.length, total: ids.length }, null, 2), 'utf8');
console.log(`Batch complete: ${selected.length}/${ids.length}; nextStart=${start + selected.length}`);
