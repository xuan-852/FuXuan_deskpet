'use strict';
/* 隔离运行时驱动：验证认证动作的生产执行链路。
 * 用法: node scripts/test/certified_motion_runtime_drive.cjs <DesktopPet.exe> <skill-id> <candidate.json>
 * 流程: 隔离根安装 .test_mode + certified_motions 数据 → @@sim:certified-motion 触发
 *       → 断言 admitted / pose-restored / released / cleanup 日志与截图帧。
 */
const fs=require('fs'),os=require('os'),path=require('path'),{spawn}=require('child_process');
const exe=process.argv[2], skillId=process.argv[3], candidateFile=process.argv[4];
if(!exe||!fs.existsSync(exe))throw Error('missing exe');
if(!skillId||!/^[a-z0-9_\-]+$/i.test(skillId))throw Error('missing skill id');
if(!candidateFile||!fs.existsSync(candidateFile))throw Error('missing candidate json');
const root=path.join(os.tmpdir(),'fuxuan_certified_motion_'+skillId.toLowerCase().replace(/[^a-z0-9]/g,'_'));
const inbox=path.join(root,'inbox.txt'), sleep=ms=>new Promise(r=>setTimeout(r,ms));
const log=()=>{try{return fs.readFileSync(path.join(root,'logs','player_log.txt'),'utf8')}catch{return ''}};
async function send(x,ms=350){fs.writeFileSync(inbox,x);await sleep(ms);fs.writeFileSync(inbox,'');await sleep(60)}
async function wait(mark){for(let end=Date.now()+90000;Date.now()<end;await sleep(200))if(log().includes(mark))return;throw Error('timeout '+mark)}
async function count(n){let d=path.join(root,'test_screenshots');for(let end=Date.now()+4000;Date.now()<end;await sleep(80))if(fs.existsSync(d)&&fs.readdirSync(d).filter(x=>x.endsWith('.png')).length>=n)return;throw Error('missing frame '+n)}
(async()=>{fs.rmSync(root,{recursive:true,force:true});fs.mkdirSync(path.join(root,'certified_motions'),{recursive:true});
fs.writeFileSync(path.join(root,'.test_mode'),'');
fs.copyFileSync(candidateFile,path.join(root,'certified_motions',skillId+'.json'));
fs.writeFileSync(inbox,'');
let p=spawn(exe,[],{env:{...process.env,FU_XUAN_DATA:root},stdio:'ignore'});
try{await wait('[DesktopPet] 落地');await send('@@sim:idle-actions:off');await send('@@sim:walk:stop');await sleep(1600);
await send('@@sim:status');await wait('velocity=(0,0)');await sleep(500);
await send('@@sim:certified-motion:'+skillId);
for(let i=0;i<6;i++){await send('@@sim:model-measurement-snapshot',450);await count(i+1)}
await sleep(2500);await send('@@sim:model-measurement-snapshot');await count(7);
await send('@@test:quit',1200);
const l=log();
for(const mark of [`[CertifiedMotion] started: ${skillId}`,'[EmbodiedRuntimeAdmission] admitted: '+skillId,'[EmbodiedSafeRecovery] pose-restored','[EmbodiedRuntimeAdmission] released: certified-motion-completed','[CertifiedMotion] cleanup: certified-motion-completed'])
  if(!l.includes(mark))throw Error('log missing: '+mark);
console.log(root)}finally{if(!p.killed)p.kill()}})().catch(e=>{console.error(e.message);process.exitCode=1});
