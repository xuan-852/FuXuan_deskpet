'use strict';
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const { spawnSync } = require('child_process');
const { readManifest, validateManifest } = require('../lib/manifest.cjs');
const { saveState, loadState } = require('../lib/run-state.cjs');
const { buildCapabilityCatalog } = require('../lib/catalog.cjs');
const { runExperiment, buildExperimentReport } = require('../lib/experiment.cjs');
const { validateRegions, analyzeRoiFrames, analyzeRoiPhases } = require('../lib/roi.cjs');

const args = process.argv.slice(2);
const command = args[0] || 'help';
const hasFlag = name => args.includes(name);
function option(name) {
  const index = args.indexOf(name);
  return index < 0 || index + 1 >= args.length || args[index + 1].startsWith('--') ? null : args[index + 1];
}
function runConfiguration(manifest) { return JSON.stringify({writerMode: manifest.run?.writerMode || 'frozen', probeArgs: manifest.probe?.args || []}); }
function runProbe(resolved, manifest) {
  const env = {
    ...process.env,
    FU_XUAN_DATA: resolved.outputRoot,
    FU_XUAN_PROBE_SCOPE: 'all',
    FU_XUAN_PROBE_WRITER_MODE: manifest.run?.writerMode || 'frozen',
  };
  const timeout = Number(process.env.LIVE2D_TIMEOUT_MS || manifest.run?.timeoutMs || 900000);
  const result = spawnSync(resolved.probeExecutable, manifest.probe?.args || [], { env, encoding: 'utf8', timeout });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`Probe failed with exit ${result.status}${result.stderr ? `: ${result.stderr.trim()}` : ''}`);
  const reportPath = path.join(resolved.outputRoot, 'capability-report-probe-window.json');
  if (!fs.existsSync(reportPath)) throw new Error(`Probe report missing: ${reportPath}`);
  return JSON.parse(fs.readFileSync(reportPath, 'utf8'));
}
function run() {
  if (!['validate', 'scan', 'experiment', 'status'].includes(command)) throw new Error('usage: live2d-tools.cjs validate|scan|status --manifest <absolute-path> [--output-root <absolute-path>] [--resume]');
  const manifestFile = option('--manifest');
  const manifest = readManifest(manifestFile);
  if (manifest.roi) validateRegions(manifest.roi);
  const outputRoot = option('--output-root') || process.env.LIVE2D_OUTPUT_ROOT || manifest.run?.outputRoot;
  if (command === 'status') {
    const state = loadState(outputRoot);
    if (!state) throw new Error(`state not found: ${outputRoot}`);
    console.log(JSON.stringify(state, null, 2));
    return;
  }
  const existingState = outputRoot ? loadState(outputRoot) : null;
  const resume = (command === 'scan' || command === 'experiment') && hasFlag('--resume') && (existingState?.status === 'completed' || existingState?.status === 'scanned');
  const configuration = runConfiguration(manifest); const resolved = validateManifest(manifest, {
    outputRoot,
    allowExistingMarked: existingState?.status === 'validated' || resume,
  });
  const statePath = path.join(resolved.outputRoot, 'state.json');
  const setFailure = error => {
    const previous = loadState(resolved.outputRoot);
    saveState(resolved.outputRoot, {
      status: 'failed', manifestFile: path.resolve(manifestFile),
      modelSha256: previous?.status === 'scanned' ? previous.modelSha256 : resolved.modelSha256,
      configuration,
      expectedParameterCount: resolved.expectedParameterCount,
      nextCursor: previous?.nextCursor || 0, reportSha256: previous?.reportSha256 || null, error: error.message,
    });
  };
  if (command === 'experiment') {
    try {
      const previous = loadState(resolved.outputRoot);
      if (previous?.status === 'completed' && !resume) throw new Error('output already completed; pass --resume to reuse verified experiment');
      if (resume) {
        if (previous.modelSha256 !== resolved.modelSha256 || previous.configuration !== configuration || previous.experimentConfiguration !== JSON.stringify(manifest.experiment)) throw new Error('resume refused: experiment drift');
        const reportFiles = previous.reportFiles || [previous.reportFile];
        if (!reportFiles.every(file => fs.existsSync(path.join(resolved.outputRoot, file)))) throw new Error('experiment report missing for resume');
        const reportSha256 = crypto.createHash('sha256').update(reportFiles.map(file => crypto.createHash('sha256').update(fs.readFileSync(path.join(resolved.outputRoot, file))).digest('hex')).join('|')).digest('hex');
        if (reportSha256 !== previous.reportSha256) throw new Error('resume refused: experiment report hash drift');
        console.log(JSON.stringify({status:'completed',outputRoot:resolved.outputRoot,resumed:true},null,2)); return;
      }
      const result = runExperiment(resolved, manifest);
      const reportFrames = result.report.frames || result.report.repeatsData?.flatMap(repeat => (repeat.frames || []).map(frame => frame.image)) || [];
      let roiEvidence = null;
      if (manifest.roi) {
        try { roiEvidence = result.members ? result.members.map(member => ({runId:member.removedParameterId || 'full',parameterIds:member.parameterIds,evidence:analyzeRoiFrames((member.frames || []).map(file => ({path:file,runId:member.removedParameterId || 'full'})), manifest.roi)})) : analyzeRoiFrames(reportFrames.map(file => ({path:file,runId:result.reportName})), manifest.roi); }
        catch (error) { roiEvidence = {status:'failed',error:error.message,productionStatus:'not-certified'}; }
      }
      const phaseGroups = result.report.repeatsData?.[0]?.frames ? Object.values(result.report.repeatsData[0].frames.reduce((groups, frame) => { if(!frame || typeof frame.stage!=='string'||typeof frame.image!=='string') throw Error('invalid sequence frame entry'); (groups[frame.stage] ||= []).push(frame.image); return groups; }, {})).map((files, index) => ({stage: Object.keys(result.report.repeatsData[0].frames.reduce((groups, frame) => { groups[frame.stage] ||= true; return groups; }, {}))[index], files})) : null;
      let phaseRoiEvidence = null;
      if (manifest.roi && phaseGroups) { try { phaseRoiEvidence = analyzeRoiPhases(phaseGroups, manifest.roi); } catch (error) { phaseRoiEvidence = {status:'failed',error:error.message,productionStatus:'not-certified'}; } }
      const unified = buildExperimentReport(result, resolved.modelSha256, roiEvidence, phaseRoiEvidence);
      fs.writeFileSync(path.join(resolved.outputRoot, 'live2d-experiment-report.json'), JSON.stringify(unified, null, 2) + '\n');
      saveState(resolved.outputRoot, {status:'completed',experiment:result.experiment,experimentConfiguration:JSON.stringify(manifest.experiment),reportFile:result.reportName,reportFiles:unified.source.reportFiles,reportSha256:unified.source.reportSha256,modelSha256:resolved.modelSha256,configuration,productionStatus:'not-certified',nextCursor:result.experiment.parameterIds.length,error:null});
      console.log(JSON.stringify({status:'completed',outputRoot:resolved.outputRoot,experiment:result.experiment.type,productionStatus:'not-certified'},null,2)); return;
    } catch (error) { setFailure(error); throw error; }
  }
  if (command === 'validate') {
    if (fs.existsSync(resolved.outputRoot) && fs.readdirSync(resolved.outputRoot).some(name => name !== '.test_mode') && existingState?.status !== 'validated') throw new Error('validate refuses existing completed or unknown outputRoot');
    const state = saveState(resolved.outputRoot, {
      status: 'validated', manifestFile: path.resolve(manifestFile), modelSha256: resolved.modelSha256,
      configuration,
      expectedParameterCount: resolved.expectedParameterCount, nextCursor: 0, error: null,
    });
    console.log(JSON.stringify({ status: state.status, outputRoot: resolved.outputRoot, modelSha256: resolved.modelSha256 }, null, 2));
    return;
  }
  try {
    const previous = loadState(resolved.outputRoot);
    if (previous?.status === 'scanned' && !resume && command === 'scan') throw new Error('output already scanned; pass --resume to reuse verified report');
    if (previous?.status === 'scanned' && hasFlag('--resume') && previous.configuration !== configuration) throw new Error('resume refused: configuration drift');
    if (command === 'experiment' && previous?.status === 'completed' && hasFlag('--resume') && previous.experimentConfiguration !== JSON.stringify(manifest.experiment)) throw new Error('resume refused: experiment drift');
    if (resume) {
      if (previous.modelSha256 !== resolved.modelSha256) throw new Error('resume refused: model3 JSON hash drift');
      if (previous.configuration !== configuration) throw new Error('resume refused: configuration drift');
      const reportPath = path.join(resolved.outputRoot, 'capability-report-probe-window.json');
      if (!fs.existsSync(reportPath)) throw new Error('resume state exists but cached probe report is missing');
      const reportSha256 = crypto.createHash('sha256').update(fs.readFileSync(reportPath)).digest('hex');
      if (!previous.reportSha256 || reportSha256 !== previous.reportSha256) throw new Error('resume refused: cached probe report hash drift');
      const catalog = buildCapabilityCatalog(reportPath, path.join(resolved.outputRoot, 'capability-catalog.json'), resolved.expectedParameterCount, resolved.modelSha256);
      saveState(resolved.outputRoot, { ...previous, catalogSchema: catalog.schema, error: null });
      console.log(JSON.stringify({ status: 'scanned', outputRoot: resolved.outputRoot, parameterCount: previous.parameterCount, resumed: true }, null, 2));
      return;
    }
    const report = runProbe(resolved, manifest);
    if (!Array.isArray(report.parameters) || report.parameters.length !== resolved.expectedParameterCount) throw new Error(`parameter count drift: expected ${resolved.expectedParameterCount}, got ${report.parameters?.length || 0}`);
    const reportPath = path.join(resolved.outputRoot, 'capability-report-probe-window.json');
    const catalog = buildCapabilityCatalog(reportPath, path.join(resolved.outputRoot, 'capability-catalog.json'), resolved.expectedParameterCount, resolved.modelSha256);
    saveState(resolved.outputRoot, {
      status: 'scanned', manifestFile: path.resolve(manifestFile), modelSha256: resolved.modelSha256,
      configuration,
      expectedParameterCount: resolved.expectedParameterCount, parameterCount: report.parameters.length,
      nextCursor: report.parameters.length, error: null, catalogSchema: catalog.schema,
      reportSha256: catalog.source.reportSha256,
    });
    console.log(JSON.stringify({ status: 'scanned', outputRoot: resolved.outputRoot, parameterCount: report.parameters.length }, null, 2));
  } catch (error) {
    setFailure(error);
    throw error;
  }
}
try { run(); } catch (error) { console.error(`live2d-tools: ${error.message}`); process.exitCode = 1; }
