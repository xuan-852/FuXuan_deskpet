/* DeepSeek-only attribution review for members already captured in skeleton combinations.
 * Usage: node scripts/live2d-probe/review_skeleton_members.cjs <isolated-root>
 */
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const root = process.argv[2];
if (!root || !path.isAbsolute(root) || !fs.existsSync(path.join(root, '.test_mode')))
  throw new Error('Pass an absolute isolated root containing .test_mode.');
if (!process.env.DEEPSEEK_API_KEY) throw new Error('DEEPSEEK_API_KEY is required.');
const report = JSON.parse(fs.readFileSync(path.join(root, 'skeleton-combination-report.json'), 'utf8'));
const reviewDir = path.join(root, 'cloud_review');
fs.mkdirSync(reviewDir, { recursive: true });

const members = [...new Set(report.combinations.flatMap(item => item.parameterIds))];
function locate(member, suffix) {
  for (const combination of report.combinations) {
    if (!combination.parameterIds.includes(member)) continue;
    const file = combination.frames.find(value => path.basename(value).endsWith(suffix));
    if (file && fs.existsSync(file)) return file;
  }
  throw new Error(`Missing ${suffix} for ${member}.`);
}

async function review(member) {
  const baseline = locate(member, '_baseline.png');
  const min = locate(member, `_min_${member}.png`);
  const max = locate(member, `_max_${member}.png`);
  const reset = locate(member, '_reset.png');
  const files = [baseline, min, max, reset];
  const prompt = [
    '你在复核同一 Live2D 模型的单成员骨架探针。图像顺序为 baseline、min、max、reset。',
    '图像仅含隔离模型。不要从参数 ID 推断语义，只根据画面判断。',
    `待归因成员 ID：${member}。`,
    '只返回 JSON：',
    '{"visible_change":true|false,"primary_region":"head|torso|left_arm|right_arm|both_arms|hair_or_accessory|unknown","secondary_regions":["head|torso|left_arm|right_arm|both_arms|hair_or_accessory"],"min_vs_max_direction":"不超过80字","side_specificity":"left|right|both|none|unknown","reset_matches_baseline":true|false,"confidence":"high|medium|low","summary":"不超过100字"}'
  ].join('\n');
  const fingerprint = crypto.createHash('sha256').update('skeleton-member-attribution-v1' + prompt + files.map(file => fs.readFileSync(file)).join('')).digest('hex').slice(0, 20);
  const cache = path.join(reviewDir, `${member}-attribution-deepseek-${fingerprint}.json`);
  if (fs.existsSync(cache)) return JSON.parse(fs.readFileSync(cache, 'utf8'));
  const response = await fetch('https://api.deepseek.com/chat/completions', {
    method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${process.env.DEEPSEEK_API_KEY}` },
    body: JSON.stringify({ model: 'deepseek-v4-flash', temperature: 0, max_tokens: 240, thinking: { type: 'disabled' },
      messages: [{ role: 'user', content: [{ type: 'text', text: prompt }, ...files.map(file => ({ type: 'image_url', image_url: { url: `data:image/png;base64,${fs.readFileSync(file).toString('base64')}` } }))] }] })
  });
  const raw = await response.text(); let payload; try { payload = JSON.parse(raw); } catch { payload = {}; }
  const content = payload.choices?.[0]?.message?.content || '';
  const normalized = String(content).replace(/^\s*```(?:json)?\s*/i, '').replace(/\s*```\s*$/, '');
  let verdict = null; try { verdict = JSON.parse(normalized); } catch { /* raw retained */ }
  const result = { member, provider: 'deepseek', model: 'deepseek-v4-flash', fingerprint, status: response.ok ? 'ok' : 'http_error',
    httpStatus: response.status, content: response.ok ? content : '', verdict, error: response.ok ? null : (payload.error?.message || 'request rejected'), usage: payload.usage || null,
    frames: files.map(file => path.basename(file)) };
  fs.writeFileSync(cache, JSON.stringify(result, null, 2), 'utf8');
  return result;
}

(async () => {
  const results = [];
  for (const member of members) results.push(await review(member));
  const destination = path.join(reviewDir, 'skeleton-member-attribution-deepseek-summary.json');
  fs.writeFileSync(destination, JSON.stringify({ schema: 'live2d-skeleton-member-attribution/v1', source: 'isolated-probe', results }, null, 2), 'utf8');
  console.log(`Review written: ${destination}`);
  for (const result of results) console.log(`${result.member}\t${result.status}\t${result.usage?.total_tokens || 0}`);
})().catch(error => { console.error(error.message); process.exitCode = 1; });
