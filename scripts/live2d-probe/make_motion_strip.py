#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把帧目录采样成运动条带图（帧号烧录），供视觉模型做时序动作评审。

用法:
  python make_motion_strip.py --frames-dir <dir> --out <strip.png> \
      [--max-frames 8] [--columns 4] [--tile 420] [--diff] [--shuffle] [--static]

帧按文件名升序视为时间顺序；--shuffle 打乱顺序构成"乱动"负样本；
--static 用首帧重复构成"静止"对照；--diff 在每帧旁附加相对首帧的
差异放大热图（无变化=纯黑，无法被模型脑补成"有变化"）。
标签只用 ASCII（f01..f16，d01..d16），避免字体依赖。
"""
import argparse
import os
import random

from PIL import Image, ImageDraw


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--frames-dir", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--max-frames", type=int, default=8)
    ap.add_argument("--columns", type=int, default=4)
    ap.add_argument("--tile", type=int, default=420)
    ap.add_argument("--diff", action="store_true", help="每帧右侧附加 x8 放大的差异热图")
    ap.add_argument("--shuffle", action="store_true")
    ap.add_argument("--static", action="store_true")
    ap.add_argument("--seed", type=int, default=20260918)
    args = ap.parse_args()

    files = sorted(
        f for f in os.listdir(args.frames_dir)
        if f.lower().endswith(".png")
    )
    if len(files) < 3:
        raise SystemExit("帧数不足: %d" % len(files))

    # 均匀抽样
    picked = [files[round(i * (len(files) - 1) / (args.max_frames - 1))] for i in range(args.max_frames)]
    if args.shuffle:
        rng = random.Random(args.seed)
        rng.shuffle(picked)
    if args.static:
        picked = [files[0]] * args.max_frames

    tile = args.tile
    cols = args.columns
    rows = (len(picked) + cols - 1) // cols
    pad = 4
    label_h = 18
    cell_w = tile * (2 if args.diff else 1) + pad
    sheet = Image.new("RGB", (cols * (cell_w + pad) + pad, rows * (tile + label_h + pad) + pad), (12, 12, 16))
    draw = ImageDraw.Draw(sheet)

    base_img = None
    for i, name in enumerate(picked):
        img = Image.open(os.path.join(args.frames_dir, name)).convert("RGB")
        if i == 0:
            base_img = img
        # 等比缩放到 tile 内
        scale = min(tile / img.width, tile / img.height)
        img_small = img.resize((max(1, int(img.width * scale)), max(1, int(img.height * scale))))
        col = i % cols
        row = i // cols
        x = pad + col * (cell_w + pad)
        y = pad + row * (tile + label_h + pad)
        sheet.paste(img_small, (x, y))
        draw.text((x + 2, y + tile + 2), "f%02d" % (i + 1), fill=(240, 240, 240))

        if args.diff and base_img is not None:
            # 与首帧逐像素差值 x8 放大（无变化=纯黑）
            diff = Image.blend(base_img, img, 0)  # 复制 base 尺寸
            base_px = base_img.load()
            img_px = img.load()
            dw, dh = base_img.size
            diff_img = Image.new("RGB", (dw, dh))
            dpx = diff_img.load()
            for yy in range(0, dh, 2):
                for xx in range(0, dw, 2):
                    r1, g1, b1 = base_px[xx, yy][:3]
                    r2, g2, b2 = img_px[xx, yy][:3]
                    v = min(255, (abs(r1 - r2) + abs(g1 - g2) + abs(b1 - b2)) * 8)
                    color = (v, v // 2, 0) if v > 12 else (0, 0, 0)
                    dpx[xx, yy] = color
                    if xx + 1 < dw:
                        dpx[xx + 1, yy] = color
            diff_small = diff_img.resize((tile // 2, tile // 2))
            sheet.paste(diff_small, (x + tile + 2, y + tile // 4))
            draw.text((x + tile + 2, y + tile + 2), "d%02d" % (i + 1), fill=(255, 160, 60))

    sheet.save(args.out)
    print("strip saved: %s (%dx%d tiles=%d mode=%s diff=%s)" % (
        args.out, cols, rows, len(picked),
        "shuffle" if args.shuffle else ("static" if args.static else "ordered"), args.diff))


if __name__ == "__main__":
    main()
