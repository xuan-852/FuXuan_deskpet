#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_user_bubble.py — 检测聊天面板中是否已渲染"用户蓝色气泡"。

用法:
    python check_user_bubble.py <pid> [--shot <tmp.png>]
输出（规范化，一行）:
    USER_BUBBLE <bx> <by> <bw> <bh>   # 面板内蓝色气泡 bbox（可空，未检测到时）
    NO_BUBBLE <reason>                # 未检测到（面板未找到 / 无蓝色像素）

★ 用途：pet_chat_test.ps1 的"用户气泡特写"轮询器。
   注入消息后 ChatManager 若处于 _isWaiting（如 AutoChat 正在处理），
   消息会入队、不会立即加入 History → 用户气泡尚未渲染。
   本脚本每轮截图 → 定位面板 → 扫描用户蓝色气泡，一旦出现即可特写，
   彻底规避"固定等 N 秒结果气泡还没渲染/已被长回复顶出"两类问题。
"""
import sys
import ctypes
import ctypes.wintypes as wt

from PIL import Image

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32


def capture_window(hwnd: int, out_path: str) -> bool:
    rect = wt.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        print(f"ERROR: GetWindowRect failed hwnd={hwnd}", file=sys.stderr)
        return False
    w = rect.right - rect.left
    h = rect.bottom - rect.top
    if w <= 0 or h <= 0:
        print(f"ERROR: 窗口尺寸异常 {w}x{h}", file=sys.stderr)
        return False
    hdc = user32.GetWindowDC(hwnd)
    if not hdc:
        print(f"ERROR: GetWindowDC failed hwnd={hwnd}", file=sys.stderr)
        return False
    try:
        mdc = gdi32.CreateCompatibleDC(hdc)
        bmp = gdi32.CreateCompatibleBitmap(hdc, w, h)
        if not bmp:
            print("ERROR: CreateCompatibleBitmap failed", file=sys.stderr)
            return False
        try:
            gdi32.SelectObject(mdc, bmp)
            user32.PrintWindow(hwnd, mdc, 2)

            class BITMAPINFOHEADER(ctypes.Structure):
                _fields_ = [
                    ("biSize", wt.DWORD),
                    ("biWidth", ctypes.c_long),
                    ("biHeight", ctypes.c_long),
                    ("biPlanes", wt.WORD),
                    ("biBitCount", wt.WORD),
                    ("biCompression", wt.DWORD),
                    ("biSizeImage", wt.DWORD),
                    ("biXPelsPerMeter", ctypes.c_long),
                    ("biYPelsPerMeter", ctypes.c_long),
                    ("biClrUsed", wt.DWORD),
                    ("biClrImportant", wt.DWORD),
                ]

            bi = BITMAPINFOHEADER()
            bi.biSize = 40
            bi.biWidth = w
            bi.biHeight = -h
            bi.biPlanes = 1
            bi.biBitCount = 32
            bi.biCompression = 0
            buf = ctypes.create_string_buffer(w * h * 4)
            if not gdi32.GetDIBits(mdc, bmp, 0, h, buf, ctypes.byref(bi), 0):
                print("ERROR: GetDIBits failed", file=sys.stderr)
                return False
            img = Image.frombuffer("RGB", (w, h), buf, "raw", "BGRX", 0, 1)
            img.save(out_path)
            return True
        finally:
            gdi32.DeleteObject(bmp)
    finally:
        user32.ReleaseDC(hwnd, hdc)
        gdi32.DeleteDC(mdc)


def is_panel_px(c):
    r, g, b = c
    return b > r and (b - r) > 8 and b > 20 and r < 90 and g < 80


def is_user_blue_px(c):
    # 用户气泡蓝：渐变 (61,107,153)→(36,71,112)，边框 (140,184,242)
    # 特征：g 明显 > r（绿通道高），且 b 高
    r, g, b = c
    return g > r + 15 and b >= 100 and g > 100 and b > g


def find_panel(img):
    # ★ 用低阈值检测完整聊天面板（深紫背景），而非 find_panel.py 的高密度块：
    #   大窗口下 find_panel 的 12% 阈值会漏掉面板下半部（气泡/输入框区域），
    #   导致用户气泡位于检测区域之外 → 误报 NO_BUBBLE。
    w, h = img.size
    px = img.load()
    step = 3
    col_density = [sum(1 for y in range(0, h, step) if is_panel_px(px[x, y])) for x in range(w)]
    row_density = [sum(1 for x in range(0, w, step) if is_panel_px(px[x, y])) for y in range(h)]

    def runs(density, threshold):
        out, start = [], None
        for i, v in enumerate(density):
            if v > threshold and start is None:
                start = i
            elif v <= threshold and start is not None:
                out.append([start, i - 1]); start = None
        if start is not None:
            out.append([start, len(density) - 1])
        return out

    def merge(runs_, gap=20):
        if not runs_:
            return []
        m = [list(runs_[0])]
        for s, e in runs_[1:]:
            if s - m[-1][1] <= gap:
                m[-1][1] = e
            else:
                m.append([s, e])
        return m

    # 低阈值：约 2% 行/列密度即可认定属于面板
    col_th = max(3, int(h / step * 0.02))
    row_th = max(3, int(w / step * 0.02))
    col_runs = merge(runs(col_density, col_th))
    row_runs = merge(runs(row_density, row_th))
    if not col_runs or not row_runs:
        return None
    col = max(col_runs, key=lambda r: r[1] - r[0])
    row = max(row_runs, key=lambda r: r[1] - r[0])
    x, x1 = col
    y, y1 = row
    return (x, y, x1 - x + 1, y1 - y + 1)


def main() -> int:
    args = sys.argv[1:]
    if not args:
        print("usage: check_user_bubble.py <pid> [--shot <tmp.png>]", file=sys.stderr)
        return 2
    pid = args[0]
    shot = None
    if "--shot" in args:
        shot = args[args.index("--shot") + 1]

    # 用 ctypes 按 pid 找主窗口（可见 + 有标题的最顶层窗口）
    found = [None]

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
    def _enum(handle, lparam):
        if not user32.IsWindowVisible(handle):
            return True
        pid_ = wt.DWORD()
        user32.GetWindowThreadProcessId(handle, ctypes.byref(pid_))
        if pid_.value == int(pid):
            title = ctypes.create_unicode_buffer(512)
            user32.GetWindowTextW(handle, title, 512)
            if title.value:
                found[0] = handle
                return False  # 停止枚举
        return True

    user32.EnumWindows(_enum, 0)
    hwnd = found[0]
    if hwnd is None:
        print("NO_BUBBLE window-not-found")
        return 0

    import tempfile, os
    if shot is None:
        shot = os.path.join(tempfile.gettempdir(), "bubble_check_tmp.png")
    if not capture_window(hwnd, shot):
        print("NO_BUBBLE capture-failed")
        return 0

    img = Image.open(shot).convert("RGB")
    panel = find_panel(img)
    if panel is None:
        print("NO_BUBBLE no-panel")
        return 0
    px, py, pw, ph = panel
    pimg = img.crop((px, py, px + pw, py + ph))
    ppx = pimg.load()

    # 扫描蓝色气泡像素（面板内）
    # ★ 用户气泡 = 面板内靠右的连续蓝色块（气泡宽通常 ≥60px）。
    #   直接统计全面板蓝色像素会把标题栏"思考中…"等小文字也算进去，
    #   因此改为：每行蓝色像素数 ≥ MIN_ROW_BLUE（气泡宽度特征）才算气泡行，
    #   再合并连续行段取 bbox。
    MIN_ROW_BLUE = 40      # 一行至少 40 个蓝色像素（气泡宽度特征）
    row_blue = []
    for yy in range(ph):
        cnt = 0
        for xx in range(pw):
            if is_user_blue_px(ppx[xx, yy]):
                cnt += 1
        row_blue.append(cnt)

    # 找气泡行段（连续满足 MIN_ROW_BLUE 的行）
    y_runs, start = [], None
    for yy, cnt in enumerate(row_blue):
        if cnt >= MIN_ROW_BLUE and start is None:
            start = yy
        elif cnt < MIN_ROW_BLUE and start is not None:
            if yy - start >= 3:
                y_runs.append((start, yy - 1))
            start = None
    if start is not None and ph - start >= 3:
        y_runs.append((start, ph - 1))
    if not y_runs:
        print("NO_BUBBLE no-blue-pixels")
        return 0
    y0, y1 = max(y_runs, key=lambda r: r[1] - r[0])
    # 该行段内的 x 范围
    minx, maxx = 10 ** 9, -1
    total = 0
    for yy in range(y0, y1 + 1):
        for xx in range(pw):
            if is_user_blue_px(ppx[xx, yy]):
                total += 1
                if xx < minx: minx = xx
                if xx > maxx: maxx = xx
    print(f"USER_BUBBLE {minx} {y0} {maxx - minx + 1} {y1 - y0 + 1} px={total}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
