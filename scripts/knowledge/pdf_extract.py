#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
PDF 文本提取器 — 为「藏书阁」知识库索引提供 PDF 文本。

用法:
  python pdf_extract.py <pdf_path> [max_chars]

输入:
  pdf_path   PDF 文件绝对路径（必须存在）
  max_chars  可选，最多提取的字符数（默认 500000，超长截断，防止大 PDF 拖垮索引）

输出: 打印 JSON 到 stdout:
  {"success": true, "text": "...", "pages": 12, "chars": 3456, "is_scanned": false}
  {"success": false, "error": "..."}

依赖: pypdf（纯 Python，无重依赖；若未安装会自动提示安装命令）
扫描版 PDF（无文本层）会返回 success:false + is_scanned:true，
上层可提示用户：该 PDF 是扫描图片，需要 OCR 才能索引。

编码说明: 输出统一 UTF-8；Windows 控制台需 PYTHONIOENCODING=utf-8。
"""

import json
import sys
import os

# 输出编码（Windows GBK 控制台防乱码/防编码崩溃）
if sys.stdout.encoding and sys.stdout.encoding.lower() not in ('utf-8', 'utf8'):
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass


def extract_pdf_text(pdf_path, max_chars=500000):
    """提取 PDF 文本。优先 PyMuPDF（中文 CMap 支持好），fallback pypdf。

    返回 (text, page_count) 或抛异常。
    """
    # —— 引擎 1：PyMuPDF（fitz/pymupdf），对中文内嵌子集字体解码最佳 ——
    try:
        try:
            import pymupdf as fitz  # PyMuPDF >= 1.24 推荐入口
        except ImportError:
            import fitz  # 旧版入口
        return extract_with_pymupdf(fitz, pdf_path, max_chars)
    except ImportError:
        pass  # 没有 PyMuPDF，走 pypdf

    # —— 引擎 2：pypdf（纯 Python 兜底）——
    try:
        from pypdf import PdfReader
    except ImportError:
        raise RuntimeError(
            "未安装 PDF 库，无法提取文本。请运行："
            "py -3.12 -m pip install pymupdf  （推荐，中文支持好）"
            "或 py -3.12 -m pip install pypdf"
        )

    reader = PdfReader(pdf_path)
    pages = len(reader.pages)
    chunks = []
    total = 0
    for i, page in enumerate(reader.pages):
        try:
            txt = page.extract_text() or ""
        except Exception:
            txt = ""
        txt = txt.strip()
        if not txt:
            continue
        chunks.append(f"【第{i + 1}页】\n{txt}")
        total += len(txt)
        if total >= max_chars:
            break
    return "\n\n".join(chunks), pages


def extract_with_pymupdf(fitz, pdf_path, max_chars):
    """PyMuPDF 引擎：逐页提取，保留页码标记。"""
    doc = fitz.open(pdf_path)
    pages = doc.page_count
    chunks = []
    total = 0
    try:
        for i in range(pages):
            page = doc[i]
            try:
                txt = page.get_text("text") or ""
            except Exception:
                txt = ""
            txt = txt.strip()
            if not txt:
                continue
            chunks.append(f"【第{i + 1}页】\n{txt}")
            total += len(txt)
            if total >= max_chars:
                break
    finally:
        doc.close()
    return "\n\n".join(chunks), pages


def main():
    if len(sys.argv) < 2:
        print(json.dumps({"success": False, "error": "用法: python pdf_extract.py <pdf_path 或 参数JSON文件> [max_chars]"}, ensure_ascii=False))
        return 1

    arg1 = sys.argv[1]
    max_chars = 500000

    # 支持两种调用方式：
    #   1) 命令行直接传路径：python pdf_extract.py "D:\\a.pdf" [max_chars]
    #   2) 传参数 JSON 文件（桥接用，避免转义问题）：python pdf_extract.py tmp.json
    pdf_path = arg1
    if os.path.isfile(arg1) and arg1.lower().endswith('.json'):
        try:
            with open(arg1, 'r', encoding='utf-8') as f:
                params = json.load(f)
            pdf_path = params.get('path', '')
            if params.get('max_chars'):
                max_chars = int(params['max_chars'])
        except Exception as e:
            print(json.dumps({"success": False, "error": f"参数 JSON 解析失败: {e}"}, ensure_ascii=False))
            return 1
    elif len(sys.argv) >= 3:
        try:
            max_chars = int(sys.argv[2])
        except ValueError:
            pass

    if not pdf_path:
        print(json.dumps({"success": False, "error": "未提供 PDF 文件路径"}, ensure_ascii=False))
        return 1

    if not os.path.isfile(pdf_path):
        print(json.dumps({"success": False, "error": f"PDF 文件不存在: {pdf_path}"}, ensure_ascii=False))
        return 1

    try:
        text, page_count = extract_pdf_text(pdf_path, max_chars)
        if not text or not text.strip():
            print(json.dumps({
                "success": False,
                "error": "该 PDF 没有可提取的文本层（可能是扫描版图片 PDF）",
                "is_scanned": True,
                "pages": page_count
            }, ensure_ascii=False))
            return 2
        print(json.dumps({
            "success": True,
            "text": text,
            "pages": page_count,
            "chars": len(text)
        }, ensure_ascii=False))
        return 0
    except Exception as e:
        print(json.dumps({"success": False, "error": f"PDF 提取失败: {e}"}, ensure_ascii=False))
        return 1


if __name__ == "__main__":
    sys.exit(main())
