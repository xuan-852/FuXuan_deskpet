/* DeepSeek-only visual review of isolated skeleton combination probe frames.
 * Usage: node scripts/live2d-probe/review_skeleton_combinations.cjs <isolated-root>
 */
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const root = process.argv[2];
if (!root || !path.isAbsolute(root) || !fs.existsSync(path.join(root, '.test_mode')))
  throw new Error('Pass an absolute isolated root containing .test_mode.');
const report = JSON.parse(fs.readFileSync(path.join(root, 'skeleton-combination-report.json'), 'utf8'));
const key = process.env.DEEPSEEK_API_KEY;
if (!key) throw new Error('DEEPSEEK_API_KEY is required.');
const outputDir = path.join(root, 'cloud_review');
fs.mkdirSync(outputDir, { recursive: true });

function frame(item, suffix) {
  const file = item.frames.find(value => path.basename(value).endsWith(suffix));
  if (!file || !fs.existsSync(file)) throw new Error(`Missing ${suffix} for ${item.combinationId}.`);
  return { suffix, file, dataUrl: `data:image/png;base64,${fs.readFileSync(file).toString('base64')}` };
}

async function review(item) {
  const frames = [frame(item, '_baseline.png'), frame(item, '_combined_min.png'), frame(item, '_combined_max.png'), frame(item, '_reset.png')];
  const prompt = [
    '你在复核同一 Live2D 模型的骨架参数组合探针。图像顺序为 baseline、组合最小值、组合最大值、reset。',
    '图中只有隔离模型，不含桌面、用户数据、UI 或角色行为。不要根据参数名臆测，只判断画面。',
    `本组参数 ID：${item.parameterIds.join(', ')}。`,
    '只返回 JSON：',
    '{"visible_change":true|false,"affected_regions":["torso|head|left_arm|right_arm|hair|unknown"],"min_vs_max_direction":"不超过80字","naturalness":"pass|caution|fail","reset_matches_baseline":true|false,"confidence":"high|medium|low","summary":"不超过100字"}'
  ].join('\n');
  const fingerprint = crypto.createHash('sha256').update('skeleton-combination-v1' + prompt + frames.map(x => fs.readFileSync(x.file)).join('')).digest('hex').slice(0, 20);
  const cache = path.join(outputDir, `${item.combinationId}-deepseek-${fingerprint}.json`);
  if (fs.existsSync(cache)) return JSON.parse(fs.readFileSync(cache, 'utf8'));
  const response = await fetch('https://api.deepseek.com/chat/completions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${key}` },
    body: JSON.stringify({ model: 'deepseek-v4-flash', temperature: 0, max_tokens: 260,
      thinking: { type: 'disabled' }, messages: [{ role: 'user', content: [
        { type: 'text', text: prompt }, ...frames.map(x => ({ type: 'image_url', image_url: { url: x.dataUrl } }))
      ] }] })
  });
  const raw = await response.text();
  let payload; try { payload = JSON.parse(raw); } catch { payload = {}; }
  const content = payload.choices?.[0]?.message?.content || '';
  const normalized = String(content).replace(/^\s*```(?:json)?\s*/i, '').replace(/\s*```\s*$/, '');
  let verdict = null; try { verdict = JSON.parse(normalized); } catch { /* retained as raw evidence */ }
  const result = { combinationId: item.combinationId, parameterIds: item.parameterIds, provider: 'deepseek', model: 'deepseek-v4-flash', fingerprint,
    status: response.ok ? 'ok' : 'http_error', httpStatus: response.status, content: response.ok ? content : '', verdict,
    error: response.ok ? null : (payload.error?.message || 'request rejected'), usage: payload.usage || null,
    frames: frames.map(x => path.basename(x.file)) };
  fs.writeFileSync(cache, JSON.stringify(result, null, 2), 'utf8');
  return result;
}

(async () => {
  const results = [];
  for (const item of report.combinations) results.push(await review(item));
  const output = { schema: 'live2d-skeleton-combination-review/v1', source: 'isolated-probe', results };
  const destination = path.join(outputDir, 'skeleton-combination-deepseek-summary.json');
  fs.writeFileSync(destination, JSON.stringify(output, null, 2), 'utf8');
  console.log(`Review written: ${destination}`);
  for (const result of results) console.log(`${result.combinationId}\t${result.status}\t${result.usage?.total_tokens || 0}`);
})().catch(error => { console.error(error.message); process.exitCode = 1; });
