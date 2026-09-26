'use strict';

// Passive observation of an ordinary DesktopPet session. This process never
// launches or controls the pet, and never stores or prints raw application logs.
const fs = require('fs');
const os = require('os');
const path = require('path');
const { StringDecoder } = require('string_decoder');

const args = process.argv.slice(2);
const minutesArg = args.find(arg => arg.startsWith('--minutes='));
const minutes = minutesArg ? Number(minutesArg.slice('--minutes='.length)) : 15;
if (!Number.isFinite(minutes) || minutes <= 0 || minutes > 120) {
  console.error('--minutes must be between 0 and 120');
  process.exit(2);
}

function getDataRoot() {
  const configured = process.env.FU_XUAN_DATA;
  if (configured) return path.resolve(configured.trim().replace(/^"|"$/g, ''));
  for (const legacy of ['C:\\DesktopPetData', 'D:\\DesktopPetData']) {
    if (fs.existsSync(legacy)) return legacy;
  }
  return path.join(process.env.LOCALAPPDATA || os.homedir(), 'FuXuan', 'DesktopPetData');
}

const root = getDataRoot();
if (fs.existsSync(path.join(root, '.test_mode'))) {
  console.error('数据根处于 .test_mode；用户级观察必须连接正常实例。');
  process.exit(2);
}
const logPath = path.join(root, 'logs', 'player_log.txt');
const startedAt = Date.now();
const deadline = startedAt + minutes * 60_000;
const counts = Object.create(null);
let offset = fs.existsSync(logPath) ? fs.statSync(logPath).size : 0;
let partial = '';
let decoder = new StringDecoder('utf8');
let lastHeartbeat = 0;

const events = [
  ['启动', /\[DesktopPet\] 落地/],
  ['桌宠可见', /\[NativeLive2DOverlay\] sync visible/],
  ['真实点击', /\[DragHandler\] 轻击宠物/],
  ['真实拖拽开始', /\[DragHandler\] 拖动已启动/],
  ['拖拽结束', /\[DragHandler\] 抛掷:/],
  ['行走转拖拽', /\[DragHandoff\] walking-to-drag accepted/],
  ['动作开始', /\[CertifiedMotion\] started:/],
  ['动作完成', /\[CertifiedMotion\] cleanup: certified-motion-completed/],
  ['动作收束', /\[CertifiedMotion\] cleanup: (?!certified-motion-completed)/],
  ['姿态恢复', /\[EmbodiedSafeRecovery\] pose-restored:/],
  ['恢复失败', /\[EmbodiedSafeRecovery\].*(?:failed|error)/i],
  ['运行错误', /^\[\d\d:\d\d:\d\d\.\d+\] (?:Error|Exception):/],
];

function inspect(line) {
  for (const [label, pattern] of events) {
    if (!pattern.test(line)) continue;
    counts[label] = (counts[label] || 0) + 1;
    // A label and time are enough to correlate a human observation. Never print the line.
    console.log(`${new Date().toLocaleTimeString('zh-CN')} ${label}`);
    break;
  }
}

function poll() {
  try {
    if (!fs.existsSync(logPath)) return;
    const size = fs.statSync(logPath).size;
    if (size < offset) { offset = 0; partial = ''; decoder = new StringDecoder('utf8'); }
    if (size === offset) return;
    const fd = fs.openSync(logPath, 'r');
    try {
      while (offset < size) {
        const buffer = Buffer.allocUnsafe(Math.min(64 * 1024, size - offset));
        const read = fs.readSync(fd, buffer, 0, buffer.length, offset);
        if (!read) break;
        offset += read;
        const lines = (partial + decoder.write(buffer.subarray(0, read))).split(/\r?\n/);
        partial = lines.pop();
        for (const line of lines) inspect(line);
      }
    } finally { fs.closeSync(fd); }
  } catch (error) {
    if (!['ENOENT', 'EACCES', 'EBUSY'].includes(error.code)) {
      console.error(`日志读取失败：${error.code || error.name}`);
    }
  }
}

function finish() {
  clearInterval(timer);
  poll();
  console.log('观察结束。事件计数：' + JSON.stringify(counts));
  console.log('这些是后台事件；画面可见性、动作自然度和使用感受以用户现场观察为准。');
}

console.log(`用户级只读观察已启用，时长 ${minutes} 分钟。`);
console.log('连接正常桌宠数据根；仅显示新事件的类别和时间，不控制桌宠。');
const timer = setInterval(() => {
  poll();
  if (Date.now() >= deadline) return finish();
  if (Date.now() - lastHeartbeat >= 30_000) {
    lastHeartbeat = Date.now();
    console.log(`${new Date().toLocaleTimeString('zh-CN')} 等待正常使用事件…`);
  }
}, 1000);
process.once('SIGINT', () => { finish(); process.exit(0); });
