'use strict';
/* 黑盒回归：certified_motion_runtime_drive.cjs 的隔离根分配与清理归属。
 *
 * 被修复的缺陷
 * ------------
 * 该入口原先按 skillId 算出一个**固定名**目录
 *   %TEMP%\fuxuan_certified_motion_<skillId 归一化>
 * 并在启动播放器**之前**无条件执行 fs.rmSync(root,{recursive:true,force:true})。
 * 固定名意味着路径可预测，而无条件递归删除意味着同参数第二次运行会先抹掉上一次
 * 的产物——更严重的是，任何与该 skillId 同名的既有目录都会被删掉。日期真相文档
 * docs/truth/l3-screen-side-arm-raise-isolated-human-review.md 就引用过
 * fuxuan_certified_motion_screen_side_arm_raise 内的审核包。
 *
 * 本测试断言什么
 * --------------
 * 用测试自建的假 .cmd 顶替播放器（**绝不启动真实 Player**），用含本次唯一 token 的
 * 合法 skillId 按**旧公式**推导出固定目录名，并由测试自己先建立该固定名目录 +
 * 哨兵文件（占用检查：已存在则停止）。然后以同一参数运行新入口两次，断言：
 *   * 旧固定名目录与哨兵正文/mtime 逐字节不变（旧实现会把它删掉）；
 *   * 固定名目录内没有被写入 .test_mode 或 certified_motions（入口不再碰它）；
 *   * 两次运行得到两个**互异**的新根，且都带本次前缀；
 *   * .test_mode 与候选副本只出现在新根内，且候选副本内容等于源文件；
 *   * 启动失败时新根被保留（失败保留语义未变），stderr 打印 preserved test root；
 *   * 假播放器**从未真正执行**（它会写 marker；marker 不存在即证明未执行）。
 *
 * 真实历史固定目录
 * ----------------
 * fuxuan_certified_motion_screen_side_arm_raise 只做**只读**存在性与前后快照核对：
 * 存在则要求逐字节不变，不存在也不判失败；本测试既不创建也不清理它。
 *
 * 清理归属
 * --------
 * 本测试自己会删临时目录，因此清理前逐个核实：绝对路径与真实路径都是系统 Temp 的
 * **直接子目录**、名称含本次 token、对象仍是原先创建的真实目录而非链接/联接、
 * 且就是登记的精确路径。无法证明归属则保留现场并非零退出，绝不强删。
 */
const { spawnSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const REPO_ROOT = path.resolve(__dirname, '..', '..', '..');
const ENTRY = path.join(REPO_ROOT, 'scripts', 'test', 'certified_motion_runtime_drive.cjs');
const TMP = path.resolve(os.tmpdir());
const TMP_REAL = fs.realpathSync(TMP);

// 日期真相文档引用的历史固定名目录：只读核对，绝不创建或清理。
const HISTORICAL_SKILL = 'screen_side_arm_raise';
const HISTORICAL_DIR = path.join(TMP, 'fuxuan_certified_motion_' + HISTORICAL_SKILL);

const RUN_TOKEN = ('t' + process.pid.toString(36) + Date.now().toString(36)
  + Math.random().toString(36).slice(2, 8)).replace(/[^a-z0-9]/g, '');
if (!/^[a-z0-9]+$/.test(RUN_TOKEN)) {
  throw new Error('generated run token is not usable as a skill id fragment: ' + RUN_TOKEN);
}

// 合法 skillId（入口的校验是 /^[a-z0-9_\-]+$/i），token 保证本次运行的目录名唯一。
const SKILL_ID = 'certified_regress_' + RUN_TOKEN;
const SLUG = SKILL_ID.toLowerCase().replace(/[^a-z0-9]/g, '_');
// 旧实现的固定目录公式（注意：结尾没有下划线）。
const FIXED_DIR = path.join(TMP, 'fuxuan_certified_motion_' + SLUG);
// 新实现的前缀：可识别前缀保留，并由 mkdtempSync 追加 6 位随机后缀。
const ROOT_PREFIX = 'fuxuan_certified_motion_' + SLUG + '_';

const results = [];
function check(name, ok, detail) {
  results.push({ name, ok: !!ok, detail });
}

function lstatOrNull(target) {
  try {
    return fs.lstatSync(target);
  } catch {
    return null;
  }
}

function sha256(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}

// 目录结构 + 大小 + mtime + 内容 SHA-256：仅比对大小与 mtime 对「长度不变、mtime 被
// 还原的重写」是盲的，哈希才能证明正文没被改过。
function snapshot(dir) {
  if (!fs.existsSync(dir)) return null;
  const rows = ['<self>|' + fs.statSync(dir).mtimeMs];
  const walk = (current, rel) => {
    for (const entry of fs.readdirSync(current, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const abs = path.join(current, entry.name);
      const relPath = rel ? rel + '/' + entry.name : entry.name;
      if (entry.isDirectory()) {
        rows.push(relPath + '|<dir>|' + fs.statSync(abs).mtimeMs);
        walk(abs, relPath);
      } else {
        const stat = fs.statSync(abs);
        rows.push(relPath + '|' + stat.size + '|' + stat.mtimeMs + '|sha256=' + sha256(abs));
      }
    }
  };
  walk(dir, '');
  return rows.join('\n');
}

// 归属判定：true = 可证明属于本次运行；null = 已不存在；字符串 = 不能证明的原因。
// exactCreatedAt 传入时，路径必须**就是**登记的那一刻创建的精确路径，不从前缀推断。
function verifyOwned(target, exactCreatedAt) {
  const abs = path.resolve(target);
  if (path.dirname(abs) !== TMP) return 'absolute path is not a direct child of os.tmpdir()';
  const stat = lstatOrNull(abs);
  if (stat === null) return null;
  if (!path.basename(abs).includes(RUN_TOKEN)) return 'name does not carry this run\'s token';
  if (exactCreatedAt && abs !== exactCreatedAt) return 'path is not the exact path this test created';
  if (stat.isSymbolicLink()) return 'path is a link now, not the directory this test created';
  if (!stat.isDirectory()) return 'path is no longer a directory';
  let real;
  try {
    real = fs.realpathSync(abs);
  } catch (error) {
    return 'cannot resolve the real location (' + (error.code || error.message) + ')';
  }
  if (path.dirname(real) !== TMP_REAL) return 'real location is not a direct child of os.tmpdir()';
  if (!path.basename(real).includes(RUN_TOKEN)) return 'real name does not carry this run\'s token';
  return true;
}

function runEntry(fakePlayer, candidateFile) {
  return spawnSync(process.execPath, [ENTRY, fakePlayer, SKILL_ID, candidateFile], {
    encoding: 'utf8',
    timeout: 60000,
    cwd: REPO_ROOT
  });
}

function extractPreservedRoot(output) {
  const match = /^preserved test root: (.+)$/m.exec(output);
  return match ? match[1].trim() : null;
}

const cleanupTargets = [];
let cleanupFailed = 0;
const preserved = [];
const located = [];
let scaffoldDir = null;
let fixedDirCreated = false;

try {
  // ---------------------------------------------------------------- 前置事实
  check('target entry exists at its current path', fs.existsSync(ENTRY), ENTRY);
  check('generated skill id is legal for the entry', /^[a-z0-9_\-]+$/i.test(SKILL_ID), SKILL_ID);
  check('skill id carries this run\'s token', SKILL_ID.includes(RUN_TOKEN), SKILL_ID);
  check('new root prefix keeps the recognizable family prefix',
    ROOT_PREFIX.startsWith('fuxuan_certified_motion_'), ROOT_PREFIX);
  check('new root prefix is derived from the old fixed name',
    ROOT_PREFIX === 'fuxuan_certified_motion_' + SLUG + '_');
  check('new root prefix cannot collide with the old fixed name',
    !path.basename(FIXED_DIR).startsWith(ROOT_PREFIX), path.basename(FIXED_DIR));

  // 只读快照：历史固定目录（存在则比对，不存在不判失败）。
  const historicalBefore = snapshot(HISTORICAL_DIR);
  check('historical fixed dir is only inspected read-only (never created by this test)',
    historicalBefore === null || lstatOrNull(HISTORICAL_DIR).isDirectory());

  // ------------------------------------------------- 测试脚手架（含本次 token）
  scaffoldDir = fs.mkdtempSync(path.join(TMP, 'fuxuan_certmotion_scaffold' + RUN_TOKEN + '_'));
  cleanupTargets.push({ target: scaffoldDir, createdAt: scaffoldDir, label: 'scaffold' });
  const candidateFile = path.join(scaffoldDir, 'candidate.json');
  const candidateBody = JSON.stringify({ skillId: SKILL_ID, note: 'regression placeholder, never loaded by a real player' });
  fs.writeFileSync(candidateFile, candidateBody);

  // 假播放器：若被真正执行就会写 marker，从而暴露「其实启动了子进程」。
  const markerPath = path.join(scaffoldDir, 'fake-player-ran.marker');
  const fakePlayer = path.join(scaffoldDir, 'fake-player.cmd');
  fs.writeFileSync(fakePlayer,
    '@echo off\r\n'
    + 'echo ran > "' + markerPath + '"\r\n'
    + 'exit /b 0\r\n');
  check('fake .cmd player was created by this test', fs.existsSync(fakePlayer));
  check('fake player marker does not exist yet', !fs.existsSync(markerPath));

  // ------------------------------------- 占用检查 + 建立旧固定名目录与哨兵
  // 旧公式推导出的固定目录必须先查占用：已存在则停止，绝不复用、绝不覆盖。
  if (fs.existsSync(FIXED_DIR)) {
    throw new Error('refusing to run: the fixed-name directory derived by the old formula already exists: ' + FIXED_DIR);
  }
  // 非递归 mkdir：目录若在检查与创建之间出现，会以 EEXIST 失败而不是静默复用。
  fs.mkdirSync(FIXED_DIR);
  fixedDirCreated = true;
  cleanupTargets.push({ target: FIXED_DIR, createdAt: FIXED_DIR, label: 'fixed-name sentinel' });
  const sentinelPath = path.join(FIXED_DIR, 'sentinel.txt');
  const sentinelBody = 'sentinel for ' + RUN_TOKEN + '\n';
  fs.writeFileSync(sentinelPath, sentinelBody);
  fs.mkdirSync(path.join(FIXED_DIR, 'probe_window'));
  fs.writeFileSync(path.join(FIXED_DIR, 'probe_window', 'frames.txt'), 'nested sentinel\n');
  const sentinelStatBefore = fs.statSync(sentinelPath);
  const sentinelMtimeBefore = sentinelStatBefore.mtimeMs;
  const sentinelHashBefore = sha256(sentinelPath);
  const fixedSnapshotBefore = snapshot(FIXED_DIR);
  check('fixed-name sentinel directory created by this test', lstatOrNull(FIXED_DIR).isDirectory());
  check('sentinel file created with known content', sha256(sentinelPath) === sentinelHashBefore);
  // 非空过保证：下面的「快照不变」断言必须是「两个真实快照相等」，而不是
  // 「null === null」。若固定目录在运行中被删掉，after 为 null 而 before 非 null，
  // 相等断言即失败——这正是旧实现的失败模式。
  check('pre-run fixed-name snapshot is a real snapshot (not null)', fixedSnapshotBefore !== null);
  check('sentinel hash is a real sha256', /^[0-9a-f]{64}$/.test(sentinelHashBefore), sentinelHashBefore);

  // ------------------------------------------- 断言工具自身的灵敏度自检
  // 只使用本测试自己的目录，不涉及被修复入口、不运行任何旧实现。
  const sensitivityDir = path.join(scaffoldDir, 'snapshot-sensitivity');
  fs.mkdirSync(sensitivityDir);
  const sensitivityFile = path.join(sensitivityDir, 'probe.txt');
  fs.writeFileSync(sensitivityFile, 'AAAA');
  const sensitivityBefore = snapshot(sensitivityDir);
  const sensitivityStat = fs.statSync(sensitivityFile);
  // 长度不变的重写 + 把 mtime 还原：仅比对大小/mtime 的指纹对此是盲的，SHA-256 不是。
  fs.writeFileSync(sensitivityFile, 'BBBB');
  fs.utimesSync(sensitivityFile, sensitivityStat.atime, sensitivityStat.mtime);
  const sensitivityAfter = snapshot(sensitivityDir);
  check('snapshot() detects a same-size, mtime-restored rewrite (assertions have teeth)',
    sensitivityBefore !== null && sensitivityAfter !== null && sensitivityBefore !== sensitivityAfter);

  // 归属闸门的负向对照：只有「系统 Temp 的直接子目录」且「不是链接」才放行。
  const notDirectChild = path.join(TMP, 'fuxuan_certmotion_nested' + RUN_TOKEN, 'inner');
  check('ownership gate rejects a path that is not a direct child of os.tmpdir()',
    typeof verifyOwned(notDirectChild, null) === 'string',
    String(verifyOwned(notDirectChild, null)));
  const linkPath = path.join(TMP, 'fuxuan_certmotion_link' + RUN_TOKEN);
  fs.symlinkSync(scaffoldDir, linkPath, 'junction');
  cleanupTargets.push({ target: linkPath, createdAt: linkPath, label: 'junction control', kind: 'control', expect: 'link' });
  check('ownership gate rejects a link/junction even when it is a direct child of os.tmpdir()',
    verifyOwned(linkPath, linkPath) === 'path is a link now, not the directory this test created',
    String(verifyOwned(linkPath, linkPath)));
  const filePath = path.join(TMP, 'fuxuan_certmotion_file' + RUN_TOKEN);
  fs.writeFileSync(filePath, '');
  cleanupTargets.push({ target: filePath, createdAt: filePath, label: 'file control', kind: 'control', expect: 'file' });
  check('ownership gate rejects a non-directory direct child of os.tmpdir()',
    verifyOwned(filePath, filePath) === 'path is no longer a directory',
    String(verifyOwned(filePath, filePath)));
  check('ownership gate rejects a path that is not the exact path this test created',
    typeof verifyOwned(FIXED_DIR, path.join(TMP, 'fuxuan_certmotion_other' + RUN_TOKEN)) === 'string');

  // ------------------------------------------- 同一参数运行新入口两次
  const roots = [];
  for (let run = 0; run < 2; run++) {
    const tag = 'run ' + (run + 1);
    const res = runEntry(fakePlayer, candidateFile);
    const output = String(res.stdout || '') + String(res.stderr || '');
    const reported = extractPreservedRoot(output);
    check(tag + ': exited non-zero (fake player cannot be launched)', res.status !== 0 && res.status !== null,
      'status=' + res.status);
    check(tag + ': no test-process timeout (the 90s wait was never reached)',
      !res.error && res.signal === null, 'error=' + (res.error && res.error.code) + ' signal=' + res.signal);
    check(tag + ': reported a preserved test root', reported !== null);
    if (reported === null) continue;

    const abs = path.resolve(reported);
    roots.push(abs);
    located.push(abs);
    cleanupTargets.push({ target: abs, createdAt: abs, label: tag + ' root' });
    check(tag + ': root is a direct child of os.tmpdir()', path.dirname(abs) === TMP, abs);
    check(tag + ': root name carries the new unique prefix',
      path.basename(abs).startsWith(ROOT_PREFIX), path.basename(abs));
    check(tag + ': root name is <prefix> + a fresh random suffix',
      new RegExp('^' + ROOT_PREFIX.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '[A-Za-z0-9]{6}$').test(path.basename(abs)),
      path.basename(abs));
    check(tag + ': root is a real directory, not a link',
      (lstatOrNull(abs) || {}).isDirectory?.() === true && lstatOrNull(abs).isSymbolicLink() === false);
    check(tag + ': root differs from the old fixed name', abs !== FIXED_DIR);
    check(tag + ': failure kept the evidence root (preserved, not deleted)', fs.existsSync(abs));

    // .test_mode 与候选副本只应出现在本次新根内。
    check(tag + ': .test_mode is inside the new root', fs.existsSync(path.join(abs, '.test_mode')));
    const copied = path.join(abs, 'certified_motions', SKILL_ID + '.json');
    check(tag + ': candidate copy is inside the new root', fs.existsSync(copied), copied);
    if (fs.existsSync(copied)) {
      check(tag + ': candidate copy content equals the source file', sha256(copied) === sha256(candidateFile));
    }
  }

  // ------------------------------------------- 两次运行互异、且不复用旧固定名
  check('both runs reported a root', roots.length === 2, JSON.stringify(roots));
  if (roots.length === 2) {
    check('the two runs used different roots (no reuse)', roots[0] !== roots[1], JSON.stringify(roots));
    check('run 1\'s root still exists after run 2 (not reused, not deleted)', fs.existsSync(roots[0]), roots[0]);
    check('run 2\'s root still exists (nothing deletes it)', fs.existsSync(roots[1]), roots[1]);
    check('neither run root is the old fixed-name directory',
      roots[0] !== FIXED_DIR && roots[1] !== FIXED_DIR);
    check('neither run root collides with the fixed-name directory',
      !roots.some(root => path.basename(root) === path.basename(FIXED_DIR)));
  }

  // ------------------------------------------- 旧固定名目录与哨兵必须原样
  check('fixed-name sentinel directory still exists', lstatOrNull(FIXED_DIR) !== null, FIXED_DIR);
  const fixedSnapshotAfter = snapshot(FIXED_DIR);
  check('fixed-name sentinel directory is byte-identical (structure/size/mtime/sha256)',
    fixedSnapshotAfter === fixedSnapshotBefore,
    fixedSnapshotBefore === fixedSnapshotAfter ? '' : 'snapshot changed');
  const sentinelPathAfter = path.join(FIXED_DIR, 'sentinel.txt');
  check('sentinel file content is unchanged', fs.existsSync(sentinelPathAfter)
    && sha256(sentinelPathAfter) === sentinelHashBefore);
  check('sentinel file mtime is unchanged', fs.existsSync(sentinelPathAfter)
    && fs.statSync(sentinelPathAfter).mtimeMs === sentinelMtimeBefore);
  check('nested sentinel file survived', fs.existsSync(path.join(FIXED_DIR, 'probe_window', 'frames.txt')));
  // 旧实现会 rmSync 后重建固定目录并写入 .test_mode / certified_motions。
  check('entry did NOT write .test_mode into the fixed-name directory',
    !fs.existsSync(path.join(FIXED_DIR, '.test_mode')));
  check('entry did NOT write certified_motions into the fixed-name directory',
    !fs.existsSync(path.join(FIXED_DIR, 'certified_motions')));
  check('entry did NOT write inbox.txt into the fixed-name directory',
    !fs.existsSync(path.join(FIXED_DIR, 'inbox.txt')));

  // ------------------------------------------- 假播放器从未真正执行
  check('fake .cmd player never actually ran (marker absent)', !fs.existsSync(markerPath), markerPath);
  check('fake .cmd player still exists (untouched)', fs.existsSync(fakePlayer));

  // ------------------------------------------- 历史固定目录：只读前后核对
  const historicalAfter = snapshot(HISTORICAL_DIR);
  check('historical fixed dir unchanged by this test (or still absent)',
    historicalAfter === historicalBefore,
    'before=' + (historicalBefore === null ? 'absent' : 'present') + ' after=' + (historicalAfter === null ? 'absent' : 'present'));
  // 非空过：存在时必须仍是同一个真实快照，不能靠「前后都是 null」蒙过去。
  check('historical fixed dir comparison is not vacuous',
    historicalBefore === null ? historicalAfter === null : historicalAfter !== null,
    'before=' + (historicalBefore === null ? 'absent' : 'present') + ' after=' + (historicalAfter === null ? 'absent' : 'present'));
  check('historical fixed dir was not created by this test',
    historicalBefore !== null || historicalAfter === null);
  check('historical fixed dir is still a real directory, not a link',
    historicalAfter === null || (lstatOrNull(HISTORICAL_DIR).isDirectory() && lstatOrNull(HISTORICAL_DIR).isSymbolicLink() === false));
  check('historical fixed dir is not registered for cleanup',
    !cleanupTargets.some(item => path.resolve(item.target) === path.resolve(HISTORICAL_DIR)));
} catch (error) {
  check('regression harness completed without an unexpected error', false, error.message);
} finally {
  // ------------------------------------------------------ 带归属证明的清理
  for (const item of cleanupTargets.slice().reverse()) {
    const abs = path.resolve(item.target);
    if (item.kind === 'control') {
      // 负向对照**故意**不是目录（一个普通文件、一个目录联接），因此不能套用
      // 「必须是本次创建的真实目录」那道闸门；改用同等的直接子目录 + 本次 token +
      // 精确路径 + 预期对象类型核对，并用 unlink 只移除该对象本身（不跟随联接）。
      const stat = lstatOrNull(abs);
      if (stat === null) continue;
      const proven = path.dirname(abs) === TMP
        && path.basename(abs).includes(RUN_TOKEN)
        && abs === item.createdAt
        && (item.expect === 'link'
          ? stat.isSymbolicLink()
          : !stat.isSymbolicLink() && !stat.isDirectory());
      if (!proven) {
        preserved.push(item.label + ' -> ' + abs + ' (ownership unproven)');
        cleanupFailed++;
        continue;
      }
      try {
        fs.unlinkSync(abs);
      } catch (error) {
        preserved.push(item.label + ' -> ' + abs + ' (delete failed: ' + (error.code || error.message) + ')');
        cleanupFailed++;
      }
      continue;
    }
    const verdict = verifyOwned(item.target, item.createdAt);
    if (verdict === null) continue;
    if (verdict !== true) {
      preserved.push(item.label + ' -> ' + abs + ' (' + verdict + ')');
      cleanupFailed++;
      continue;
    }
    try {
      fs.rmSync(abs, { recursive: true, force: true });
    } catch (error) {
      preserved.push(item.label + ' -> ' + abs + ' (delete failed: ' + (error.code || error.message) + ')');
      cleanupFailed++;
    }
  }
  check('every directory this test created was safely cleaned up',
    cleanupFailed === 0, preserved.join(' | '));
  check('no per-run root left behind',
    located.every(root => lstatOrNull(root) === null),
    located.filter(root => lstatOrNull(root) !== null).join(', '));
  check('fixed-name sentinel directory was cleaned up',
    fixedDirCreated && lstatOrNull(FIXED_DIR) === null, FIXED_DIR);
  check('scaffold directory was cleaned up',
    scaffoldDir !== null && lstatOrNull(scaffoldDir) === null, String(scaffoldDir));
  check('negative-control objects (junction + file) were cleaned up',
    ['fuxuan_certmotion_link' + RUN_TOKEN, 'fuxuan_certmotion_file' + RUN_TOKEN]
      .every(name => lstatOrNull(path.join(TMP, name)) === null));
  // 历史目录永远不在清理目标内；这里再确认一次它没被顺手删掉。
  check('historical fixed dir was NOT deleted by the cleanup',
    snapshot(HISTORICAL_DIR) === null ? true : lstatOrNull(HISTORICAL_DIR) !== null);
}

const failed = results.filter(r => !r.ok);
for (const r of results) {
  if (!r.ok) console.log('  [FAIL] ' + r.name + (r.detail ? ' :: ' + r.detail : ''));
}
if (preserved.length) {
  console.log('  preserved scenes (ownership unproven):');
  for (const line of preserved) console.log('    ' + line);
}
const historicalState = snapshot(HISTORICAL_DIR) === null ? 'absent' : 'present';
if (failed.length === 0) {
  console.log('certified-motion runtime root regression OK (' + results.length
    + ' checks, 2 fresh roots, 1 fixed-name sentinel, historical fixed dir ' + historicalState + ')');
  process.exit(0);
}
console.log('certified-motion runtime root regression FAILED: ' + failed.length + '/' + results.length + ' checks');
process.exit(1);
