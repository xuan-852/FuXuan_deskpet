'use strict';
const fs=require('fs'), os=require('os'), path=require('path'), {spawnSync}=require('child_process');
const root=fs.mkdtempSync(path.join(os.tmpdir(),'capability-catalog-test-'));
fs.writeFileSync(path.join(root,'.test_mode'),'');
fs.writeFileSync(path.join(root,'capability-report-probe-window.json'),JSON.stringify({
  repeats:3, writerMode:'frozen', model:{parameterCount:2, modelHash:'model-test'},
  parameters:[
    {parameterId:'ParamAngleX',baseline:0,minimum:-30,maximum:30,resetStable:true,maxResetMeanDifference:0,minMeanDifference:1.2,midMeanDifference:2.1,maxMeanDifference:3.4,frames:['a.png']},
    {parameterId:'Param911',baseline:0,minimum:0,maximum:1,resetStable:false,maxResetMeanDifference:0.4,minMeanDifference:0,midMeanDifference:0,maxMeanDifference:0,frames:[]}
  ]
},null,2));
const out=path.join(root,'catalog.json');
const r=spawnSync(process.execPath,['scripts/live2d-probe/build_capability_catalog.cjs',root,out,'2'],{cwd:path.resolve(__dirname,'../..'),encoding:'utf8'});
if(r.status!==0) throw new Error('catalog builder failed: '+r.stderr);
const catalog=JSON.parse(fs.readFileSync(out,'utf8'));
if(catalog.schema!=='capability-catalog/v1') throw new Error('wrong schema');
if(catalog.parameters.length!==2) throw new Error('wrong parameter count');
if(catalog.parameters[0].status!=='VisibleCandidate') throw new Error('visible classification missing');
if(catalog.parameters[1].status!=='Unknown') throw new Error('unstable parameter not downgraded');
if(catalog.parameters.some(x=>x.productionStatus!=='not-certified')) throw new Error('unsafe production status');
console.log('capability-catalog-smoke: pass');
