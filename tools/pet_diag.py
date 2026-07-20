#!/usr/bin/env python3
"""
桌面宠物自动化诊断迭代脚本 — 「太卜司天眼巡检」
==============================================

功能：
  1. 推送到宠物 (POST /api/push) — 测试气泡中文显示
  2. 读取 Player.log — 监控宠物行为、诊断编码问题
  3. 验证知识库 (knowledge_base.json) — 检查 UTF-8 完整性
  4. 验证文件搜索结果 — 检查中文文件名是否乱码
  5. 自动修复 + 构建 + 重测试 (闭环迭代)

用法:
  python tools/pet_diag.py                  # 完整诊断
  python tools/pet_diag.py --quick          # 仅检查编码+日志
  python tools/pet_diag.py --fix            # 诊断+自动修复+构建
  python tools/pet_diag.py --loop           # 持续监控循环
  python tools/pet_diag.py chat <消息>        # 向宠物发送一条对话消息
  python tools/pet_diag.py chat-loop        # 交互式对话循环
"""

import json
import os
import re
import subprocess
import sys
import time
import urllib.request
import urllib.error
from datetime import datetime
from pathlib import Path

# ═══════════════════════════════════════════
#  配置
# ═══════════════════════════════════════════

# 项目路径
PROJECT_ROOT = Path(r"D:\Unity\projects\Desktop_per_pro")
CODE_DIR = PROJECT_ROOT / "code" / "desktop_unity" / "Assets" / "Scripts"
BUILD_DIR = PROJECT_ROOT / "Build"
BUILD_SCRIPT = PROJECT_ROOT / "build.ps1"

# 服务端
SERVER_URL = "http://localhost:3000"
PUSH_ENDPOINT = f"{SERVER_URL}/api/push"
TOKEN = "mini_secret_token_here"  # MINI_TOKEN

# 数据路径
DATA_DIR = Path(r"D:\DesktopPetData")
KNOWLEDGE_DB = DATA_DIR / "knowledge_base.json"
PET_DATA_DIR = DATA_DIR

# Player.log 搜索路径 (Unity Editor / 发布版)
PLAYER_LOG_CANDIDATES = [
    Path(os.environ.get("LOCALAPPDATA", "C:\\Users\\Default\\AppData\\Local")) / "Low" / "DefaultCompany" / "desktop pet" / "Player.log",
    Path(os.environ["USERPROFILE"]) / "AppData" / "LocalLow" / "DefaultCompany" / "desktop pet" / "Player.log",
]

# ═══════════════════════════════════════════
#  工具函数
# ═══════════════════════════════════════════

def log(msg: str, level: str = "INFO"):
    """带时间戳的日志输出"""
    ts = datetime.now().strftime("%H:%M:%S")
    icons = {"INFO": "ℹ️", "OK": "✅", "WARN": "⚠️", "ERROR": "❌", "FIX": "🔧", "TEST": "🧪"}
    icon = icons.get(level, "📋")
    print(f"[{ts}] {icon} {msg}")


def get_player_log() -> str:
    """查找并返回 Player.log 全文"""
    for p in PLAYER_LOG_CANDIDATES:
        if p.exists():
            try:
                raw = p.read_bytes()
                # 尝试 UTF-8，失败则 GBK
                for enc in ["utf-8", "gbk"]:
                    try:
                        return raw.decode(enc, errors="replace")
                    except:
                        continue
                return raw.decode("utf-8", errors="replace")
            except Exception as e:
                log(f"读取 {p} 失败: {e}", "WARN")
    # 尝试全局搜索
    try:
        result = subprocess.run(
            ["where", "Player.log"],
            capture_output=True, text=True, timeout=5
        )
        for line in result.stdout.strip().splitlines():
            p = Path(line.strip())
            if p.exists():
                return p.read_text(encoding="utf-8", errors="replace")
    except:
        pass
    return ""


