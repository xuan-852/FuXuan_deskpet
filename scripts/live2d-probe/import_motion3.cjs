/* Import external .motion3.json files as LegacyCandidate evidence inputs.
 * Usage: node scripts/live2d-probe/import_motion3.cjs <motions-root> <capability-catalog.json> <output-dir>
 * - Matches motion curve parameter IDs against the isolated capability catalog.
 * - Clamps values into the probed [minimum, maximum] range per parameter.
 * - Emits an aggregate import report and one candidate definition per motion.
 * - Never writes maps, catalogs or runtime state; output is candidate evidence only.
 * License note: official Live2D sample motions are Free Material; raw motions and
 * derived curve data stay outside version control (redistribution prohibited).
 */
'use strict';
const fs = require('fs');
const path = require('path');

const motionsRoot = process.argv[2];
const catalogPath = process.argv[3];
const outputDir = process.argv[4];
for (const [name, p, needDir] of [['motions-root', motionsRoot, true], ['catalog', catalogPath, false]]) {
  if (!p || !path.isAbsolute(p) || (needDir && !fs.existsSync(p))) throw new Error(`Pass an absolute ${name}.`);
}
const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
const params = new Map(catalog.parameters.map(x => [x.parameterId, x]));
fs.mkdirSync(outputDir, { recursive: true });

function walk(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
    const full = path.join(dir, e.name);
    return e.isDirectory() ? walk(full) : (e.name.endsWith('.motion3.json') ? [full] : []);
  });
}
// Collect segments into (time,value) samples including segment endpoints and controls.
function curveSamples(segments) {
  const points = [];
  let t = segments[0], v = segments[1];
  points.push([t, v]);
  let i = 2;
  while (i < segments.length) {
    const type = segments[i];
    if (type === 0) { // linear
      t = segments[i + 1]; v = segments[i + 2]; i += 3;
    } else if (type === 1 || type === 2) { // bezier / quadratic
      t = segments[i + (type === 1 ? 7 : 5)]; v = segments[i + (type === 1 ? 8 : 6)]; i += type === 1 ? 9 : 7;
    } else if (type === 3) { // stepped
      t = segments[i + 1]; v = segments[i + 2]; i += 3;
    } else { // unknown tail: stop
      break;
    }
    points.push([t, v]);
  }
  return points;
}

const files = walk(motionsRoot).sort();
const report = { generatedAt: new Date().toISOString(), catalog: catalogPath, source: 'Live2D CubismWebSamples (develop), Free Material License', motions: [], totals: { motions: 0, fullyCovered: 0, unmatchedCurves: 0, clampedPoints: 0 } };
for (const file of files) {
  const rel = path.relative(motionsRoot, file);
  const parts = rel.split(path.sep);
  // Expect .../<ModelName>/motions/<id>.motion3.json inside the samples tree.
  const modelName = parts.length >= 3 ? parts[parts.length - 3] : 'unknown';
  let doc;
  try { doc = JSON.parse(fs.readFileSync(file, 'utf8')); } catch (e) { report.motions.push({ file: rel, error: 'parse-failed:' + e.message }); continue; }
  const meta = doc.Meta || {};
  const curves = doc.Curves || [];
  let matched = 0, partCurves = 0, clamped = 0;
  const paramCoverage = [];
  const candidateCurves = [];
  for (const curve of curves) {
    const id = curve.Id || '';
    if (!id.startsWith('Param')) { partCurves += 1; continue; }
    const entry = params.get(id);
    if (!entry) { paramCoverage.push({ id, matched: false }); continue; }
    matched += 1;
    const min = entry.range.minimum, max = entry.range.maximum;
    const samples = curveSamples(curve.Segments || []).map(([t, v]) => {
      let cv = v;
      if (cv < min) { cv = min; clamped += 1; } else if (cv > max) { cv = max; clamped += 1; }
      return [t, cv];
    });
    const lo = Math.min(...samples.map(s => s[1])), hi = Math.max(...samples.map(s => s[1]));
    paramCoverage.push({ id, matched: true, low: lo, high: hi });
    candidateCurves.push({ parameterId: id, segments: curve.Segments, samples });
  }
  const motionId = path.basename(file).replace(/\.motion3\.json$/, '');
  const fullyCovered = matched > 0 && paramCoverage.filter(p => p.matched === false).length === 0;
  const def = {
    schema: 'l3-external-motion-candidate/v1',
    candidateId: `external_${modelName}_${motionId}`,
    sourceModel: modelName,
    sourceFile: rel,
    durationSeconds: meta.Duration || null,
    fadeIn: meta.FadeInTime ?? null, fadeOut: meta.FadeOutTime ?? null,
    matchedParameterCount: matched, partCurveCount: partCurves,
    unmatchedParameterIds: paramCoverage.filter(p => !p.matched).map(p => p.id),
    clampedPointCount: clamped,
    curves: candidateCurves,
    certification: { status: 'uncertified', note: 'LegacyCandidate from external sample motions; requires four-layer certification on Fuxuan model.' }
  };
  const defPath = path.join(outputDir, `${path.parse(motionId).name}-candidate.json`);
  fs.writeFileSync(defPath, JSON.stringify(def, null, 1) + '\n', 'utf8');
  report.motions.push({ file: rel, model: modelName, motionId, duration: meta.Duration || null, paramCurves: curves.filter(c => (c.Id || '').startsWith('Param')).length, partCurves, matched, unmatched: def.unmatchedParameterIds.length, fullyCovered, clamped, candidateFile: defPath });
  report.totals.motions += 1;
  if (fullyCovered) report.totals.fullyCovered += 1;
  report.totals.unmatchedCurves += def.unmatchedParameterIds.length;
  report.totals.clampedPoints += clamped;
}
const reportPath = path.join(outputDir, 'motion-import-report.json');
fs.writeFileSync(reportPath, JSON.stringify(report, null, 1) + '\n', 'utf8');
console.log(JSON.stringify({ reportPath, totals: report.totals }, null, 1));
