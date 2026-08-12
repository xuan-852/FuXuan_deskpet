# -*- coding: utf-8 -*-
"""
⚠️ 历史一次性迁移工具（2026-08-05 使用，已完成使命）。
下方 TARGETS 中 r'tools\...' 路径是迁移当时的旧路径快照，
脚本现已在 scripts/<分类>/ 下（2026-08-12 目录重构），本文件仅作记录，不再执行。

批量给含中文的 .ps1 脚本接入统一编码协议：
1. 自动检测 param() 块（PowerShell 要求 param 必须是第一个语句，插入点须在其后）
2. 在正确位置插入 dot-source init-utf8.ps1 行
3. 全部转存为 UTF-8 with BOM
"""
import os
import re

ROOT = r'D:\Unity\projects\Desktop_per_pro'

# 需要接入的脚本（相对 ROOT），深度用于计算 ../ 相对路径
TARGETS = [
    'test_chunked_latex.ps1',
    r'code\desktop_unity\screenshots\analyze_grid.ps1',
    r'code\desktop_unity\screenshots\analyze_ui.ps1',
    r'code\desktop_unity\scripts\generate_pixel_candidates_fixed.ps1',
    r'code\desktop_unity\scripts\generate_pixel_from_live2d.ps1',
    r'code\desktop_unity\scripts\generate_pixel_quick.ps1',
    r'scripts\param_mapper\download_samples.ps1',
    r'tools\analyze_log.ps1',
    r'tools\check_encoding.ps1',
    r'tools\count_log.ps1',
    r'tools\fuxuan_pixel_art.ps1',
    r'tools\pet_chat_test.ps1',
    r'tools\pixelize.ps1',
    r'tools\read_pixel_map.ps1',
    r'tools\render_fuxuan_17x24.ps1',
]

PARAM_RE = re.compile(r'^\s*param\s*\(', re.MULTILINE)
DOTSOURCE_RE = re.compile(r'init-utf8\.ps1')


def find_param_end(text, start):
    """从 param( 开始找匹配的 ) 位置（跳过字符串/注释内括号，简化版）"""
    depth = 0
    in_str = False
    in_str_ch = None
    i = start
    while i < len(text):
        ch = text[i]
        if in_str:
            if ch == in_str_ch:
                in_str = False
            elif ch == '`':  # PS 转义
                i += 1
        else:
            if ch in ('"', "'"):
                in_str = True
                in_str_ch = ch
            elif ch == '(':
                depth += 1
            elif ch == ')':
                depth -= 1
                if depth == 0:
                    return i
        i += 1
    return -1


def rel_depth(rel_path):
    """文件相对 ROOT 的目录深度"""
    d = os.path.dirname(rel_path)
    return 0 if not d else len(d.split(os.sep))


def make_dot_source(rel_path):
    depth = rel_depth(rel_path)
    if depth == 0:
        up = r'tools\init-utf8.ps1'
    else:
        up = '..\\' * depth + r'tools\init-utf8.ps1'
    return f'# ── 统一编码协议：UTF-8 环境初始化（PS 5.1 防乱码）──\n. "$PSScriptRoot\\{up}"'


def process(rel_path):
    full = os.path.join(ROOT, rel_path)
    raw = open(full, 'rb').read()
    if raw.startswith(b'\xef\xbb\xbf'):
        text = raw.decode('utf-8-sig')
        had_bom = True
    else:
        text = raw.decode('utf-8')
        had_bom = False

    if DOTSOURCE_RE.search(text):
        print(f'[SKIP] {rel_path} 已接入')
        return

    # 找 param 块
    m = PARAM_RE.search(text)
    if m:
        end = find_param_end(text, m.start())
        if end < 0:
            print(f'[FAIL] {rel_path} param 块无法解析')
            return
        # 找 param 结束行的行尾
        nl = text.find('\n', end)
        if nl < 0:
            nl = len(text)
        insert_at = nl + 1
        block = text[:insert_at] + '\n' + make_dot_source(rel_path) + '\n' + text[insert_at:]
    else:
        # 无 param：插到文件顶部（最前）
        block = make_dot_source(rel_path) + '\n\n' + text.lstrip('\ufeff')

    # 写回 UTF-8 with BOM
    with open(full, 'wb') as f:
        f.write(b'\xef\xbb\xbf' + block.encode('utf-8'))
    bom = 'BOM+' if not had_bom else 'BOM='
    print(f'[OK] {bom} {rel_path} ({len(block.splitlines())} 行)')


for t in TARGETS:
    process(t)

print('=== 全部完成 ===')