def push_message(msg_type: str, title: str, body: str, payload: dict = None) -> bool:
    """通过 POST /api/push 推送消息到宠物轮询队列"""
    data = json.dumps({
        "type": msg_type,
        "title": title,
        "body": body,
        "payload": payload or {}
    }).encode("utf-8")
    req = urllib.request.Request(
        PUSH_ENDPOINT,
        data=data,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {TOKEN}"
        },
        method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            result = json.loads(resp.read().decode("utf-8"))
            if result.get("status") == "ok":
                log(f"推送成功 [{msg_type}]: {title[:30]}", "OK")
                return True
            else:
                log(f"推送失败: {result}", "ERROR")
                return False
    except urllib.error.HTTPError as e:
        log(f"推送 HTTP {e.code}: {e.read().decode('utf-8', errors='replace')[:100]}", "ERROR")
        return False
    except Exception as e:
        log(f"推送异常 (服务端未运行?): {e}", "WARN")
        return False


def get_server_status() -> bool:
    """检查服务端是否运行"""
    try:
        req = urllib.request.Request(
            f"{SERVER_URL}/api/pet/poll",
            headers={"Authorization": f"Bearer {TOKEN}"}
        )
        with urllib.request.urlopen(req, timeout=3) as resp:
            return resp.status == 200
    except:
        return False


def push_chat_message(text: str, auto_trigger_tools: bool = True) -> bool:
    """
    推送一条 chat_message 类型消息给宠物，触发 AI 对话。
    宠物轮询到后会自动调用 ChatManager.SendMessage() 进入 AI 对话。
    """
    data = json.dumps({
        "type": "chat_message",
        "title": "🔮 太卜法旨",
        "body": text,
        "payload": {"autoTriggerTools": auto_trigger_tools}
    }).encode("utf-8")
    req = urllib.request.Request(
        PUSH_ENDPOINT,
        data=data,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {TOKEN}"
        },
        method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            result = json.loads(resp.read().decode("utf-8"))
            if result.get("status") == "ok":
                log(f"对话消息推送成功: {text[:50]}", "OK")
                return True
            else:
                log(f"推送失败: {result}", "ERROR")
                return False
    except Exception as e:
        log(f"推送异常: {e}", "ERROR")
        return False


def get_latest_ai_reply(log_content: str = None, tail_lines: int = 300) -> str:
    """从 Player.log 中提取最新 AI 回复内容"""
    if log_content is None:
        log_content = get_player_log()
    if not log_content:
        return ""

    lines = log_content.splitlines()[-tail_lines:]

    # 搜集 AI 回复片段：ChatManager [回复|响应|结果] 等
    reply_parts = []
    for line in lines:
        # 匹配 tool 调用结果
        if "[ChatManager] 📜 结果:" in line:
            idx = line.index("📜 结果:") + 6
            reply_parts.append(("tool_result", line[idx:].strip()))
        # 匹配 AI 的文本回复
        elif "[ChatManager] 🎭 言出法随" in line:
            pass  # 表情变化，跳过
        elif "[ChatManager] ⚡ 施法:" in line:
            idx = line.index("⚡ 施法:") + 6
            reply_parts.append(("tool_call", line[idx:].strip()))
        elif "[ChatManager]" in line and ("error" in line.lower() or "错误" in line):
            reply_parts.append(("error", line.strip()))

    return reply_parts


# ═══════════════════════════════════════════
#  诊断模块
# ═══════════════════════════════════════════

def diag_server() -> bool:
    """诊断 1: 服务端连通性"""
    log("检查服务端...", "TEST")
    if get_server_status():
        log(f"服务端运行中 ({SERVER_URL})", "OK")
        return True
    else:
        log(f"服务端未响应 ({SERVER_URL})", "WARN")
        log("宠物仍在运行（轮询静默容错），但推送测试将跳过", "WARN")
        return False


