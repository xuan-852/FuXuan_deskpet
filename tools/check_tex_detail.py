# -*- coding: utf-8 -*-
"""精确核对 .tex 的构成：head + 各节 + end，与日志对比。"""
import io, re

p = r'D:\DesktopPetData\Documents\乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfnh1df\乐鑫嵌入式国赛-ESP32-S3学习文档.tex'
s = io.open(p, encoding='utf-8').read()

print("总长:", len(s))

# 找 \begin{document} 位置（head 应该到第一个 \section 前）
m_doc = re.search(r'\\begin\{document\}', s)
print("begin{document} at", m_doc.start() if m_doc else None)

# 所有 \section 的偏移
secs = [(m.start(), m.group(1)) for m in re.finditer(r'\\section\{([^}]+)\}', s)]
print(f"\n共 {len(secs)} 个 \\section")
for off, t in secs:
    print(f"  offset {off:6d}  {t}")

# head = 第一个 section 之前
head = s[:secs[0][0]] if secs else s
print(f"\nhead 长度: {len(head)}")
print(f"head 开头: {head[:80]!r}")
print(f"head 结尾: {head[-80:]!r}")

# 检查 body 部分（第一个 section 到 end{document}）
end_off = s.rfind('\\end{document}')
print(f"\nend{{document}} at {end_off}")
print(f"end 之后剩余: {len(s) - end_off} chars")

# 检查是否每个 section 前都有独立标题，各节 body 从 section 开始
bodies_total = end_off - secs[0][0]
print(f"bodies 总长（第一个 section 到 end）: {bodies_total}")

# 搜 LEDC 上下文，看是否真的是表格提及
print("\nLEDC 出现的上下文:")
for m in re.finditer('LEDC', s):
    line_no = s.count('\n', 0, m.start()) + 1
    line_start = s.rfind('\n', 0, m.start()) + 1
    line_end = s.find('\n', m.start())
    line = s[line_start:line_end if line_end != -1 else len(s)]
    print(f"  line {line_no}: {line.strip()[:90]}")
