#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
find_panel.py — 在完整窗口截图中自动定位聊天面板（紫色深空背景）的边界。

用法:
    python find_panel.py <shot.png>
输出（规范化，一行）:
    PANEL <x> <y> <w> <h>        # 面板边界（窗口坐标系）
    NONE                          # 未检测到面板

原理：面板背景为深紫（RGB 约 (12,10,25)~(45,32,70)，蓝>红），
对每列/每行统计紫色像素密度，取连续高密度区间的合并结果。
"""
import sys
from PIL import Image


def is_panel_px(c):
    r, g, b = c
    return b > r and (b - r) > 8 and b > 20 and r < 90 and g < 80


def find_runs(density, threshold):
    runs = []
    start = None
    for i, v in enumerate(density):
        if v > threshold and start is None:
            start = i
        elif v <= threshold and start is not None:
            runs.append([start, i - 1])
            start = None
    if start is not None:
        runs.append([start, len(density) - 1])
    return runs


def merge(runs, gap=20):
    if not runs:
        return []
    merged = [list(runs[0])]
    for s, e in runs[1:]:
        if s - merged[-1][1] <= gap:
            merged[-1][1] = e
        else:
            merged.append([s, e])
    return merged


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: find_panel.py <shot.png>", file=sys.stderr)
        return 2
    path = sys.argv[1]
    img = Image.open(path).convert("RGB")
    w, h = img.size
    px = img.load()

    step = 3
    col_density = []
    for x in range(w):
        col_density.append(sum(1 for y in range(0, h, step) if is_panel_px(px[x, y])))
    row_density = []
    for y in range(h):
        row_density.append(sum(1 for x in range(0, w, step) if is_panel_px(px[x, y])))

    # 阈值：低阈值（约 2% 行列密度）检测【完整】面板。
    # ★ 早期用 12% 只抓到"气泡密集区"，大窗口下会漏掉面板下半部
    #   （输入框/用户气泡区域），导致裁剪不完整、用户气泡被切掉。
    col_th = max(3, int(h / step * 0.02))
    row_th = max(3, int(w / step * 0.02))
    col_runs = merge(find_runs(col_density, col_th))
    row_runs = merge(find_runs(row_density, row_th))

    if not col_runs or not row_runs:
        print("NONE")
        return 0

    # 取最宽列区间 + 最高行区间（面板是最大连续紫色块）
    col = max(col_runs, key=lambda r: r[1] - r[0])
    row = max(row_runs, key=lambda r: r[1] - r[0])
    x, x1 = col
    y, y1 = row
    print(f"PANEL {x} {y} {x1 - x + 1} {y1 - y + 1}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
