'use strict';

/* Mechanical visibility analysis for isolated Live2DProbe PNG evidence.
 * This does not identify hands or certify motion semantics.
 * Usage: node analyze_visibility_boundary.cjs <visibility-boundary-input-manifest.json>
 */
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const crypto = require('crypto');

const manifestPath = process.argv[2];
if (!manifestPath) throw new Error('Usage: analyze_visibility_boundary.cjs <manifest.json>');
const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));

function pngFrame(file) {
  const bytes = fs.readFileSync(file);
  if (bytes.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a') throw new Error('Not a PNG: ' + file);
  let offset = 8, width, height, depth, type, chunks = [];
  while (offset < bytes.length) {
    const size = bytes.readUInt32BE(offset); const kind = bytes.toString('ascii', offset + 4, offset + 8);
    const data = bytes.subarray(offset + 8, offset + 8 + size); offset += size + 12;
    if (kind === 'IHDR') { width = data.readUInt32BE(0); height = data.readUInt32BE(4); depth = data[8]; type = data[9]; }
    if (kind === 'IDAT') chunks.push(data);
    if (kind === 'IEND') break;
  }
  if (depth !== 8 || (type !== 2 && type !== 6)) throw new Error(`Unsupported PNG: ${file}`);
  const bpp = type === 6 ? 4 : 3, stride = width * bpp;
  const raw = zlib.inflateSync(Buffer.concat(chunks)), decoded = Buffer.alloc(stride * height);
  let source = 0;
  for (let y = 0; y < height; y++) {
    const filter = raw[source++], row = y * stride;
    for (let x = 0; x < stride; x++) {
      const value = raw[source++], left = x >= bpp ? decoded[row + x - bpp] : 0;
      const up = y ? decoded[row - stride + x] : 0, ul = y && x >= bpp ? decoded[row - stride + x - bpp] : 0;
      if (filter === 0) decoded[row + x] = value;
      else if (filter === 1) decoded[row + x] = (value + left) & 255;
      else if (filter === 2) decoded[row + x] = (value + up) & 255;
      else if (filter === 3) decoded[row + x] = (value + Math.floor((left + up) / 2)) & 255;
      else if (filter === 4) { const p = left + up - ul, pa = Math.abs(p - left), pb = Math.abs(p - up), pc = Math.abs(p - ul); decoded[row + x] = (value + (pa <= pb && pa <= pc ? left : pb <= pc ? up : ul)) & 255; }
      else throw new Error('Unsupported PNG filter: ' + filter);
    }
  }
  const pixels = type === 6 ? decoded : Buffer.alloc(width * height * 4);
  if (type === 2) for (let i = 0, j = 0; i < decoded.length; i += 3, j += 4) { pixels[j] = decoded[i]; pixels[j + 1] = decoded[i + 1]; pixels[j + 2] = decoded[i + 2]; pixels[j + 3] = 255; }
  return { width, height, pixels };
}

function visibleMask(frame) {
  const background = [frame.pixels[0], frame.pixels[1], frame.pixels[2]];
  const mask = new Uint8Array(frame.width * frame.height);
  let area = 0;
  for (let p = 0, i = 0; p < mask.length; p++, i += 4) {
    const alpha = frame.pixels[i + 3];
    const distance = Math.abs(frame.pixels[i] - background[0]) + Math.abs(frame.pixels[i + 1] - background[1]) + Math.abs(frame.pixels[i + 2] - background[2]);
    if (alpha > 20 && distance > 12) { mask[p] = 1; area++; }
  }
  return { mask, area };
}
function compareMasks(a, b) {
  let union = 0, changed = 0;
  for (let i = 0; i < a.length; i++) { if (a[i] || b[i]) { union++; if (a[i] !== b[i]) changed++; } }
  return +(changed / Math.max(1, union)).toFixed(6);
}
function sha256(file) { return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'); }
function frameEvidence(file, baseline) {
  const frame = pngFrame(file), current = visibleMask(frame);
  const areaRatio = current.area / Math.max(1, baseline.area);
  const silhouette = compareMasks(baseline.mask, current.mask);
  const blank = current.area < Math.max(25, baseline.area * 0.08);
  const suddenLoss = areaRatio < 0.55;
  const classification = blank || suddenLoss ? 'reject_obvious_loss' : silhouette > 0.35 || areaRatio < 0.8 ? 'needs_human_review' : 'safe_candidate';
  return { image: file, sha256: sha256(file), width: frame.width, height: frame.height, subjectPixels: current.area, baselineSubjectPixels: baseline.area, subjectAreaRatio: +areaRatio.toFixed(6), silhouetteChangeRatio: silhouette, blank, suddenLoss, visibility_pass: !blank && !suddenLoss, classification };
}
function analyzeRun(run) {
  const files = [];
  const runRoot = run.root;
  const sweep = path.join(runRoot, 'parameter-sweep-report.json');
  const combo = path.join(runRoot, 'custom-combination-sweep-report.json');
  const report = fs.existsSync(sweep) ? JSON.parse(fs.readFileSync(sweep, 'utf8')) : fs.existsSync(combo) ? JSON.parse(fs.readFileSync(combo, 'utf8')) : null;
  if (!report) return { name: run.name, error: 'no supported probe report' };
  const framePaths = report.frames || (report.combinations || []).flatMap(item => item.frames || []);
  for (const item of framePaths) { const file = path.isAbsolute(item) ? item : path.join(runRoot, item); if (fs.existsSync(file)) files.push(file); }
  if (files.length < 2) return { name: run.name, error: 'fewer than two PNG frames' };
  const baseline = visibleMask(pngFrame(files[0]));
  const frames = files.map(file => frameEvidence(file, baseline));
  const rejected = frames.filter(frame => frame.classification === 'reject_obvious_loss').length;
  const reviewed = frames.filter(frame => frame.classification === 'needs_human_review').length;
  return { name: run.name, report, baselineSubjectPixels: baseline.area, visibility_pass: rejected === 0, classification: rejected ? 'reject_obvious_loss' : reviewed ? 'needs_human_review' : 'safe_candidate', frames };
}

const runs = (manifest.runs || []).filter(run => run.name !== 'param97_native').map(analyzeRun);
const output = { schema: 'live2d-visibility-boundary-report.v1', sourceManifest: manifestPath, mapWriteAllowed: false, llmExposed: false, semanticStatus: 'unassigned', disclaimer: 'visibility_pass only means no obvious whole-model blanking or subject-area collapse; it does not identify hand visibility, wave semantics, naturalness, or certification.', runs };
const outPath = path.join(path.dirname(manifestPath), 'visibility-boundary-report.json');
fs.writeFileSync(outPath, JSON.stringify(output, null, 2) + '\n');
console.log(JSON.stringify({ output: outPath, runs: runs.map(run => ({ name: run.name, visibility_pass: run.visibility_pass, classification: run.classification, frameCount: run.frames ? run.frames.length : 0 })) }, null, 2));
