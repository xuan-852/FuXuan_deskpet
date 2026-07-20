#!/usr/bin/env python3
"""
AI PDF 文本提取桥 — 用 PyMuPDF 提取 PDF 中的文字
输出纯文本（UTF-8），供 AI 阅读

用法: python tools/pdf_extract.py <pdf路径> [最大字符数]

示例:
  python tools/pdf_extract.py "D:\书籍\控制理论.pdf"
  python tools/pdf_extract.py "D:\书籍\控制理论.pdf" 3000
"""
import sys
import os
import json

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

def extract_text(pdf_path, max_chars=5000):
    """用 PyMuPDF 提取 PDF 文本内容"""
    try:
        import fitz  # PyMuPDF
    except ImportError:
        return json.dumps({
            "success": False,
            "error": "PyMuPDF 未安装，请执行: pip install PyMuPDF",
            "text": ""
        }, ensure_ascii=False)

    if not os.path.isfile(pdf_path):
        return json.dumps({
            "success": False,
            "error": f"文件不存在: {pdf_path}",
            "text": ""
        }, ensure_ascii=False)

    try:
        doc = fitz.open(pdf_path)
        total_pages = len(doc)
        extracted = []
        total_chars = 0

        for i in range(total_pages):
            page = doc[i]
            text = page.get_text()
            if text.strip():
                extracted.append(f"--- 第 {i+1} 页 ---\n{text.strip()}")
                total_chars += len(text)
                if total_chars >= max_chars:
                    extracted.append(f"\n...（已达到输出上限 {max_chars} 字符，共 {total_pages} 页）")
                    break

        doc.close()

        result_text = "\n\n".join(extracted)
        
        # 如果一页都没提取到文字，可能是扫描版 PDF
        if not result_text.strip():
            return json.dumps({
                "success": False,
                "error": "此 PDF 未提取到文字内容，可能是扫描件（图片版），需要 OCR",
                "text": "",
                "pages": total_pages
            }, ensure_ascii=False)

        return json.dumps({
            "success": True,
            "text": result_text[:max_chars],
            "pages": total_pages,
            "total_chars": min(total_chars, max_chars)
        }, ensure_ascii=False)

    except Exception as e:
        return json.dumps({
            "success": False,
            "error": str(e),
            "text": ""
        }, ensure_ascii=False)

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"success": False, "error": "用法: python pdf_extract.py <pdf路径> [最大字符数]", "text": ""}, ensure_ascii=False))
        sys.exit(1)

    pdf_path = sys.argv[1]
    max_chars = int(sys.argv[2]) if len(sys.argv) > 2 else 5000
    print(extract_text(pdf_path, max_chars))
