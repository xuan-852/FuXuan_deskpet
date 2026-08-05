// 测试 isValidSectionBody 边界情况（与 openclaw_bridge.js 中实现保持一致）
function normalizeTitle(t) {
    return (t || '').replace(/\\_/g, '_').replace(/\\[a-zA-Z]+\b/g, '').replace(/\s+/g, '').toLowerCase();
}
function isValidSectionBody(text, expectedTitle) {
    if (typeof text !== 'string' || !text.trim()) return false;
    if (/"(runId|stopReason|sessionKey|agentId|state)"\s*:/.test(text)) return false;
    if (text.trim().length < 30) return false;
    if (!/\\section\s*\*?\s*\{/.test(text)) return false;
    const m = text.match(/\\section\s*\*?\s*\{([^}]*)\}/);
    if (!m) return false;
    const bodyTitle = normalizeTitle(m[1]);
    const expTitle = normalizeTitle(expectedTitle);
    if (bodyTitle === expTitle) return true;
    if (expTitle.length >= 4 && bodyTitle.includes(expTitle)) return true;
    if (bodyTitle.length >= 4 && expTitle.includes(bodyTitle)) return true;
    return false;
}

let pass = 0, fail = 0;
function check(name, actual, expected) {
    if (actual === expected) { pass++; console.log(`PASS: ${name}`); }
    else { fail++; console.log(`FAIL: ${name} (got ${actual}, want ${expected})`); }
}

const meta = '{"runId":"209b9af0","sessionKey":"agent:main:main","stopReason":"stop"}';
check('元数据JSON拦截', isValidSectionBody(meta, '模块2：定时器 GPTimer（定时中断）'), false);
check('正常节放行', isValidSectionBody('\\section{模块2：定时器 GPTimer（定时中断）}\n\n定时器是一种能自己数时间的硬件，白话讲就像一个会自动响的闹钟...', '模块2：定时器 GPTimer（定时中断）'), true);
check('错位拦截', isValidSectionBody('\\section{模块5：UART 串口通信}\n\nUART 内容...', '模块2：定时器 GPTimer（定时中断）'), false);
check('转义差异放行', isValidSectionBody('\\section{模块3：PAD_TOUCH 触摸（导电片输入）}\n\n触摸内容...', '模块3：PAD\\_TOUCH 触摸（导电片输入）'), true);
check('短内容拦截', isValidSectionBody('\\section{x}', 'x'), false);
check('无section拦截', isValidSectionBody('定时器内容 300字正文...', '模块2：定时器 GPTimer（定时中断）'), false);
check('元数据+真实节混合拦截', isValidSectionBody('{"runId":"x"}\n\\section{模块2：定时器 GPTimer（定时中断）}\n\n内容...', '模块2：定时器 GPTimer（定时中断）'), false);
check('前导空行放行', isValidSectionBody('\n\n\\section{模块6：LEDC PWM（蜂鸣器与普通 LED 调光）}\n\nLEDC 内容...', '模块6：LEDC PWM（蜂鸣器与普通 LED 调光）'), true);

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail > 0 ? 1 : 0);
