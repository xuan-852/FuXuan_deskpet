#!/usr/bin/env node
/**
 * Bridge approval recovery regression.
 *
 * Starts openclaw_bridge.js against a temporary fake Gateway package and verifies:
 *   1. failed resolve attempts retain pendingApproval for retry;
 *   2. a later successful retry clears it;
 *   3. concurrent approval POSTs yield one Gateway decision and one 409 conflict.
 *
 * The child process uses isolated tokens/data and is always terminated by this test.
 */
'use strict';

const assert = require('assert');
const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const ROOT = path.resolve(__dirname, '..', '..');
const BRIDGE_SOURCE = path.join(ROOT, 'code', 'desktop_unity', 'openclaw_bridge.js');
const TOKEN = 'bridge-approval-test-token';
const GATEWAY_TOKEN = 'gateway-approval-test-token';

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

async function waitFor(predicate, timeoutMs = 10000, intervalMs = 50) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (await predicate()) return;
        await sleep(intervalMs);
    }
    throw new Error('Timed out waiting for bridge state');
}

async function request(port, method, route, body) {
    const response = await fetch(`http://127.0.0.1:${port}${route}`, {
        method,
        headers: {
            'x-bridge-token': TOKEN,
            ...(body == null ? {} : { 'content-type': 'application/json' }),
        },
        body: body == null ? undefined : JSON.stringify(body),
    });
    const text = await response.text();
    let json;
    try { json = JSON.parse(text); } catch { json = { raw: text }; }
    return { status: response.status, body: json };
}

function writeFakeGateway(root) {
    fs.mkdirSync(path.join(root, 'dist'), { recursive: true });
    fs.writeFileSync(path.join(root, 'package.json'), JSON.stringify({ type: 'module' }));
    fs.writeFileSync(path.join(root, 'dist', 'gateway-chat-test.js'), `
let resolveCalls = 0;
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
export const GatewayChatClient = {
  async connect() {
    const client = {
      onEvent: null,
      client: {
        async request(method, payload) {
          if (method === 'chat.send') {
            setTimeout(() => client.onEvent?.({
              event: 'agent',
              payload: {
                stream: 'approval',
                sessionKey: payload.sessionKey,
                data: {
                  phase: 'requested',
                  approvalId: 'approval-test-1',
                  approvalSlug: 'exec',
                  command: 'echo approval-test',
                },
              },
            }), 20);
            return { runId: 'fake-run' };
          }
          if (method === 'exec.approval.resolve') {
            resolveCalls += 1;
            if (process.env.FAKE_APPROVAL_MODE === 'fail-once' && resolveCalls <= 2) {
              throw new Error('fake resolve failure');
            }
            if (process.env.FAKE_APPROVAL_MODE === 'delay') await delay(300);
            return { ok: true };
          }
          throw new Error('unexpected RPC: ' + method);
        },
      },
      async resolvePluginApproval() {
        resolveCalls += 1;
        if (process.env.FAKE_APPROVAL_MODE === 'fail-once' && resolveCalls <= 2) {
          throw new Error('fake plugin resolve failure');
        }
        if (process.env.FAKE_APPROVAL_MODE === 'delay') await delay(300);
        return { ok: true };
      },
      start() {},
      async waitForReady() {},
      async subscribeSessionEvents() {},
      stop() {},
    };
    return client;
  },
};
`);
}

