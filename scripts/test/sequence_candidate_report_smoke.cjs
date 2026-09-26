'use strict';
const fs=require('fs'),os=require('os'),path=require('path'),{spawnSync}=require('child_process');
const root=fs.mkdtempSync(path.join(os.tmpdir(),'sequence-report-test-'));const report=path.join(root,'report.json');const out=path.join(root,'out.json');
fs.writeFileSync(report,JSON.stringify({schema:'live2d-wave-sequence/v1',parameterIds:['Param94','Param99','Param92'],repeats:3,framesPerRepeat:34,peakMeanDifference:2.7,maxResetMeanDifference:0,resetStable:true}));
const r=spawnSync(process.execPath,['scripts/live2d-probe/build_sequence_candidate_report.cjs',out,report],{cwd:path.resolve(__dirname,'../..'),encoding:'utf8'});if(r.status!==0)throw Error(r.stderr);const j=JSON.parse(fs.readFileSync(out,'utf8'));if(j.schema!=='live2d-sequence-candidate-report/v1'||j.candidates[0].productionStatus!=='not-certified')throw Error('unsafe or wrong report');console.log('sequence-candidate-report-smoke: pass');
