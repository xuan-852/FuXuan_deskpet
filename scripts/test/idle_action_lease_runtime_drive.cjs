'use strict';
// 隔离验证：空闲动作取得写入者租约，并能在测试退出前由 ResetIdleAction 收束释放。
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');
const exe = process.argv[2];
if (!exe || !fs.existsSync(exe)) throw new Error('missing DesktopPet.exe');
const root = path.join(os.tmpdir(), 'fuxuan_idle_lease_runtime_20260918');
const inbox = path.join(root, 'inbox.txt');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const log = () => { try { return fs.readFileSync(path.join(root, 'logs', 'player_log.txt'), 'utf8'); } catch { return ''; } };
async function waitFor(marker, timeout = 30000) {
  for (const until = Date.now() + timeout; Date.now() < until; await sleep(150)) if (log().includes(marker)) return;
  throw new Error('timeout: ' + marker);
}
async function send(command, waitMs = 400) { fs.writeFileSync(inbox, command); await sleep(waitMs); fs.writeFileSync(inbox, ''); await sleep(80); }
(async () => {
  fs.rmSync(root, { recursive: true, force: true });
  fs.mkdirSync(root, { recursive: true });
  fs.writeFileSync(path.join(root, '.test_mode'), '');
  fs.writeFileSync(inbox, '');
  const player = spawn(exe, [], { env: { ...process.env, FU_XUAN_DATA: root }, stdio: 'ignore' });
  try {
    await waitFor('[DesktopPet] 落地');
    await send('@@sim:idle-actions:off');
    await send('@@sim:idle-action:1');
    await waitFor('[Live2DInputCoordinator] Accepted LegacyAction/idle-action/idle:1');
    await send('@@test:quit', 1200);
    await waitFor('Released LegacyAction/idle-action/idle:1');
    const output = log();
    for (const marker of ['[TestInbox] idle-action requested: 1', 'Accepted LegacyAction/idle-action/idle:1', 'Released LegacyAction/idle-action/idle:1'])
      if (!output.includes(marker)) throw new Error('log missing: ' + marker);
    console.log(root);
  } finally { if (!player.killed) player.kill(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
