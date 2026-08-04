/**
 * OpenClaw Bridge Server
 *
 * Connects to the OpenClaw Gateway via WebSocket and exposes
 * a simple HTTP API for C# Unity to call for web research.
 *
 * Gateway event protocol:
 *   chat.send RPC -> { runId, status: "started" }
 *   Events via onEvent:
 *     chat { sessionKey, deltaText, ... }          - intermediate deltas
 *     chat { sessionKey, stopReason, message, ... } - final response
 */

import { GatewayChatClient } from 'file:///D:/openclaw/node_modules/openclaw/dist/gateway-chat-BW6uyvQL.js';
import { createServer } from 'node:http';
import { randomUUID } from 'node:crypto';
import { execSync } from 'node:child_process';
import { writeFileSync, unlinkSync, existsSync, mkdirSync, readFileSync } from 'node:fs';
import { dirname, basename, extname, join } from 'node:path';

// ─── Configuration ───────────────────────────────────────────────────────────
const GATEWAY_URL     = process.env.GATEWAY_URL     || 'ws://127.0.0.1:18789';
const GATEWAY_TOKEN   = process.env.GATEWAY_TOKEN   || '367be203e32a4da345a6859d08298071dc058b78d4bcb203';
const BRIDGE_PORT     = parseInt(process.env.BRIDGE_PORT || '19876', 10);
const SESSION_KEY     = process.env.SESSION_KEY     || 'agent:main:main';
const CHAT_TIMEOUT_MS = parseInt(process.env.CHAT_TIMEOUT_MS || '180000', 10);

// ─── State ───────────────────────────────────────────────────────────────────
let chatClient   = null;
let connected    = false;
let connectError = null;

// Per-session waiters: Map<sessionKey, { resolve, reject, timeout }>
const waiters = new Map();

// ─── Gateway Connection ──────────────────────────────────────────────────────
async function connect_() {
    try {
        chatClient = await GatewayChatClient.connect({
            url: GATEWAY_URL,
            token: GATEWAY_TOKEN,
        });

        chatClient.onEvent = handleGatewayEvent;

        chatClient.onDisconnected = (reason) => {
            console.error(`[Bridge] Disconnected: ${reason}`);
            connected = false;
            for (const [, w] of waiters) {
                clearTimeout(w.timeout);
                w.reject(new Error(`Gateway disconnected: ${reason}`));
            }
            waiters.clear();
        };

        chatClient.start();
        await chatClient.waitForReady();
        connected = true;
        connectError = null;
        console.log(`[Bridge] Connected to Gateway at ${GATEWAY_URL}`);
    } catch (err) {
        connected = false;
        connectError = err.message;
        console.error(`[Bridge] Connection failed: ${err.message}`);
        throw err;
    }
}

// ─── Event Handler ───────────────────────────────────────────────────────────
function handleGatewayEvent(evt) {
    if (evt.event === 'chat') {
        const p = evt.payload || {};
        const sk = p.sessionKey;
        if (!sk || !waiters.has(sk)) return;

        if (p.stopReason) {
            // Final chat event — message.content has the full reply
            const w = waiters.get(sk);
            clearTimeout(w.timeout);
            waiters.delete(sk);
            const msg = p.message;
            if (msg && msg.content) {
                w.resolve(msg.content);
            } else if (p.deltaText) {
                w.resolve(p.deltaText);
            } else {
                w.resolve(JSON.stringify(p));
            }
        }
    }
}

// ─── Send Chat and Wait ──────────────────────────────────────────────────────
async function sendChatAndWait(query) {
    if (!chatClient || !connected) {
        throw new Error('Gateway not connected');
    }

    const runId = randomUUID();

    const responsePromise = new Promise((resolve, reject) => {
        const timeout = setTimeout(() => {
            waiters.delete(SESSION_KEY);
            reject(new Error('Response timeout'));
        }, CHAT_TIMEOUT_MS);

        waiters.set(SESSION_KEY, { resolve, reject, timeout });
    });

    try {
        await chatClient.client.request('chat.send', {
            sessionKey: SESSION_KEY,
            message: query,
            timeoutMs: CHAT_TIMEOUT_MS,
            idempotencyKey: runId,
        });

        return await responsePromise;
    } catch (err) {
        if (waiters.has(SESSION_KEY)) {
            const w = waiters.get(SESSION_KEY);
            clearTimeout(w.timeout);
            waiters.delete(SESSION_KEY);
        }
        throw err;
    }
}

