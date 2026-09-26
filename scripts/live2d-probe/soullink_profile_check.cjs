'use strict';

const { readJson, writeJson, auditProfile } = require('./lib/soullink_offline.cjs');

const [profilePath, catalogPath, auditPath] = process.argv.slice(2);
if (![profilePath, catalogPath, auditPath].every(Boolean)) throw new Error('Usage: soullink_profile_check.cjs <absolute-profile.json> <absolute-catalog.json> <absolute-audit.json>');
const profile = readJson(profilePath);
const catalog = readJson(catalogPath);
const report = auditProfile(profile.value, catalog.value, profile.bytes, catalog.bytes);
writeJson(auditPath, report);
console.log(JSON.stringify({ output: auditPath, status: report.status, parameterCoverage: report.parameterCoverage.length, missingParameters: report.missingParameters.length }, null, 2));
if (report.status !== 'audited') process.exitCode = 2;