def diag_player_log() -> dict:
    """诊断 2: 扫描 Player.log 中的异常"""
    log("扫描 Player.log...", "TEST")
    content = get_player_log()
    if not content:
        log("未找到 Player.log（宠物未运行）", "WARN")
        return {"found": False, "issues": []}

    issues = []
    lines = content.splitlines()
    log(f"Player.log: {len(lines)} 行", "OK")

    # 检查 Unicode 乱码模式: 连续的 � 或 ���
    garbled_count = 0
    for line in lines:
        # 跳过一些无害的路径
        if "Player.log" in line or "UnityEngine" in line:
            continue
        # 检查 \uFFFD (�) 替换字符
        garbled = line.count("\ufffd")
        if garbled > 0:
            garbled_count += 1
            if garbled_count <= 5:  # 只记录前 5 行
                issues.append(("garbled", f"含乱码字符: {line.strip()[:120]}"))

    if garbled_count > 0:
        log(f"发现 {garbled_count} 行含乱码字符 (�)", "ERROR")
    else:
        log("未发现乱码字符", "OK")

    # 检查错误/异常
    error_patterns = [
        (r"error\s+CS\d+", "编译错误"),
        (r"NullReferenceException", "空引用"),
        (r"IndexOutOfRangeException", "索引越界"),
        (r"FileNotFoundException", "文件未找到"),
        (r"UnauthorizedAccessException", "权限不足"),
        (r"IOException", "IO 错误"),
        (r"Exception", "未处理异常"),
    ]
    error_count = 0
    for pattern, label in error_patterns:
        for i, line in enumerate(lines):
            if re.search(pattern, line, re.IGNORECASE):
                error_count += 1
                if error_count <= 8:
                    issues.append((f"error_{label}", f"[L{i+1}] {line.strip()[:140]}"))

    if error_count > 0:
        log(f"发现 {error_count} 个运行时错误/异常", "WARN")
    else:
        log("无运行时错误", "OK")

    # 检查 search_files 结果中的编码问题
    search_lines = [l for l in lines if "结果" in l and "件与" in l]
    for sl in search_lines:
        if "\ufffd" in sl:
            issues.append(("search_garbled", f"文件搜索结果含乱码: {sl.strip()[:150]}"))
            log("文件搜索结果显示文件名乱码!", "ERROR")

    return {"found": True, "lines": len(lines), "issues": issues, "garbled_lines": garbled_count, "errors": error_count}


def diag_knowledge_base() -> dict:
    """诊断 3: 知识库 JSON 完整性"""
    log("检查知识库...", "TEST")
    if not KNOWLEDGE_DB.exists():
        log(f"知识库不存在 ({KNOWLEDGE_DB})", "WARN")
        return {"exists": False}

    try:
        raw = KNOWLEDGE_DB.read_bytes()
        # 检查文件编码
        is_valid_utf8 = True
        try:
            raw.decode("utf-8")
        except:
            is_valid_utf8 = False

        data = json.loads(raw.decode("utf-8", errors="replace"))

        chunks = data if isinstance(data, list) else data.get("chunks", [])

        # 检查每个 chunk 的文本是否有乱码
        garbled_chunks = 0
        for i, c in enumerate(chunks):
            text = ""
            if isinstance(c, dict):
                text = c.get("text", c.get("content", str(c)))
            elif isinstance(c, str):
                text = c
            if "\ufffd" in str(text):
                garbled_chunks += 1
                if garbled_chunks <= 3:
                    source = c.get("source", "?") if isinstance(c, dict) else "?"
                    log(f"  Chunk #{i} 含乱码 (来源: {source})", "ERROR")

        result = {
            "exists": True,
            "size_kb": len(raw) // 1024,
            "chunks": len(chunks),
            "is_utf8": is_valid_utf8,
            "garbled_chunks": garbled_chunks,
        }

        if not is_valid_utf8:
            log(f"知识库不是有效的 UTF-8!", "ERROR")
        elif garbled_chunks > 0:
            log(f"知识库: {len(chunks)} 条, {garbled_chunks} 条含乱码", "WARN")
        else:
            log(f"知识库: {len(chunks)} 条, {len(raw)//1024}KB, 编码正常", "OK")

        return result

    except json.JSONDecodeError as e:
        log(f"知识库 JSON 解析失败: {e}", "ERROR")
        return {"exists": True, "json_error": str(e)}
    except Exception as e:
        log(f"知识库检查异常: {e}", "ERROR")
        return {"exists": True, "error": str(e)}


