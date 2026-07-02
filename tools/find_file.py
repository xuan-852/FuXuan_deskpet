#!/usr/bin/env python3
"""
AI 文件搜索桥 — 解决中文文件名检索问题
输出 JSON 格式（UTF-8），AI 直接解析拿到完整 Unicode 路径

用法: python tools/find_file.py <关键词> [搜索根目录]

示例:
  python tools/find_file.py 符玄
  python tools/find_file.py 使用说明
  python tools/find_file.py .meta
  python tools/find_file.py 符玄 D:\
"""
import os
import sys
import json
from urllib.parse import quote

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

def path_to_file_uri(path):
    """把 Windows 路径转成 file:/// URI（纯 ASCII，VS Code 标准格式）"""
    abs_path = os.path.abspath(path).replace('\\', '/')
    return 'file:///' + quote(abs_path, safe='/:@')

def find_files(keyword, root_dir):
    """递归搜索文件名包含关键词的文件"""
    results = []
    keyword_lower = keyword.lower()
    max_results = 200

    for dirpath, dirnames, filenames in os.walk(root_dir):
        if len(results) >= max_results:
            break
        # 跳过隐藏目录、缓存目录、系统目录
        dirnames[:] = [d for d in dirnames
                       if not d.startswith('.')
                       and d != '__pycache__'
                       and d != 'System Volume Information'
                       and d != '$RECYCLE.BIN'
                       and d != 'Windows'
                       and d != 'ProgramData'
                       and not d.startswith('$')]
        for f in filenames:
            if len(results) >= max_results:
                break
            if keyword_lower in f.lower():
                full_path = os.path.join(dirpath, f)
                results.append({
                    "path": full_path,
                    "uri": path_to_file_uri(full_path),
                    "name": f
                })

    return results

if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(json.dumps({"error": "用法: python find_file.py <关键词> [搜索根目录]"}, ensure_ascii=False))
        sys.exit(1)

    keyword = sys.argv[1]

    if len(sys.argv) >= 3:
        root_dir = sys.argv[2]
    else:
        script_dir = os.path.dirname(os.path.abspath(__file__))
        project_root = os.path.dirname(script_dir)
        root_dir = os.path.join(project_root, "code", "desktop_unity")

    # ALL_DRIVES 标记 → 搜索所有固定磁盘
    if root_dir == "ALL_DRIVES":
        import string
        drives = [f"{d}:\\" for d in string.ascii_uppercase if os.path.exists(f"{d}:\\")]
        # 排除 A: B: (软驱) 和常见不可用盘符
        drives = [d for d in drives if d[0] not in ('A', 'B') and os.path.isdir(d)]
        if not drives:
            drives = ["C:\\"]
        all_files = []
        for drive in drives:
            files = find_files(keyword, drive)
            all_files.extend(files)
            if len(all_files) >= 200:
                break
        files = all_files[:200]
        result = {
            "keyword": keyword,
            "root": "ALL_DRIVES",
            "drives_scanned": drives,
            "count": len(files),
            "files": files
        }
        print(json.dumps(result, ensure_ascii=False, indent=2))
        sys.exit(0)

    if not os.path.exists(root_dir):
        print(json.dumps({"error": f"目录不存在: {root_dir}"}, ensure_ascii=False))
        sys.exit(1)

    files = find_files(keyword, root_dir)

    result = {
        "keyword": keyword,
        "root": root_dir,
        "count": len(files),
        "files": files       # 每个元素: { path, uri, name }
    }

    # ✨ 输出 JSON — AI 用 json.loads() 解析，中文路径完美保留
    #    直接取 uri 字段（纯 ASCII file:/// 格式）传给 read_file 绝对不出错
    print(json.dumps(result, ensure_ascii=False, indent=2))
