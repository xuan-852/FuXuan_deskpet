'use strict';
/* Summarize isolated sequence/combination evidence without certifying actions. */
const fs=require('fs'),path=require('path');
const [outputPath,...reportPaths]=process.argv.slice(2);
if(!outputPath||!reportPaths.length||!reportPaths.every(p=>path.isAbsolute(p))) throw new Error('output plus absolute reports required');
const reports=reportPaths.map(file=>{const value=JSON.parse(fs.readFileSync(file,'utf8'));return {file:path.basename(file),parameterIds:value.parameterIds||['Param94','Param99','Param92'],schema:value.schema||'custom-combination-sweep-report',repeats:value.repeats,framesPerRepeat:value.framesPerRepeat||Math.round((value.frames||[]).length/(value.repeats||1)),peakMeanDifference:value.peakMeanDifference,resetMeanDifference:value.maxResetMeanDifference,resetStable:value.resetStable}});
const candidates=reports.map((r,i)=>({candidateId:`sequence.${i+1}`,parameterIds:r.parameterIds,sourceReport:r.file,evidence:{repeats:r.repeats,framesPerRepeat:r.framesPerRepeat,peakMeanDifference:r.peakMeanDifference,resetMeanDifference:r.resetMeanDifference,resetStable:r.resetStable},status:r.resetStable?'NeedsVisualSequenceReview':'RejectedByReset',productionStatus:'not-certified'}));
const out={schema:'live2d-sequence-candidate-report/v1',safety:{productionStatus:'not-certified',mapWriteAllowed:false},candidates};fs.mkdirSync(path.dirname(outputPath),{recursive:true});fs.writeFileSync(outputPath,JSON.stringify(out,null,2)+'\n');console.log(JSON.stringify({output:outputPath,candidates:candidates.length}));
