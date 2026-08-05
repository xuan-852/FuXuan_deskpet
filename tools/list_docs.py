# -*- coding: utf-8 -*-
"""列出所有产物目录时间戳 + .tex 模块5区域(16052-18753)内容。"""
import io, os

base = r'D:\DesktopPetData\Documents'
print("=== Documents 下所有目录 ===")
for name in sorted(os.listdir(base)):
    full = os.path.join(base, name)
    if os.path.isdir(full):
        ts = os.path.getmtime(full)
        import datetime
        print(f"  {name}  {datetime.datetime.fromtimestamp(ts)}")

p = os.path.join(base, '乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfnh1df', '乐鑫嵌入式国赛-ESP32-S3学习文档.tex')
print(f"\n.tex 修改时间: {datetime.datetime.fromtimestamp(os.path.getmtime(p))}")

s = io.open(p, encoding='utf-8').read()
print("\n=== 模块5区域 (offset 16052-18753) ===")
print(s[16052:18753][:3000])
