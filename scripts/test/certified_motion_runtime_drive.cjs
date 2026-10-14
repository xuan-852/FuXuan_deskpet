'use strict';
/* 隔离运行时驱动：验证认证动作的生产执行链路。
 * 用法: node scripts/test/certified_motion_runtime_drive.cjs <DesktopPet.exe> <skill-id> <candidate.json>
 * 流程: 隔离根安装 .test_mode + certified_motions 数据 → @@sim:certified-motion 触发
 *       → 断言 admitted / pose-restored / released / cleanup 日志与截图帧。
 * 隔离根: 每次运行用 fs.mkdtempSync 在 os.tmpdir() 下原子新建唯一目录，前缀为
 *        fuxuan_certified_motion_<slug>_；不再对固定名目录做启动前递归删除，
 *        因此同参数重跑不会抹掉上一次的产物，也不会碰到历史固定名目录。
 *        失败保留本次根并打印 preserved test root；成功只在归属可证明时清理本次根。
 */
const fs=require('fs'),os=require('os'),path=require('path'),{spawn}=require('child_process');
const exe=process.argv[2], skillId=process.argv[3], candidateFile=process.argv[4];
if(!exe||!fs.existsSync(exe))throw Error('missing exe');
if(!skillId||!/^[a-z0-9_\-]+$/i.test(skillId))throw Error('missing skill id');
if(!candidateFile||!fs.existsSync(candidateFile))throw Error('missing candidate json');
const slug=skillId.toLowerCase().replace(/[^a-z0-9]/g,'_');
const ROOT_PREFIX='fuxuan_certified_motion_'+slug+'_';
const TMP=path.resolve(os.tmpdir());
const root=fs.mkdtempSync(path.join(TMP,ROOT_PREFIX));
const rootRealPath=fs.realpathSync(root);
const inbox=path.join(root,'inbox.txt'), sleep=ms=>new Promise(r=>setTimeout(r,ms));
// 归属判定：成功清理前必须能证明这个目录就是本次创建的那个。true=可证明；null=已不存在；
// 字符串=不能证明的原因。任何一项不成立都保留现场并非零报告，绝不强删。
function ownRootVerdict(){
  const abs=path.resolve(root);
  if(path.dirname(abs)!==TMP)return 'absolute path is not a direct child of '+TMP;
  if(!path.basename(abs).startsWith(ROOT_PREFIX))return 'name does not carry this run\'s prefix';
  let stat;try{stat=fs.lstatSync(abs)}catch{return null}
  if(stat.isSymbolicLink())return 'root is a link now, not the directory this run created';
  if(!stat.isDirectory())return 'root is no longer a directory';
  let real;try{real=fs.realpathSync(abs)}catch(error){return 'cannot resolve the real location ('+(error.code||error.message)+')'}
  if(path.dirname(real)!==path.dirname(rootRealPath))return 'real location is not a direct child of '+path.dirname(rootRealPath);
  if(!path.basename(real).startsWith(ROOT_PREFIX))return 'real name does not carry this run\'s prefix';
  if(real!==rootRealPath)return 'real location is no longer the directory this run created';
  return true;
}
const log=()=>{try{return fs.readFileSync(path.join(root,'logs','player_log.txt'),'utf8')}catch{return ''}};
const logTail=()=>log().split(/\r?\n/).slice(-80).join('\n');
async function send(x,ms=350){fs.writeFileSync(inbox,x);await sleep(ms);fs.writeFileSync(inbox,'');await sleep(60)}
async function wait(mark){for(let end=Date.now()+90000;Date.now()<end;await sleep(200))if(log().includes(mark))return;throw Error('timeout '+mark)}
async function count(n){let d=path.join(root,'test_screenshots');for(let end=Date.now()+4000;Date.now()<end;await sleep(80))if(fs.existsSync(d)&&fs.readdirSync(d).filter(x=>x.endsWith('.png')).length>=n)return;throw Error('missing frame '+n)}
function lifeState(){const matches=[...log().matchAll(/\[LifeState\] snapshot version=(\d+).*?action=([^ ]+).*?events=(\d+)/g)];if(!matches.length)throw Error('life-state snapshot missing');const m=matches[matches.length-1];return{version:+m[1],action:m[2],events:+m[3]}}
(async()=>{fs.mkdirSync(path.join(root,'certified_motions'),{recursive:true});
fs.writeFileSync(path.join(root,'.test_mode'),'');
fs.copyFileSync(candidateFile,path.join(root,'certified_motions',skillId+'.json'));
fs.writeFileSync(inbox,'');
let p=spawn(exe,[],{env:{...process.env,FU_XUAN_DATA:root},stdio:'ignore'});
try{await wait('[DesktopPet] 落地');await send('@@sim:idle-actions:off');await send('@@sim:walk:stop');await sleep(1600);
await send('@@sim:status');await wait('velocity=(0,0)');await sleep(500);
await send('@@sim:certified-motion:'+skillId);
await wait(`[CertifiedMotion] started: ${skillId}`);
await send('@@sim:life-state');
const active=lifeState();
if(active.action!=='Active')throw Error('life-state action not Active: '+active.action);
for(let i=0;i<6;i++){await send('@@sim:model-measurement-snapshot',450);await count(i+1)}
// 曲线时长由认证技能决定，不能以固定等待时间提前退出；否则长动作会被
// @@test:quit 取消，造成“已验证完成”的假阳性。以运行时正常释放作为终态。
await wait('[EmbodiedRuntimeAdmission] released: certified-motion-completed');
await send('@@sim:model-measurement-snapshot');await count(7);
await send('@@sim:life-state');
const completed=lifeState();
if(completed.action!=='Completed')throw Error('life-state action not Completed: '+completed.action);
await send('@@test:quit',1200);
const l=log();
for(const mark of [`[CertifiedMotion] started: ${skillId}`,'[EmbodiedRuntimeAdmission] admitted: '+skillId,'[EmbodiedSafeRecovery] pose-restored','[EmbodiedRuntimeAdmission] released: certified-motion-completed','[CertifiedMotion] cleanup: certified-motion-completed'])
  if(!l.includes(mark))throw Error('log missing: '+mark);
console.log(JSON.stringify({root,active,completed}));
}finally{
  if(!p.killed)p.kill();
}
})().then(()=>{
  const verdict=ownRootVerdict();
  if(verdict===null)return;
  if(verdict!==true){
    console.error('refusing to delete the test root: '+verdict);
    console.error('preserved test root: '+root);
    process.exitCode=1;
    return;
  }
  fs.rmSync(root,{recursive:true,force:true});
}).catch(e=>{
  console.error(e.message);
  console.error('preserved test root: '+root);
  console.error('player log tail:\n'+logTail());
  process.exitCode=1;
});
