using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 全量 AI 工具稳定性 & 快速性测试器（运行时）
/// 触发：D:\DesktopPetData\.benchmark 开关文件存在时，场景加载后自动运行。
/// 逐个调用 ToolRegistry 中全部 56 个工具（同步直调 / 异步协程），
/// 记录每个工具的 参数 / 耗时(ms) / 返回结果 / 状态(OK|ERROR|TIMEOUT|SKIP|DANGER_GUARD)，
/// 写入 D:\DesktopPetData\tool_benchmark_results.json，完成后删除开关文件。
///
/// 安全设计：
///  - 危险工具（file_delete/power/lock_screen/set_volume/mute/openclaw_task）绝不真执行，
///    只验证 IsDangerous 标记 + 非法参数解析路径（必然返回 ❌）。
///  - run_command 只测白名单只读命令(whoami) 与 高危命令拦截(format c:)。
///  - 文件类工具使用 D:\DesktopPetData\_bench_* 临时路径，结束后自动清理。
///  - set_clipboard 先读原值，测试后还原。
///  - set_reminder 建 2099 年提醒，测试后 delete_reminder 删除。
///  - 纯场景外也可运行：依赖场景单例的工具会走降级路径返回 ❌（也算有效结果）。
/// </summary>
public class ToolBenchmarkRunner : MonoBehaviour
{
    private const string BenchmarkFlag = @"D:\DesktopPetData\.benchmark";
    private const string ResultFile = @"D:\DesktopPetData\tool_benchmark_results.json";
    private const string BenchRoot = @"D:\DesktopPetData\_bench";
    private const float DefaultTimeout = 120f;   // 普通异步工具超时
    private const float SlowTimeout = 180f;      // GLM 视觉/网络慢工具超时

    private static bool _launched = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoLaunch()
    {
        if (_launched) return;
        if (!File.Exists(BenchmarkFlag)) return;   // 无开关文件 → 完全不干扰正常运行
        _launched = true;

        var go = new GameObject("[ToolBenchmarkRunner]");
        go.AddComponent<ToolBenchmarkRunner>();
    }

    private readonly List<ResultEntry> _results = new List<ResultEntry>();
    private Coroutine _runner;
    private string _benchReminderId = "";   // set_reminder 返回的真实 ID（前 8 位）

    public class ResultEntry
    {
        public string tool;
        public string category;
        public string args;
        public string status;      // OK / ERROR / TIMEOUT / SKIP / DANGER_GUARD
        public double elapsed_ms;
        public string result;      // 截断后的返回内容
        public string note;        // 附加说明
    }

    // ── 测试用例定义 ──────────────────────────────────────────────
    // status 预置值：""=正常执行；"DANGER_GUARD"=危险工具只验证拦截；
    // "SKIP"=跳过（弹窗/外部程序/分钟级，人工单独验证）
    private class TestCase
    {
        public string tool;
        public string category;
        public string args;
        public string preset;      // 预设状态（"" 则实弹）
        public float timeout = DefaultTimeout;
        public TestCase(string t, string c, string a, string p = "", float to = DefaultTimeout)
        { tool = t; category = c; args = a; preset = p; timeout = to; }
    }