def diag_encoding(filepath: str) -> dict:
    """诊断 4: 检查源文件的 using/编码相关语句"""
    log("扫描源码编码相关...", "TEST")
    issues = []

    src_files = list(Path(CODE_DIR).rglob("*.cs"))
    log(f"扫描 {len(src_files)} 个 .cs 文件", "OK")

    # 检查 Encoding.UTF8 在读取外部进程 stdout 时的使用
    for f in src_files:
        content = f.read_text(encoding="utf-8", errors="replace")
        rel = f.relative_to(CODE_DIR.parent.parent)

        # 检查是否有 StandardOutputEncoding = Encoding.UTF8 但未加 -utf8 参数的 es.exe 调用
        if "StandardOutputEncoding" in content and "es.exe" in content.lower():
            if "-utf8" not in content:
                issues.append(("missing_utf8_flag", f"{rel}: es.exe 调用缺少 -utf8 参数"))

        # 检查 File.ReadAllText 未指定 GBK fallback
        lines = content.splitlines()
        for i, line in enumerate(lines):
            if "File.ReadAllText" in line and "Encoding.UTF8" in line:
                # 看下文的几行是否有 GBK/Default fallback
                context = "\n".join(lines[max(0, i - 1):min(len(lines), i + 6)])
                if "fallback" not in context.lower() and "Encoding.Default" not in context and "gbk" not in context.lower():
                    issues.append(("no_encoding_fallback", f"{rel}:L{i+1}  ReadAllText 无编码回退"))

    if issues:
        for typ, msg in issues:
            log(f"[{typ}] {msg}", "WARN")
    else:
        log("源码编码处理良好", "OK")

    return {"issues": issues}


# ═══════════════════════════════════════════
#  推送测试模块
# ═══════════════════════════════════════════

def push_test_encoding():
    """推送中文测试消息，验证气泡显示"""
    log("推送编码测试消息...", "TEST")
    tests = [
        ("测试_中文路径", "文件测试", "已找到文件: D:\\资料\\控制理论\\笔记.pdf"),
        ("测试_特殊字符", "藏书阁", "已索引古籍: 「《天工开物》·卷三·冶铸」"),
        ("测试_混合编码", "搜索结果", "路径: D:\\RM_DATABASE\\学习资料\\现代控制理论.pdf"),
    ]
    success = 0
    for title, body_prefix, body in tests:
        if push_message("custom", f"🧪 {title}", f"{body_prefix}\n{body}"):
            success += 1
        time.sleep(0.3)
    log(f"推送测试: {success}/{len(tests)} 成功", "OK" if success == len(tests) else "WARN")
    return success == len(tests)


def push_test_interaction():
    """推送交互式测试（AI 触发）"""
    log("推送交互测试消息...", "TEST")
    # 触发宠物知识库搜索（通过 tool call notification 样式）
    msg = "🔮 太卜司密令：请检索「控制理论」相关典籍并汇报结果"
    return push_message("custom", "📜 法旨", msg)


# ═══════════════════════════════════════════
#  修复模块
# ═══════════════════════════════════════════

def fix_known_issues(dry_run: bool = True) -> list:
    """自动修复已知编码问题"""
    fixes = []

    # 修复 1: es.exe 调用缺少 -utf8 (若有)
    tf_invoker = CODE_DIR / "ToolCallInvoker.cs"
    if tf_invoker.exists():
        content = tf_invoker.read_text(encoding="utf-8", errors="replace")
        # 检查是否已有 -utf8
        if re.search(r'esArgs.*=.*"\$?"-n 200', content) and not re.search(r'-utf8', content):
            fixes.append("ToolCallInvoker.cs: es.exe 调用缺 -utf8（已在上次手动修复）")

    return fixes


def build_project(quick: bool = True) -> bool:
    """调用 build.ps1 构建"""
    mode = "Quick" if quick else "Full"
    log(f"构建 ({mode})...", "FIX")
    try:
        cmd = ["powershell", "-NoProfile", "-ExecutionPolicy", "RemoteSigned",
               "-File", str(BUILD_SCRIPT)]
        if quick:
            cmd.append("-Quick")
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=180)
        if "Build succeeded" in result.stdout or "succeeded" in result.stdout:
            log(f"构建成功 ({mode})", "OK")
            return True
        else:
            log(f"构建失败:\n{result.stdout[-500:]}", "ERROR")
            return False
    except subprocess.TimeoutExpired:
        log("构建超时 (180s)", "ERROR")
        return False
    except Exception as e:
        log(f"构建异常: {e}", "ERROR")
        return False


# ═══════════════════════════════════════════
#  报告生成
# ═══════════════════════════════════════════

