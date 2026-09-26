'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

function readManifest(file) {
  if (!file || !path.isAbsolute(file)) throw new Error('manifest path must be absolute');
  if (!fs.existsSync(file)) throw new Error(`manifest not found: ${file}`);
  const value = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
  if (value.schema !== 'live2d-tool-manifest/v1') throw new Error('unsupported manifest schema');
  return value;
}
function validateManifest(manifest, overrides = {}) {
  const model3Json = overrides.model3Json || process.env.LIVE2D_MODEL3_JSON || manifest.model?.model3Json;
  const probeExecutable = overrides.probeExecutable || process.env.LIVE2D_PROBE_EXE || manifest.probe?.executable;
  const outputRoot = overrides.outputRoot || process.env.LIVE2D_OUTPUT_ROOT || manifest.run?.outputRoot;
  if (!model3Json || !fs.existsSync(model3Json)) throw new Error(`model3Json missing: ${model3Json || '<unset>'}`);
  if (!probeExecutable || !fs.existsSync(probeExecutable)) throw new Error(`Probe executable missing: ${probeExecutable || '<unset>'}`);
  if (manifest.probe?.args !== undefined && (!Array.isArray(manifest.probe.args) || !manifest.probe.args.every(value => typeof value === 'string'))) throw new Error('probe.args must be an array of strings');
  if (manifest.run?.writerMode !== undefined && !['frozen', 'physics'].includes(manifest.run.writerMode)) throw new Error('run.writerMode must be frozen or physics');
  const timeoutMs = Number(process.env.LIVE2D_TIMEOUT_MS || manifest.run?.timeoutMs || 900000);
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) throw new Error('run.timeoutMs must be a positive number');
  if (!outputRoot || !path.isAbsolute(outputRoot)) throw new Error('outputRoot must be absolute');
  if (fs.existsSync(outputRoot) && fs.readdirSync(outputRoot).length > 0 && !fs.existsSync(path.join(outputRoot, '.test_mode')) && !overrides.allowExistingMarked) throw new Error('refusing non-empty unmarked outputRoot');
  fs.mkdirSync(outputRoot, { recursive: true });
  const marker = path.join(outputRoot, '.test_mode');
  if (!fs.existsSync(marker)) fs.writeFileSync(marker, '');
  return {
    model3Json, probeExecutable, outputRoot,
    modelSha256: crypto.createHash('sha256').update(fs.readFileSync(model3Json)).digest('hex'),
    expectedParameterCount: manifest.model?.expectedParameterCount ?? 244,
  };
}
module.exports = { readManifest, validateManifest };