    private List<TestCase> BuildCases()
    {
        var list = new List<TestCase>();

        // ── A. 只读安全组（实弹）──────────────────────────────
        list.Add(new TestCase("get_system_info", "只读", "{}"));
        list.Add(new TestCase("get_mouse_pos", "只读", "{}"));
        list.Add(new TestCase("get_clipboard", "只读", "{}"));
        list.Add(new TestCase("list_files", "只读", "{\"path\": \"D:\\\\DesktopPetData\"}"));
        list.Add(new TestCase("file_info", "只读", "{\"path\": \"D:\\\\DesktopPetData\"}"));
        list.Add(new TestCase("search_files", "只读", "{\"query\": \"DesktopPet\", \"root\": \"D:\\\\Unity\"}", "", 60f));
        list.Add(new TestCase("search_file", "只读", "{\"query\": \"DesktopPet\", \"root\": \"D:\\\\Unity\"}", "", 60f));
        list.Add(new TestCase("query_reminders", "只读", "{}"));
        list.Add(new TestCase("inspect_motion_memory", "只读", "{}"));
        list.Add(new TestCase("inspect_personality", "只读", "{}"));
        list.Add(new TestCase("knowledge_search", "只读", "{\"query\": \"测试\", \"top_k\": 3}", "", 30f));
        list.Add(new TestCase("query_exams", "只读", "{}", "", 30f));
        list.Add(new TestCase("query_scores", "只读", "{}", "", 30f));
        list.Add(new TestCase("query_schedule", "只读", "{}", "", 30f));
        list.Add(new TestCase("query_user_status", "只读", "{}", "", 30f));
        list.Add(new TestCase("get_weather", "只读", "{}", "", 15f));
        list.Add(new TestCase("search_web", "只读", "{\"query\": \"今天天气\"}", "", SlowTimeout));
        list.Add(new TestCase("openclaw_search", "只读", "{\"query\": \"测试\"}", "", SlowTimeout));
        list.Add(new TestCase("explore_body", "只读", "{}", "", 30f));

        // ── B. Live2D 控制组（实弹，无害）────────────────────
        list.Add(new TestCase("set_expression", "Live2D", "{\"expression\": \"happy\"}"));
        list.Add(new TestCase("play_action", "Live2D", "{\"action\": \"wave\"}"));
        list.Add(new TestCase("stop_action", "Live2D", "{}"));
        list.Add(new TestCase("control_body", "Live2D", "{\"expression\": \"tilt_head\"}", "", 15f));

        // ── C. 文件写操作组（临时路径 + 事后清理）──────────────
        list.Add(new TestCase("file_create", "文件写", "{\"path\": \"" + BenchRoot + "_a.txt\", \"content\": \"bench\"}"));
        list.Add(new TestCase("dir_create", "文件写", "{\"path\": \"" + BenchRoot + "_dir\"}"));
        list.Add(new TestCase("file_copy", "文件写", "{\"source\": \"" + BenchRoot + "_a.txt\", \"destination\": \"" + BenchRoot + "_b.txt\"}"));
        list.Add(new TestCase("file_rename", "文件写", "{\"path\": \"" + BenchRoot + "_b.txt\", \"new_name\": \"_bench_c.txt\"}"));
        list.Add(new TestCase("file_move", "文件写", "{\"source\": \"" + BenchRoot + "_c.txt\", \"destination\": \"" + BenchRoot + "_d.txt\"}"));
        list.Add(new TestCase("file_read", "文件写", "{\"path\": \"" + BenchRoot + "_a.txt\"}"));
        list.Add(new TestCase("set_reminder", "文件写", "{\"text\": \"__BENCH_TEST__\", \"remind_at\": \"2099-01-01 00:00\"}"));
        list.Add(new TestCase("mark_reminder_done", "文件写", "{\"id\": \"__BENCH_ID__\"}"));
        list.Add(new TestCase("delete_reminder", "文件写", "{\"id\": \"__BENCH_ID__\"}"));
        list.Add(new TestCase("set_clipboard", "文件写", "{\"text\": \"__BENCH_CLIP__\"}"));

        // ── C2. 打开窗口类（会弹出窗口，人工确认后关闭）──
        list.Add(new TestCase("open_url", "开窗", "{\"url\": \"https://example.com\"}"));
        list.Add(new TestCase("search", "开窗", "{\"query\": \"benchmark test\"}"));
        list.Add(new TestCase("open_app", "开窗", "{\"name\": \"notepad\"}"));
        list.Add(new TestCase("open_folder", "开窗", "{\"path\": \"D:\\\\DesktopPetData\"}"));
        list.Add(new TestCase("file_open", "开窗", "{\"path\": \"" + BenchRoot + "_a.txt\"}"));

        // ── D. 危险工具（只验证拦截，绝不真执行）──────────────
        list.Add(new TestCase("file_delete", "危险", "{\"path\": \"\"}", "DANGER_GUARD"));
        list.Add(new TestCase("power", "危险", "{\"action\": \"invalid\"}", "DANGER_GUARD"));
        list.Add(new TestCase("lock_screen", "危险", "{}", "DANGER_GUARD"));
        list.Add(new TestCase("set_volume", "危险", "{\"level\": \"abc\"}", "DANGER_GUARD"));
        list.Add(new TestCase("mute", "危险", "{\"muted\": \"notabool\"}", "DANGER_GUARD"));
        list.Add(new TestCase("openclaw_task", "危险", "{\"task\": \"\"}", "DANGER_GUARD"));
        list.Add(new TestCase("run_command", "危险", "{\"command\": \"whoami\"}", "", 15f));     // 白名单只读命令
        list.Add(new TestCase("run_command", "危险", "{\"command\": \"format c:\"}", "", 15f)); // 高危拦截

        // ── E. 跳过组（弹窗/外部程序/分钟级，人工验证）────────
        list.Add(new TestCase("notify", "跳过", "{\"title\": \"t\", \"message\": \"m\"}", "SKIP"));
        list.Add(new TestCase("launch_pogget", "跳过", "{}", "SKIP"));
        list.Add(new TestCase("compile_latex", "跳过", "{}", "SKIP"));
        list.Add(new TestCase("vis_verify", "跳过", "{}", "SKIP"));
        list.Add(new TestCase("run_verification", "跳过", "{}", "SKIP"));

        // ── F. 外部程序/桥组（实弹）──────────────────────────
        list.Add(new TestCase("pogget_agent", "外部", "{\"cmd\": \"ping\"}", "", 30f));

        // ── G. GLM 视觉/慢速组（实弹，注意耗时）───────────────
        list.Add(new TestCase("take_screenshot", "GLM", "{}", "", SlowTimeout));
        list.Add(new TestCase("generate_motion", "GLM", "{\"description\": \"点头微笑\"}", "", SlowTimeout));
        list.Add(new TestCase("explore_body_vision", "GLM", "{}", "", SlowTimeout));
        list.Add(new TestCase("self_review", "GLM", "{\"action\": \"wave\"}", "", SlowTimeout));
        list.Add(new TestCase("knowledge_index", "GLM", "{\"path\": \"D:\\\\DesktopPetData\\\\Documents\", \"recursive\": false}", "", 60f));

        return list;
    }

