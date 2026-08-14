#!/usr/bin/env node
/**
 * 记忆数据备份 — 保留生产记忆至少一份安全副本（2026-08-15）
 *
 * 背景：测试隔离（FU_XUAN_DATA 临时目录）上线前，旧测试链路可能写过 motion_memory /
 * activity_log / validation_log；本脚本在任何测试前后跑一次，确保「本机正在使用的记忆」
 * 始终有一份可回滚的副本。
 *
 * 用法:
 *   node scripts/backup_memory.cjs                # 备份核心记忆到 D:\DesktopPetData\_backup_<日期>
 *   node scripts/backup_memory.cjs --all          # 连同 activity/validation 日志一起备份
 * 产物: D:\DesktopPetData\_backup_YYYYMMDD\（同名日期重复运行 = 刷新覆盖）
 */
'use strict';
const fs = require('fs');
const path = require('path');

const DATA_ROOT = process.env.FU_XUAN_DATA || 'D:/DesktopPetData';
const CORE_FILES = ['pet_memory.json', 'pet_personality.json', 'knowledge_base.json', 'motion_memory.json', 'pet_preferences.json', 'reminders.json'];
const EXTRA_FILES = ['activity_log.json', 'validation_log.json', 'task_trajectories.json'];

const args = process.argv.slice(2);
const withExtra = args.includes('--all');

const stamp = new Date().toISOString().slice(0, 10).replace(/-/g, ''); // yyyymmdd
const BACKUP_DIR = path.join(DATA_ROOT, `_backup_${stamp}`);

if (!fs.existsSync(DATA_ROOT)) { console.error(`[FAIL] 数据目录不存在: ${DATA_ROOT}`); process.exit(1); }
fs.mkdirSync(BACKUP_DIR, { recursive: true });

let ok = 0, skip = 0;
for (const f of [...CORE_FILES, ...(withExtra ? EXTRA_FILES : [])]) {
    const src = path.join(DATA_ROOT, f);
    if (!fs.existsSync(src)) { console.log(`[skip] ${f}（不存在）`); skip++; continue; }
    const dst = path.join(BACKUP_DIR, f);
    fs.copyFileSync(src, dst);
    const kb = Math.round(fs.statSync(dst).size / 1024);
    console.log(`[OK] ${f} -> ${dst} (${kb} KB)`);
    ok++;
}
console.log(`\n[完成] 备份 ${ok} 个文件到 ${BACKUP_DIR}（跳过 ${skip}）`);
console.log('[提示] 生产数据请保留原目录；卸载/清理时不要删除 _backup_* 目录');
