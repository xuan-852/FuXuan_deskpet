# -*- coding: utf-8 -*-
"""决定性检查：日志中所有 'Generating LaTeX via AI' 及之后的 Section 序列，数清共几次运行。"""
import io, re

log = r'C:\Users\25295\.pm2\logs\openclaw-bridge-out.log'
s = io.open(log, encoding='utf-8', errors='replace').read()

# 找所有运行起点
starts = [m.start() for m in re.finditer(r'Generating LaTeX via AI', s)]
print(f"共 {len(starts)} 次 'Generating LaTeX via AI' 记录")

for idx, st in enumerate(starts):
    end = starts[idx + 1] if idx + 1 < len(starts) else len(s)
    seg = s[st:end]
    # 该段内的 Section 行
    secs = re.findall(r'Section (\d+)/10「([^」]+)」(\d+) chars', seg)
    totals = re.findall(r'AI generated (\d+) chars of LaTeX in ([\d.]+)s', seg)
    oks = len(re.findall(r'checkpoint OK', seg))
    print(f"\n--- 运行 #{idx + 1} (位置 {st}, 长度 {end - st}) ---")
    print(f"  Section 行数: {len(secs)}")
    for n, t, c in secs:
        print(f"    S{n}: {t} = {c} chars")
    print(f"  checkpoint OK 数: {oks}")
    print(f"  AI generated: {totals}")
    if 'Skeleton self-check' in seg:
        print(f"  Skeleton: {'OK' if 'Skeleton self-check OK' in seg else 'FAILED'}")
    if 'dropped' in seg.lower():
        print(f"  dropped 标记: {re.findall(r'dropped[^\n]*', seg)}")