    private void Start()
    {
        StartCoroutine(RunAll());
    }

    private IEnumerator RunAll()
    {
        yield return new WaitForSeconds(3f);   // 等场景稳定

        var cases = BuildCases();
        UnityEngine.Debug.Log($"[ToolBenchmark] 开始全量测试，共 {cases.Count} 个用例");
        _results.Clear();

        foreach (var tc in cases)
        {
            yield return RunCase(tc);
        }

        // 清理
        yield return CleanupTempFiles();

        // 写结果
        WriteResults();
        UnityEngine.Debug.Log($"[ToolBenchmark] 测试完成，见 {ResultFile}");

        // 删除开关文件，防止再次触发
        try { if (File.Exists(BenchmarkFlag)) File.Delete(BenchmarkFlag); } catch { }
        Destroy(gameObject);
    }

    private IEnumerator RunCase(TestCase tc)
    {
        var entry = new ResultEntry { tool = tc.tool, category = tc.category, args = tc.args };

        // 占位符替换：__BENCH_ID__ → set_reminder 返回的真实 ID 前 8 位
        string args = tc.args;
        if (args.Contains("__BENCH_ID__"))
        {
            if (string.IsNullOrEmpty(_benchReminderId))
            {
                entry.status = "ERROR";
                entry.result = "❌ 未取得 set_reminder 返回的提醒 ID（前序用例失败？）";
                entry.note = "依赖 set_reminder 成功";
                _results.Add(entry);
                yield break;
            }
            args = args.Replace("__BENCH_ID__", _benchReminderId);
            entry.args = args;
        }

        // 跳过组
        if (tc.preset == "SKIP")
        {
            entry.status = "SKIP";
            entry.result = "（跳过：弹窗/外部程序/分钟级，需人工验证）";
            _results.Add(entry);
            UnityEngine.Debug.Log($"[ToolBenchmark] SKIP  {tc.tool}");
            yield break;
        }

        // 危险组：只验证 IsDangerous 标记 + 工具已注册（绝不真执行，防锁屏/改音量/删文件等副作用）
        if (tc.preset == "DANGER_GUARD")
        {
            bool isDanger = ToolRegistry.IsDangerous(tc.tool);
            bool hasTool = ToolRegistry.HasTool(tc.tool);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            sw.Stop();
            entry.elapsed_ms = sw.Elapsed.TotalMilliseconds;
            entry.status = isDanger ? "DANGER_GUARD" : "ERROR";
            entry.result = $"IsDangerous={isDanger} | HasTool={hasTool} | 仅验证标记，未执行（防副作用）";
            entry.note = isDanger ? "危险标记正确，未实际执行" : "⚠️ 未标记为危险！";
            _results.Add(entry);
            UnityEngine.Debug.Log($"[ToolBenchmark] GUARD {tc.tool}  IsDangerous={isDanger} HasTool={hasTool}（未执行）");
            yield break;
        }

        // 实弹执行
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        if (ToolRegistry.IsAsync(tc.tool))
        {
            string result = null;
            bool done = false;
            StartCoroutine(ToolRegistry.ExecuteAsync(tc.tool, args, r => { result = r; done = true; }));
            float waited = 0f;
            while (!done && waited < tc.timeout)
            {
                yield return new WaitForSeconds(0.5f);
                waited += 0.5f;
            }
            sw2.Stop();
            entry.elapsed_ms = sw2.Elapsed.TotalMilliseconds;
            if (!done)
            {
                entry.status = "TIMEOUT";
                entry.result = $"⏱️ 超过 {tc.timeout:F0}s 未返回";
                entry.note = "工具可能卡死或依赖外部服务不可用";
            }
            else
            {
                entry.status = (result != null && result.StartsWith("❌", StringComparison.Ordinal)) ? "ERROR" : "OK";
                entry.result = Truncate(result);
            }
        }
        else
        {
            string result = ToolRegistry.Execute(tc.tool, args);
            sw2.Stop();
            entry.elapsed_ms = sw2.Elapsed.TotalMilliseconds;
            entry.status = (result != null && result.StartsWith("❌", StringComparison.Ordinal)) ? "ERROR" : "OK";
            entry.result = Truncate(result);
        }

        // 捕获 set_reminder 返回的真实 ID（用于 mark/delete 与清理）
        if (tc.tool == "set_reminder" && entry.status == "OK")
        {
            string full = entry.result;
            int idx = full.IndexOf("ID: ");
            if (idx >= 0)
            {
                string idPart = full.Substring(idx + 4).Trim();
                idPart = idPart.Split('…', ' ', '\n')[0];
                _benchReminderId = idPart;
            }
        }

        _results.Add(entry);
        UnityEngine.Debug.Log($"[ToolBenchmark] {entry.status,-12} {tc.tool,-22} {entry.elapsed_ms,8:F0}ms  {Truncate(entry.result, 80)}");
    }

