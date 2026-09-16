/* Controlled GLM cross-review for one frozen candidate-action packet.
 * Usage: node scripts/live2d-probe/review_candidate_packet_glm.cjs <isolated-root> <skill-id>
 * It mirrors review_candidate_packet_deepseek.cjs: same six deterministic review
 * phases and identical prompt, so the two verdicts are comparable. GLM Flash is
 * the independent secondary reviewer; a failure is recorded as Unavailable and
 * never retried in a loop, per the approved probe-evaluation-accuracy guidance.
 */
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const root = process.argv[2];
const skillId = process.argv[3] || 'screen_side_arm_raise';
const model = process.argv[4] || 'glm-4.6v-flash';
if (!root || !path.isAbsolute(root) || !fs.existsSync(path.join(root, '.test_mode')))
  throw new Error('Pass an absolute isolated root containing .test_mode.');
if (!process.env.GLM_API_KEY) throw new Error('GLM_API_KEY is required.');
const packetPath = path.join(root, `candidate-review-packet-${skillId}.json`);
if (!fs.existsSync(packetPath)) throw new Error(`Missing frozen review packet: ${packetPath}`);
const packet = JSON.parse(fs.readFileSync(packetPath, 'utf8'));
if (packet.schema !== 'live2d-candidate-review-packet/v1' || packet.skillId !== skillId)
  throw new Error('Packet schema or skill ID mismatch.');
const claim = packet.constraints?.semanticBoundary
  || (skillId === 'screen_side_arm_raise' ? '画面侧单臂上抬后回落；它明确不是挥手、问候或招手' : '');
if (!claim) throw new Error('Packet lacks a semantic boundary claim.');
const sha256 = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
for (const frame of packet.frames || []) {
  const file = path.join(root, frame.file);
  if (!fs.existsSync(file) || sha256(file) !== frame.sha256) throw new Error(`Frame hash mismatch: ${frame.file}`);
}

const wantedOrders = [0, 3, 6, 9, 12, packet.frames.length - 1];
const reviewFrames = wantedOrders.map(order => packet.frames[order]);
if (new Set(reviewFrames.map(x => x.order)).size !== 6) throw new Error('Packet lacks six distinct deterministic review phases.');
const prompt = [
  '你在离线复核同一 Live2D 模型的固定时序动作证据。图像顺序必须严格按以下 phase 解读：' + reviewFrames.map(x => x.phase).join(' → ') + '。',
  '不得根据参数名、文件名或人物左右臂猜测语义；只根据画面判断。该候选宣称的唯一语义边界是：' + claim,
  '评估动作是否在整段中连续、是否在最后回到基线、是否存在明显变形/突跳/不自然，以及相对普通可接受的桌宠步行基线是否至少不更差。',
  '仅返回 JSON：',
  '{"semantic_boundary":"supported|unsupported|uncertain","observed_motion":"不超过100字","sequence_continuous":true|false,"reset_stable":true|false,"obvious_visual_fault":true|false,"meets_walk_baseline":true|false,"naturalness_score":0-100,"confidence":"high|medium|low","summary":"不超过140字"}'
].join('\n');
const fingerprint = crypto.createHash('sha256').update('candidate-packet-glm-v1' + model + packet.packetSha256 + prompt).digest('hex');
const reviewDir = path.join(root, 'cloud_review');
fs.mkdirSync(reviewDir, { recursive: true });
const output = path.join(reviewDir, `${skillId}-glm-${fingerprint}.json`);
if (fs.existsSync(output)) { console.log(`Cached review: ${output}`); process.exit(0); }

(async () => {
  const response = await fetch('https://open.bigmodel.cn/api/paas/v4/chat/completions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${process.env.GLM_API_KEY}` },
    body: JSON.stringify({ model, temperature: 0, max_tokens: 360, thinking: { type: 'disabled' },
      messages: [{ role: 'user', content: [{ type: 'text', text: prompt }, ...reviewFrames.map(frame => ({ type: 'image_url', image_url: { url: `data:image/png;base64,${fs.readFileSync(path.join(root, frame.file)).toString('base64')}` } }))] }] })
  });
  const raw = await response.text(); let payload; try { payload = JSON.parse(raw); } catch { payload = {}; }
  const message = payload.choices?.[0]?.message ?? {};
  const content = message.content || message.reasoning_content || '';
  const normalized = String(content).replace(/^\s*```(?:json)?\s*/i, '').replace(/\s*```\s*$/, '');
  let verdict = null; try { verdict = JSON.parse(normalized); } catch { /* raw retained for review */ }
  const result = { schema: 'live2d-candidate-glm-review/v1', skillId, packetSha256: packet.packetSha256, fingerprint,
    provider: 'glm', model, status: response.ok ? 'ok' : 'http_error', httpStatus: response.status,
    frames: reviewFrames.map(frame => ({ order: frame.order, phase: frame.phase, sha256: frame.sha256 })), content: response.ok ? content : '',
    verdict, error: response.ok ? null : (payload.error?.message || 'request rejected'), usage: payload.usage || null };
  fs.writeFileSync(output, JSON.stringify(result, null, 2) + '\n', 'utf8');
  console.log(JSON.stringify({ output, status: result.status, httpStatus: result.httpStatus, usage: result.usage, verdict: result.verdict, error: result.error }, null, 2));
  if (!response.ok) process.exitCode = 1;
})().catch(error => { console.error(error.message); process.exitCode = 1; });
