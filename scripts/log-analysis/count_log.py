# -*- coding: utf-8 -*-
import io, sys, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

path = r'C:\Users\25295\AppData\LocalLow\DefaultCompany\desktop pet\Player.log'
with open(path, 'r', encoding='utf-8', errors='replace') as f:
    lines = f.readlines()

text = ''.join(lines)
out = ['总行数 = %d' % len(lines)]

pats = {
    '翻译成功': '翻译成功',
    '翻译失败/API失败': '翻译失败',
    'API 请求失败': 'API 请求失败',
    '本地模板命中': '本地模板命中',
    '表情模板命中': '表情本地模板命中',
    '决策:': '决策:',
    '镜鉴(GLM验证)': '镜鉴',
    '问候生成完成': '问候生成完成',
    '意图分类': '意图',
    '反思': '反思',
    '施法': '施法',
    '闲话生成完成': '闲话生成完成',
    '天气语录': '天气语录',
    '演武记录': '演武记录',
    '自评': '自评',
}
for k, pat in pats.items():
    out.append('%s = %d' % (k, text.count(pat)))

with open(r'd:\Unity\projects\Desktop_per_pro\scripts\log-analysis\count_out.txt', 'w', encoding='utf-8') as f:
    f.write('\n'.join(out))
print('done')
