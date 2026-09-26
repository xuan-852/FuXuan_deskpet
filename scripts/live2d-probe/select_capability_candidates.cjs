'use strict';
/* Select high-value semantic candidates from a catalog and static map.
 * Usage: node select_capability_candidates.cjs <catalog.json> <map.json> <output.json>
 * Selection is evidence-only: no mapping writes, certification, or runtime assets.
 */
const fs=require('fs'),path=require('path');
const [catalogPath,mapPath,outputPath]=process.argv.slice(2);
if(![catalogPath,mapPath,outputPath].every(p=>p&&path.isAbsolute(p))) throw new Error('absolute catalog, map, and output paths required');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const catalog=read(catalogPath), map=read(mapPath);
if(catalog.schema!=='capability-catalog/v1') throw new Error('unsupported catalog schema');
const entries=new Map((map.entries||[]).map(x=>[x.p,x]));
const semantic=/头|身体|上半身|手臂|手部|手指|眼|眼球|眉|嘴|下颌|呼吸|发饰|头发|裙摆|姿态|倾斜|转动|抬起|伸出|开合|朝向/i;
const excluded=/装饰|饰品|绳结|飘带|披肩|剑|特效|蒙版|透明|白圈|紫环|缩放|镜头|leg_|腿|物理/i;
const candidates=catalog.parameters.filter(p=>{
  const guess=p.visualReview?.semanticGuess||'';
  const staticEntry=entries.get(p.parameterId);
  const text=[guess,staticEntry?.s,staticEntry?.d].join(' ');
  return p.productionStatus==='not-certified' && p.localEvidence?.resetStable===true && p.semanticStatus==='candidate' && semantic.test(text) && !excluded.test(text);
}).map(p=>({
  candidateId:`candidate.${p.parameterId}`,
  parameterId:p.parameterId,
  staticSemantic:entries.get(p.parameterId)?.s||null,
  description:entries.get(p.parameterId)?.d||null,
  visualGuess:p.visualReview?.semanticGuess||'unknown',
  confidence:p.visualReview?.semanticConfidence||'unspecified',
  peakMeanPixelDifference:p.localEvidence.peakMeanPixelDifference,
  evidence:{catalog:'capability-catalog/v1',visualReview:p.visualReview?.evidenceFile||null,resetStable:p.localEvidence.resetStable},
  status:'NeedsCombinationEvidence',
  productionStatus:'not-certified'
}));
const out={schema:'capability-candidate-shortlist/v1',source:{catalog:path.basename(catalogPath),map:path.basename(mapPath)},safety:{mapWriteAllowed:false,productionStatus:'not-certified'},counts:{candidates:candidates.length},candidates};
fs.mkdirSync(path.dirname(outputPath),{recursive:true});fs.writeFileSync(outputPath,JSON.stringify(out,null,2)+'\n','utf8');
console.log(JSON.stringify({output:outputPath,candidates:candidates.length}));
