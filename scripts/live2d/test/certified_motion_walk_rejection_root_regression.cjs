'use strict';
/* 黑盒回归：certified_motion_walk_rejection_drive.cjs 的原子目录分配、预清理与
 * skillId 文件路径边界。
 *
 * 被修复的三个缺陷
 * ----------------
 * 1) 非原子目录分配。旧实现用 skillId + PID + Date.now() 拼出目录名：
 *      %TEMP%\fuxuan_certified_motion_rejection_<skillId 归一化>_<pid>_<Date.now()>
 *    唯一性靠"pid 与时钟不会重复"来*假定*，而不是由文件系统原子保证；紧跟其后的
 *    fs.mkdirSync(...,{recursive:true}) 对已存在的目录会静默复用。
 * 2) 启动前预清理。旧实现在 spawn 之前无条件执行
 *      fs.rmSync(root, { recursive: true, force: true })
 *    即对推导出的路径做递归删除。
 * 3) skillId 文件路径边界。目录名经过归一化（`/`、`\` 都被替换为 `_`），但曲线副本
 *    文件名用的是**原始 skillId**：path.join(root,'certified_motions',`${skillId}.json`)。
 *    path.join 会折叠 `..`，实测：
 *      skillId=../../evil    -> 副本写到 %TEMP%\evil.json（本次根**之外**，Temp 之内）
 *      skillId=../../../evil -> 副本写到 %TEMP% 的**父目录**\evil.json（Temp 之外）
 *
 * 本测试断言什么
 * --------------
 * 用测试自建假 .cmd 顶替播放器（Node 无法在无 shell 下 spawn .cmd，spawn() 同步
 * 抛错），**绝不启动真实 Player**。以同一组合法参数运行新入口两次，断言：
 *   * 两次运行得到两个**互异**的新根，且都带本次新前缀；
 *   * 测试自建的**旧式命名**目录（含 PID/时间戳形状）与其哨兵逐字节不变；
 *   * .test_mode / inbox.txt / 曲线副本只出现在各自新根内，副本内容等于源文件；
 *   * 根创建后的失败**保留**本次根并打印其路径（可定位）；
 *   * 假播放器**从未真正执行**（它会写 marker；marker 不存在即证明）。
 *
 * 另跑一批**非法 skillId**（含 `/`、`\`、`..`）：必须在任何目录创建与复制**之前**被
 * 拒绝，非零退出、**不创建任何新根**、**不出现**任何 preserved test root 声称。并只读
 * 核对旧公式下这些非法值本会指向的越界路径（%TEMP%\evil.json 与 Temp 父目录\
 * evil.json）在运行前后都**不存在**。
 *
 * 旧式目录名含 PID 与时间戳，因此本测试**不**声称命中了旧版某次运行的确切名称；
 * "启动前 rmSync 已移除"这一点以**入口源码**核对（源码中不得再出现 rmSync，且根分配
 * 不得再用 PID/Date.now）。
 *
 * 清理归属
 * --------
 * 清理前逐个核实：绝对路径与真实路径都是系统 Temp 的**直接子目录**、名称含本次 token、
 * 对象仍是原先创建的真实目录而非链接/联接、且就是登记的精确路径。无法证明归属则保留
 * 现场并非零退出，绝不强删。既有 fuxuan_certified_motion_rejection* 目录**只读**：
 * 测试前后比对其快照，且从不进入清理目标。
 */
const { spawnSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const REPO_ROOT = path.resolve(__dirname, '..', '..', '..');
const ENTRY = path.join(REPO_ROOT, 'scripts', 'test', 'certified_motion_walk_rejection_drive.cjs');
const TMP = path.resolve(os.tmpdir());
const TMP_REAL = fs.realpathSync(TMP);
const FAMILY_PREFIX = 'fuxuan_certified_motion_rejection';

const RUN_TOKEN = ('t' + process.pid.toString(36) + Date.now().toString(36)
  + Math.random().toString(36).slice(2, 8)).replace(/[^a-z0-9]/g, '');
if (!/^[a-z0-9]+$/.test(RUN_TOKEN)) {
  throw new Error('generated run token is not usable as a skill id fragment: ' + RUN_TOKEN);
}

// 合法 skillId：仅 ASCII 字母、数字、下划线（入口校验为 /^[A-Za-z0-9_-]+$/）。
const SKILL_ID = 'certified_walk_rejection_regress_' + RUN_TOKEN;
// 旧实现的目录名公式（PID + 时钟），仅用于**测试自建**哨兵，不声称命中旧版运行。
const OLD_SLUG = SKILL_ID.toLowerCase().replace(/[^a-z0-9]/g, '_');
const OLD_STYLE_DIR = path.join(TMP, `${FAMILY_PREFIX}_${OLD_SLUG}_${process.pid}_${Date.now()}`);
// 新实现的前缀：可识别前缀保留，并由 mkdtempSync 追加 6 位随机后缀。
const ROOT_PREFIX = `${FAMILY_PREFIX}_${SKILL_ID.toLowerCase()}_`;
const NEW_ROOT_RE = new RegExp('^' + ROOT_PREFIX.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '[A-Za-z0-9]{6}$');
// 非法 skillId：含 `/`、`\`、`..` 等，必须在任何目录创建与复制之前被拒绝。
const INVALID_IDS = ['../../evil', '..\\..\\evil', '../../../evil', 'a/b', 'a\\b', '..', 'with space', 'semi;colon', 'star*'];

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

function familyNames() {
  return fs.readdirSync(TMP).filter(name => name.startsWith(FAMILY_PREFIX)).sort();
}

// 只统计**不含本次 token** 的目录，即真正预先存在的那些。
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

function runEntry(exe, skill, candidate) {
  return spawnSync(process.execPath, [ENTRY, exe, skill, candidate], {
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
let oldStyleCreated = false;
let preExistingBefore = null;

try {
  // ---------------------------------------------------------------- 前置事实
  check('target entry exists at its current path', fs.existsSync(ENTRY), ENTRY);
  check('generated skill id is legal for the entry', /^[A-Za-z0-9_-]+$/.test(SKILL_ID), SKILL_ID);
  check('skill id carries this run\'s token', SKILL_ID.includes(RUN_TOKEN), SKILL_ID);
  check('new root prefix keeps the recognizable family prefix', ROOT_PREFIX.startsWith(FAMILY_PREFIX + '_'), ROOT_PREFIX);
  check('new root prefix is the skill id lowercased plus a separator',
    ROOT_PREFIX === `${FAMILY_PREFIX}_${SKILL_ID.toLowerCase()}_`, ROOT_PREFIX);
  check('old-style slug formula collapses to the same lowercase id (formula shape verified)',
    OLD_SLUG === SKILL_ID.toLowerCase(), OLD_SLUG);

  // 只读基线：既有 fuxuan_certified_motion_rejection* 目录（本机有 3 个旧公式产物）。
  preExistingBefore = preExistingNames();
  const preExistingSnapshotBefore = snapshotNames(preExistingBefore);
  check('pre-existing family directories were inventoried read-only',
    Array.isArray(preExistingBefore), 'count=' + preExistingBefore.length);

  // 旧公式下非法 skillId 本会指向的**越界**路径：只读核对，必须始终不存在。
  const escapeProbePaths = [
    path.join(TMP, 'evil.json'),                 // skillId='../../evil'
    path.join(path.dirname(TMP), 'evil.json')    // skillId='../../../evil'（Temp 之外）
  ];
  const escapeBefore = escapeProbePaths.map(p => lstatOrNull(p) === null);
  if (!escapeBefore.every(Boolean)) {
    throw new Error('refusing to run: an escape probe path already exists and cannot be attributed: '
      + escapeProbePaths.filter((p, i) => !escapeBefore[i]).join(', '));
  }
  check('escape probe paths are absent before the run (nothing to mis-attribute)',
    escapeBefore.every(Boolean), escapeProbePaths.join(', '));

  // ------------------------------------------------- 测试脚手架（含本次 token）
  scaffoldDir = fs.mkdtempSync(path.join(TMP, `${FAMILY_PREFIX}_scaffold${RUN_TOKEN}_`));
  cleanupTargets.push({ target: scaffoldDir, createdAt: scaffoldDir, label: 'scaffold' });
  const candidateFile = path.join(scaffoldDir, 'candidate.json');
  fs.writeFileSync(candidateFile, JSON.stringify({ skillId: SKILL_ID, note: 'regression placeholder, never loaded by a real player' }));

  // 假播放器：若被真正执行就会写 marker，从而暴露「其实启动了子进程」。
  const markerPath = path.join(scaffoldDir, 'fake-player-ran.marker');
  const fakePlayer = path.join(scaffoldDir, 'fake-player.cmd');
  fs.writeFileSync(fakePlayer,
    '@echo off\r\n'
    + 'echo ran > "' + markerPath + '"\r\n'
    + 'exit /b 0\r\n');
  check('fake .cmd player was created by this test', fs.existsSync(fakePlayer));
  check('fake player marker does not exist yet', !fs.existsSync(markerPath));

  // ------------------------------- 入口源码核对：rmSync 已移除、不再用 PID/时钟
  const entrySource = fs.readFileSync(ENTRY, 'utf8');
  const entryCode = entrySource
    .replace(/\/\*[\s\S]*?\*\//g, '')   // 去掉头部块注释，避免注释里的示例被当成代码
    .replace(/^\s*\/\/.*$/gm, '');      // 去掉行注释
  check('entry no longer contains any rmSync call (pre-launch pre-clean removed)',
    !/rmSync\s*\(/.test(entryCode));
  check('entry root allocation no longer uses process.pid', !/process\.pid/.test(entryCode));
  check('entry root allocation no longer uses Date.now() for the directory name',
    !/mkdtempSync\([^)]*Date\.now/.test(entryCode));
  check('entry allocates its root with fs.mkdtempSync', /fs\.mkdtempSync\s*\(/.test(entryCode));
  check('entry declares the root as null until creation succeeds', /let\s+root\s*=\s*null\s*;/.test(entryCode));
  check('entry charset gate is present', /\/\^\[A-Za-z0-9_-\]\+\$\/\.test\(skillId\)/.test(entryCode));

  // ---------------------- 测试自建「旧式命名」目录 + 哨兵（PID/时间戳形状）
  if (fs.existsSync(OLD_STYLE_DIR)) {
    throw new Error('refusing to run: the old-style directory this test would build already exists: ' + OLD_STYLE_DIR);
  }
  // 非递归 mkdir：目录若在检查与创建之间出现，会以 EEXIST 失败而不是静默复用。
  fs.mkdirSync(OLD_STYLE_DIR);
  oldStyleCreated = true;
  cleanupTargets.push({ target: OLD_STYLE_DIR, createdAt: OLD_STYLE_DIR, label: 'old-style sentinel' });
  const sentinelPath = path.join(OLD_STYLE_DIR, 'sentinel.txt');
  fs.writeFileSync(sentinelPath, 'old-style sentinel for ' + RUN_TOKEN + '\n');
  fs.mkdirSync(path.join(OLD_STYLE_DIR, 'logs'));
  fs.writeFileSync(path.join(OLD_STYLE_DIR, 'logs', 'player_log.txt'), 'pre-existing old-style sentinel log\n');
  fs.mkdirSync(path.join(OLD_STYLE_DIR, 'certified_motions'));
  const sentinelMtimeBefore = fs.statSync(sentinelPath).mtimeMs;
  const sentinelHashBefore = sha256(sentinelPath);
  const oldStyleSnapshotBefore = snapshot(OLD_STYLE_DIR);
  check('old-style (pid/timestamp shaped) sentinel directory created by this test',
    lstatOrNull(OLD_STYLE_DIR).isDirectory());
  check('old-style sentinel name carries a pid and a timestamp',
    /_\d+_\d{10,}$/.test(path.basename(OLD_STYLE_DIR)), path.basename(OLD_STYLE_DIR));
  check('pre-run old-style snapshot is a real snapshot (not null)', oldStyleSnapshotBefore !== null);
  check('sentinel hash is a real sha256', /^[0-9a-f]{64}$/.test(sentinelHashBefore), sentinelHashBefore);

  // ------------------------------------------- 断言工具自身的灵敏度自检
  const sensitivityDir = path.join(scaffoldDir, 'snapshot-sensitivity');
  fs.mkdirSync(sensitivityDir);
  const sensitivityFile = path.join(sensitivityDir, 'probe.txt');
  fs.writeFileSync(sensitivityFile, 'AAAA');
  const sensitivityBefore = snapshot(sensitivityDir);
  const sensitivityStat = fs.statSync(sensitivityFile);
  fs.writeFileSync(sensitivityFile, 'BBBB');
  fs.utimesSync(sensitivityFile, sensitivityStat.atime, sensitivityStat.mtime);
  const sensitivityAfter = snapshot(sensitivityDir);
  check('snapshot() detects a same-size, mtime-restored rewrite (assertions have teeth)',
    sensitivityBefore !== null && sensitivityAfter !== null && sensitivityBefore !== sensitivityAfter);

  // 归属闸门的负向对照。
  const notDirectChild = path.join(TMP, `${FAMILY_PREFIX}_nested${RUN_TOKEN}`, 'inner');
  check('ownership gate rejects a path that is not a direct child of os.tmpdir()',
    typeof verifyOwned(notDirectChild, null) === 'string', String(verifyOwned(notDirectChild, null)));
  const linkPath = path.join(TMP, `${FAMILY_PREFIX}_link${RUN_TOKEN}`);
  fs.symlinkSync(scaffoldDir, linkPath, 'junction');
  cleanupTargets.push({ target: linkPath, createdAt: linkPath, label: 'junction control', kind: 'control', expect: 'link' });
  check('ownership gate rejects a link/junction even when it is a direct child of os.tmpdir()',
    verifyOwned(linkPath, linkPath) === 'path is a link now, not the directory this test created',
    String(verifyOwned(linkPath, linkPath)));
  const filePath = path.join(TMP, `${FAMILY_PREFIX}_file${RUN_TOKEN}`);
  fs.writeFileSync(filePath, '');
  cleanupTargets.push({ target: filePath, createdAt: filePath, label: 'file control', kind: 'control', expect: 'file' });
  check('ownership gate rejects a non-directory direct child of os.tmpdir()',
    verifyOwned(filePath, filePath) === 'path is no longer a directory', String(verifyOwned(filePath, filePath)));

  // ------------------------------------------- 同一参数运行新入口两次
  const roots = [];
  for (let run = 0; run < 2; run++) {
    const tag = 'run ' + (run + 1);
    const res = runEntry(fakePlayer, SKILL_ID, candidateFile);
    const output = String(res.stdout || '') + String(res.stderr || '');
    const reported = extractPreservedRoot(output);
    check(tag + ': exited non-zero (fake player cannot be launched)', res.status !== 0 && res.status !== null,
      'status=' + res.status);
    check(tag + ': no test-process timeout (the 90s wait was never reached)',
      !res.error && res.signal === null, 'error=' + (res.error && res.error.code) + ' signal=' + res.signal);
    check(tag + ': reported a preserved test root (failure path stays locatable)', reported !== null);
    check(tag + ': reported the preserved root exactly once',
      (String(res.stderr || '').match(/^preserved test root: /gm) || []).length === 1);
    check(tag + ': did NOT claim nothing was preserved', !output.includes('no test root was created'));
    check(tag + ': did NOT print the success root line (it never reached success)',
      !/^fuxuan_certified_motion_rejection_/m.test(String(res.stdout || '').trim().split('\n').pop() || ''));
    if (reported === null) continue;

    const abs = path.resolve(reported);
    roots.push(abs);
    located.push(abs);
    cleanupTargets.push({ target: abs, createdAt: abs, label: tag + ' root' });
    check(tag + ': root is a direct child of os.tmpdir()', path.dirname(abs) === TMP, abs);
    check(tag + ': root name carries the new unique prefix',
      path.basename(abs).startsWith(ROOT_PREFIX), path.basename(abs));
    check(tag + ': root name is <prefix> + a fresh 6-char random suffix',
      NEW_ROOT_RE.test(path.basename(abs)), path.basename(abs));
    check(tag + ': root is NOT the test\'s old-style sentinel', abs !== OLD_STYLE_DIR);
    check(tag + ': root is a real directory, not a link',
      (lstatOrNull(abs) || {}).isDirectory?.() === true && lstatOrNull(abs).isSymbolicLink() === false);
    check(tag + ': failure kept the evidence root (preserved, not deleted)', fs.existsSync(abs));

    // 数据只应写入本次新根。
    check(tag + ': .test_mode is inside the new root', fs.existsSync(path.join(abs, '.test_mode')));
    check(tag + ': inbox.txt is inside the new root', fs.existsSync(path.join(abs, 'inbox.txt')));
    const copied = path.join(abs, 'certified_motions', SKILL_ID + '.json');
    check(tag + ': curve copy is inside the new root', fs.existsSync(copied), copied);
    if (fs.existsSync(copied)) {
      check(tag + ': curve copy content equals the source file', sha256(copied) === sha256(candidateFile));
    }
  }

  // ------------------------------------------- 两次运行互异、且不复用旧式目录
  check('both runs reported a root', roots.length === 2, JSON.stringify(roots));
  if (roots.length === 2) {
    check('the two runs used different roots (atomic allocation, no reuse)', roots[0] !== roots[1], JSON.stringify(roots));
    check('run 1\'s root still exists after run 2 (not reused, not deleted)', fs.existsSync(roots[0]), roots[0]);
    check('run 2\'s root still exists (nothing deletes it)', fs.existsSync(roots[1]), roots[1]);
    check('neither run root is the old-style sentinel',
      roots[0] !== OLD_STYLE_DIR && roots[1] !== OLD_STYLE_DIR);
  }

  // ------------------------------------------- 旧式目录与哨兵必须原样
  check('old-style sentinel directory still exists', lstatOrNull(OLD_STYLE_DIR) !== null, OLD_STYLE_DIR);
  const oldStyleSnapshotAfter = snapshot(OLD_STYLE_DIR);
  check('old-style sentinel directory is byte-identical (structure/size/mtime/sha256)',
    oldStyleSnapshotAfter === oldStyleSnapshotBefore,
    oldStyleSnapshotAfter === oldStyleSnapshotBefore ? '' : 'snapshot changed');
  const sentinelPathAfter = path.join(OLD_STYLE_DIR, 'sentinel.txt');
  check('old-style sentinel content is unchanged', fs.existsSync(sentinelPathAfter)
    && sha256(sentinelPathAfter) === sentinelHashBefore);
  check('old-style sentinel mtime is unchanged', fs.existsSync(sentinelPathAfter)
    && fs.statSync(sentinelPathAfter).mtimeMs === sentinelMtimeBefore);
  check('pre-existing old-style sentinel log survived',
    fs.existsSync(path.join(OLD_STYLE_DIR, 'logs', 'player_log.txt')));
  check('entry did NOT write .test_mode into the old-style directory',
    !fs.existsSync(path.join(OLD_STYLE_DIR, '.test_mode')));
  check('entry did NOT write a curve into the old-style directory',
    fs.readdirSync(path.join(OLD_STYLE_DIR, 'certified_motions')).length === 0);
  check('entry did NOT write inbox.txt into the old-style directory',
    !fs.existsSync(path.join(OLD_STYLE_DIR, 'inbox.txt')));

  // ------------------------------------------- 假播放器从未真正执行
  check('fake .cmd player never actually ran (marker absent)', !fs.existsSync(markerPath), markerPath);
  check('fake .cmd player still exists (untouched)', fs.existsSync(fakePlayer));

  // ------------------------------------------- 非法 skillId 必须在建根与复制前被拒
  const knownRoots = new Set(roots.map(root => path.basename(root)));
  const unexpectedPrefixDirs = () => familyNames()
    .filter(name => NEW_ROOT_RE.test(name) && !knownRoots.has(name) && name !== path.basename(OLD_STYLE_DIR));
  const prefixDirsBeforeInvalid = unexpectedPrefixDirs();
  for (const invalidId of INVALID_IDS) {
    const res = runEntry(fakePlayer, invalidId, candidateFile);
    const err = String(res.stderr || '');
    const out = String(res.stdout || '') + err;
    const label = 'invalid skill id ' + JSON.stringify(invalidId);
    check(label + ': exited non-zero', res.status !== 0 && res.status !== null, 'status=' + res.status);
    check(label + ': no test-process timeout', !res.error && res.signal === null);
    check(label + ': rejected by the charset gate',
      err.includes('invalid skill id'), err.split('\n')[0]);
    check(label + ': did NOT claim a preserved root',
      extractPreservedRoot(out) === null && !err.includes('preserved test root:'));
    // 字符集闸门是模块级 throw、位于异步 IIFE 之前，因此失败处理器根本不会运行：
    // 此时既没有根，也没有任何"已保存根"的声称。要求出现处理器文案反而是断言实现细节。
    check(label + ': rejected before any root existed (no root claim of any kind)',
      !out.includes('preserved test root'));
    check(label + ': created no new root',
      unexpectedPrefixDirs().join(',') === prefixDirsBeforeInvalid.join(','),
      'after=[' + unexpectedPrefixDirs().join(',') + ']');
  }
  // 旧公式下这些非法值本会指向的越界路径：必须始终不存在。
  const escapeAfter = escapeProbePaths.map(p => lstatOrNull(p) === null);
  check('no escape path was created by the invalid-id cases',
    escapeAfter.every(Boolean), escapeProbePaths.filter((p, i) => !escapeAfter[i]).join(', '));
  check('invalid ids did not disturb the old-style sentinel',
    snapshot(OLD_STYLE_DIR) === oldStyleSnapshotBefore);

  // ------------------------------------------- 既有家族目录：只读前后核对
  const preExistingAfter = preExistingNames();
  const preExistingSnapshotAfter = snapshotNames(preExistingAfter);
  check('pre-existing family directories unchanged (structure/size/mtime/sha256)',
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
  check('old-style sentinel directory was cleaned up',
    oldStyleCreated && lstatOrNull(OLD_STYLE_DIR) === null, OLD_STYLE_DIR);
  check('scaffold directory was cleaned up',
    scaffoldDir !== null && lstatOrNull(scaffoldDir) === null, String(scaffoldDir));
  check('negative-control objects (junction + file) were cleaned up',
    [`${FAMILY_PREFIX}_link${RUN_TOKEN}`, `${FAMILY_PREFIX}_file${RUN_TOKEN}`]
      .every(name => lstatOrNull(path.join(TMP, name)) === null));
  check('family directory list returned to its pre-test baseline',
    preExistingBefore !== null && preExistingNames().join(',') === preExistingBefore.join(','),
    'before=[' + String(preExistingBefore) + '] after=[' + preExistingNames().join(',') + ']');
  check('no fuxuan_certified_motion_rejection* directory is left behind by this test',
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
  console.log('certified-motion walk-rejection root regression OK (' + results.length
    + ' checks, 2 fresh roots, 1 old-style sentinel, ' + INVALID_IDS.length + ' invalid-id cases, '
    + (preExistingBefore ? preExistingBefore.length : 0) + ' pre-existing rejection dirs)');
  process.exit(0);
}
console.log('certified-motion walk-rejection root regression FAILED: ' + failed.length + '/' + results.length + ' checks');
process.exit(1);