// ─── HTTP Server ─────────────────────────────────────────────────────────────
function startHttpServer() {
    const server = createServer(async (req, res) => {
        res.setHeader('Access-Control-Allow-Origin', '*');
        res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
        if (req.method === 'OPTIONS') { res.writeHead(204); res.end(); return; }

        const u = new URL(req.url, `http://${req.headers.host}`);
        const path = u.pathname;

        if (path === '/health') {
            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ status: connected ? 'ok' : 'error', connected, error: connectError }));
            return;
        }

        if (path === '/search') {
            const query = u.searchParams.get('q') || u.searchParams.get('query') || '';
            if (!query.trim()) {
                res.writeHead(400, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ error: 'Missing ?q=' }));
                return;
            }

            if (!connected) {
                try { await connect_(); }
                catch (err) {
                    res.writeHead(503, { 'Content-Type': 'application/json' });
                    res.end(JSON.stringify({ error: `Connection failed: ${err.message}` }));
                    return;
                }
            }

            try {
                console.log(`[Bridge] Search: "${query.substring(0, 100)}..."`);
                const t0 = Date.now();
                const text = await sendChatAndWait(query);
                const elapsed = Date.now() - t0;
                console.log(`[Bridge] Done (${text.length} chars, ${elapsed >= 1000 ? (elapsed/1000).toFixed(1)+'s' : elapsed+'ms'})`);

                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ success: true, query, response: text, elapsed_ms: elapsed }));
            } catch (err) {
                console.error(`[Bridge] Error: ${err.message}`);
                res.writeHead(500, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({ success: false, error: err.message }));
            }
            return;
        }

        // ─── POST /compile_latex ─────────────────────────────────────
        if (path === '/compile_latex' && req.method === 'POST') {
            let body = '';
            req.on('data', chunk => body += chunk);
            req.on('end', async () => {
                try {
                    const { source, description, output_path, compiler: requestedCompiler, title, pin_to_desktop } = JSON.parse(body);

                    // ── 获取 LaTeX 源码：直接提供或由 AI 生成 ──
                    let latexSource = source && source.trim() ? source : null;
                    if (!latexSource) {
                        if (!description || !description.trim()) {
                            res.writeHead(400, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({ success: false, error: '需要提供 source（直接源码）或 description（描述需求由 AI 生成）' }));
                            return;
                        }
                        // 让 Gateway AI 生成 LaTeX 源码
                        if (!connected) {
                            try { await connect_(); }
                            catch (err) {
                                res.writeHead(503, { 'Content-Type': 'application/json' });
                                res.end(JSON.stringify({ success: false, error: `AI 生成连接失败: ${err.message}` }));
                                return;
                            }
                        }
                        const prompt = `你是一个 LaTeX 专家。请根据以下需求生成完整的 LaTeX 文档源码。

需求：${description}

要求：
- 输出纯 LaTeX 源码（以 \\documentclass 开头）
- 中文文档用 ctexart 或 xeCJK，英文用 article
- 包含 \\begin{document} 和 \\end{document}
- 结构完整、排版美观
- **只输出 LaTeX 源码，不要任何解释、不要 Markdown 代码块包裹**`;
                        console.log(`[Bridge] Generating LaTeX via AI for: "${description.substring(0, 80)}..."`);
                        const t0 = Date.now();
                        const aiResponse = await sendChatAndWait(prompt);
                        const elapsed = Date.now() - t0;
                        // 确保返回字符串
                        let rawText = (typeof aiResponse === 'string') ? aiResponse : JSON.stringify(aiResponse);
                        // Gateway 可能返回结构化 JSON 格式，提取 text 字段
                        try {
                            const parsed = JSON.parse(rawText);
                            if (Array.isArray(parsed)) {
                                rawText = parsed
                                    .filter(item => item.type === 'text' && item.text)
                                    .map(item => item.text)
                                    .join('\n');
                            }
                        } catch { /* 不是 JSON，直接使用 */ }
                        latexSource = rawText;
                        // 清理可能的代码块包裹
                        latexSource = latexSource.replace(/^```(?:latex|tex|)\s*/i, '').replace(/\s*```$/i, '').trim();
                        console.log(`[Bridge] AI generated ${latexSource.length} chars of LaTeX in ${elapsed >= 1000 ? (elapsed/1000).toFixed(1)+'s' : elapsed+'ms'}`);

                        // ★ BUG 修复：校验 AI 生成结果是否为有效 LaTeX 源码。
                        // 此前 Gateway 生成失败时会返回 "⚠️ Agent couldn't generate a response..."，
                        // 该文本被直接写入 .tex 导致编译必然失败且报错不明。
                        if (!/\\documentclass/.test(latexSource)) {
                            const snippet = latexSource.length > 200 ? latexSource.slice(0, 200) + '…' : latexSource;
                            console.error(`[Bridge] AI returned invalid LaTeX (${latexSource.length} chars): ${snippet}`);
                            res.writeHead(502, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({
                                success: false,
                                error: 'AI 未能生成有效的 LaTeX 源码（返回内容不以 \\documentclass 开头），可能因文档太长超出生成能力。建议分段生成，或换一种更明确的描述重试。',
                                ai_response_snippet: snippet
                            }));
                            return;
                        }
                    }

                    // ── 选择编译器 ──
                    const compiler = requestedCompiler || 'xelatex';  // 默认 xelatex（中文友好）
                    const compilerPath = process.env.LATEX_COMPILER || compiler;
                    try {
                        execSync(`where "${compilerPath}"`, { stdio: 'pipe', windowsHide: true, timeout: 5000, encoding: 'utf-8' });
                    } catch {
                        res.writeHead(412, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({
                            success: false,
                            error: `未找到编译器「${compilerPath}」。请安装 TeX Live (https://tug.org/texlive/) 并确保 ${compilerPath} 在 PATH 中。`,
                            compiler: compilerPath
                        }));
                        return;
                    }

                    // ── 确定输出路径（按标题建文件夹） ──
                    const docTitle = (title || 'document').replace(/[<>:"\/\\|?*]/g, '_');
                    let texPath, outDir;
                    if (output_path) {
                        texPath = output_path.endsWith('.tex') ? output_path : output_path + '.tex';
                        outDir = dirname(texPath);
                    } else {
                        const folderName = `${docTitle}_${new Date().toISOString().slice(0,10).replace(/-/g,'')}_${Date.now().toString(36)}`;
                        outDir = join('D:\\DesktopPetData\\Documents', folderName);
                        texPath = join(outDir, `${docTitle}.tex`);
                    }
                    if (!existsSync(outDir)) mkdirSync(outDir, { recursive: true });

                    // ── 写 .tex 文件 ──
                    writeFileSync(texPath, latexSource, 'utf-8');

                    // ── 编译前检查可用内存（Windows）──
                    // ★ BUG 修复：此前内存不足（如仅剩 ~800MB）时 xelatex 启动即崩溃，
                    //   只留下 .tex 没有 .pdf/.log，报错不明。现在低于阈值直接给出明确提示。
                    const MIN_FREE_MEM_GB = parseFloat(process.env.LATEX_MIN_FREE_MEM_GB || '1.5');
                    try {
                        const memOut = execSync(
                            `powershell -NoProfile -Command "$os=Get-CimInstance Win32_OperatingSystem; [math]::Round($os.FreePhysicalMemory/1MB,1)"`,
                            { windowsHide: true, timeout: 15000, encoding: 'utf-8' }
                        ).trim();
                        const freeGB = parseFloat(memOut);
                        if (!isNaN(freeGB) && freeGB < MIN_FREE_MEM_GB) {
                            console.warn(`[Bridge] Low memory ${freeGB}GB < ${MIN_FREE_MEM_GB}GB, aborting compile`);
                            res.writeHead(503, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({
                                success: false,
                                error: `系统可用内存不足（仅 ${freeGB.toFixed(1)}GB，低于安全阈值 ${MIN_FREE_MEM_GB}GB），长文档编译可能崩溃。请先关闭部分程序释放内存后重试。`,
                                free_mem_gb: freeGB, min_free_mem_gb: MIN_FREE_MEM_GB
                            }));
                            return;
                        }
                        console.log(`[Bridge] Free memory OK: ${freeGB}GB (min ${MIN_FREE_MEM_GB}GB)`);
                    } catch (memErr) {
                        console.warn(`[Bridge] Memory check skipped: ${memErr.message}`);
                    }

                    // ── 编译 ──
                    // 长文档 xelatex 可能需要较长时间，超时可由环境变量 LATEX_TIMEOUT_MS 调整（默认 300s）
                    const COMPILE_TIMEOUT_MS = parseInt(process.env.LATEX_TIMEOUT_MS || '300000', 10);
                    const compileArgs = `-interaction=nonstopmode -halt-on-error -output-directory="${outDir}" "${texPath}"`;
                    const baseNoExt = join(outDir, basename(texPath, '.tex'));
                    const logPath = baseNoExt + '.log';

                    for (let pass = 1; pass <= 2; pass++) {
                        try {
                            execSync(`"${compilerPath}" ${compileArgs}`, {
                                cwd: outDir, timeout: COMPILE_TIMEOUT_MS, windowsHide: true, stdio: 'pipe', encoding: 'utf-8',
                            });
                        } catch (e) {
                            // 提取错误信息：优先读 .log 文件尾部（比 stderr 更完整）
                            const stderr = (e.stderr || '').trim();
                            const lines = stderr ? stderr.split('\n') : (e.stdout || '').split('\n');
                            let tail = lines.slice(-10).join('\n').trim();
                            try {
                                if (existsSync(logPath)) {
                                    const logLines = readFileSync(logPath, 'utf-8').split('\n');
                                    const logTail = logLines.slice(-40).join('\n').trim();
                                    if (logTail) tail = logTail;
                                }
                            } catch { /* ignore */ }

                            // 智能识别常见失败原因，给出可操作提示
                            let friendly = `编译失败（第 ${pass} 遍）`;
                            if (/capacity exceeded|main memory|Memory capacity/i.test(tail)) {
                                friendly = '文档过长导致 TeX 内存溢出（TeX capacity exceeded）。建议分段生成：例如「先写第 1-4 模块，再写第 5-8 模块」并分别编译。';
                            } else if (/Undefined control sequence|! LaTeX Error/i.test(tail)) {
                                friendly = 'LaTeX 语法或宏包错误（可能是缺失 \\end{document} 或宏包冲突）。可读取 .tex 源码检查修正。';
                            }

                            res.writeHead(500, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({
                                success: false, error: friendly,
                                compiler: compilerPath, log_tail: tail, log_path: logPath
                            }));
                            return;
                        }
                    }

                    // ── 清理中间产物（保留 .log） ──
                    const allExts = ['.aux', '.out', '.toc', '.lof', '.lot', '.idx', '.bbl', '.blg', '.fls', '.synctex.gz'];
                    for (const ext of allExts) {
                        const p = baseNoExt + ext;
                        try { if (existsSync(p)) unlinkSync(p); } catch { /* ignore */ }
                    }

                    const pdfPath = baseNoExt + '.pdf';
                    if (!existsSync(pdfPath)) {
                        res.writeHead(500, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ success: false, error: '编译失败，未生成 PDF' }));
                        return;
                    }

                    // ── 创建桌面快捷方式 ──
                    let shortcutPath = null;
                    if (pin_to_desktop === true) {
                        try {
                            const desktopDir = join(process.env.USERPROFILE || 'C:\\Users\\25295', 'Desktop');
                            shortcutPath = join(desktopDir, `${docTitle}.lnk`);
                            const psCmd = `$wshell = New-Object -ComObject WScript.Shell; $lnk = $wshell.CreateShortcut('${shortcutPath.replace(/'/g, "''")}'); $lnk.TargetPath = '${pdfPath.replace(/'/g, "''")}'; $lnk.Save()`;
                            execSync(`powershell -Command \"${psCmd.replace(/"/g, '\\"')}\"`, { windowsHide: true, timeout: 10000 });
                            console.log(`[Bridge] Shortcut created: ${shortcutPath}`);
                        } catch (e) {
                            console.error(`[Bridge] Shortcut creation failed: ${e.message}`);
                            shortcutPath = null;
                        }
                    }

                    res.writeHead(200, { 'Content-Type': 'application/json' });
                    res.end(JSON.stringify({
                        success: true, pdf_path: pdfPath, tex_path: texPath,
                        folder_path: outDir, shortcut_path: shortcutPath,
                        title: docTitle, compiler: compilerPath
                    }));
                } catch (err) {
                    console.error(`[Bridge] Compile error: ${err.message}`);
                    res.writeHead(500, { 'Content-Type': 'application/json' });
                    res.end(JSON.stringify({ success: false, error: err.message }));
                }
            });
            return;
        }

        res.writeHead(404, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Not found. Use /search?q=, /compile_latex, or /health' }));
    });

    server.listen(BRIDGE_PORT, '127.0.0.1', () => {
        console.log(`[Bridge] HTTP server on http://127.0.0.1:${BRIDGE_PORT}`);
    });
}

async function main() {
    console.log(`[Bridge] Starting...`);
    try { await connect_(); } catch (e) { console.error(`[Bridge] Initial connect failed: ${e.message}`); }
    startHttpServer();
    process.on('SIGINT', () => { console.log('\n[Bridge] Shutdown'); chatClient?.stop(); process.exit(0); });
    process.on('SIGTERM', () => { console.log('\n[Bridge] Shutdown'); chatClient?.stop(); process.exit(0); });
    process.on('uncaughtException', (e) => { console.error(`[Bridge] Uncaught: ${e.message}`); });
    process.on('unhandledRejection', (e) => { console.error(`[Bridge] Unhandled rejection: ${e?.message || e}`); });
}

main().catch(e => { console.error(`[Bridge] Fatal: ${e.message}`); process.exit(1); });
