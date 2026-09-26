/* 表情探索漏斗 v1：批量生成面部表情候选 → 隔离探针采集 → 本地指标预筛 → 接触表人工评审。
 *
 * 动机（2026-09-27）：认证动作链证明孤立单参数曲线必然诡异、真人评审是唯一自然度终审；
 * 表情是批量探索成本最低的层，但视觉模型只能做粗筛（点头 v1 双模型 80/80 方向仍错），
 * 因此本工具把 GLM/DeepSeek 放在预筛位、把人放在接触表终审位。
 *
 * 用法（分步或 run 一步到位）:
 *   node expression_funnel.cjs generate --count 60 --seed 20260927 [--catalog <capability-catalog.json>] [--out <dir>]
 *   node expression_funnel.cjs capture  --batch <dir> --probe-exe <Live2DProbe.exe> [--start 0] [--limit 60] [--steps 16]
 *   node expression_funnel.cjs prescreen --batch <dir> [--min-peak 0.6] [--max-peak 6.0]
 *   node expression_funnel.cjs sheet    --batch <dir>
 *   node expression_funnel.cjs run      --probe-exe <exe> [--count 60] [--seed N] [--catalog <path>]
 *
 * 采集复用独立 Live2DProbe.exe 的 motion-playback 模式（隔离 FU_XUAN_DATA + .test_mode，
 * 每候选 18 帧：基线 + 16 步 + 复位），候选 JSON 与认证动作同为曲线段 schema。
 * 预筛只做本地指标闸门（复位稳定 + 峰值像素差上下限）；云评审请沿用
 * build_candidate_review_packet.cjs + review_candidate_packet_*.cjs，需显式授权。
 * 接触表产出 contact-sheet.html（浏览器打开，零依赖），供人工三问评审：
 * 可辨认 / 自然 / 愿意让她做。
 */
