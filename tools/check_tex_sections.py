# -*- coding: utf-8 -*-
"""对比 .tex 各节实际长度与 bridge 日志报告的 chars。"""
import io, re, sys

p = r'D:\DesktopPetData\Documents\乐鑫嵌入式国赛-ESP32-S3学习文档_20260805_msfnh1df\乐鑫嵌入式国赛-ESP32-S3学习文档.tex'
s = io.open(p, encoding='utf-8').read()

# 每节起始（\section 级别）
sec_starts = [(m.start(), m.group(1)) for m in re.finditer(r'\\section\{([^}]+)\}', s)]
starts = [x[0] for x in sec_starts] + [len(s)]

print("== .tex 各 \section 实际长度 ==")
for i in range(len(starts) - 1):
    seg = s[starts[i]:starts[i + 1]]
    title = sec_starts[i][1]
    print(f"  [{len(seg):5d}] {title}")

print("\n== 日志报告的 chars（最后一次运行）==")
log_chars = [
    ("环境与开发板总览", 1967),
    ("模块1：GPIO 输入输出（按键轮询与中断）", 3536),
    ("模块2：定时器 GPTimer（定时中断）", 140),
    ("模块3：PAD\\_TOUCH 触摸（导电片输入）", 2991),
    ("模块4：ADC 采样", 2978),
    ("模块5：UART 串口通信", 3179),
    ("模块6：LEDC PWM（蜂鸣器与普通 LED 调光）", 2699),
    ("模块7：WS2812 可编程全彩 LED", 3130),
    ("模块8：FreeRTOS 任务基础（贯穿所有模块）", 2800),
    ("比赛速查表", 2609),
]
for t, c in log_chars:
    print(f"  [{c:5d}] {t}")

print("\n== 模块6 关键词在 .tex 中的位置 ==")
for kw in ['LEDC', 'PWM', '蜂鸣器', '调光']:
    positions = [s.count(kw)]
    print(f"  {kw}: {positions[0]} 次")