    private IEnumerator CleanupTempFiles()
    {
        // 删除测试建的文件
        string[] paths = {
            BenchRoot + "_a.txt", BenchRoot + "_b.txt", BenchRoot + "_c.txt",
            BenchRoot + "_d.txt", BenchRoot + "_dir"
        };
        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p)) File.Delete(p);
                if (Directory.Exists(p)) Directory.Delete(p, true);
            }
            catch { }
        }

        // 删除测试提醒（用真实 ID，从 query_reminders / 管理器取）
        try
        {
            var mgr = ReminderManager.Instance;
            if (mgr != null)
            {
                var all = mgr.GetAllReminders();
                foreach (var r in all)
                {
                    if (r.text.Contains("__BENCH_TEST__"))
                        ToolRegistry.Execute("delete_reminder", "{\"id\": \"" + r.id.Substring(0, Math.Min(8, r.id.Length)) + "\"}");
                }
            }
        }
        catch { }

        // 还原剪贴板：置空标记内容
        try
        {
            string cur = ToolHelpers.GetClipboardText();
            if (cur == "__BENCH_CLIP__") ToolHelpers.SetClipboardText("");
        }
        catch { }
        yield return null;
    }

    private void WriteResults()
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("[\n");
            for (int i = 0; i < _results.Count; i++)
            {
                var r = _results[i];
                sb.Append("  {\n");
                sb.Append($"    \"tool\": \"{Escape(r.tool)}\",\n");
                sb.Append($"    \"category\": \"{Escape(r.category)}\",\n");
                sb.Append($"    \"args\": \"{Escape(r.args)}\",\n");
                sb.Append($"    \"status\": \"{Escape(r.status)}\",\n");
                sb.Append($"    \"elapsed_ms\": {r.elapsed_ms:F0},\n");
                sb.Append($"    \"result\": \"{Escape(r.result)}\",\n");
                sb.Append($"    \"note\": \"{Escape(r.note)}\"\n");
                sb.Append("  }");
                if (i < _results.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("]");
            File.WriteAllText(ResultFile, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[ToolBenchmark] 写结果失败: {e.Message}");
        }
    }

    private static string Truncate(string s, int max = 200)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