'use strict';
const { spawnSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const CDI3_PATH = path.resolve(__dirname, '../../code/desktop_unity/Assets/StreamingAssets/Live2D/Fuxuan/符玄.cdi3.json');
// 特殊效果/物理驱动参数不参与 v1 表情采样：黑脸/泪眼/眼镜发光是效果层，
// 眼球与眼眶物理、睫毛由 CubismPhysics 驱动， gaze(ParamEyeBall*) 归闲置微动层管。
const EXCLUDE_NAME_PATTERNS = [/黑脸/, /泪眼/, /眼镜发光/, /物理/, /睫毛/];
const EXCLUDE_IDS = new Set(['ParamEyeBallX', 'ParamEyeBallY']);
// 无 capability catalog 时的保守内建区间（Cubism 标准面部参数）；catalog 优先。
const DEFAULT_RANGES = {
  ParamEyeLOpen: { minimum: 0, maximum: 1, baseline: 1 },
  ParamEyeROpen: { minimum: 0, maximum: 1, baseline: 1 },
  ParamEyeLSmile: { minimum: 0, maximum: 1, baseline: 0 },
  ParamEyeRSmile: { minimum: 0, maximum: 1, baseline: 0 },
  ParamMouthForm: { minimum: -1, maximum: 1, baseline: 0 },
  ParamMouthOpenY: { minimum: 0, maximum: 1, baseline: 0 },
  ParamBrowLY: { minimum: -1, maximum: 1, baseline: 0 },
  ParamBrowRY: { minimum: -1, maximum: 1, baseline: 0 },
  ParamBrowLX: { minimum: -1, maximum: 1, baseline: 0 },
  ParamBrowRX: { minimum: -1, maximum: 1, baseline: 0 },
  ParamBrowLForm: { minimum: 0, maximum: 2, baseline: 1 },
  ParamBrowRForm: { minimum: 0, maximum: 2, baseline: 1 },
  ParamBrowLAngle: { minimum: -1, maximum: 1, baseline: 0 },
  ParamBrowRAngle: { minimum: -1, maximum: 1, baseline: 0 },
};
const GROUP_BY_ID = (id) => {
  if (/Eye[LR](Open|Smile)$/.test(id) || /^Param1(49|50)$/.test(id)) return 'eye';
  if (/^ParamBrow/.test(id)) return 'brow';
  if (/Mouth/.test(id) || id === 'Param250') return 'mouth';
  return null;
};

function parseArgs(argv) {
  const cmd = argv[0];
  if (!cmd || !['generate', 'capture', 'prescreen', 'sheet', 'run'].includes(cmd)) {
    throw new Error('用法: expression_funnel.cjs <generate|capture|prescreen|sheet|run> [options]');
  }
  const opts = { cmd };
  for (let i = 1; i < argv.length; i++) {
    const key = argv[i];
    if (!key.startsWith('--')) throw new Error(`意外参数: ${key}`);
    if (key === '--writer-mode-physics') { opts.physics = true; continue; }
    const next = argv[i + 1];
    if (next === undefined || next.startsWith('--')) { opts[key.slice(2)] = true; continue; }
    opts[key.slice(2)] = next;
    i++;
  }
  return opts;
}

function gitSha() {
  const r = spawnSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' });
  return r.status === 0 ? r.stdout.trim() : 'unknown';
}

function sha256File(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}

function readJsonNoBom(file) {
  return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
}

// ---------------------------------------------------------------- generate

function collectFacialParams(catalog) {
  const cdi = readJsonNoBom(CDI3_PATH);
  const params = [];
  const skipped = [];
  for (const p of cdi.Parameters || []) {
    const id = p.Id, name = p.Name || '';
    const group = GROUP_BY_ID(id);
    if (!group) continue;
    if (EXCLUDE_IDS.has(id) || EXCLUDE_NAME_PATTERNS.some(re => re.test(name))) { skipped.push(id); continue; }
    const range = catalog?.[id] || DEFAULT_RANGES[id];
    if (!range) { skipped.push(`${id}(无区间来源)`); continue; }
    params.push({ parameterId: id, name, group, ...range });
  }
  if (!params.length) throw new Error('没有可采样的面部参数：检查 cdi3.json 或提供 --catalog。');
  return { params, skipped };
}

// 表情包络：0→0.3s 贝塞尔切入目标，0.3→1.4s 保持，1.4→2.0s 贝塞尔回基线。
// 贝塞尔控制点为绝对坐标（EmbodiedMotionCurve 语义）。
function expressionCurve(baseline, target, duration = 2.0) {
  const tIn = 0.3, tHold = 1.4, tEnd = duration;
  const ease = (t0, v0, t1, v1) => [1, t0 + (t1 - t0) * 0.35, v0, t0 + (t1 - t0) * 0.65, v1, t1, v1];
  return [
    0, baseline,
    ...ease(0, baseline, tIn, target),
    0, tHold, target,
    ...ease(tHold, target, tEnd, baseline),
  ];
}

function generateCandidates(opts) {
  let catalog = null;
  if (opts.catalog) {
    const entries = readJsonNoBom(path.resolve(opts.catalog));
    const list = Array.isArray(entries) ? entries : entries.parameters || [];
    catalog = Object.fromEntries(list.map(e => [e.parameterId, e.range]));
    console.log(`[generate] catalog: ${Object.keys(catalog).length} 个参数区间`);
  }
  const { params, skipped } = collectFacialParams(catalog);
  console.log(`[generate] 面部参数池: ${params.length}（排除: ${skipped.join(', ') || '无'}）`);

  const seed = Number.parseInt(opts.seed || '20260927', 10);
  const count = Number.parseInt(opts.count || '60', 10);
  const rng = mulberry32(seed);
  const outDir = path.resolve(opts.out || path.join('logs', 'expression_funnel', `batch-${new Date().toISOString().replace(/[:.]/g, '-')}`));
  fs.mkdirSync(outDir, { recursive: true });

  const byGroup = new Map();
  for (const p of params) {
    if (!byGroup.has(p.group)) byGroup.set(p.group, []);
    byGroup.get(p.group).push(p);
  }
  const groups = [...byGroup.keys()];
  const manifest = { schema: 'expression-funnel/v1', seed, count, commitSha: gitSha(), catalog: opts.catalog || null, candidates: [] };

  for (let n = 0; n < count; n++) {
    // 1-2 个组、共 1-3 个参数；同组参数保证组合读起来像连贯表情而非乱动。
    const groupCount = rng() < 0.6 ? 1 : 2;
    const picked = new Set();
    for (let g = 0; g < groupCount && picked.size < 3; g++) {
      const group = groups[Math.floor(rng() * groups.length)];
      const pool = byGroup.get(group);
      const take = 1 + Math.floor(rng() * Math.min(2, pool.length));
      for (let k = 0; k < take; k++) picked.add(pool[Math.floor(rng() * pool.length)]);
    }
    const curves = [];
    const targets = [];
    for (const p of picked) {
      const span = p.maximum - p.baseline, spanDown = p.baseline - p.minimum;
      const magnitude = (0.35 + rng() * 0.65) * Math.max(span, spanDown);
      const sign = (rng() < 0.5 || spanDown === 0) && span > 0 ? 1 : -1;
      const target = clamp(p.baseline + sign * magnitude, p.minimum, p.maximum);
      curves.push({ parameterId: p.parameterId, segments: expressionCurve(p.baseline, target) });
      targets.push({ id: p.parameterId, name: p.name, target: round3(target), baseline: round3(p.baseline) });
    }
    const candidateId = `expr_${String(n).padStart(3, '0')}`;
    const file = path.join(outDir, `${candidateId}.json`);
    fs.writeFileSync(file, JSON.stringify({ schema: 'embodied-motion-candidate/v1', candidateId, durationSeconds: 2.0, curves }, null, 1));
    manifest.candidates.push({ candidateId, file: path.basename(file), sha256: sha256File(file), targets, status: 'generated' });
  }

  fs.writeFileSync(path.join(outDir, 'manifest.json'), JSON.stringify(manifest, null, 1));
  console.log(`[generate] ${count} 个候选 → ${outDir}（seed=${seed}, commit=${manifest.commitSha.slice(0, 8)}）`);
  return outDir;
}

function mulberry32(a) {
  return function () {
    a |= 0; a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const round3 = v => Math.round(v * 1000) / 1000;
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
// 临时根（C:）与批次目录（可能 D:）常跨盘符，rename 会 EXDEV，必须 copy+delete。
function moveFile(src, dest) {
  fs.copyFileSync(src, dest);
  fs.rmSync(src, { force: true });
}

// ---------------------------------------------------------------- capture

function capture(opts) {
  const batchDir = requireBatch(opts.batch);
  const exe = path.resolve(requireOpt(opts, 'probe-exe'));
  if (!fs.existsSync(exe)) throw new Error(`探针 exe 不存在: ${exe}`);
  const manifest = readManifest(batchDir);
  const start = Number.parseInt(opts.start || '0', 10);
  const limit = Number.parseInt(opts.limit || String(manifest.candidates.length), 10);
  const steps = Number.parseInt(opts.steps || '16', 10);
  const selected = manifest.candidates.slice(start, start + limit);
  if (!selected.length) { console.log('[capture] 范围为空，无事可做。'); return; }

  // 整批共用一个隔离根（与 capture_motion_candidate 同一安全模式），逐候选读取并归档报告。
  const root = path.join(os.tmpdir(), 'fuxuan_expr_funnel_' + crypto.createHash('sha1').update(batchDir).digest('hex').slice(0, 8));
  fs.rmSync(root, { recursive: true, force: true });
  fs.mkdirSync(root, { recursive: true });
  fs.writeFileSync(path.join(root, '.test_mode'), '');
  const capturesDir = path.join(batchDir, 'captures');
  fs.mkdirSync(capturesDir, { recursive: true });
  const env = {
    ...process.env, FU_XUAN_DATA: root,
    FU_XUAN_PROBE_MOTION_STEPS: String(steps),
    FU_XUAN_PROBE_WRITER_MODE: opts.physics ? 'physics' : 'frozen',
  };
  let ok = 0, failed = 0;
  for (const entry of selected) {
    const candidatePath = path.join(batchDir, entry.file);
    env.FU_XUAN_PROBE_MOTION_PLAYBACK = path.resolve(candidatePath);
    const r = spawnSync(exe, [], { env, encoding: 'utf8', timeout: 120000, stdio: 'ignore' });
    const reportPath = path.join(root, 'motion-playback-report.json');
    if (r.status !== 0 || !fs.existsSync(reportPath)) {
      const failure = path.join(root, 'capability-report-probe-window.failure.txt');
      const detail = fs.existsSync(failure) ? fs.readFileSync(failure, 'utf8').split('\n').slice(0, 3).join(' | ') : `exit=${r.status}`;
      console.error(`[capture] ${entry.candidateId} 失败: ${detail}`);
      entry.status = 'capture-failed'; entry.captureError = detail; failed++; continue;
    }
    const perCandidateDir = path.join(capturesDir, entry.candidateId);
    fs.mkdirSync(perCandidateDir, { recursive: true });
    const report = readJsonNoBom(reportPath);
    for (const frame of report.frames || []) {
      if (frame && fs.existsSync(frame)) moveFile(frame, path.join(perCandidateDir, path.basename(frame)));
    }
    fs.writeFileSync(path.join(perCandidateDir, 'playback-report.json'), JSON.stringify(report, null, 1));
    entry.status = 'captured';
    entry.metrics = {
      peakMeanDifference: report.peakMeanDifference, maxAdjacentMeanDifference: report.maxAdjacentMeanDifference,
      resetMeanDifference: report.resetMeanDifference, resetStable: report.resetStable, steps: report.steps,
    };
    ok++;
    console.log(`[capture] ${entry.candidateId} peak=${entry.metrics.peakMeanDifference?.toFixed(3)} resetStable=${entry.metrics.resetStable}`);
  }
  fs.rmSync(root, { recursive: true, force: true });
  writeManifest(batchDir, manifest);
  console.log(`[capture] 完成: ${ok} 成功 / ${failed} 失败（批根已清理）`);
}

// ---------------------------------------------------------------- prescreen

function prescreen(opts) {
  const batchDir = requireBatch(opts.batch);
  const manifest = readManifest(batchDir);
  const minPeak = Number.parseFloat(opts['min-peak'] || '0.012');
  const maxPeak = Number.parseFloat(opts['max-peak'] || '1.2');
  // 实测校准（2026-09-27 批次）：面部表情的全帧均值峰值差在 0.002–0.05 量级
  // （脸只占全帧一小部分像素；全身动作参考值 1.7–8.6 不适用于面部）。
  // 预筛只做粗排序，真值由 contact-sheet 人工评审给出。
  let surviving = 0;
  const regatable = new Set(['captured', 'surviving', 'invisible', 'gross-change', 'reset-unstable']);
  for (const entry of manifest.candidates) {
    if (!regatable.has(entry.status)) continue;
    const m = entry.metrics;
    let gate;
    if (!m.resetStable) gate = 'reset-unstable';
    else if (m.peakMeanDifference < minPeak) gate = 'invisible';
    else if (m.peakMeanDifference > maxPeak) gate = 'gross-change';
    else gate = 'surviving';
    entry.status = gate;
    if (gate === 'surviving') surviving++;
    console.log(`[prescreen] ${entry.candidateId}: ${gate} (peak=${m.peakMeanDifference?.toFixed(3)})`);
  }
  writeManifest(batchDir, manifest);
  console.log(`[prescreen] 存活 ${surviving} / ${manifest.candidates.filter(c => c.status !== 'captured' && c.status !== 'capture-failed').length} 已采集。评审闸门是本工具的上限——请用 contact-sheet.html 人工终审。`);
}

// ---------------------------------------------------------------- sheet

function sheet(opts) {
  const batchDir = requireBatch(opts.batch);
  const manifest = readManifest(batchDir);
  const order = { 'surviving': 0, 'gross-change': 1, 'reset-unstable': 2, 'capture-failed': 4 };
  const rows = manifest.candidates
    .filter(c => c.metrics || c.status === 'capture-failed')
    .sort((a, b) => (order[a.status] ?? 3) - (order[b.status] ?? 3) ||
      (b.metrics?.peakMeanDifference ?? 0) - (a.metrics?.peakMeanDifference ?? 0));
  if (!rows.length) throw new Error('没有可展示的候选（需要先 capture）。');
  const holdFrameIndex = String(Math.floor(((rows[0].metrics?.steps || 16) * 0.6))).padStart(3, '0');
  const cards = rows.map(entry => {
    const dir = path.join('captures', entry.candidateId);
    const targets = entry.targets.map(t => `${t.name}→${t.target}`).join('；');
    const m = entry.metrics || {};
    const frames = fs.existsSync(path.join(batchDir, dir))
      ? fs.readdirSync(path.join(batchDir, dir)).filter(f => f.endsWith('.png')).sort()
      : [];
    const hold = frames.find(f => f.includes(`_${holdFrameIndex}.png`)) || frames[Math.floor(frames.length / 2)] || '';
    const reset = frames[frames.length - 1] || '';
    return `<figure class="${entry.status}">
  <figcaption><b>${entry.candidateId}</b> [${entry.status}] peak=${(m.peakMeanDifference ?? NaN).toFixed?.(3) ?? m.peakMeanDifference} reset=${m.resetStable}<br><small>${escapeHtml(targets)}</small></figcaption>
  <div><img src="${dir}/${hold}" loading="lazy"><img src="${dir}/${reset}" loading="lazy"></div>
</figure>`;
  }).join('\n');
  const html = `<!doctype html><meta charset="utf-8"><title>表情漏斗接触表 — ${path.basename(batchDir)}</title>
<style>body{background:#1b1b1f;color:#ddd;font:14px/1.5 system-ui;max-width:1500px;margin:24px auto;padding:0 16px}
h1{font-size:18px}figure{display:inline-block;margin:10px;padding:8px;background:#26262c;border-radius:8px;vertical-align:top;width:460px}
figure.gross-change{outline:2px solid #b5651d}figure.reset-unstable{outline:2px solid #b5303d}
figcaption small{color:#999}div{display:flex;gap:6px}img{width:220px;height:220px;object-fit:contain;background:#111;border-radius:4px}
.surviving{outline:2px solid #3f7d4e}</style>
<h1>表情漏斗接触表 — ${escapeHtml(path.basename(batchDir))}（左=保持相 60%，右=复位相；绿框=预筛存活，橙框=变化过大待判，红框=复位不稳）</h1>
<p>人工三问：① 情绪可辨认？② 表情自然（不诡异）？③ 愿意让她在日常对话里做？三问全 yes 才进入语义赋值。</p>
${cards}`;
  const out = path.join(batchDir, 'contact-sheet.html');
  fs.writeFileSync(out, html);
  console.log(`[sheet] 接触表: ${out}（${rows.length} 个候选）`);
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
}

// ---------------------------------------------------------------- helpers

function requireBatch(v) {
  const dir = path.resolve(requireOpt(v ? { batch: v } : {}, 'batch'));
  if (!fs.existsSync(path.join(dir, 'manifest.json'))) throw new Error(`批次目录缺少 manifest.json: ${dir}`);
  return dir;
}
function requireOpt(opts, key) {
  const v = opts[key] || opts[key.replace(/-([a-z])/g, (_, c) => c.toUpperCase())];
  if (!v || v === true) throw new Error(`缺少 --${key}`);
  return v;
}
function readManifest(dir) { return readJsonNoBom(path.join(dir, 'manifest.json')); }
function writeManifest(dir, manifest) {
  manifest.updatedAt = new Date().toISOString();
  fs.writeFileSync(path.join(dir, 'manifest.json'), JSON.stringify(manifest, null, 1));
}

// ---------------------------------------------------------------- main

const opts = parseArgs(process.argv.slice(2));
if (opts.cmd === 'generate') generateCandidates(opts);
else if (opts.cmd === 'capture') capture(opts);
else if (opts.cmd === 'prescreen') prescreen(opts);
else if (opts.cmd === 'sheet') sheet(opts);
else if (opts.cmd === 'run') {
  const batchDir = generateCandidates(opts);
  capture({ ...opts, batch: batchDir });
  prescreen({ ...opts, batch: batchDir });
  sheet({ ...opts, batch: batchDir });
  console.log(`[run] 批次完成: ${batchDir}`);
}
