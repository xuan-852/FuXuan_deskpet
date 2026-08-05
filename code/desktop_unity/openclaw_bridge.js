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
import { writeFileSync, unlinkSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, copyFileSync } from 'node:fs';
import { dirname, basename, extname, join } from 'node:path';
import { tmpdir } from 'node:os';

// ─── Configuration ───────────────────────────────────────────────────────────
const GATEWAY_URL     = process.env.GATEWAY_URL     || 'ws://127.0.0.1:18789';
// 🔒 Gateway Token：优先从环境变量读取；未配置时回退到旧默认值并警告（建议尽快轮换并在 PM2/启动脚本中配置）
const GATEWAY_TOKEN   = process.env.GATEWAY_TOKEN   || (() => {
    console.warn('[Bridge] ⚠️ GATEWAY_TOKEN 未配置，正在使用内置默认 Token。建议在 PM2/启动脚本中设置环境变量 GATEWAY_TOKEN 并轮换该 Token。');
    return '367be203e32a4da345a6859d08298071dc058b78d4bcb203';
})();
// 🔒 Bridge HTTP 鉴权 Token：Unity 客户端必须携带 x-bridge-token 头（与 GATEWAY_TOKEN 独立，可单独配置）
const BRIDGE_TOKEN    = process.env.BRIDGE_TOKEN   || GATEWAY_TOKEN;
const BRIDGE_PORT     = parseInt(process.env.BRIDGE_PORT || '19876', 10);
const SESSION_KEY     = process.env.SESSION_KEY     || 'agent:main:main';
const CHAT_TIMEOUT_MS = parseInt(process.env.CHAT_TIMEOUT_MS || '180000', 10);

// ─── State ───────────────────────────────────────────────────────────────────
let chatClient   = null;
let connected    = false;
let connectError = null;

// Per-session waiters: Map<sessionKey, { resolve, reject, timeout }>
const waiters = new Map();

// ★ 健壮化：请求串行锁。Gateway 事件只带 sessionKey、无法按 runId 匹配，
//   并发请求会互相覆盖 waiter 导致响应错位（run#7 曾发生模块2 混入元数据、模块6 丢失）。
//   逐节生成本就是串行任务，加锁无性能损失。
let requestChain = Promise.resolve();

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
            const msg = p.message || {};
            let content = null;
            if (typeof msg === 'string') {
                content = msg;
            } else if (Array.isArray(msg.content)) {
                // ★ Gateway final 事件的 message.content 是数组
                //   [{type:'text', text:'...'}]，需展开为纯文本字符串
                content = msg.content
                    .map(x => typeof x === 'string' ? x : (x.text || x.content || ''))
                    .filter(Boolean).join('\n');
            } else if (msg.content) {
                content = msg.content;
            } else if (Array.isArray(msg.parts) && msg.parts.length > 0) {
                content = msg.parts
                    .map(x => typeof x === 'string' ? x : (x.text || x.content || ''))
                    .filter(Boolean).join('\n');
            } else if (typeof msg.text === 'string') {
                content = msg.text;
            } else if (p.deltaText) {
                content = p.deltaText;
            }
            if (content) {
                w.resolve(content);
            } else {
                // ★ 健壮化：final 事件拿不到正文时不再把元数据 JSON 当内容
                //   （此前兜底 resolve(JSON.stringify(p)) 会把 {runId, stopReason...}
                //    写进 .tex，run#7 模块2 的 140 chars 即由此混入）
                w.reject(new Error('Gateway final event missing message content'));
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

    // 串行化：同一时刻只允许一个在途请求，等前一个完成（或失败）再发下一个
    const prev = requestChain;
    let release;
    requestChain = new Promise((r) => { release = r; });
    await prev.catch(() => { /* 忽略前序请求的失败 */ });

    try {
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

            const raw = await responsePromise;
            // ★ 健壮化：先规范化再校验——Gateway 可能返回字符串或
            //   [{type:'text',text:'...'}] 数组（cleanLatexFence 统一转纯文本），
            //   若规范化后仍是元数据 JSON 特征（{runId, stopReason...}）则视为失败
            const content = cleanLatexFence(raw);
            if (/"(runId|stopReason|sessionKey|agentId|state)"\s*:/.test(content)) {
                throw new Error('Response is gateway metadata, not content');
            }
            return content;
        } catch (err) {
            if (waiters.has(SESSION_KEY)) {
                const w = waiters.get(SESSION_KEY);
                clearTimeout(w.timeout);
                waiters.delete(SESSION_KEY);
            }
            throw err;
        }
    } finally {
        release();
    }
}

