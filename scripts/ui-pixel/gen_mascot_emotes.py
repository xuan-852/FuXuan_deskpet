# -*- coding: utf-8 -*-
"""
复刻 RightPanel.cs 的 LoadMascotEmote 像素逻辑（坐标已修正为 x列,y行）。
生成 9 个表情帧 PNG 供视觉验证。
脸部坐标：左眼(5,12) 右眼(9,12) 符咒(8,11) 腮红y13-14 嘴y15(两端黑x5/x11,缝线x6-7/9-10,暗肤x8)
"""
from PIL import Image
import os

SRC = 'D:/Unity/projects/Desktop_per_pro/code/desktop_unity/Assets/Resources/PixelFuXuan_17x24.png'
OUT = 'C:/Users/25295/.vscode_vision_screenshots/mascot_emotes'
UPSCALE = 16

skin   = (255, 243, 235)  # 肤色
eye    = (159, 117, 148)  # 原紫眼
line   = (202, 202, 212)  # 闭眼缝线
dark   = (72, 70, 78)     # 深描边（眉/嘴）
blushC = (255, 150, 175)  # 腮红粉
tearC  = (140, 200, 255)  # 泪滴蓝
heartC = (255, 105, 180)  # 爱心粉

def apply(n, px, w, h):
    def set(x, y, c):  # x列, y行
        if 0 <= x < w and 0 <= y < h:
            px[x, y] = c + (255,)
    def smile():  # 抹掉嘴部缝线留两端黑 = 微笑弧线
        for x in range(6, 11):
            set(x, 15, skin)
    if n == 'happy':      # ⌒⌒ 眯眼笑 + 微笑
        set(5, 12, line); set(9, 12, line); smile()
    elif n == 'angry':    # 怒眉压低 + 抿嘴
        set(5, 10, dark); set(9, 10, dark)
        set(7, 15, dark); set(8, 15, dark); set(9, 15, dark)
    elif n == 'sad':      # 八字垂眉 + 泪滴 + 委屈嘴
        set(4, 10, dark); set(10, 10, dark)
        set(5, 13, tearC); set(9, 13, tearC)
        set(7, 15, dark); set(8, 15, dark); set(9, 15, dark)
    elif n == 'surprise': # 2x2 大眼 ○○ + O 嘴
        for xx in (4, 5):
            set(xx, 11, eye); set(xx, 12, eye)
        for xx in (8, 9):
            set(xx, 11, eye); set(xx, 12, eye)
        set(7, 14, dark); set(6, 15, dark); set(8, 15, dark); set(7, 16, dark)
        set(7, 15, skin)
    elif n == 'confused': # 挑眉 + 右眼眯 + 歪嘴
        set(5, 10, dark)
        set(9, 12, line)
        set(8, 15, dark)
    elif n == 'sleepy':   # 闭眼 + 哈欠 O
        set(5, 12, line); set(9, 12, line)
        set(7, 14, dark); set(7, 15, dark); set(8, 15, dark)
    elif n == 'blush':    # 闭眼 + 大红脸
        set(5, 12, line); set(9, 12, line)
        for xx in (4, 5):
            set(xx, 13, blushC); set(xx, 14, blushC)
        for xx in (9, 10):
            set(xx, 13, blushC); set(xx, 14, blushC)
        set(7, 15, line); set(8, 15, line)
    elif n == 'love':     # ♥ 微笑嘴 + 脸颊粉（爱心只由徽章表达，爱心眼太小删掉）
        set(4, 13, blushC); set(10, 13, blushC); smile()
    elif n == 'tear':     # 泪汪汪（双竖泪滴 + 撇嘴）
        set(5, 13, tearC); set(5, 14, tearC)
        set(9, 13, tearC); set(9, 14, tearC)
        set(7, 15, dark); set(8, 15, dark)

def main():
    os.makedirs(OUT, exist_ok=True)
    names = ['happy', 'angry', 'sad', 'surprise', 'confused', 'sleepy', 'blush', 'love', 'tear']
    base = Image.open(SRC).convert('RGBA')
    bw, bh = base.size
    for n in names:
        img = base.copy()
        px = img.load()
        apply(n, px, bw, bh)
        diff = [(x, y) for y in range(bh) for x in range(bw) if px[x, y] != base.getpixel((x, y))]
        rows = {}
        for x, y in diff:
            rows.setdefault(y, []).append(x)
        print(f'{n:9s} 修改 {len(diff):2d}px: ' + '; '.join(f'y{y}:x{",".join(map(str,xs))}' for y, xs in sorted(rows.items())))
        big = img.resize((bw * UPSCALE, bh * UPSCALE), Image.NEAREST)
        big.save(os.path.join(OUT, n + '_v2.png'))
    # 3x3 拼图
    cell_w, cell_h = 17 * UPSCALE, 24 * UPSCALE
    grid = Image.new('RGBA', (cell_w * 3 + 40, cell_h * 3 + 40), (20, 20, 40, 255))
    for i, n in enumerate(names):
        r, c = divmod(i, 3)
        img = Image.open(SRC).convert('RGBA')
        px = img.load()
        apply(n, px, bw, bh)
        grid.paste(img.resize((cell_w, cell_h), Image.NEAREST), (10 + c * (cell_w + 10), 10 + r * (cell_h + 10)))
    gp = os.path.join(OUT, 'grid_v2.png')
    grid.save(gp)
    print(f'saved {gp}')

if __name__ == '__main__':
    main()
