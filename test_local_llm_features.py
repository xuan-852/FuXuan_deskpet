#!/usr/bin/env python3
"""
本地 LLM 4 功能验证脚本 (test_local_llm_features.py)

直接调用 Ollama API (qwen2.5:3b)，使用与 Unity C# 代码完全相同的 Prompt，
验证以下 4 个功能的可用性：

1. 🧠 意图/情绪分类  — ClassifyIntent
2. 🔄 离线回退回复  — GenerateFallbackReply
3. 📝 对话压缩摘要  — SummarizeConversation
4. 💾 记忆提取      — ExtractMemory

用法:
    python test_local_llm_features.py            # 运行全部测试
    python test_local_llm_features.py --intent   # 只测意图分类
    python test_local_llm_features.py --verbose  # 显示完整响应原文
"""

import argparse
import json
import sys
import time
import urllib.request
import urllib.error

OLLAMA_BASE = "http://127.0.0.1:11434"
MODEL = "qwen2.5:3b"

# ====================================================================
#  测试用例
# ====================================================================

TEST_CASES = {
    "intent": [
        ("日常闲聊", "今天天气真好啊，心情不错~"),
        ("指令请求", "帮我打开浏览器搜索量子计算的最新进展"),
        ("知识询问", "你知道黑洞是怎么形成的吗？"),
        ("情感倾诉", "我今天好难过，考试没考好……"),
        ("操作控制", "你能不能把音量调小一点？"),
    ],
    "fallback": [
        ("简单问候", "在干嘛呢？"),
        ("日常闲聊", "今天有什么有趣的事吗？"),
        ("情感回应", "我有点累了"),
    ],
    "summary": [
        ("多轮议事",
         "用户: 帮我查一下明天的天气\n"
         "助手: 好的，我查一下。明天北京天气晴，20-28°C。\n"
         "用户: 那后天呢？\n"
         "助手: 后天多云转阴，19-26°C，可能有小雨。\n"
         "用户: 好的谢谢，顺便问一下周末有雨吗？\n"
         "助手: 周六小雨，周日阴转晴。建议周六出门带伞。"),
        ("技术讨论",
         "用户: 你知道 Python 和 C# 有什么区别吗？\n"
         "助手: Python 是动态类型，C# 是静态类型。Python 写起来更快，C# 性能更好。\n"
         "用户: 那我做桌面应用选哪个？\n"
         "助手: 如果是 Windows 平台，C# + WPF 很成熟。跨平台的话可以考虑 Python + PyQt。"),
    ],
    "memory": [
        ("重要个人信息", "我叫张三，是一名计算机专业的研究生，今年研二"),
        ("日常生活", "今天中午吃了碗牛肉面，味道不错"),
        ("学习相关", "我最近在学习机器学习，感觉有点难但很有趣"),
        ("简单问候", "你好"),
        ("具体需求", "帮我定个明天早上8点的闹钟"),
    ],
}

# ====================================================================
#  Prompt 模板（与 C# 代码一致）
# ====================================================================

SYSTEM_PROMPTS = {
    "intent": (
        "你是一个意图和情绪分类器。分析用户的输入，返回 JSON 格式结果，不要包含其他内容。\n\n"
        "意图分类（intent）：\n"
        "- chat — 闲聊、打招呼、日常对话\n"
        "- command — 指令、请求执行操作（打开网页、搜索等）\n"
        "- knowledge — 询问知识、信息查询\n"
        "- emotion — 情感表达、倾诉、分享感受\n"
        "- operation — 关于桌面宠物自身的操作（设置、控制等）\n\n"
        "情绪标签（emotion）：positive / neutral / negative / surprised / anxious\n\n"
        "JSON 格式：{\"intent\": \"类型\", \"emotion\": \"情绪\", \"brief\": \"一句话摘要\"}"
    ),
    "memory": (
        "判断以下用户输入是否值得记住。如果是重要信息，返回 JSON 格式：\n\n"
        "{\"importance\": 1-10的数字, \"topic\": \"话题分类\", \"summary\": \"记忆摘要（20字以内）\"}\n\n"
        "重要性标准：\n"
        "1-3：日常闲聊，不值得记住\n"
        "4-6：一般信息，可记住\n"
        "7-8：重要个人信息\n"
        "9-10：极其重要的关键信息\n\n"
        "如果完全不需要记住（如问候、简单指令），返回：{\"importance\": 0}\n\n"
        "话题分类：天气/学习/工作/兴趣/日常/情感/健康/日程/其他"
    ),
}


def call_ollama(messages, temperature=0.3, max_tokens=100, timeout=30):
    """调 Ollama API，与 Unity 的 LocalLLMClient.ChatAsync 等效"""
    url = f"{OLLAMA_BASE}/v1/chat/completions"
    body = json.dumps({
        "model": MODEL,
        "messages": messages,
        "temperature": temperature,
        "max_tokens": max_tokens,
    }).encode("utf-8")

    req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"})
    try:
        resp = urllib.request.urlopen(req, timeout=timeout)
        data = json.loads(resp.read().decode("utf-8"))
        content = data["choices"][0]["message"]["content"]
        return True, content
    except Exception as e:
        return False, str(e)