// ─── LaTeX 清理工具 ──────────────────────────────────────────────────────────
// Gateway 可能返回结构化 JSON（text 块数组）或带 ``` 代码块包裹的文本，
// 统一提取为纯 LaTeX 源码字符串。
function cleanLatexFence(text) {
    let raw = (typeof text === 'string') ? text : JSON.stringify(text);
    try {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed)) {
            raw = parsed
                .filter(item => item.type === 'text' && item.text)
                .map(item => item.text)
                .join('\n');
        }
    } catch { /* 不是 JSON，直接使用 */ }
    return raw.replace(/^```(?:latex|tex|)\s*/i, '').replace(/\s*```$/i, '').trim();
}

// ─── 节内容合法性校验 ──────────────────────────────────────────────────────
// 防止两类坏响应混入 .tex：
//   1) Gateway 元数据 JSON（{runId, stopReason, ...}）——run#7 模块2 曾混入 140 chars
//   2) 响应错位（返回的是其他节的内容）——run#7 模块6 整节丢失
function normalizeTitle(t) {
    return (t || '')
        .replace(/\\_/g, '_')
        .replace(/\\[a-zA-Z]+\b/g, '')
        .replace(/\s+/g, '')
        .toLowerCase();
}

function isValidSectionBody(text, expectedTitle) {
    if (typeof text !== 'string' || !text.trim()) return false;
    // 1) 元数据 JSON 特征
    if (/"(runId|stopReason|sessionKey|agentId|state)"\s*:/.test(text)) return false;
    // 2) 过短（纯 JSON / 空壳占位）
    if (text.trim().length < 30) return false;
    // 3) 必须以 \section{ 开头（允许前导空白）
    if (!/\\section\s*\*?\s*\{/.test(text)) return false;
    // 4) 首个 \section 标题应与期望标题匹配（归一化后）
    const m = text.match(/\\section\s*\*?\s*\{([^}]*)\}/);
    if (!m) return false;
    const bodyTitle = normalizeTitle(m[1]);
    const expTitle = normalizeTitle(expectedTitle);
    if (bodyTitle === expTitle) return true;
    if (expTitle.length >= 4 && bodyTitle.includes(expTitle)) return true;
    if (bodyTitle.length >= 4 && expTitle.includes(bodyTitle)) return true;
    return false;  // 标题不匹配 → 大概率错位响应，拒绝
}

// ─── 检查点编译（增量验证）─────────────────────────────────────────────
// 每生成一节就拼「骨架 + 已完成节 + 新节」编译一次（本地编译免费，不耗 token）。
// 目的：
//   1) 坏节立即暴露——错误精确定位到某一节
//   2) 坏节自动重试/跳过，不拖垮整份文档（此前 \uline 缺宏包、引用不存在文件
//      会让整份文档编译失败）
function compileCheckpoint(head, sectionBodies, checkpointDir, compiler) {
    const tex = head + '\n\n' + sectionBodies.join('\n\n') + '\n\n\\end{document}\n';
    const tmp = join(checkpointDir, 'checkpoint.tex');
    try {
        writeFileSync(tmp, tex, 'utf-8');
        execSync(`"${compiler}" -interaction=nonstopmode -halt-on-error -output-directory="${checkpointDir}" "${tmp}"`, {
            cwd: checkpointDir, timeout: 120000, windowsHide: true, stdio: 'pipe', encoding: 'utf-8'
        });
        return { ok: true, tail: '' };
    } catch (e) {
        const stderr = (e.stderr || '').trim();
        const lines = stderr ? stderr.split('\n') : (e.stdout || '').split('\n');
        return { ok: false, tail: lines.slice(-15).join('\n') };
    }
}