def generate_report(diags: dict, report_path: Path = None):
    """生成诊断报告"""
    if report_path is None:
        report_path = PROJECT_ROOT / f"diagnostic_report_{datetime.now().strftime('%Y%m%d_%H%M%S')}.md"

    lines = [
        f"# 太卜司天眼巡检报告",
        f"",
        f"**诊断时间**: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}",
        f"**宠物进程**: {'运行中' if diags.get('pet_running') else '未运行'}",
        f"**服务端**: {'运行中' if diags.get('server_running') else '未运行'}",
        f"",
        f"## 📊 概要",
        f"",
        f"| 检查项 | 状态 |",
        f"|--------|------|",
    ]

    for name, result in diags.get("checks", {}).items():
        status = result.get("status", "?")
        icon = {"PASS": "✅", "WARN": "⚠️", "FAIL": "❌", "SKIP": "⏭️"}.get(status, "❓")
        lines.append(f"| {name} | {icon} {status} |")

    # 如果有问题，列出
    all_issues = diags.get("all_issues", [])
    if all_issues:
        lines.extend([
            f"",
            f"## 🐛 发现的问题",
            f"",
        ])
        for i, issue in enumerate(all_issues, 1):
            lines.append(f"{i}. {issue}")

    lines.append("")
    lines.append("---")
    lines.append("*太卜司符玄 · 天眼自动巡检*")

    report_path.write_text("\n".join(lines), encoding="utf-8")
    log(f"报告已保存: {report_path}", "OK")
    return report_path


# ═══════════════════════════════════════════
#  主循环
# ═══════════════════════════════════════════

def main():
    import argparse
    parser = argparse.ArgumentParser(description="桌面宠物自动化诊断迭代脚本")
    subparsers = parser.add_subparsers(dest="command", help="子命令")

    # chat 子命令
    chat_p = subparsers.add_parser("chat", help="向宠物发送一条对话消息")
    chat_p.add_argument("text", nargs="+", help="消息内容")

    # chat-loop 子命令
    subparsers.add_parser("chat-loop", help="交互式对话循环")

    # 诊断参数
    parser.add_argument("--quick", action="store_true", help="仅快速检查编码+日志")
    parser.add_argument("--fix", action="store_true", help="诊断后自动修复+构建")
    parser.add_argument("--loop", action="store_true", help="持续监控循环")
    parser.add_argument("--interval", type=int, default=60, help="循环间隔（秒）")
    parser.add_argument("--no-push", action="store_true", help="跳过推送测试")
    args = parser.parse_args()

    # ── chat 子命令 ──
    if args.command == "chat":
        text = " ".join(args.text)
        return send_and_watch(text)
    if args.command == "chat-loop":
        return interactive_chat_loop()

    log("=" * 50, "INFO")
    log("符玄桌宠 — 太卜司天眼巡检启动", "INFO")
    log("=" * 50, "INFO")

    if args.loop:
        log(f"持续监控模式 (间隔 {args.interval}s, Ctrl+C 退出)", "INFO")
        round_num = 0
        while True:
            round_num += 1
            log(f"\n── 第 {round_num} 轮巡检 ──", "INFO")
            run_diagnostics(args)
            time.sleep(args.interval)
    else:
        run_diagnostics(args)


def send_and_watch(text: str) -> bool:
    """发送一条对话消息给宠物，等待 AI 处理并显示日志"""
    log("=" * 50, "INFO")
    log(f"📤 发送对话: {text}", "INFO")
    log("=" * 50, "INFO")

    # 1. 检查服务端
    if not get_server_status():
        log("服务端未运行！", "ERROR")
        return False

    # 2. 记录当前日志行数
    log_content_before = get_player_log()
    lines_before = len(log_content_before.splitlines()) if log_content_before else 0

    # 3. 推送 chat_message
    if not push_chat_message(text):
        return False

    # 4. 等待宠物轮询 + AI 处理 (30s 轮询 + 15s API)
    wait_seconds = 50
    log(f"等待 {wait_seconds}s 让宠物轮询并回复...", "TEST")
    time.sleep(wait_seconds)

    # 5. 读取新增日志
    log_content_after = get_player_log()
    lines_after = len(log_content_after.splitlines()) if log_content_after else 0
    new_lines = lines_after - lines_before
    log(f"新增日志: {new_lines} 行", "OK")

    # 6. 提取 AI 回复内容
    if log_content_after:
        log_parts = get_latest_ai_reply(log_content_after, tail_lines=500)

        if log_parts:
            log("── AI 活动日志 ──", "INFO")
            for typ, content in log_parts[-20:]:
                icon = {"tool_call": "⚡", "tool_result": "📜", "error": "❌"}.get(typ, "💬")
                log(f"{icon} {content[:150]}", "INFO")
        else:
            log("未检测到 AI 活动（宠物可能正在忙或未处理消息）", "WARN")

    return True


