# -*- coding: utf-8 -*-
"""精确验证 .tex 结构：\section 列表、各节偏移、总长。"""
import io, re

p = r'D:\DesktopPetData\Documents\乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfnh1df\乐鑫嵌入式国赛-ESP32-S3学习文档.tex'
s = io.open(p, encoding='utf-8').read()
print(f"总长: {len(s)}")

# 所有 \section 出现位置
for m in re.finditer(r'\\section\{', s):
    start = m.start()
    end_brace = s.index('}', start)
    title = s[start:end_brace+1]
    print(f"  offset {start}: {title}")

# \end{document} 位置
ed = s.rfind('\\end{document}')
print(f"\\end{{document}} 在 {ed}, 其后 {len(s)-ed} chars")

# head = 第一个 \section 前
first = s.index('\\section{')
print(f"第一个 \\section 在 {first} (head 长度 {first})")
print(f"head 尾部: ...{s[first-60:first]!r}")
