'use strict';
/* Build a unified capability-catalog/v1 from one isolated Probe report.
 * Usage: node build_capability_catalog.cjs <isolated-root> [output-path]
 * Evidence only: never writes mappings, runtime skills, or LLM allowlists.
 */
const fs=require('fs'),path=require('path');
const [root,requestedOutput,expectedRaw]=process.argv.slice(2);
if(!root||!path.isAbsolute(root)||!fs.existsSync(path.join(root,'.test_mode'))) throw new Error('isolated .test_mode root required');
const reportPath=path.join(root,'capability-report-probe-window.json');
if(!fs.existsSync(reportPath)) throw new Error('missing capability-report-probe-window.json');
const report=JSON.parse(fs.readFileSync(reportPath,'utf8'));
const aggregatePath=path.join(root,'cloud_review','aggregate-summary.json');
const aggregate=fs.existsSync(aggregatePath)?JSON.parse(fs.readFileSync(aggregatePath,'utf8')):null;
const reviews=new Map((aggregate?.entries||[]).filter(x=>x.provider==='deepseek'&&x.status==='ok'&&x.parseStatus==='parsed').map(x=>[x.parameterId,x]));
if(!Array.isArray(report.parameters)) throw new Error('probe report parameters missing');
const expected=expectedRaw===undefined?244:Number(expectedRaw);
if(!Number.isInteger(expected)||expected<=0) throw new Error('expected parameter count must be positive');
if(report.parameters.length!==expected) throw new Error(`runtime parameter count drift: expected ${expected}, got ${report.parameters.length}`);
const seen=new Set();
function finite(v){return typeof v==='number'&&Number.isFinite(v)}
function classify(p){
  if(!p.resetStable||!finite(p.maxResetMeanDifference)) return 'Unknown';
  const peak=Math.max(Number(p.minMeanDifference)||0,Number(p.midMeanDifference)||0,Number(p.maxMeanDifference)||0);
  if(peak<0.25) return 'Mechanical';
  return 'VisibleCandidate';
}
const parameters=report.parameters.map((p,index)=>{
  if(!p.parameterId||seen.has(p.parameterId)) throw new Error(`duplicate or missing parameterId at ${index}`);
  seen.add(p.parameterId);
  const status=classify(p);
  const review=reviews.get(p.parameterId);
  return {
    capabilityId:`parameter.${p.parameterId}`,
    parameterId:p.parameterId,
    range:{minimum:p.minimum,maximum:p.maximum,baseline:p.baseline},
    status,
    physicsDependency:report.writerMode==='physics'?'observed-physics-mode':'unresolved',
    localEvidence:{
      writerMode:report.writerMode||'unknown', repeats:report.repeats||null,
      resetStable:Boolean(p.resetStable), maxResetMeanDifference:p.maxResetMeanDifference,
      peakMeanPixelDifference:Math.max(Number(p.minMeanDifference)||0,Number(p.midMeanDifference)||0,Number(p.maxMeanDifference)||0),
      frameCount:Array.isArray(p.frames)?p.frames.length:0,
      reportFile:'capability-report-probe-window.json'
    },
    semanticStatus:review?.verdict?.semantic_guess&&review.verdict.semantic_guess!=='unknown'?'candidate':'unassigned',
    visualReview:review?{provider:'deepseek',model:review.model,visibleChange:Boolean(review.verdict.visible_change),resetMatchesBaseline:Boolean(review.verdict.reset_matches_baseline),semanticGuess:review.verdict.semantic_guess||'unknown',semanticConfidence:review.verdict.semantic_confidence||'unspecified',summary:review.verdict.change_summary||'',evidenceFile:review.evidenceFile}:null,
    naturalnessStatus:'not-evaluated',
    productionStatus:'not-certified',
    evidence:{mechanical:true,visual:Boolean(review?.verdict?.visible_change),semantic:Boolean(review?.verdict?.semantic_guess&&review.verdict.semantic_guess!=='unknown'),naturalness:false}
  };
});
const catalog={schema:'capability-catalog/v1',generatedAtUtc:new Date().toISOString(),source:{reportFile:'capability-report-probe-window.json',reviewFile:aggregate?'cloud_review/aggregate-summary.json':null,writerMode:report.writerMode||'unknown',repeats:report.repeats||null,parameterCount:parameters.length,reviewedCount:reviews.size},safety:{productionStatus:'not-certified',mapWriteAllowed:false,llmExposureAllowed:false},counts:parameters.reduce((a,p)=>(a[p.status]=(a[p.status]||0)+1,a),{}),parameters};
const output=requestedOutput?path.resolve(requestedOutput):path.join(root,'capability-catalog.json');
fs.writeFileSync(output,JSON.stringify(catalog,null,2)+'\n','utf8');
console.log(JSON.stringify({output,parameterCount:parameters.length,counts:catalog.counts}));