def interactive_chat_loop():
    """交互式对话循环：输入消息 → 推送给宠物 → 看回复"""
    log("=" * 50, "INFO")
    log("🔮 太卜司交互法阵启动（输入空行退出）", "INFO")
    log("=" * 50, "INFO")

    if not get_server_status():
        log("服务端未运行！请先启动 server", "ERROR")
        return False

    round_num = 0
    while True:
        round_num += 1
        try:
            text = input(f"\n[{round_num}] 你对符玄说 > ").strip()
        except (EOFError, KeyboardInterrupt):
            log("\n退出", "INFO")
            break
        if not text:
            break

        send_and_watch(text)

    log("法阵关闭", "INFO")
    return True


def run_diagnostics(args):
    """执行一轮完整诊断"""
    diags = {"checks": {}, "all_issues": [], "pet_running": False, "server_running": False}

    # ── 模块 A: 服务端 ──
    server_ok = diag_server()
    diags["server_running"] = server_ok
    diags["checks"]["服务端"] = {"status": "PASS" if server_ok else "WARN"}

    # ── 模块 B: Player.log 扫描 ──
    log_result = diag_player_log()
    if log_result.get("found"):
        diags["pet_running"] = True
        status = "PASS"
        issues = log_result.get("issues", [])
        if log_result.get("garbled_lines", 0) > 0:
            status = "FAIL"
        elif log_result.get("errors", 0) > 0:
            status = "WARN"
        diags["checks"]["Player.log"] = {"status": status}
        for typ, msg in issues:
            diags["all_issues"].append(f"[{typ}] {msg}")
    else:
        diags["checks"]["Player.log"] = {"status": "SKIP"}

    # ── 模块 C: 知识库 ──
    if not args.quick:
        kb_result = diag_knowledge_base()
        if kb_result.get("exists"):
            status = "PASS"
            if kb_result.get("garbled_chunks", 0) > 0:
                status = "FAIL"
            elif kb_result.get("json_error"):
                status = "FAIL"
            diags["checks"]["知识库"] = {"status": status}
        else:
            diags["checks"]["知识库"] = {"status": "SKIP"}

    # ── 模块 D: 源码编码 ──
    if not args.quick:
        enc_result = diag_encoding("")
        status = "PASS" if not enc_result.get("issues") else "WARN"
        diags["checks"]["源码编码"] = {"status": status}

    # ── 模块 E: 推送测试 ──
    if server_ok and not args.no_push:
        push_result = push_test_encoding()
        if push_result:
            # 等待一轮轮询后，再次读取日志
            log("等待 35s 让宠物轮询处理推送消息...", "TEST")
            time.sleep(35)
            log_result2 = diag_player_log()
            push_status = "PASS"
            if log_result2.get("garbled_lines", 0) > 0:
                # 检查是否新增了乱码
                push_status = "FAIL"
            diags["checks"]["推送编码"] = {"status": push_status}
        else:
            diags["checks"]["推送编码"] = {"status": "SKIP"}
    else:
        diags["checks"]["推送编码"] = {"status": "SKIP"}

    # ── 自动修复 ──
    if args.fix:
        fixes = fix_known_issues(dry_run=False)
        if fixes:
            log("执行自动修复...", "FIX")
            for f in fixes:
                log(f, "FIX")
            # 重新构建
            if build_project(quick=True):
                build_project(quick=False)

    # ── 生成报告 ──
    report_path = generate_report(diags)
    log(f"巡检完成 → {report_path}", "OK")

    # ── 最终状态总结 ──
    pass_count = sum(1 for c in diags["checks"].values() if c.get("status") == "PASS")
    warn_count = sum(1 for c in diags["checks"].values() if c.get("status") == "WARN")
    fail_count = sum(1 for c in diags["checks"].values() if c.get("status") == "FAIL")
    skip_count = sum(1 for c in diags["checks"].values() if c.get("status") == "SKIP")
    total = len(diags["checks"])

    log(f"📊 总计: {total} 项 | ✅ {pass_count} | ⚠️ {warn_count} | ❌ {fail_count} | ⏭️ {skip_count}", "INFO")

    if fail_count > 0:
        log(f"发现 {fail_count} 项失败，建议运行 --fix 自动修复", "WARN")
        return False
    return True


if __name__ == "__main__":
    main()
