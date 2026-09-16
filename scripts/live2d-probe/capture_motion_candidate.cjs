/* End-to-end certification capture for one imported motion candidate.
 * Usage: node scripts/live2d-probe/capture_motion_candidate.cjs <probe-exe> <candidate.json> <skill-id>
 * Creates an isolated .test_mode root, runs Live2DProbe.exe in motion playback
 * mode, freezes the frame sequence into a review packet, then runs DeepSeek and
 * GLM packet reviews (cached; explicit user authorization assumed as with all
 * cloud reviews). Prints both verdicts.
 */
'use strict';
const { spawnSync, spawn } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const exe = process.argv[2];
const candidateFile = process.argv[3];
const skillId = process.argv[4];
const semanticClaim = process.argv[5] || '';
if (!exe || !fs.existsSync(exe)) throw new Error('Missing probe exe.');
if (!candidateFile || !fs.existsSync(candidateFile)) throw new Error('Missing candidate json.');
if (!skillId || !/^[a-z0-9_\-]+$/i.test(skillId)) throw new Error('Missing valid skill id.');
// semanticClaim 缺省时仅取证（capture-only）；候选包/评审阶段才强制要求声明。
const candidate = JSON.parse(fs.readFileSync(candidateFile, 'utf8'));
if (candidate.candidateId !== skillId) throw new Error(`candidateId mismatch: ${candidate.candidateId} != ${skillId}`);

const root = path.join(os.tmpdir(), 'fuxuan_motion_cert_' + crypto.createHash('sha1').update(skillId).digest('hex').slice(0, 8));
fs.rmSync(root, { recursive: true, force: true });
fs.mkdirSync(root, { recursive: true });
fs.writeFileSync(path.join(root, '.test_mode'), '');

const abs = p => path.resolve(p);
const env = { ...process.env, FU_XUAN_DATA: root, FU_XUAN_PROBE_MOTION_PLAYBACK: abs(candidateFile), FU_XUAN_PROBE_MOTION_STEPS: process.env.FU_XUAN_PROBE_MOTION_STEPS || '16' };
console.log(`[capture] probe root: ${root}`);
const child = spawn(abs(exe), [], { env, stdio: 'ignore' });
const exitCode = awaitExit(child, 180000);

function awaitExit(child, timeoutMs) {
  return new Promise((resolve) => {
    const timer = setTimeout(() => { try { child.kill(); } catch { /* already dead */ } resolve(-1); }, timeoutMs);
    child.on('exit', code => { clearTimeout(timer); resolve(code); });
  });
}

(async () => {
  const code = await exitCode;
  if (code !== 0) {
    const failure = path.join(root, 'capability-report-probe-window.failure.txt');
    if (fs.existsSync(failure)) console.error(fs.readFileSync(failure, 'utf8').split('\n').slice(0, 6).join('\n'));
    throw new Error('Probe playback failed with exit code ' + code);
  }
  const report = JSON.parse(fs.readFileSync(path.join(root, 'motion-playback-report.json'), 'utf8'));
  console.log(`[capture] frames=${report.frames.length} peak=${report.peakMeanDifference?.toFixed(3)} adjacent=${report.maxAdjacentMeanDifference?.toFixed(3)} reset=${report.resetMeanDifference?.toFixed(3)} resetStable=${report.resetStable}`);
  if (!semanticClaim) {
    console.log(`[capture] capture-only done (no claim). frame dir: ${path.join(root, 'probe_window')}`);
    return;
  }

  const run = (args) => {
    const r = spawnSync(process.execPath, args, { encoding: 'utf8', timeout: 240000, env: process.env });
    return { code: r.status, out: (r.stdout || '') + (r.stderr || '') };
  };
  const packet = run([path.join(__dirname, 'build_candidate_review_packet.cjs'), root, skillId, 'probe_window', semanticClaim].filter(a => a !== ''));
  if (packet.code !== 0) throw new Error('Packet build failed:\n' + packet.out);
  const packetSha = JSON.parse(packet.out.split('\n').find(l => l.startsWith('{')) ? packet.out.slice(packet.out.indexOf('{')) : '{}');
  console.log(`[capture] packet: ${packetSha.packetSha256?.slice(0, 16)}… frames=${packetSha.frameCount}`);

  const deepseek = run([path.join(__dirname, 'review_candidate_packet_deepseek.cjs'), root, skillId]);
  console.log('--- deepseek ---\n' + deepseek.out.trim().split('\n').slice(-14).join('\n'));
  const glm = run([path.join(__dirname, 'review_candidate_packet_glm.cjs'), root, skillId, process.env.FU_XUAN_GLM_MODEL || 'glm-4.5v']);
  console.log('--- glm ---\n' + glm.out.trim().split('\n').slice(-14).join('\n'));
  console.log(`[capture] done. isolated root: ${root}`);
})().catch(e => { console.error(e.message); process.exitCode = 1; });