async function startBridge(mode) {
    const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'fuxuan-bridge-approval-'));
    const fakeGateway = path.join(tempRoot, 'fake-gateway');
    const dataRoot = path.join(tempRoot, 'data');
    fs.mkdirSync(dataRoot, { recursive: true });
    writeFakeGateway(fakeGateway);
    // The repository root is CommonJS, while the bridge intentionally uses ESM syntax.
    // Copy only the source under a temporary package boundary so the child starts exactly
    // the same code without changing the production package metadata.
    const bridgeDir = path.join(tempRoot, 'bridge');
    fs.mkdirSync(bridgeDir, { recursive: true });
    const bridgePath = path.join(bridgeDir, 'openclaw_bridge.js');
    fs.writeFileSync(path.join(bridgeDir, 'package.json'), JSON.stringify({ type: 'module' }));
    fs.copyFileSync(BRIDGE_SOURCE, bridgePath);
    const port = 19000 + Math.floor(Math.random() * 1000);
    const child = spawn(process.execPath, [bridgePath], {
        cwd: ROOT,
        env: {
            ...process.env,
            BRIDGE_PORT: String(port),
            BRIDGE_TOKEN: TOKEN,
            GATEWAY_TOKEN,
            OPENCLAW_NODE_MODULES: fakeGateway,
            FU_XUAN_DATA: dataRoot,
            FAKE_APPROVAL_MODE: mode,
        },
        stdio: ['ignore', 'pipe', 'pipe'],
        windowsHide: true,
    });
    let output = '';
    child.stdout.on('data', chunk => { output += chunk.toString(); });
    child.stderr.on('data', chunk => { output += chunk.toString(); });
    try {
        await waitFor(async () => {
            try {
                const result = await fetch(`http://127.0.0.1:${port}/health`);
                return result.status === 200;
            } catch { return false; }
        });
        return { child, port, tempRoot, getOutput: () => output };
    } catch (error) {
        child.kill();
        throw new Error(`${error.message}\n${output}`);
    }
}

async function waitForPending(port) {
    let taskId = null;
    const submitted = await request(port, 'POST', '/task', { task: 'approval recovery test' });
    assert.strictEqual(submitted.status, 200, JSON.stringify(submitted.body));
    taskId = submitted.body.task_id;
    await waitFor(async () => {
        const result = await request(port, 'GET', `/task/${taskId}`);
        return result.body.pendingApproval?.id === 'approval-test-1';
    });
    return taskId;
}

async function runFailureAndRetryCase() {
    const bridge = await startBridge('fail-once');
    try {
        const taskId = await waitForPending(bridge.port);
        const failed = await request(bridge.port, 'POST', `/task/${taskId}/approve`, { decision: 'allow-once' });
        assert.strictEqual(failed.status, 500, JSON.stringify(failed.body));
        const retained = await request(bridge.port, 'GET', `/task/${taskId}`);
        assert.strictEqual(retained.body.pendingApproval?.id, 'approval-test-1', JSON.stringify(retained.body));

        const retried = await request(bridge.port, 'POST', `/task/${taskId}/approve`, { decision: 'allow-once' });
        assert.strictEqual(retried.status, 200, JSON.stringify(retried.body));
        await waitFor(async () => {
            const result = await request(bridge.port, 'GET', `/task/${taskId}`);
            return !result.body.pendingApproval;
        });
    } finally {
        bridge.child.kill();
        fs.rmSync(bridge.tempRoot, { recursive: true, force: true });
    }
}

async function runConcurrentCase() {
    const bridge = await startBridge('delay');
    try {
        const taskId = await waitForPending(bridge.port);
        const results = await Promise.all([
            request(bridge.port, 'POST', `/task/${taskId}/approve`, { decision: 'allow-once' }),
            request(bridge.port, 'POST', `/task/${taskId}/approve`, { decision: 'allow-once' }),
        ]);
        const statuses = results.map(result => result.status).sort((a, b) => a - b);
        assert.deepStrictEqual(statuses, [200, 409], JSON.stringify(results));
        await waitFor(async () => {
            const result = await request(bridge.port, 'GET', `/task/${taskId}`);
            return !result.body.pendingApproval;
        });
    } finally {
        bridge.child.kill();
        fs.rmSync(bridge.tempRoot, { recursive: true, force: true });
    }
}

(async () => {
    await runFailureAndRetryCase();
    await runConcurrentCase();
    console.log('[PASS] bridge approval failure retention, retry, and concurrency');
})().catch(error => {
    console.error('[FAIL] bridge approval regression:', error.stack || error);
    process.exitCode = 1;
});
