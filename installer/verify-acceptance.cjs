#!/usr/bin/env node
/**
 * 安装验收脚本 — 在目标机（干净 VM / 新电脑）上运行，对应 installer-plan §八 验收清单
 *
 * 用法:
 *   node verify-acceptance.cjs [--dir <安装目录>] [--token <BRIDGE_TOKEN>] [--keep-alive]
 *     --dir        安装目录（默认探测: C:\Program Files\FuXuan）
 *     --token      桥接 token（默认读环境变量或 HKCU\Environment 注册表）
 *     --keep-alive 测试后保留桌宠运行（默认结束）
 *
 * 输出: [PASS] / [FAIL] / [MANUAL] 逐项报告；存在 FAIL 即退出码 1。
 * 自动项覆盖 §八 1,3,4,5,6,8；手动项（2,7,9,10,11,12）在末尾列出人工勾选。
 * 记忆安全：聊天/工具测试用隔离数据目录（FU_XUAN_DATA 指向 %TEMP%），不碰生产记忆。
 */
'use strict';
const { spawn, execSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const args = process.argv.slice(2);
const getArg = name => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : null; };
const installDir = getArg('--dir') || 'C:\\Program Files\\FuXuan';
const keepAlive = args.includes('--keep-alive');

const PLAYER_LOG = path.join(os.homedir(), 'AppData', 'LocalLow', 'DefaultCompany', 'desktop pet', 'Player.log');
const DATA_ROOT = process.env.FU_XUAN_DATA || 'D:\\DesktopPetData';
const TEST_DATA = path.join(os.tmpdir(), 'fuxuan_accept_test');

const sleep = ms => new Promise(r => setTimeout(r, ms));
const log = m => console.log(m);
const results = [];
function check(name, ok, detail) {
    results.push({ name, ok });
    log(`${ok ? '[PASS]' : '[FAIL]'} ${name}${detail ? ' — ' + detail : ''}`);
}
function manual(name, detail) { log(`[MANUAL] ${name}${detail ? ' — ' + detail : ''}`); }

function regEnv(name) {
    try {
        const out = execSync(`reg query "HKCU\\Environment" /v ${name} 2>nul`, { encoding: 'utf8', windowsHide: true, stdio: ['ignore', 'pipe', 'ignore'] });
        const m = out.match(/REG_\w+\s+(.+)/);
        return m ? m[1].trim() : '';
    } catch { return ''; }
}
function readLogSafe() {
    for (let i = 0; i < 20; i++) { try { return fs.readFileSync(PLAYER_LOG, 'utf8'); } catch { /* busy */ } }
    return '';
}
function writeFileSafe(p, c) { for (let i = 0; i < 20; i++) { try { fs.writeFileSync(p, c); return; } catch {} } }
function killPet() { try { execSync('taskkill /IM DesktopPet.exe /F /T', { stdio: 'ignore', windowsHide: true }); } catch {} }

async function httpJson(url, opts = {}, token) {
    return new Promise((resolve) => {
        const u = new URL(url);
        const http = require('http');
        const req = http.request(u, {
            method: opts.method || 'GET',
            headers: { 'x-bridge-token': token || '', 'Content-Type': 'application/json', ...(opts.headers || {}) },
            timeout: 60000
        }, res => {
            let b = '';
            res.on('data', d => b += d);
            res.on('end', () => { try { resolve({ status: res.statusCode, body: JSON.parse(b) }); } catch { resolve({ status: res.statusCode, body: b }); } });
        });
        req.on('error', e => resolve({ status: 0, body: { error: e.message } }));
        req.on('timeout', () => { req.destroy(); resolve({ status: 0, body: { error: 'timeout' } }); });
        if (opts.body) req.write(JSON.stringify(opts.body));
        req.end();
    });
}

