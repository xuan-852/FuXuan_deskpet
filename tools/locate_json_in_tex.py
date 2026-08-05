# -*- coding: utf-8 -*-
"""定位 .tex 中所有 runId/JSON 元数据片段，dump 每个 section 区域首尾。"""
import io, re

p = r'D:\DesktopPetData\Documents\乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfnh1df\乐鑫嵌入式国赛-ESP32-S3学习文档.tex'
s = io.open(p, encoding='utf-8').read()

print("=== 所有 runId/JSON 元数据出现位置 ===")
for m in re.finditer(r'\{[^}]*"runId"[^}]*\}', s):
    print(f"  offset {m.start()}: {m.group()[:150]}")
    print(f"    前后: ...{s[max(0,m.start()-80):m.start()]!r} ||| {s[m.end():m.end()+80]!r}")

print(f"\nrunId 出现次数: {s.count('runId')}")
print(f"stopReason 出现次数: {s.count('stopReason')}")
print(f"sessionKey 出现次数: {s.count('sessionKey')}")

# 每个 section 区域首尾
print("\n=== 每个 section 区域（首 50 + 尾 50）===")
secs = [(m.start(), s.index('}', m.start())) for m in re.finditer(r'\\section\{', s)]
for i, (st, _) in enumerate(secs):
    en = secs[i+1][0] if i+1 < len(secs) else s.rfind('\\end{document}')
    body = s[st:en]
    title = re.search(r'\\section\{([^}]+)\}', s[st:]).group(1)
    print(f"\n[{st}-{en}] {title} (len={en-st})")
    print(f"  头: {body[:80]!r}")
    print(f"  尾: {body[-80:]!r}")