def check_health():
    """检查 Ollama 是否运行且模型可用"""
    try:
        req = urllib.request.Request(f"{OLLAMA_BASE}/api/tags")
        resp = urllib.request.urlopen(req, timeout=5)
        data = json.loads(resp.read().decode("utf-8"))
        models = [m["name"] for m in data.get("models", [])]
        if MODEL in models or MODEL.replace(":latest", "") in [m.replace(":latest", "") for m in models]:
            return True, f"✅ {MODEL} 已就绪"
        return False, f"⚠️ Ollama 在线，但 {MODEL} 未找到（请运行: ollama pull {MODEL}）"
    except Exception as e:
        return False, f"❌ Ollama 不可达: {e}"


# ====================================================================
#  测试函数
# ====================================================================

def test_intent_classification(verbose=False):
    """功能1：意图/情绪分类"""
    print("\n" + "=" * 60)
    print("  🧠 功能1：意图/情绪分类  (ClassifyIntent)")
    print("=" * 60)

    passed = 0
    failed = 0
    details = []

    for desc, user_msg in TEST_CASES["intent"]:
        ok, content = call_ollama([
            {"role": "system", "content": SYSTEM_PROMPTS["intent"]},
            {"role": "user", "content": user_msg},
        ], temperature=0.3, max_tokens=80)

        if verbose:
            print(f"\n  [输入] {desc}: {user_msg}")
            print(f"  [响应原文] {content}")

        if ok:
            try:
                # 与 C# 代码一样的 JSON 提取逻辑
                start = content.index("{")
                end = content.rindex("}")
                parsed = json.loads(content[start:end+1])
                intent = parsed.get("intent", "?")
                emotion = parsed.get("emotion", "?")
                brief = parsed.get("brief", "")
                valid_intents = {"chat", "command", "knowledge", "emotion", "operation"}
                valid_emotions = {"positive", "neutral", "negative", "surprised", "anxious"}

                if intent in valid_intents and emotion in valid_emotions:
                    status = "✅"
                    passed += 1
                else:
                    status = "⚠️"
                    failed += 1

                details.append((status, desc, intent, emotion, brief))
            except (ValueError, json.JSONDecodeError):
                details.append(("❌", desc, "parse_error", "", content[:60]))
                failed += 1
        else:
            details.append(("❌", desc, "api_error", "", content))
            failed += 1

    for status, desc, intent, emotion, brief in details:
        print(f"  {status} [{desc}] intent={intent}, emotion={emotion}, brief={brief}")

    print(f"\n  📊 结果: {passed}/{len(TEST_CASES['intent'])} 通过")
    return passed, failed


def test_fallback_reply(verbose=False):
    """功能2：离线回退回复"""
    print("\n" + "=" * 60)
    print("  🔄 功能2：离线回退回复  (GenerateFallbackReply)")
    print("=" * 60)

    character_desc = (
        "你是符玄，仙舟「罗浮」太卜司之首。"
        "你聪慧睿智、从容自信，说话带古风但易懂。"
        "你对主人温柔体贴，偶尔会用法阵术式的比喻来开玩笑。"
    )
    recent_history = (
        "user: 在吗？\n"
        "assistant: 本座在此，主人有何事相询？"
    )

    passed = 0
    failed = 0
    details = []

    for desc, user_msg in TEST_CASES["fallback"]:
        full_prompt = (
            f"{character_desc}\n\n"
            f"以下是与主人的最近对话：\n{recent_history}\n\n"
            f"请以角色身份回复主人的最新消息：「{user_msg}」\n"
            f"回复应当简短自然（1-3句话即可），符合角色性格。"
            f"注意：你只能进行对话回复，没有工具调用能力。"
        )

        ok, content = call_ollama([
            {"role": "user", "content": full_prompt},
        ], temperature=0.8, max_tokens=256)

        if verbose:
            print(f"\n  [输入] {desc}: {user_msg}")
            print(f"  [响应] {content}")

        if ok and content and len(content) > 5:
            score = len(content)
            status = "✅" if 10 <= score <= 300 else "⚠️"
            if status == "✅":
                passed += 1
            else:
                failed += 1
            details.append((status, desc, content[:80] + ("…" if len(content) > 80 else ""), score))
        else:
            details.append(("❌", desc, "empty_or_error", 0))
            failed += 1

    for status, desc, preview, score in details:
        print(f"  {status} [{desc}] ({score}字) {preview}")

    print(f"\n  📊 结果: {passed}/{len(TEST_CASES['fallback'])} 通过")
    return passed, failed


