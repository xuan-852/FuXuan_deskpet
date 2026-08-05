# -*- coding: utf-8 -*-
"""对比 msfnh1df（第4次）与 msfmp8qe（第2次）的 .tex 内容。"""
import io

p4 = r'D:\DesktopPetData\Documents\乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfnh1df\乐鑫嵌入式国赛-ESP32-S3学习文档.tex'
p2 = r'D:\DesktopPetData\Documents\乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfmp8qe\乐鑫嵌入式国赛-ESP32-S3学习文档.tex'

s4 = io.open(p4, encoding='utf-8').read()
s2 = io.open(p2, encoding='utf-8').read()

print("第4次 .tex 总长:", len(s4))
print("第2次 .tex 总长:", len(s2))
print()

# 第4次模块2 开头 300 chars
import re
m4 = re.search(r'\\section\{模块2[^\n]*\n', s4)
print("=== 第4次 模块2 开头 260 chars ===")
if m4:
    print(repr(s4[m4.start():m4.start()+260]))
print()

m2 = re.search(r'\\section\{模块2[^\n]*\n', s2)
print("=== 第2次 模块2 开头 260 chars ===")
if m2:
    print(repr(s2[m2.start():m2.start()+260]))
print()

# 检查第2次 .tex 是否含模块6
print("第2次含模块6 section:", '模块6' in s2 and 'LEDC' in s2)
for m in re.finditer(r'\\section\{([^}]+)\}', s2):
    print("  第2次:", m.group(1))
