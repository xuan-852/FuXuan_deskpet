/**
 * 诊断：直连 Gateway 发一个最小 chat.send，打印所有事件的原始结构
 * 用法: node tools/diag_gateway_raw.js "测试：生成中文LaTeX文档《测试》。共1章：1.背景介绍"
 */
import { GatewayChatClient } from 'file:///D:/openclaw/node_modules/openclaw/dist/gateway-chat-BW6uyvQL.js';

const GATEWAY_URL   = process.env.GATEWAY_URL   || 'ws://127.0.0.1:18789';
const GATEWAY_TOKEN = process.env.GATEWAY_TOKEN || '367be203e32a4da345a6859d08298071dc058b78d4bcb203';
const SESSION_KEY   = process.env.SESSION_KEY   || 'agent:main:main';

const query = process.argv[2] || '你好';

let client = null;
try {
    client = await GatewayChatClient.connect({ url: GATEWAY_URL, token: GATEWAY_TOKEN });
    client.onEvent = (evt) => {
        const p = evt.payload || {};
        console.log('=== EVENT ===', JSON.stringify(evt).slice(0, 4000));
        if (p && p.stopReason) {
            console.log('--- FINAL detected, msg keys:', p.message ? Object.keys(p.message) : '(no message)');
        }
    };
    client.onDisconnected = (r) => console.log('DISCONNECTED:', r);
    client.start();
    await client.waitForReady();
    console.log('CONNECTED, sending chat.send...');
    const res = await client.client.request('chat.send', {
        sessionKey: SESSION_KEY,
        message: query,
        timeoutMs: 120000,
        idempotencyKey: 'diag-' + Date.now(),
    });
    console.log('=== chat.send RPC return ===', JSON.stringify(res).slice(0, 2000));
    // 等 final 事件
    await new Promise(r => setTimeout(r, 15000));
    console.log('DONE waiting 15s');
    process.exit(0);
} catch (e) {
    console.error('DIAG ERROR:', e.message);
    process.exit(1);
}