async function main() {
    log('============================================');
    log('  FuXuan 安装验收脚本 (installer-plan §八)');
    log('============================================');
    log(`安装目录: ${installDir}`);
    log(`生产数据目录: ${DATA_ROOT}`);
    log('');

    // ── 1. 安装产物 ──
    log('── 1. 安装产物 ──');
    const exe = path.join(installDir, 'DesktopPet.exe');
    const files = [['DesktopPet.exe', exe], ['version.txt', path.join(installDir, 'version.txt')],
        ['bridge', path.join(installDir, 'bridge', 'openclaw_bridge.js')],
        ['node 运行时', path.join(installDir, 'bridge', 'node', 'node.exe')],
        ['组件脚本', path.join(installDir, 'extras', 'components', 'install-service.cmd')]];
    for (const [n, p] of files) check(`安装产物 ${n}`, fs.existsSync(p), p);

    // ── 2. 环境变量 ──
    log('\n── 2. 环境变量（HKCU\Environment）──');
    const envNames = ['FU_XUAN_DATA', 'BRIDGE_TOKEN', 'OFFICE_SCRIPTS_DIR', 'KNOWLEDGE_SCRIPTS_DIR', 'OPENCLAW_NODE_MODULES'];
    for (const n of envNames) { const v = regEnv(n); check(`环境变量 ${n}`, v !== '', v ? v : '未设置'); }

    // ── 3. 桥接服务 ──
    log('\n── 3. 桥接服务 ──');
    let svcRunning = false;
    try {
        const sc = execSync('sc query FuXuanBridge', { encoding: 'utf8', windowsHide: true });
        svcRunning = /RUNNING/.test(sc);
        check('桥服务 FuXuanBridge', svcRunning, sc.split('\n')[3]?.trim() || '');
    } catch { check('桥服务 FuXuanBridge', false, '服务不存在（sc query 失败）'); }
    if (!svcRunning) {
        try { svcRunning = /LISTENING/.test(execSync('netstat -ano | findstr 19876', { encoding: 'utf8', windowsHide: true })); check('端口 19876 兜底', svcRunning, ''); } catch { check('端口 19876 兜底', false, ''); }
    }

    // ── 4. 桥接健康 ──
    log('\n── 4. 桥接 /health ──');
    const token = getArg('--token') || process.env.BRIDGE_TOKEN || regEnv('BRIDGE_TOKEN');
    const health = await httpJson('http://127.0.0.1:19876/health', {}, token);
    check('GET /health', health.status === 200 && health.body.status === 'ok', JSON.stringify(health.body));

    // ── 5. 桌宠启动（生产数据目录 + 测试模式防写）──
    log('\n── 5. 桌宠启动（生产数据目录）──');
    killPet(); await sleep(3000);
    try { fs.unlinkSync(PLAYER_LOG); } catch {}
    const prodTestMode = path.join(DATA_ROOT, '.test_mode');
    fs.writeFileSync(prodTestMode, ''); // 测试模式：正常启动但零写入（防污染生产记忆）
    const petProc = spawn(exe, [], { stdio: 'ignore', windowsHide: true });
    petProc.unref();
    let booted = false;
    for (let i = 0; i < 120; i++) {
        if (readLogSafe().includes('[DesktopPet] 落地')) { booted = true; break; }
        await sleep(500);
    }
    const lc = readLogSafe();
    check('桌宠启动（落地标记）', booted, '');
    check('无 NullReferenceException', !/NullReferenceException/.test(lc), '');
    check('透明窗口就绪', /透明窗口已就绪/.test(lc), '');
    killPet(); await sleep(1000);
    try { fs.unlinkSync(prodTestMode); } catch {}

    // ── 6. 聊天链路（DeepSeek，隔离数据目录防污染）──
    log('\n── 6. 聊天链路（DeepSeek）──');
    fs.rmSync(TEST_DATA, { recursive: true, force: true });
    fs.mkdirSync(TEST_DATA, { recursive: true });
    const inbox = path.join(TEST_DATA, 'inbox.txt');
    fs.writeFileSync(path.join(TEST_DATA, '.test_mode'), '');
    writeFileSafe(inbox, '');
    // 用隔离目录重启桌宠做聊天测试
    killPet(); await sleep(2000);
    try { fs.unlinkSync(PLAYER_LOG); } catch {}
    spawn(exe, [], { stdio: 'ignore', windowsHide: true, env: { ...process.env, FU_XUAN_DATA: TEST_DATA } }).unref();
    for (let i = 0; i < 120; i++) { if (readLogSafe().includes('[DesktopPet] 落地')) break; await sleep(500); }
    writeFileSafe(inbox, '你好，请用一句话回应我');
    await sleep(35000);
    const chatLog = readLogSafe();
    const okChat = /ApiClient.*usage/.test(chatLog) && !/API 请求失败|离线回退成功/.test(chatLog);
    check('聊天收到真实 AI 回复', okChat, /离线回退成功/.test(chatLog) ? '走了离线回退（检查 DeepSeek Key）' : (okChat ? '' : '未看到 ApiClient usage'));

    // ── 7. 本地工具链路 ──
    log('\n── 7. 本地工具（get_system_info）──');
    writeFileSafe(inbox, '帮我查询一下系统信息');
    await sleep(30000);
    const toolLog = readLogSafe();
    check('工具施法 get_system_info', /施法: get_system_info/.test(toolLog), '');
    check('工具返回结果', /📜 结果/.test(toolLog), '');

    // ── 8. 办公文档（generate_ppt，Python 链路）──
    log('\n── 8. 办公文档 generate_ppt（Python 链路）──');
    const ppt = await httpJson('http://127.0.0.1:19876/generate_office', { method: 'POST', body: { type: 'ppt', description: '做一个三页的验收测试 PPT：封面、介绍、结束', title: 'acceptance_test' } }, token);
    check('POST /generate_office', ppt.status === 200 && ppt.body.success === true, ppt.status === 200 ? (ppt.body.path || ppt.body.error || '') : JSON.stringify(ppt.body).slice(0, 120));
    if (ppt.body && ppt.body.path) check('PPT 文件存在', fs.existsSync(ppt.body.path), ppt.body.path);

    // ── 9. PDF 提取（PyMuPDF 链路）──
    log('\n── 9. PDF 提取（PyMuPDF 链路）──');
    let pdfOk = true;
    try {
        const py = regEnv('OFFICE_PYTHON') || 'python';
        const testPdf = path.join(os.tmpdir(), 'fuxuan_accept_test.pdf');
        execSync(`"${py}" -c "from pypdf import PdfWriter; w=PdfWriter(); w.add_blank_page(200,200); w.write(r'${testPdf}')"`, { stdio: 'ignore', windowsHide: true });
        const pdf = await httpJson('http://127.0.0.1:19876/extract_pdf', { method: 'POST', body: { path: testPdf } }, token);
        check('POST /extract_pdf', pdf.status === 200 && pdf.body.success === true, pdf.status === 200 ? `pages=${pdf.body.pages}` : JSON.stringify(pdf.body).slice(0, 120));
    } catch (e) { check('POST /extract_pdf', false, '无法生成测试 PDF: ' + e.message); }

    // ── 清理 ──
    fs.rmSync(TEST_DATA, { recursive: true, force: true });
    if (!keepAlive) killPet();

    // ── 手动验收项 ──
    log('\n── 手动验收项（人工确认，写入验收记录）──');
    manual('安装向导 UX（8 步页面/密钥页/数据目录页）', '检查无卡死、无管理员弹窗异常');
    manual('openclaw_task 提交→审批弹窗→放行→返回结果', '用「帮我查一下 B 站更新」类指令验证');
    manual('compile_latex 出 PDF', '仅当勾选安装了 TeX 时');
    manual('重启后自启', '重启电脑：桥服务自启 + 桌宠（若勾选自启）自启');
    manual('卸载流程', '卸载→确认保留数据→目录/服务/环境变量清理，数据保留');
    manual('升级流程', '旧数据目录完好，新版本正常');

    // ── 汇总 ──
    log('\n============================================');
    const fails = results.filter(r => !r.ok);
    const passes = results.filter(r => r.ok);
    log(`自动项: ${passes.length} PASS / ${fails.length} FAIL / 共 ${results.length}`);
    if (fails.length > 0) {
        log('存在 FAIL 项，请修复后重跑：');
        fails.forEach(f => log('  ✗ ' + f.name));
        process.exit(1);
    }
    log('自动项全部通过 ✅（手动项请按清单人工验收）');
    process.exit(0);
}

main().catch(e => { console.error('[FAIL] 验收脚本异常: ' + e.message); process.exit(1); });
