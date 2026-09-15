/*
 * Controlled cloud visual review for isolated Live2D probe frames.
 * Inputs are limited to FU_XUAN_DATA/probe_window PNGs.  It deliberately
 * refuses any non-test root and never writes API credentials or HTTP headers.
 * Usage: node scripts/live2d-probe/cloud_review.cjs <isolated-probe-root> [parameterId] [deepseek|glm]
 */
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const root = process.argv[2];
const parameterId = process.argv[3] || 'ParamAngleX';
const providerFilter = process.argv[4] || 'both';
const retryCachedErrors = process.env.FU_XUAN_CLOUD_RETRY_ERRORS === '1';
if (!['both', 'deepseek', 'glm'].includes(providerFilter)) throw new Error('Provider must be deepseek, glm, or omitted.');
if (!root || !path.isAbsolute(root)) throw new Error('Pass an absolute isolated probe root.');
if (!fs.existsSync(path.join(root, '.test_mode'))) throw new Error('Refusing cloud review: .test_mode is required.');
const reportPath = path.join(root, 'capability-report-probe-window.json');
if (!fs.existsSync(reportPath)) throw new Error('Probe report is missing.');

const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));
const item = report.parameters.find(x => x.parameterId === parameterId);
if (!item) throw new Error(`Parameter not found: ${parameterId}`);
const wanted = ['baseline', 'min', 'max', 'reset'];
const frames = wanted.map(label => {
  const filename = `${parameterId}_r1_${label}.png`;
  const file = item.frames.find(x => path.basename(x) === filename);
  if (!file || !fs.existsSync(file)) throw new Error(`Frame missing: ${filename}`);
  return { label, file, dataUrl: `data:image/png;base64,${fs.readFileSync(file).toString('base64')}` };
});
const prompt = [
  '你在复核同一 Live2D 模型的单参数探针帧。图像顺序为 baseline、min、max、reset；除该参数外没有桌宠动作、物理、UI 或用户桌面内容。',
  `被测参数：${parameterId}。请只根据图像回答 JSON，禁止猜测模型内部命名。`,
  '{"visible_change":true|false,"change_summary":"不超过60字","min_vs_max":"不超过60字","reset_matches_baseline":true|false,"semantic_confidence":"high|medium|low","semantic_guess":"若无法可靠判断则unknown"}'
].join('\n');
const reviewDir = path.join(root, 'cloud_review');
fs.mkdirSync(reviewDir, { recursive: true });

async function call(name, endpoint, model, key) {
  if (!key) return { provider: name, status: 'skipped', reason: 'API key not present' };
  const fingerprint = crypto.createHash('sha256').update('review-v2' + model + prompt + frames.map(x => fs.readFileSync(x.file)).join('')).digest('hex').slice(0, 20);
  const cache = path.join(reviewDir, `${parameterId}-${name}-${fingerprint}.json`);
  if (fs.existsSync(cache)) {
    const cached = JSON.parse(fs.readFileSync(cache, 'utf8'));
    if (cached.status === 'ok' || !retryCachedErrors) return cached;
  }
  const body = {
    model,
    temperature: 0,
    max_tokens: 220,
    thinking: { type: 'disabled' },
    messages: [{ role: 'user', content: [
      { type: 'text', text: prompt },
      ...frames.map(x => ({ type: 'image_url', image_url: { url: x.dataUrl } }))
    ] }]
  };
  const response = await fetch(endpoint, {
    method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${key}` }, body: JSON.stringify(body)
  });
  const raw = await response.text();
  let payload;
  try { payload = JSON.parse(raw); } catch { payload = { nonJsonResponse: true }; }
  const message = payload.choices?.[0]?.message ?? {};
  const content = message.content || message.reasoning_content || '';
  const result = {
    provider: name, model, parameterId, fingerprint, status: response.ok ? 'ok' : 'http_error', httpStatus: response.status,
    content: response.ok ? content : '',
    responseField: response.ok && message.content ? 'content' : (response.ok && message.reasoning_content ? 'reasoning_content' : ''),
    error: response.ok ? undefined : (payload.error?.message ?? 'request rejected'),
    usage: payload.usage ? { prompt_tokens: payload.usage.prompt_tokens, completion_tokens: payload.usage.completion_tokens, total_tokens: payload.usage.total_tokens } : undefined
  };
  fs.writeFileSync(cache, JSON.stringify(result, null, 2), 'utf8');
  return result;
}

(async () => {
  const output = path.join(reviewDir, `${parameterId}-summary.json`);
  let existingReviews = [];
  if (fs.existsSync(output)) {
    try { existingReviews = JSON.parse(fs.readFileSync(output, 'utf8')).reviews || []; }
    catch { existingReviews = []; }
  }
  const reviews = [];
  if (providerFilter === 'both' || providerFilter === 'deepseek') reviews.push(await call('deepseek', 'https://api.deepseek.com/chat/completions', 'deepseek-v4-flash', process.env.DEEPSEEK_API_KEY));
  if (providerFilter === 'both' || providerFilter === 'glm') reviews.push(await call('glm', 'https://open.bigmodel.cn/api/paas/v4/chat/completions', 'glm-4.6v-flash', process.env.GLM_API_KEY));
  const merged = new Map(existingReviews.map(item => [item.provider, item]));
  for (const review of reviews) merged.set(review.provider, review);
  const summary = { parameterId, isolatedRoot: root, frames: frames.map(x => path.basename(x.file)), reviews: [...merged.values()] };
  fs.writeFileSync(output, JSON.stringify(summary, null, 2), 'utf8');
  console.log(`Cloud review written: ${output}`);
  for (const result of summary.reviews) console.log(`${result.provider}: ${result.status}${result.httpStatus ? ` (${result.httpStatus})` : ''}`);
})().catch(error => { console.error(`Cloud review failed: ${error.message}`); process.exitCode = 1; });