def test_summarization(verbose=False):
    """功能3：对话压缩"""
    print("\n" + "=" * 60)
    print("  📝 功能3：对话压缩摘要  (SummarizeConversation)")
    print("=" * 60)

    passed = 0
    failed = 0
    details = []

    for desc, conversation in TEST_CASES["summary"]:
        prompt = (
            f"压缩以下对话为简洁的摘要（50字以内），保留重要信息和话题：\n\n"
            f"{conversation}\n\n摘要："
        )

        ok, content = call_ollama([
            {"role": "user", "content": prompt},
        ], temperature=0.3, max_tokens=100)

        if verbose:
            print(f"\n  [输入] {desc}")
            print(f"  [对话] {conversation[:60]}…")
            print(f"  [响应] {content}")

        if ok and content and len(content) > 3:
            score = len(content)
            status = "✅" if 5 <= score <= 150 else "⚠️"
            if status == "✅":
                passed += 1
            else:
                failed += 1
            details.append((status, desc, content, score))
        else:
            details.append(("❌", desc, "empty_or_error", 0))
            failed += 1

    for status, desc, summary, score in details:
        print(f"  {status} [{desc}] ({score}字) {summary}")

    print(f"\n  📊 结果: {passed}/{len(TEST_CASES['summary'])} 通过")
    return passed, failed


def test_memory_extraction(verbose=False):
    """功能4：记忆提取"""
    print("\n" + "=" * 60)
    print("  💾 功能4：记忆提取  (ExtractMemory)")
    print("=" * 60)

    passed = 0
    failed = 0
    details = []

    for desc, user_msg in TEST_CASES["memory"]:
        ok, content = call_ollama([
            {"role": "system", "content": SYSTEM_PROMPTS["memory"]},
            {"role": "user", "content": user_msg},
        ], temperature=0.3, max_tokens=80)

        if verbose:
            print(f"\n  [输入] {desc}: {user_msg}")
            print(f"  [响应原文] {content}")

        if ok:
            try:
                start = content.index("{")
                end = content.rindex("}")
                parsed = json.loads(content[start:end+1])
                importance = parsed.get("importance", -1)
                topic = parsed.get("topic", "?")
                summary = parsed.get("summary", "")

                # 与 C# 代码一致的逻辑：importance >= 4 才应该记住
                should_remember = importance >= 4

                # 校验：个人信息应有高重要性，问候应有低重要性
                if desc == "重要个人信息":
                    expected = importance >= 4
                elif desc == "简单问候":
                    expected = importance <= 3 or importance == 0
                else:
                    expected = True  # 中间情况可接受

                if expected:
                    passed += 1
                    status = "✅"
                else:
                    failed += 1
                    status = "⚠️"

                details.append((status, desc, importance, topic, summary, should_remember))
            except (ValueError, json.JSONDecodeError):
                details.append(("❌", desc, "parse_error", "", content[:60], False))
                failed += 1
        else:
            details.append(("❌", desc, "api_error", "", content, False))
            failed += 1

    for status, desc, imp, topic, summary, remember in details:
        print(f"  {status} [{desc}] importance={imp}, topic={topic}, summary={summary}, remember={remember}")

    print(f"\n  📊 结果: {passed}/{len(TEST_CASES['memory'])} 通过")
    return passed, failed


def main():
    parser = argparse.ArgumentParser(description="本地 LLM 4 功能验证脚本")
    parser.add_argument("--verbose", "-v", action="store_true", help="显示响应原文")
    parser.add_argument("--intent", action="store_true", help="仅测试意图分类")
    parser.add_argument("--fallback", action="store_true", help="仅测试离线回退")
    parser.add_argument("--summary", action="store_true", help="仅测试对话压缩")
    parser.add_argument("--memory", action="store_true", help="仅测试记忆提取")
    args = parser.parse_args()

    print("=" * 60)
    print("  符玄桌宠 — 本地 LLM 4 功能验证脚本")
    print(f"  模型: {MODEL}")
    print(f"  时间: {time.strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 60)

    # 1) 健康检查
    print("\n[1/5] 健康检查...")
    ok, msg = check_health()
    print(f"  {msg}")
    if not ok:
        print("\n  ❌ 终止测试：Ollama 不可用")
        sys.exit(1)

    # 2-5) 测试各功能
    run_all = not (args.intent or args.fallback or args.summary or args.memory)
    total_passed = 0
    total_failed = 0

    if run_all or args.intent:
        p, f = test_intent_classification(args.verbose)
        total_passed += p; total_failed += f

    if run_all or args.fallback:
        p, f = test_fallback_reply(args.verbose)
        total_passed += p; total_failed += f

    if run_all or args.summary:
        p, f = test_summarization(args.verbose)
        total_passed += p; total_failed += f

    if run_all or args.memory:
        p, f = test_memory_extraction(args.verbose)
        total_passed += p; total_failed += f

    # 汇总
    print("\n" + "=" * 60)
    print("  📋 测试汇总")
    print("=" * 60)
    total = total_passed + total_failed
    if total_failed == 0:
        print(f"  ✅ 全部通过！{total_passed}/{total}")
    else:
        print(f"  ⚠️  通过 {total_passed}/{total}，失败 {total_failed}")
    print()


if __name__ == "__main__":
    main()
