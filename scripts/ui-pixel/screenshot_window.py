#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
screenshot_window.py — 按 PID 或窗口句柄截取窗口内容（PrintWindow 后台截图）
规范化输出：成功打印 "saved <path> (<w>x<h>)"，失败打印 "ERROR: <原因>" 并退出码 1。

用法:
    python screenshot_window.py <pid> <out.png>
    python screenshot_window.py --hwnd <hwnd> <out.png>

依赖: Pillow（pip install pillow）
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
            # PW_RENDERFULLCONTENT = 2：后台渲染完整窗口（含 Unity 内容）
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
            bi.biHeight = -h  # 负值 → 自上而下行序
            bi.biPlanes = 1
            bi.biBitCount = 32
            bi.biCompression = 0
            buf = ctypes.create_string_buffer(w * h * 4)
            if not gdi32.GetDIBits(mdc, bmp, 0, h, buf, ctypes.byref(bi), 0):
                print("ERROR: GetDIBits failed", file=sys.stderr)
                return False
            img = Image.frombuffer("RGB", (w, h), buf, "raw", "BGRX", 0, 1)
            img.save(out_path)
            print(f"saved {out_path} ({w}x{h})")
            return True
        finally:
            gdi32.DeleteObject(bmp)
            gdi32.DeleteDC(mdc)
    finally:
        user32.ReleaseDC(hwnd, hdc)


def main() -> int:
    args = sys.argv[1:]
    if len(args) < 2:
        print(__doc__, file=sys.stderr)
        return 2

    hwnd = None
    out_path = None
    if args[0] == "--hwnd" and len(args) == 3:
        hwnd = int(args[1])
        out_path = args[2]
    elif args[0].isdigit() and len(args) == 2:
        pid = int(args[0])
        out_path = args[1]
        hwnd = _find_hwnd_by_pid(pid)
        if hwnd is None:
            print(f"ERROR: 找不到 PID {pid} 的主窗口句柄", file=sys.stderr)
            return 1
    else:
        print("ERROR: 参数错误，用法见文件头", file=sys.stderr)
        return 2

    return 0 if capture_window(hwnd, out_path) else 1


def _find_hwnd_by_pid(pid: int):
    """通过 EnumWindows 找进程主窗口（比 Get-Process MainWindowHandle 更可靠）"""
    result = {"hwnd": None}

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
    def enum_proc(hwnd, lparam):
        if not user32.IsWindowVisible(hwnd):
            return True
        pid_ = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid_))
        if pid_.value == pid:
            result["hwnd"] = hwnd
            return False  # 停止枚举
        return True

    user32.EnumWindows(enum_proc, 0)
    return result["hwnd"]


if __name__ == "__main__":
    sys.exit(main())
