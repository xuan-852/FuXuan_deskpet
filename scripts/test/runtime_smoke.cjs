#!/usr/bin/env node
'use strict';

/*
 * COMPATIBILITY WRAPPER — not a second implementation.
 *
 * Real entry: scripts/test/smoke/runtime_smoke.cjs
 *
 * This file exists only so the pre-migration path recorded in historical docs
 * (dated acceptance reports, project evaluations, task inventory, module logs)
 * keeps working. Those records document what was run at the time and are not
 * rewritten; this wrapper keeps their commands executable.
 *
 * It duplicates no smoke logic: it forwards argv unchanged, inherits stdio, and
 * returns the real entry's exit code unchanged.
 *
 * Do not add logic here. Change the real entry instead.
 */
const path = require('path');
const { spawnSync } = require('child_process');

const TARGET = path.join(__dirname, 'smoke', 'runtime_smoke.cjs');
const result = spawnSync(process.execPath, [TARGET, ...process.argv.slice(2)], { stdio: 'inherit' });
if (result.error) {
    console.error(`compatibility wrapper could not start ${TARGET}: ${result.error.message}`);
    process.exit(1);
}
process.exit(result.status === null ? 1 : result.status);