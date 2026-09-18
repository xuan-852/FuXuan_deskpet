#!/usr/bin/env node
// 用 GLM-4.6V 对运动条带图做结构化动作评审（试点）。
//
// 证据形式：make_motion_strip.py 生成的 4xN 条带图（帧号烧录、按时间顺序）。
// 三段式提问：① 盲描述（不给预期答案，治谄媚）② 分项时序判断 ③ 结构化 JSON 结论。
// 用法: node review_motion_strip.cjs <strip.png> [语义边界文字]
// 输出: <strip>.review.json（含盲描述、分项结论、结构化结果与用量）
const fs = require('fs');

const strip = process.argv[2];
if (!strip || !fs.existsSync(strip)) throw Error('missing strip png');
const boundary = (process.argv[3] || '').trim();

const model = process.env.FU_XUAN_GLM_MODEL || 'glm-4.6v';
const key = process.env.GLM_API_KEY;
if (!key) throw Error('GLM_API_KEY 未设置');

const dataUrl = `data:image/png;base64,${fs.readFileSync(strip).toString('base64')}`;

const prompt = `你是严谨的视频帧序列分析员。这张图包含 f01..f08 共 8 帧，按时间先后从一段录像中均匀抽取。d01..d08 是程序逐像素计算的差异热图：dNN 是 fNN 相对 f01 的差异，橙色亮斑=有差异，纯黑=完全相同。

请严格按步骤作答：

【第一步 逐张分级】依次给出 d01 到 d08 每一张的亮斑强度等级（0=纯黑，1=微弱，2=明显，3=强烈），格式：d01=0, d02=1, ...。必须逐张单独观察后给出，不允许全部填同一个值敷衍。

【第二步 盲描述】基于第一步的轨迹回答：
1. 变化集中在画面哪个部位？
2. 假设这是角色的一个连续动作，它最像什么动作（给 1-3 个候选描述，例如"向左侧倾后回正"/"原地轻微晃动"/"无动作"）？
3. 轨迹形状更接近：先增后减回零 / 一直增大 / 忽大忽小 / 全程为零？

【第三步 结论】输出 JSON（不要多余文字）：
{"motion_detected": true/false, "trajectory": "rise-fall|monotonic|erratic|none", "region": "<变化部位>", "semantic_guesses": ["<候选1>", "<候选2>"], "smoothness": "smooth|jittery|none", "confidence": "high|medium|low", "gradings": {"d01": 0, "d02": 0, "d03": 0, "d04": 0, "d05": 0, "d06": 0, "d07": 0, "d08": 0}}`;

(async () => {
  const res = await fetch('https://open.bigmodel.cn/api/paas/v4/chat/completions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${key}` },
    body: JSON.stringify({
      model,
      temperature: 0.2,
      messages: [{
        role: 'user',
        content: [
          { type: 'image_url', image_url: { url: dataUrl } },
          { type: 'text', text: prompt }
        ]
      }]
    })
  });
  const body = await res.json();
  if (!res.ok) throw Error(`HTTP ${res.status}: ${JSON.stringify(body).slice(0, 400)}`);
  const content = body.choices?.[0]?.message?.content || '';
  const jsonStart = content.indexOf('{');
  const jsonEnd = content.lastIndexOf('}');
  let verdict = null;
  try { verdict = JSON.parse(content.slice(jsonStart, jsonEnd + 1)); } catch { }

  // 第二次调用：纯文本"盲描述 vs 语义边界"比对（评审员看不到边界，先描述后比对，治谄媚）
  let match = null;
  if (boundary && verdict) {
    const matchPrompt = `以下是对一段角色动作录像的独立观察记录：
变化部位：${verdict.region || ''}
轨迹形状：${verdict.trajectory || ''}
语义候选：${(verdict.semantic_guesses || []).join('；')}
平滑性：${verdict.smoothness || ''}

动作语义边界（该动作声称应该是什么）：${boundary}

请判断观察记录与语义边界是否一致（方向、部位、平滑度均需吻合；轻微幅度差异不算不一致）。输出 JSON：{"supported": true/false, "reason": "<一句话理由>"}`;
    const res2 = await fetch('https://open.bigmodel.cn/api/paas/v4/chat/completions', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${key}` },
      body: JSON.stringify({
        model, temperature: 0.2,
        messages: [{ role: 'user', content: matchPrompt }]
      })
    });
    const body2 = await res2.json();
    if (res2.ok) {
      const c2 = body2.choices?.[0]?.message?.content || '';
      const s2 = c2.indexOf('{'), e2 = c2.lastIndexOf('}');
      try { match = JSON.parse(c2.slice(s2, e2 + 1)); } catch { match = { raw: c2 }; }
      body.usage = mergeUsage(body.usage, body2.usage);
    }
  }

  const out = {
    strip,
    boundary: boundary || null,
    model,
    reviewedAtUtc: new Date().toISOString(),
    verdict,
    boundaryMatch: match,
    rawContent: content,
    usage: body.usage || null
  };
  const outPath = strip.replace(/\.png$/i, '') + '.review.json';
  fs.writeFileSync(outPath, JSON.stringify(out, null, 2), 'utf8');
  console.log(`reviewed: ${strip} -> ${outPath}`);
  console.log(`trajectory=${verdict?.trajectory} region=${verdict?.region || '?'} guesses=${(verdict?.semantic_guesses || []).join('/')} match=${match ? (match.supported ? 'supported' : 'unsupported') : 'n/a'} usage=${JSON.stringify(body.usage?.total_tokens || 0)}tok`);
})().catch(e => { console.error(e.message); process.exit(1); });

function mergeUsage(a, b) {
  if (!b) return a;
  a = a || {};
  const out = { ...a };
  for (const k of ['prompt_tokens', 'completion_tokens', 'total_tokens'])
    out[k] = (a[k] || 0) + (b[k] || 0);
  return out;
}