// 把节内声明的 \usepackage 提升到 preamble（去重），返回 { tex, head }
function liftSectionPackages(sectionTex, head, headPkgs) {
    const pkgs = [...sectionTex.matchAll(/\\usepackage(?:\[[^\]]*\])?\{[^}]+\}/g)].map(m => m[0]);
    const cleaned = sectionTex.replace(/^\s*\\usepackage(?:\[[^\]]*\])?\{[^}]+\}\s*$/gm, '').trim();
    let newHead = head;
    const newOnes = pkgs.filter(p => !headPkgs.has(p));
    if (newOnes.length > 0) {
        newHead = head.replace(/\\begin\{document\}/, newOnes.join('\n') + '\n\n\\begin{document}');
        newOnes.forEach(p => headPkgs.add(p));
        console.log(`[Bridge] Promoted ${newOnes.length} usepackage(s): ${newOnes.join(', ')}`);
    }
    return { tex: cleaned, head: newHead };
}

// ─── 分块生成 LaTeX（AgentWrite 式）──────────────────────────────────────
// 超长文档一次生成容易超时/截断（日志里曾出现 "Error: Agent run ended before
// producing a complete result." 的 58 字符残篇）。改为：
//   1) 第一轮 AI 生成 \documentclass + preamble + 全部 \section 骨架
//   2) 之后每个 \section 单独请求 AI 填充内容（每节 1-2KB，几秒完成）
//   3) 拼接成完整源码再编译
async function generateChunkedLatex(description, compiler = 'xelatex') {
    const outlinePrompt = `你是 LaTeX 专家。请根据以下需求，先输出一份完整的「文档骨架」源码。

需求：${description}

骨架要求（只输出 LaTeX 源码）：
- 以 \\documentclass 开头，中文用 ctexart，英文用 article
- preamble 包含常用宏包：amsmath、amssymb、graphicx、booktabs、array、xcolor、listings、fancyhdr、enumitem、titlesec、parskip、hyperref、ulem、multicol、float、subcaption、longtable、multirow、caption、upquote（按需）
- 列出文档所有章节，每个 \\section{章节标题} 后紧跟一行 % 注释说明该节要写什么内容
- 以 \\begin{document} 开头、\\end{document} 结尾，骨架本身可编译
- **所有内容直接写在同一个文档里，绝对不要用 \\input、\\include、\\lstinputlisting 引用任何外部文件**（章节正文由后续步骤逐节补充，骨架阶段只列 \\section 标题）
- **不要使用 \\titleformat、\\titlespacing 等 titlesec 自定义命令**（默认章节样式即可，这些命令极易写坏导致编译失败）
- **不要使用 ① ② ③ 等圈号字符（会缺字），用「1.」「2.」或「第一」替代**
- **只输出 LaTeX 源码，不要任何解释、不要 Markdown 代码块包裹**`

    // ★ 健壮化：骨架请求也加重试（此前骨架 sendChatAndWait 抛错会直接 500，
    //   15:17 实测骨架偶发返回元数据 JSON 被校验拦截，加 2 次重试避免整次任务失败）
    let outline = null;
    for (let attempt = 1; attempt <= 2 && !outline; attempt++) {
        try {
            outline = cleanLatexFence(await sendChatAndWait(outlinePrompt));
            if (typeof outline !== 'string' || outline.trim().length < 50) {
                console.error(`[Bridge] Outline attempt ${attempt} invalid: ${JSON.stringify(String(outline).slice(0, 80))}`);
                outline = null;
            }
        } catch (e) {
            console.error(`[Bridge] Outline attempt ${attempt} failed: ${e.message}`);
        }
    }
    if (!outline) {
        throw new Error('Outline generation failed after retries');
    }
    let sectionTitles = [...outline.matchAll(/\\section\{([^}]+)\}/g)].map(m => m[1].trim());
    console.log(`[Bridge] Chunked mode: outline has ${sectionTitles.length} sections`);

    if (sectionTitles.length === 0) {
        // 骨架没解析出章节，直接退回使用骨架（保持可编译）
        return outline;
    }

    // 临时检查点目录（编译验证用，结束后删除）
    const ckDir = mkdtempSync(join(tmpdir(), 'latex-ck-'));
    try {
        // ── 骨架处理 ──
        // ★ BUG 修复 1：head 只保留 preamble + \begin{document}（剥离骨架中的 \section 占位，
        //   因为每节正文会自带 \section，骨架里再留一份会导致最终文档章节重复）
        // ★ BUG 修复 2：剥离 titlesec 的 \titleformat/\titlespacing 配置（AI 常写坏或被截断，
        //   导致 "Paragraph ended before \ttl@format@i" 崩溃；titlesec 配置只是美化，去掉不影响内容）
        const extractHead = (src) => {
            const eIdx = src.lastIndexOf('\\end{document}');
            const pre = eIdx >= 0 ? src.slice(0, eIdx) : src;
            const firstSec = pre.indexOf('\\section');
            return firstSec >= 0 ? pre.slice(0, firstSec) : pre;
        };
        const stripTitlesecConfig = (src) => {
            const lines = src.split('\n');
            const out = [];
            let skipping = false;
            let depth = 0;
            for (let i = 0; i < lines.length; i++) {
                const line = lines[i];
                if (skipping) {
                    if (line.includes('\\begin{document}')) {
                        // 坏配置吞到了 \begin{document}，必须保留它并停止跳过
                        skipping = false;
                        out.push(line);
                        continue;
                    }
                    const code = line.replace(/%[^\\]*$/, '');
                    for (const ch of code) {
                        if (ch === '{') depth++;
                        else if (ch === '}') depth--;
                    }
                    if (depth <= 0 && /}/.test(line)) {
                        // 括号闭合，但下一行若以 { 开头（titlesec 多行参数）则继续吞
                        const next = (lines[i + 1] || '').trim();
                        if (!next.startsWith('{')) skipping = false;
                    }
                    continue;
                }
                if (/\\title(format|spacing|line)\b/.test(line)) {
                    skipping = true;
                    depth = 0;
                    const code = line.replace(/%[^\\]*$/, '');
                    for (const ch of code) {
                        if (ch === '{') depth++;
                        else if (ch === '}') depth--;
                    }
                    if (depth <= 0 && /}/.test(line)) {
                        const next = (lines[i + 1] || '').trim();
                        if (!next.startsWith('{')) skipping = false;
                    }
                    continue;
                }
                out.push(line);
            }
            return out.join('\n');
        };

        // 自检一次 head（可编译则 ok）
        const selfCheck = (h) => compileCheckpoint(h, [], ckDir, compiler);

        let head = stripTitlesecConfig(extractHead(outline));
        let headPkgs = new Set();
        let skeletonOk = selfCheck(head).ok;
        if (!skeletonOk) {
            // 失败时把错误尾部反馈给 AI（此前不反馈，AI 只能瞎猜）
            const failTail = selfCheck(extractHead(outline)).tail.slice(-300);
            console.error(`[Bridge] Skeleton self-check FAILED, regenerating once\n  └─ tail: ${failTail.split('\n').slice(-6).join('\n')}`);
            try {
                const retryOutline = cleanLatexFence(await sendChatAndWait(
                    outlinePrompt + '\n\n注意：你上次输出的骨架无法编译，错误信息（尾部）：\n' + failTail + '\n请修正后重新输出一份确定可编译的骨架。'
                ));
                const newTitles = [...retryOutline.matchAll(/\\section\{([^}]+)\}/g)].map(m => m[1].trim());
                if (newTitles.length > 0) {
                    outline = retryOutline;
                    sectionTitles = newTitles;
                    console.log(`[Bridge] Skeleton regenerated, now ${sectionTitles.length} sections`);
                }
                // ★ BUG 修复 3：重生成后必须重新提取 head（此前 head 还是旧的坏骨架）并再次自检
                head = stripTitlesecConfig(extractHead(outline));
                skeletonOk = selfCheck(head).ok;
                if (!skeletonOk) {
                    console.error('[Bridge] Regenerated skeleton STILL fails self-check, continuing with stripped head');
                }
            } catch (e) {
                console.error(`[Bridge] Skeleton retry failed: ${e.message}`);
            }
        }
        if (skeletonOk) console.log('[Bridge] Skeleton self-check OK');

        const bodies = [];
        const dropped = [];
        for (let i = 0; i < sectionTitles.length; i++) {
            const title = sectionTitles[i];
            const sectionPrompt = `你是 LaTeX 专家。你正在分块编写一份长文档，现在写其中一节。

文档整体需求：${description}
文档章节列表：${sectionTitles.map((t, idx) => `${idx + 1}. ${t}`).join('；')}

当前任务：编写第 ${i + 1} 节「${title}」。

要求：
- 只输出这一节的 LaTeX 内容（**必须以 \\section{${title}} 精确开头**，标题不要改写、不要加序号、不要放在注释里），不要 \\documentclass、不要 preamble、不要 \\begin{document}/\\end{document}
- 内容充实具体（约 300-800 字正文），可用 \\subsection、列表、表格、代码环境（listings/verbatim）
- 若有代码示例，直接内嵌在 listings 或 verbatim 环境里；**绝对不要用 \\lstinputlisting、\\input、\\include 引用任何外部文件**
- **只用基础命令**（\\section/\\subsection/\\textbf/\\emph/\\texttt/\\begin{itemize}/\\begin{enumerate}/\\begin{tabular}/\\begin{figure}/\\underline 等）；**不要用需要额外宏包的命令**（如 \\uline 请用 \\underline 代替，\\sout、\\uwave、\\overbrace 等一律不用）
- 如果本节确实需要特殊宏包，在节首单独写一行 \\usepackage{宏包名}（会自动提升到 preamble）
- **不要使用 ① ② ③ 等圈号字符，用「1.」「2.」或「第一」替代**
- **只输出 LaTeX，不要任何解释、不要 Markdown 代码块包裹**`;

            let sectionTex = null;
            for (let attempt = 1; attempt <= 2 && !sectionTex; attempt++) {
                try {
                    sectionTex = cleanLatexFence(await sendChatAndWait(sectionPrompt));
                    if (!isValidSectionBody(sectionTex, title)) {
                        console.error(`[Bridge] Section ${i + 1}「${title}」attempt ${attempt} invalid body: ${JSON.stringify(String(sectionTex).slice(0, 80))}`);
                        sectionTex = null;
                    }
                } catch (e) {
                    console.error(`[Bridge] Section ${i + 1}「${title}」attempt ${attempt} failed: ${e.message}`);
                }
            }
            if (!sectionTex) {
                bodies.push(`% [第 ${i + 1} 节「${title}」生成失败，请手动补充]`);
                dropped.push(title);
                console.error(`[Bridge] Section ${i + 1}「${title}」generation failed after retries`);
                continue;
            }

            // 提升宏包 + 再校验 + 检查点编译（骨架 + 已完成节 + 新节）
            const lifted = liftSectionPackages(sectionTex, head, headPkgs);
            head = lifted.head;
            if (!isValidSectionBody(lifted.tex, title)) {
                console.error(`[Bridge] Section ${i + 1}「${title}」invalid after package lift: ${JSON.stringify(lifted.tex.slice(0, 80))}`);
                bodies.push(`% [第 ${i + 1} 节「${title}」内容校验未通过，已跳过，请手动补充]`);
                dropped.push(title);
                continue;
            }
            const ck = compileCheckpoint(head, [...bodies, lifted.tex], ckDir, compiler);
            if (ck.ok) {
                bodies.push(lifted.tex);
                console.log(`[Bridge] Section ${i + 1}/${sectionTitles.length}「${title}」${lifted.tex.length} chars, checkpoint OK | head: ${lifted.tex.slice(0, 50).replace(/\n/g, ' ')}`);
                continue;
            }

            // 检查点编译失败 → 把错误反馈给 AI 重试 1 次
            console.error(`[Bridge] Section ${i + 1}「${title}」checkpoint FAILED, retrying with error feedback`);
            console.error(`[Bridge]   └─ compile error tail:\n${ck.tail.split('\n').map(l => '      ' + l).join('\n')}`);
            // 错误特征像「文件找不到」时，额外强调禁止外部文件引用
            const missingFileHint = /not found|cannot find|No file|找不到|No such file/i.test(ck.tail)
                ? '\n注意：如果错误与「找不到文件」有关，说明你引用了外部文件（\\input/\\include/\\lstinputlisting）。请删除所有外部文件引用，内容全部直接写在当前节里。'
                : '';
            let retryTex = null;
            try {
                retryTex = cleanLatexFence(await sendChatAndWait(
                    sectionPrompt + '\n\n你上一版输出编译失败，错误信息：\n' + ck.tail.slice(-20) + '\n请修正后重新输出这一节。' + missingFileHint
                ));
            } catch (e) {
                console.error(`[Bridge] Section ${i + 1}「${title}」retry failed: ${e.message}`);
            }
            if (retryTex) {
                if (!isValidSectionBody(retryTex, title)) {
                    console.error(`[Bridge] Section ${i + 1}「${title}」retry invalid body: ${JSON.stringify(retryTex.slice(0, 80))}`);
                } else {
                    const lifted2 = liftSectionPackages(retryTex, head, headPkgs);
                    head = lifted2.head;
                    if (!isValidSectionBody(lifted2.tex, title)) {
                        console.error(`[Bridge] Section ${i + 1}「${title}」retry invalid after lift: ${JSON.stringify(lifted2.tex.slice(0, 80))}`);
                    } else {
                        const ck2 = compileCheckpoint(head, [...bodies, lifted2.tex], ckDir, compiler);
                        if (ck2.ok) {
                            bodies.push(lifted2.tex);
                            console.log(`[Bridge] Section ${i + 1}「${title}」retry OK (${lifted2.tex.length} chars)`);
                            continue;
                        }
                        console.error(`[Bridge] Section ${i + 1}「${title}」retry still fails`);
                        console.error(`[Bridge]   └─ retry compile error tail:\n${ck2.tail.split('\n').map(l => '      ' + l).join('\n')}`);
                    }
                }
            }
            bodies.push(`% [第 ${i + 1} 节「${title}」编译未通过，已跳过，请手动补充]`);
            dropped.push(title);
        }

        if (dropped.length > 0) {
            console.warn(`[Bridge] Chunked mode: ${dropped.length} section(s) dropped: ${dropped.join('、')}`);
        }

        // ★ 健壮化：组装前最终防线——过滤任何残留的元数据 JSON 片段
        const finalBodies = bodies.filter(b => !/"(runId|stopReason|sessionKey|agentId|state)"\s*:/.test(b));
        if (finalBodies.length !== bodies.length) {
            console.warn(`[Bridge] Final assembly removed ${bodies.length - finalBodies.length} suspicious fragment(s)`);
        }

        return head + '\n\n' + finalBodies.join('\n\n') + '\n\n\\end{document}\n';
    } finally {
        try { rmSync(ckDir, { recursive: true, force: true }); } catch { /* ignore */ }
    }
}

