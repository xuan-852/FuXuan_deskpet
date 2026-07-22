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
import { writeFileSync, unlinkSync, existsSync, mkdirSync } from 'node:fs';
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
            req.on('end', () => {
                try {
                    const { source, output_path, compiler: requestedCompiler, title, pin_to_desktop } = JSON.parse(body);
                    if (!source || !source.trim()) {
                        res.writeHead(400, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ success: false, error: 'Missing source' }));
                        return;
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
                    writeFileSync(texPath, source, 'utf-8');

                    // ── 编译 ──
                    const compileArgs = `-interaction=nonstopmode -halt-on-error -output-directory="${outDir}" "${texPath}"`;
                    const baseNoExt = join(outDir, basename(texPath, '.tex'));

                    for (let pass = 1; pass <= 2; pass++) {
                        try {
                            execSync(`"${compilerPath}" ${compileArgs}`, {
                                cwd: outDir, timeout: 120000, windowsHide: true, stdio: 'pipe', encoding: 'utf-8',
                            });
                        } catch (e) {
                            // 提取最后几行错误信息
                            const stderr = (e.stderr || '').trim();
                            const lines = stderr ? stderr.split('\n') : (e.stdout || '').split('\n');
                            const tail = lines.slice(-10).join('\n').trim();
                            res.writeHead(500, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({
                                success: false, error: `编译失败（第 ${pass} 遍）`,
                                compiler: compilerPath, log_tail: tail || e.message
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
