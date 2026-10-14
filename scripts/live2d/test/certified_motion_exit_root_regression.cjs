'use strict';
/* 黑盒回归：certified_motion_exit_cleanup_drive.cjs 的隔离根分配与清理归属。
 *
 * 被修复的缺陷
 * ------------
 * 该入口原先按 skillId 算出一个**固定名**目录
 *   %TEMP%\fuxuan_certified_exit_<skillId 归一化>
 * 并在启动播放器**之前**执行 fs.rmSync(root,{recursive:true,force:true})，
 * 成功后（第 90 行）又无条件递归删除同一个路径。固定名意味着路径可预测，而无条件
 * 删除意味着同参数第二次运行会先抹掉上一次的产物——任何同名既有目录都会被删掉。
 *
 * 本测试断言什么
 * --------------
 * 用测试自建的假 .cmd 顶替播放器（Node 无法在无 shell 下 spawn .cmd，spawn() 同步
 * 抛错），**绝不启动真实 Player**。用含本次唯一 token 的合法 skillId 按**旧公式**
 * 推导固定目录名，先查占用（已占用则停止），仅在未占用时自建哨兵。然后以同一参数
 * 运行新入口两次，断言：
 *   * 两次运行得到两个**互异**的新根，且都带本次新前缀；
 *   * 旧固定名目录与哨兵正文/mtime/结构**逐字节不变**（旧实现会把它删掉）；
 *   * 入口**未**向固定名目录写入 .test_mode / certified_motions / inbox.txt；
 *   * .test_mode、曲线副本、inbox.txt 只出现在本次新根内，且曲线副本内容等于源文件；
 *   * 启动失败时新根被保留（失败保留语义未变），并打印 preserved test root；
 *   * 假播放器**从未真正执行**（它会写 marker；marker 不存在即证明）。
 *
 * 参数校验不得创建根
 * ------------------
 * 另跑两条无效输入（exe 不存在、curve 不存在）：必须非零退出、**不创建任何新根**，
 * 且**不得**声称保存了一个尚未创建的根（不得出现 preserved test root 行）。
 *
 * 清理归属
 * --------
 * 本测试自己会删临时目录，因此清理前逐个核实：绝对路径与真实路径都是系统 Temp 的
 * **直接子目录**、名称含本次 token、对象仍是原先创建的真实目录而非链接/联接、且就是
 * 登记的精确路径。无法证明归属则保留现场并非零退出，绝不强删。既有 fuxuan_certified_exit*
 * 目录**只读**：测试前后比对其快照，且它们从不进入清理目标。
 */
const { spawnSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const REPO_ROOT = path.resolve(__dirname, '..', '..', '..');
const ENTRY = path.join(REPO_ROOT, 'scripts', 'test', 'certified_motion_exit_cleanup_drive.cjs');
const TMP = path.resolve(os.tmpdir());
const TMP_REAL = fs.realpathSync(TMP);
const FAMILY_PREFIX = 'fuxuan_certified_exit';

const RUN_TOKEN = ('t' + process.pid.toString(36) + Date.now().toString(36)
  + Math.random().toString(36).slice(2, 8)).replace(/[^a-z0-9]/g, '');
if (!/^[a-z0-9]+$/.test(RUN_TOKEN)) {
  throw new Error('generated run token is not usable as a skill id fragment: ' + RUN_TOKEN);
}

// 合法 skillId（入口校验为 /^[a-z0-9_-]+$/i）；token 保证本次运行的目录名唯一。
const SKILL_ID = 'certified_exit_regress_' + RUN_TOKEN;
// 入口的归一化是 replace(/[^a-z0-9]/gi,'_')：**保留大小写**，小写 skillId 原样通过。
const SLUG = SKILL_ID.replace(/[^a-z0-9]/gi, '_');
// 旧实现的固定目录公式（注意：结尾没有下划线）。
const FIXED_DIR = path.join(TMP, `${FAMILY_PREFIX}_${SLUG}`);
// 新实现的前缀：可识别前缀保留，并由 mkdtempSync 追加 6 位随机后缀。
const ROOT_PREFIX = `${FAMILY_PREFIX}_${SLUG}_`;

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

// 既有 fuxuan_certified_exit* 目录的只读清单（名称 + 内容快照）。只统计**不含本次
// token** 的目录，即真正预先存在的那些；本次自建的目录不属于「既有」。
function familyNames() {
  return fs.readdirSync(TMP).filter(name => name.startsWith(FAMILY_PREFIX)).sort();
}

function preExistingNames() {
  return familyNames().filter(name => !name.includes(RUN_TOKEN));
}

function snapshotNames(names) {
  return names.map(name => name + '\n' + snapshot(path.join(TMP, name))).join('\n===\n');
}

// 归属判定：true = 可证明属于本次运行；null = 已不存在；字符串 = 不能证明的原因。
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

function runEntry(exe, skill, curve) {
  return spawnSync(process.execPath, [ENTRY, exe, skill, curve], {
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
// 在 try 内赋值，finally 仍可读（const 会因块级作用域在 finally 中不可见）。
let preExistingBefore = null;

try {
  // ---------------------------------------------------------------- 前置事实
  check('target entry exists at its current path', fs.existsSync(ENTRY), ENTRY);
  check('generated skill id is legal for the entry', /^[a-z0-9_-]+$/i.test(SKILL_ID), SKILL_ID);
  check('skill id carries this run\'s token', SKILL_ID.includes(RUN_TOKEN), SKILL_ID);
  check('slug is derived exactly as the entry derives it', SLUG === SKILL_ID.replace(/[^a-z0-9]/gi, '_'), SLUG);
  check('new root prefix keeps the recognizable family prefix', ROOT_PREFIX.startsWith(FAMILY_PREFIX + '_'), ROOT_PREFIX);
  check('new root prefix is the old fixed name plus a separator',
    ROOT_PREFIX === path.basename(FIXED_DIR) + '_', ROOT_PREFIX);
  check('new root prefix cannot collide with the old fixed name',
    !path.basename(FIXED_DIR).startsWith(ROOT_PREFIX), path.basename(FIXED_DIR));

  // 只读基线：既有 fuxuan_certified_exit* 目录。本机当前为空，如实记录。
  preExistingBefore = preExistingNames();
  const preExistingSnapshotBefore = snapshotNames(preExistingBefore);
  check('pre-existing family directories were inventoried read-only',
    Array.isArray(preExistingBefore), 'count=' + preExistingBefore.length);

  // ------------------------------------------------- 测试脚手架（含本次 token）
  scaffoldDir = fs.mkdtempSync(path.join(TMP, FAMILY_PREFIX + '_scaffold' + RUN_TOKEN + '_'));
  cleanupTargets.push({ target: scaffoldDir, createdAt: scaffoldDir, label: 'scaffold' });
  const curveFile = path.join(scaffoldDir, 'curve.json');
  const curveBody = JSON.stringify({ skillId: SKILL_ID, note: 'regression placeholder, never loaded by a real player' });
  fs.writeFileSync(curveFile, curveBody);

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
  fs.writeFileSync(sentinelPath, 'sentinel for ' + RUN_TOKEN + '\n');
  fs.mkdirSync(path.join(FIXED_DIR, 'logs'));
  fs.writeFileSync(path.join(FIXED_DIR, 'logs', 'player_log.txt'), 'pre-existing sentinel log\n');
  const sentinelStatBefore = fs.statSync(sentinelPath);
  const sentinelMtimeBefore = sentinelStatBefore.mtimeMs;
  const sentinelHashBefore = sha256(sentinelPath);
  const fixedSnapshotBefore = snapshot(FIXED_DIR);
  check('fixed-name sentinel directory created by this test', lstatOrNull(FIXED_DIR).isDirectory());
  check('sentinel file created with known content', sha256(sentinelPath) === sentinelHashBefore);
  check('pre-run fixed-name snapshot is a real snapshot (not null)', fixedSnapshotBefore !== null);
  check('sentinel hash is a real sha256', /^[0-9a-f]{64}$/.test(sentinelHashBefore), sentinelHashBefore);

  // ------------------------------------------- 断言工具自身的灵敏度自检
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

  // 归属闸门的负向对照：只有「Temp 直接子目录」且「不是链接」才放行。
  const notDirectChild = path.join(TMP, FAMILY_PREFIX + '_nested' + RUN_TOKEN, 'inner');
  check('ownership gate rejects a path that is not a direct child of os.tmpdir()',
    typeof verifyOwned(notDirectChild, null) === 'string', String(verifyOwned(notDirectChild, null)));
  const linkPath = path.join(TMP, FAMILY_PREFIX + '_link' + RUN_TOKEN);
  fs.symlinkSync(scaffoldDir, linkPath, 'junction');
  cleanupTargets.push({ target: linkPath, createdAt: linkPath, label: 'junction control', kind: 'control', expect: 'link' });
  check('ownership gate rejects a link/junction even when it is a direct child of os.tmpdir()',
    verifyOwned(linkPath, linkPath) === 'path is a link now, not the directory this test created',
    String(verifyOwned(linkPath, linkPath)));
  const filePath = path.join(TMP, FAMILY_PREFIX + '_file' + RUN_TOKEN);
  fs.writeFileSync(filePath, '');
  cleanupTargets.push({ target: filePath, createdAt: filePath, label: 'file control', kind: 'control', expect: 'file' });
  check('ownership gate rejects a non-directory direct child of os.tmpdir()',
    verifyOwned(filePath, filePath) === 'path is no longer a directory', String(verifyOwned(filePath, filePath)));
  check('ownership gate rejects a path that is not the exact path this test created',
    typeof verifyOwned(FIXED_DIR, path.join(TMP, FAMILY_PREFIX + '_other' + RUN_TOKEN)) === 'string');

  // ------------------------------------------- 同一参数运行新入口两次
  const roots = [];
  for (let run = 0; run < 2; run++) {
    const tag = 'run ' + (run + 1);
    const res = runEntry(fakePlayer, SKILL_ID, curveFile);
    const output = String(res.stdout || '') + String(res.stderr || '');
    const reported = extractPreservedRoot(output);
    check(tag + ': exited non-zero (fake player cannot be launched)', res.status !== 0 && res.status !== null,
      'status=' + res.status);
    check(tag + ': no test-process timeout (the 90s wait was never reached)',
      !res.error && res.signal === null, 'error=' + (res.error && res.error.code) + ' signal=' + res.signal);
    check(tag + ': reported a preserved test root', reported !== null);
    check(tag + ': reported the preserved root exactly once',
      (String(res.stderr || '').match(/^preserved test root: /gm) || []).length === 1);
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

    // 数据只应写入本次新根。
    check(tag + ': .test_mode is inside the new root', fs.existsSync(path.join(abs, '.test_mode')));
    check(tag + ': inbox.txt is inside the new root', fs.existsSync(path.join(abs, 'inbox.txt')));
    const copied = path.join(abs, 'certified_motions', SKILL_ID + '.json');
    check(tag + ': curve copy is inside the new root', fs.existsSync(copied), copied);
    if (fs.existsSync(copied)) {
      check(tag + ': curve copy content equals the source file', sha256(copied) === sha256(curveFile));
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
  check('pre-existing sentinel log survived',
    fs.existsSync(path.join(FIXED_DIR, 'logs', 'player_log.txt')));
  // 旧实现会 rmSync 后重建固定目录并写入 .test_mode / certified_motions / inbox.txt。
  check('entry did NOT write .test_mode into the fixed-name directory',
    !fs.existsSync(path.join(FIXED_DIR, '.test_mode')));
  check('entry did NOT write certified_motions into the fixed-name directory',
    !fs.existsSync(path.join(FIXED_DIR, 'certified_motions')));
  check('entry did NOT write inbox.txt into the fixed-name directory',
    !fs.existsSync(path.join(FIXED_DIR, 'inbox.txt')));

  // ------------------------------------------- 假播放器从未真正执行
  check('fake .cmd player never actually ran (marker absent)', !fs.existsSync(markerPath), markerPath);
  check('fake .cmd player still exists (untouched)', fs.existsSync(fakePlayer));

  // ------------------------------------------- 无效输入不得创建根、不得声称保存
  // 无效用例与两次运行使用**同一个 skillId**，因此若它们错误地创建了根，目录名会带
  // 同一个 ROOT_PREFIX。已知的两条运行根要排除在外，只比较「额外出现」的目录。
  const knownRoots = new Set(roots.map(root => path.basename(root)));
  const unexpectedPrefixDirs = () => familyNames()
    .filter(name => name.startsWith(ROOT_PREFIX) && !knownRoots.has(name));
  const prefixDirsBeforeInvalid = unexpectedPrefixDirs();
  const invalidCases = [
    { label: 'missing exe', exe: path.join(scaffoldDir, 'does-not-exist.exe'), skill: SKILL_ID, curve: curveFile },
    { label: 'missing curve', exe: process.execPath, skill: SKILL_ID, curve: path.join(scaffoldDir, 'no-such-curve.json') }
  ];
  for (const invalidCase of invalidCases) {
    const res = runEntry(invalidCase.exe, invalidCase.skill, invalidCase.curve);
    const err = String(res.stderr || '');
    const out = String(res.stdout || '') + err;
    check('invalid input (' + invalidCase.label + '): exited non-zero',
      res.status !== 0 && res.status !== null, 'status=' + res.status);
    check('invalid input (' + invalidCase.label + '): no test-process timeout',
      !res.error && res.signal === null);
    check('invalid input (' + invalidCase.label + '): did NOT claim a preserved root',
      extractPreservedRoot(out) === null && !err.includes('preserved test root:'));
    check('invalid input (' + invalidCase.label + '): said nothing was preserved',
      err.includes('no test root was created'));
    check('invalid input (' + invalidCase.label + '): created no new root',
      unexpectedPrefixDirs().join(',') === prefixDirsBeforeInvalid.join(','),
      'before=[' + prefixDirsBeforeInvalid.join(',') + '] after=[' + unexpectedPrefixDirs().join(',') + ']');
    check('invalid input (' + invalidCase.label + '): printed no root path on stdout',
      !String(res.stdout || '').split(/\r?\n/).some(line => line.includes(FAMILY_PREFIX)));
  }

  // ------------------------------------------- 既有家族目录：只读前后核对
  const preExistingAfter = preExistingNames();
  const preExistingSnapshotAfter = snapshotNames(preExistingAfter);
  check('pre-existing family directories unchanged (or still absent)',
    preExistingSnapshotAfter === preExistingSnapshotBefore,
    'before=[' + preExistingBefore.join(',') + '] after=[' + preExistingAfter.join(',') + ']');
  check('no pre-existing family directory was deleted',
    preExistingBefore.every(name => preExistingAfter.includes(name)));
  check('no pre-existing family directory is registered for cleanup',
    !cleanupTargets.some(item => !path.basename(path.resolve(item.target)).includes(RUN_TOKEN)));
} catch (error) {
  check('regression harness completed without an unexpected error', false, error.message);
} finally {
  // ------------------------------------------------------ 带归属证明的清理
  for (const item of cleanupTargets.slice().reverse()) {
    const abs = path.resolve(item.target);
    if (item.kind === 'control') {
      // 负向对照**故意**不是目录，因此不能套用「必须是本次创建的真实目录」那道闸门；
      // 改用同等的直接子目录 + 本次 token + 精确路径 + 预期对象类型核对，
      // 并用 unlink 只移除该对象本身（不跟随联接）。
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
    [FAMILY_PREFIX + '_link' + RUN_TOKEN, FAMILY_PREFIX + '_file' + RUN_TOKEN]
      .every(name => lstatOrNull(path.join(TMP, name)) === null));
  check('family directory list returned to its pre-test baseline',
    preExistingBefore !== null && preExistingNames().join(',') === preExistingBefore.join(','),
    'before=[' + String(preExistingBefore) + '] after=[' + preExistingNames().join(',') + ']');
  check('no fuxuan_certified_exit* directory is left behind by this test',
    preExistingBefore !== null && familyNames().length === preExistingBefore.length,
    'leftover=[' + familyNames().join(',') + ']');
}

const failed = results.filter(r => !r.ok);
for (const r of results) {
  if (!r.ok) console.log('  [FAIL] ' + r.name + (r.detail ? ' :: ' + r.detail : ''));
}
if (preserved.length) {
  console.log('  preserved scenes (ownership unproven):');
  for (const line of preserved) console.log('    ' + line);
}
if (failed.length === 0) {
  console.log('certified-motion exit-cleanup root regression OK (' + results.length
    + ' checks, 2 fresh roots, 1 fixed-name sentinel, 2 invalid-input cases)');
  process.exit(0);
}
console.log('certified-motion exit-cleanup root regression FAILED: ' + failed.length + '/' + results.length + ' checks');
process.exit(1);
