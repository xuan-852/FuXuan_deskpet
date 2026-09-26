'use strict';
const path=require('path'),{spawnSync}=require('child_process');
const manifest=process.argv[2];if(!manifest||!path.isAbsolute(manifest))throw Error('absolute manifest required');
const r=spawnSync(process.execPath,[path.resolve(__dirname,'../cli/live2d-tools.cjs'),'scan','--manifest',manifest],{stdio:'inherit'});process.exitCode=r.status||0;
