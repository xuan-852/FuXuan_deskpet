#!/usr/bin/env node
// 质量测试启动前检查：不打印完整 API key，只打印配置状态和安全 Key ID。
'use strict';
const fs = require('fs');
const path = require('path');

const mode = process.argv[2];
const visual = process.argv.includes('--visual');
if (!['local', 'cloud'].includes(mode)) {
  console.error('用法: node scripts/test/quality_preflight.cjs <local|cloud> [--visual]');
  process.exit(2);
}

const root = path.resolve(__dirname, '..', '..');
const exe = path.join(root, 'Build', 'DesktopPet.exe');
const dll = path.join(root, 'Build', 'DesktopPet_Data', 'Managed', 'Assembly-CSharp.dll');
const dataRoot = process.env.FU_XUAN_DATA;
const expectedDeepSeekKeyId = process.env.FU_XUAN_EXPECTED_DEEPSEEK_KEY_ID || '';
const errors = [];

function configured(name) { return Boolean(process.env[name]); }
function keyId(key) {
  if (!key) return 'missing';
  if (key.length <= 11) return 'configured_short';
  return `${key.slice(0, 7)}*****${key.slice(-4)}`;
}

function keyIdMatches(actual, expected) {
  if (actual === expected) return true;
  const actualParts = actual.split('*****');
  const expectedParts = expected.split('*****');
  if (actualParts.length !== 2 || expectedParts.length !== 2) return false;
  return actualParts[1] === expectedParts[1]
    && (actualParts[0].startsWith(expectedParts[0]) || expectedParts[0].startsWith(actualParts[0]))
    && Math.abs(actualParts[0].length - expectedParts[0].length) <= 1;
}

if (!fs.existsSync(exe)) errors.push(`缺少 exe: ${exe}`);
if (!fs.existsSync(dll)) errors.push(`缺少运行时 DLL: ${dll}`);
if (fs.existsSync(dll)) {
  const dllText = fs.readFileSync(dll).toString('ascii');
  if (!dllText.includes('QualityTelemetry')) errors.push('运行时 DLL 不含 QualityTelemetry，疑似旧构建');
}
if (!dataRoot) errors.push('未设置 FU_XUAN_DATA，必须使用独立采样目录');
if (dataRoot && fs.existsSync(path.join(dataRoot, '.test_mode'))) {
  errors.push('采样目录存在 .test_mode，质量对照会被测试拦截污染');
}

if (mode === 'local') {
  if (!['1', 'true'].includes(String(process.env.FU_XUAN_OLLAMA).toLowerCase())) errors.push('本地组未设置 FU_XUAN_OLLAMA=1');
  if (process.env.FU_XUAN_CLOUD_BASELINE) errors.push('本地组不应设置 FU_XUAN_CLOUD_BASELINE');
} else {
  if (!['1', 'true'].includes(String(process.env.FU_XUAN_CLOUD_BASELINE).toLowerCase())) errors.push('云端组未设置 FU_XUAN_CLOUD_BASELINE=1');
  if (process.env.FU_XUAN_OLLAMA) errors.push('云端组不应设置 FU_XUAN_OLLAMA');
  if (!configured('DEEPSEEK_API_KEY')) errors.push('未配置 DEEPSEEK_API_KEY');
  if (!expectedDeepSeekKeyId) errors.push('云端组必须设置 FU_XUAN_EXPECTED_DEEPSEEK_KEY_ID，防止消耗归属错误账户');
  if (expectedDeepSeekKeyId && !keyIdMatches(keyId(process.env.DEEPSEEK_API_KEY), expectedDeepSeekKeyId)) {
    errors.push(`DeepSeek Key ID 不匹配：当前 ${keyId(process.env.DEEPSEEK_API_KEY)}，预期 ${expectedDeepSeekKeyId}`);
  }
}
if (visual && !configured('GLM_API_KEY')) errors.push('视觉评分要求 GLM_API_KEY，但当前未配置');

console.log(`模式: ${mode === 'local' ? 'Ollama 本地' : 'DeepSeek 纯云端'}${visual ? ' + GLM 视觉评分' : ''}`);
console.log(`数据目录: ${dataRoot || '(missing)'}`);
console.log(`DeepSeek key: ${configured('DEEPSEEK_API_KEY') ? 'configured' : 'missing'}`);
console.log(`DeepSeek key id: ${keyId(process.env.DEEPSEEK_API_KEY)}`);
if (mode === 'cloud') console.log(`Expected key id: ${expectedDeepSeekKeyId || '(missing)'}`);
console.log(`GLM key: ${configured('GLM_API_KEY') ? 'configured' : 'missing'}`);
console.log(`Reply judge: ${process.env.FU_XUAN_REPLY_JUDGE || 'rule'}`);

if (errors.length) {
  console.error('预检失败：');
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}
console.log('[PASS] 质量测试预检通过。');