// ─── 编译前检查外部文件引用 ────────────────────────────────────────────────
// AI 生成的文档可能引用不存在的代码/图片文件（如 \lstinputlisting{code/gpio_poll.c}），
// 直接编译必然失败且报错晦涩。这里提前检查并给出明确提示。
function checkMissingExternalRefs(tex, baseDir) {
    const patterns = [
        { cmd: '\\lstinputlisting', re: /\\lstinputlisting(?:\[[^\]]*\])?\{([^}]+)\}/g },
        { cmd: '\\includegraphics', re: /\\includegraphics(?:\[[^\]]*\])?\{([^}]+)\}/g },
        { cmd: '\\input',            re: /\\input\{([^}]+)\}/g },
        { cmd: '\\include',          re: /\\include\{([^}]+)\}/g },
    ];
    const missing = [];
    for (const { cmd, re } of patterns) {
        for (const m of tex.matchAll(re)) {
            const ref = m[1].trim();
            if (!ref || ref.startsWith('http')) continue;
            // \input{foo} 可能是 foo.tex / foo.sty / foo.cls，都试一下
            const candidates = ref.includes('.') ? [ref] : [`${ref}.tex`, `${ref}.sty`, `${ref}.cls`];
            const found = candidates.some(c => {
                try { return existsSync(join(baseDir, c)); } catch { return false; }
            });
            if (!found) missing.push({ cmd, ref });
        }
    }
    return missing;
}

