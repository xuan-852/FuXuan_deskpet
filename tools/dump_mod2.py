# -*- coding: utf-8 -*-
"""查看 .tex 模块2 段落完整内容（257-360 行附近），判断内容来源。"""
import io

p = r'D:\DesktopPetData\Documents\乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfnh1df\乐鑫嵌入式国赛-ESP32-S3学习文档.tex'
lines = io.open(p, encoding='utf-8').read().split('\n')

# 模块2 从 257 行到模块3（360行）
print("=== 模块2 内容 257-360 行 ===")
for i in range(256, 360):
    print(f"{i+1}: {lines[i]}")
