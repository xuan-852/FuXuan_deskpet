'use strict';
const fs=require('fs'),path=require('path');
function saveState(root,state){const output={schema:'live2d-tool-run-state/v1',updatedAtUtc:new Date().toISOString(),...state};fs.mkdirSync(root,{recursive:true});fs.writeFileSync(path.join(root,'state.json'),JSON.stringify(output,null,2)+'\n','utf8');return output}
function loadState(root){const file=path.join(root,'state.json');return fs.existsSync(file)?JSON.parse(fs.readFileSync(file,'utf8')):null}
module.exports={saveState,loadState};