// ─── HTTP Server ─────────────────────────────────────────────────────────────
function startHttpServer() {
    const server = createServer(async (req, res) => {
        // 🔒 鉴权：除 /health 外所有请求必须携带 x-bridge-token，防止任意网页/进程滥用
        //   （避免 CSRF：网页 JS 无法跨域携带自定义头，且下面移除了 CORS 通配头）
        const authToken = req.headers['x-bridge-token'];
        if (authToken !== BRIDGE_TOKEN) {
            res.writeHead(401, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ error: 'Unauthorized: missing or invalid x-bridge-token header' }));
            return;
        }

        // 不设置任何 CORS 头：浏览器跨域页面将无法读取响应（Unity 原生客户端不受 CORS 限制，不受影响）
        if (req.method === 'OPTIONS') { res.writeHead(405, { 'Content-Type': 'application/json' }); res.end(); return; }

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
            // ★ BUG 修复：必须用 Buffer.concat 收集请求体。
            // 之前用 `body += chunk` 会把每个 TCP 数据块单独转成字符串，
            // 当描述文本较长被拆成多个块时，跨块边界的 UTF-8 中文字符会被截断成乱码
            // （日志里 "???????????,?50?" 就是此问题导致的）。
            const bodyChunks = [];
            req.on('data', chunk => bodyChunks.push(chunk));
            req.on('end', async () => {
                const body = Buffer.concat(bodyChunks).toString('utf-8');
                try {
                    const { source, description, output_path, compiler: requestedCompiler, title, pin_to_desktop, mode } = JSON.parse(body);

                    // ── 获取 LaTeX 源码：直接提供或由 AI 生成 ──
                    let latexSource = source && source.trim() ? source : null;
                    let aiGenerated = false;  // 外部文件引用检查只针对 AI 生成的源码
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
                        aiGenerated = true;
                        // 超长文档自动切分块模式（也可通过 mode: 'chunked' 显式指定）
                        const wantChunked = (mode === 'chunked')
                            || /超长|长文档|分块|分段|长篇幅|多章节|几十页|数十页|100页|50页|50 页/i.test(description);
                        console.log(`[Bridge] Generating LaTeX via AI for: "${description.substring(0, 80)}..."${wantChunked ? ' [chunked]' : ''}`);
                        const t0 = Date.now();
                        let aiResponse, elapsed;
                        if (wantChunked) {
                            aiResponse = await generateChunkedLatex(description, requestedCompiler || 'xelatex');
                            elapsed = Date.now() - t0;
                        } else {
                            const prompt = `你是一个 LaTeX 专家。请根据以下需求生成完整的 LaTeX 文档源码。

需求：${description}

要求：
- 输出纯 LaTeX 源码（以 \\documentclass 开头）
- 中文文档用 ctexart 或 xeCJK，英文用 article
- 包含 \\begin{document} 和 \\end{document}
- 结构完整、排版美观
- 若有代码示例，直接内嵌在 listings/verbatim 环境，**不要引用外部代码文件**（如 \\lstinputlisting）
- **只输出 LaTeX 源码，不要任何解释、不要 Markdown 代码块包裹**`;
                            aiResponse = await sendChatAndWait(prompt);
                            elapsed = Date.now() - t0;
                        }
                        latexSource = cleanLatexFence(aiResponse);
                        console.log(`[Bridge] AI generated ${latexSource.length} chars of LaTeX in ${elapsed >= 1000 ? (elapsed/1000).toFixed(1)+'s' : elapsed+'ms'}${wantChunked ? ' (chunked)' : ''}`);

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

                    // ── 编译前检查外部文件引用（仅 AI 生成的源码）──
                    // AI 常引用不存在的代码/图片文件（如 \lstinputlisting{code/gpio_poll.c}），
                    // 直接编译必然失败且报错晦涩，提前检查并给出可操作提示。
                    if (aiGenerated) {
                        const missingRefs = checkMissingExternalRefs(latexSource, outDir);
                        if (missingRefs.length > 0) {
                            const detail = missingRefs.map(r => `${r.cmd}{${r.ref}}`).join('、');
                            console.error(`[Bridge] Missing external refs: ${detail}`);
                            res.writeHead(422, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({
                                success: false,
                                error: `AI 生成的文档引用了不存在的文件：${detail}。请重新描述需求，明确要求「代码示例直接内嵌在 listings/verbatim 环境，不要引用外部代码文件」；或自行创建这些文件后重试。`,
                                missing_refs: missingRefs,
                                tex_path: texPath
                            }));
                            return;
                        }
                    }

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
                    // ★ BUG 修复：xelatex 的 putenv 在 Windows 上无法处理含中文/非 ASCII 的输出目录
                    //   （TEXMF_OUTPUT_DIRECTORY=中文路径 → "fatal: putenv(...)"）。
                    //   因此先复制 .tex 到 ASCII 临时目录编译，成功后再把产物拷回中文输出目录。
                    const COMPILE_TIMEOUT_MS = parseInt(process.env.LATEX_TIMEOUT_MS || '300000', 10);
                    const compileDir = mkdtempSync(join(tmpdir(), 'latex-main-'));
                    const compileTex = join(compileDir, 'main.tex');
                    copyFileSync(texPath, compileTex);
                    const compileArgs = `-interaction=nonstopmode -halt-on-error -output-directory="${compileDir}" "${compileTex}"`;
                    const baseNoExt = join(outDir, basename(texPath, '.tex'));
                    const logPath = baseNoExt + '.log';
                    const compilePdf = join(compileDir, 'main.pdf');
                    const compileLog = join(compileDir, 'main.log');

                    for (let pass = 1; pass <= 2; pass++) {
                        try {
                            execSync(`"${compilerPath}" ${compileArgs}`, {
                                cwd: compileDir, timeout: COMPILE_TIMEOUT_MS, windowsHide: true, stdio: 'pipe', encoding: 'utf-8',
                            });
                        } catch (e) {
                            // 提取错误信息：优先读 .log 文件尾部（比 stderr 更完整）
                            const stderr = (e.stderr || '').trim();
                            const lines = stderr ? stderr.split('\n') : (e.stdout || '').split('\n');
                            let tail = lines.slice(-10).join('\n').trim();
                            try {
                                if (existsSync(compileLog)) {
                                    const logLines = readFileSync(compileLog, 'utf-8').split('\n');
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

                            // 失败时也把 .log 拷回中文目录（便于用户查看），并清理临时目录
                            try {
                                if (existsSync(compileLog)) copyFileSync(compileLog, logPath);
                            } catch { /* ignore */ }
                            try { rmSync(compileDir, { recursive: true, force: true }); } catch { /* ignore */ }

                            res.writeHead(500, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({
                                success: false, error: friendly,
                                compiler: compilerPath, log_tail: tail, log_path: logPath
                            }));
                            return;
                        }
                    }

                    // ── 编译成功：把产物从 ASCII 临时目录拷回中文输出目录 ──
                    try {
                        copyFileSync(compilePdf, baseNoExt + '.pdf');
                        copyFileSync(compileLog, logPath);
                        copyFileSync(compileTex, texPath);
                    } catch (copyErr) {
                        console.error(`[Bridge] Copy compile output failed: ${copyErr.message}`);
                    } finally {
                        try { rmSync(compileDir, { recursive: true, force: true }); } catch { /* ignore */ }
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
