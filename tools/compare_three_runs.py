# -*- coding: utf-8 -*-
"""决定性对比：run#4(msfmjvbu) / run#5(msfmp8qe) / run#7(msfnh1df) 三份 .tex 的结构与模块2/模块6内容指纹。"""
import io, re, hashlib

base = r'D:\DesktopPetData\Documents'
runs = {
    'run#4(msfmjvbu)': f'{base}\\乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfmjvbu\\乐鑫嵌入式国赛-ESP32-S3学习文档.tex',
    'run#5(msfmp8qe)': f'{base}\\乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfmp8qe\\乐鑫嵌入式国赛-ESP32-S3学习文档.tex',
    'run#7(msfnh1df)': f'{base}\\乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfnh1df\\乐鑫嵌入式国赛-ESP32-S3学习文档.tex',
}

def sections(s):
    """返回 [(title, start, end)]"""
    out = []
    for m in re.finditer(r'\\section\{', s):
        start = m.start()
        eb = s.index('}', start)
        title = s[start+9:eb]
        out.append([title, start, None])
    for i in range(len(out)-1):
        out[i][2] = out[i+1][1]
    if out:
        out[-1][2] = s.rfind('\\end{document}')
    return out

def md5(t):
    return hashlib.md5(t.encode('utf-8')).hexdigest()[:10]

for name, p in runs.items():
    s = io.open(p, encoding='utf-8').read()
    secs = sections(s)
    print(f"\n===== {name}  总长 {len(s)} =====")
    print(f"  \\section 数量: {len(secs)}")
    for title, st, en in secs:
        body = s[st:en]
        print(f"  [{st:>6}-{en:>6}] {title}  len={en-st}  md5={md5(body)}")
    # 模块2与模块6内容关键词检查
    mod2 = re.search(r'\\section\{模块2[^}]*\}', s)
    mod6 = re.search(r'\\section\{模块6[^}]*\}', s)
    print(f"  模块2 存在: {bool(mod2)}, 模块6 存在: {bool(mod6)}")
    for kw in ['GPTimer', 'gptimer_', 'LEDC', 'ledc_', '蜂鸣器']:
        print(f"  关键词 {kw}: {s.count(kw)} 处")
