# -*- coding: utf-8 -*-
"""打印最后一次运行（#7）的完整原始日志。"""
import io, re

log = r'C:\Users\25295\.pm2\logs\openclaw-bridge-out.log'
s = io.open(log, encoding='utf-8', errors='replace').read()

starts = [m.start() for m in re.finditer(r'Generating LaTeX via AI', s)]
st = starts[-1]
seg = s[st:]
print(f"=== 最后一次运行 (位置 {st}) ===")
print(seg[:6000])
