using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// 长效 Token 消耗日志（2026-08-16）— 跨重启持久化到磁盘，用于定位"越花越多"来自哪个调用点。
///
/// 与内存版 UsageStats 的区别：
///   UsageStats   — 内存累计，供「消耗」面板实时展示（重启丢失）
///   UsageLogger  — 追加式 JSONL 落盘，每次 API 调用记一行（时间/来源/模型/tokens/费用），
///                  重启后仍保留，且启动时回灌 UsageStats 让面板显示跨重启累计。
///
/// 文件位置：DataPathConfig.DataRoot/usage_log.jsonl
/// 存储上限：MAX_BYTES（默认 2MB），超限从头部截断保留最新。
///
/// 来源标记（source）约定：
///   chat       — ChatManager 日常对话（含工具调用）
///   motion     — MotionTranslator DeepSeek 兜底（本地优先失败后）
///   idle       — IdleChatGenerator 闲话/问候回退
///   weather    — TimeWeatherController 天气语录回退
///   reflect    — PetMemory 记忆提炼
///   glm        — GLM 视觉/镜鉴调用
///   local      — Ollama 本地模型（免费，记录对比用）
/// </summary>
public static class UsageLogger
{
    private const long MAX_BYTES = 2 * 1024 * 1024;   // 2MB 存储上限
    private const int MAX_LINES = 20000;               // 行数兜底上限

    private static readonly object _lock = new object();
    private static string _filePath;
    private static bool _runtimeIdentityRecorded;

    /// <summary>确保日志文件路径初始化（DataPathConfig 可用后调用）</summary>
    public static void EnsureInit()
    {
        if (_filePath != null) return;
        _filePath = Path.Combine(DataPathConfig.DataRoot, "usage_log.jsonl");
        try { Directory.CreateDirectory(DataPathConfig.DataRoot); } catch { }
    }

    /// <summary>日志文件路径（未初始化时为 null）</summary>
    public static string FilePath => _filePath;

    /// <summary>
    /// 记录本次进程实际读取到的密钥身份。只写短标识和 SHA-256，不写完整 Key。
    /// 官方平台账单无法由桌宠实时读取，因此账单归属必须以该身份和平台后台人工核对。
    /// </summary>
    public static void RecordRuntimeIdentity()
    {
        EnsureInit();
        lock (_lock)
        {
            if (_runtimeIdentityRecorded || string.IsNullOrEmpty(_filePath)) return;

            string deepSeekKey = ChatConfig.ApiKey;
            string glmKey = ChatConfig.GlmApiKey;
            string mode = ChatConfig.UseOllamaMode ? "ollama" : "cloud";
            string line = new StringBuilder(512)
                .Append("{\"t\":\"")
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append("\",\"kind\":\"runtime_identity\",\"mode\":\"")
                .Append(JsonEscape(mode))
                .Append("\",\"deepseek_key_id\":\"")
                .Append(JsonEscape(ApiKeyIdentity.GetKeyId(deepSeekKey)))
                .Append("\",\"deepseek_key_hash\":\"")
                .Append(JsonEscape(ApiKeyIdentity.GetKeyHash(deepSeekKey)))
                .Append("\",\"glm_key_id\":\"")
                .Append(JsonEscape(ApiKeyIdentity.GetKeyId(glmKey)))
                .Append("\",\"glm_key_hash\":\"")
                .Append(JsonEscape(ApiKeyIdentity.GetKeyHash(glmKey)))
                .Append("\",\"billing_attribution\":\"")
                .Append(ApiKeyIdentity.ManualBillingCheck)
                .Append("\"}\n")
                .ToString();

            try
            {
                File.AppendAllText(_filePath, line, new UTF8Encoding(false));
                _runtimeIdentityRecorded = true;
                UnityEngine.Debug.Log(
                    $"[UsageLogger] 运行身份已记录: mode={mode}, " +
                    $"deepseek={ApiKeyIdentity.GetKeyId(deepSeekKey)}, " +
                    $"glm={ApiKeyIdentity.GetKeyId(glmKey)}; " +
                    "账单归属仍需官方平台人工核对");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[UsageLogger] 运行身份写入失败: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 记录一次 API 调用消耗并落盘。
    /// </summary>
    /// <param name="source">调用来源（见类注释约定）</param>
    /// <param name="model">模型名（如 deepseek-v4-flash / glm-4.6v-flash / qwen2.5:3b）</param>
    /// <param name="prompt">输入 tokens（含缓存命中）</param>
    /// <param name="cacheHit">缓存命中 tokens</param>
    /// <param name="completion">输出 tokens</param>
    /// <param name="costOverride">可选费用覆盖（元）；null 时按 DeepSeek 非高峰价估算</param>
    public static void Record(string source, string model, long prompt, long cacheHit, long completion, double? costOverride = null)
    {
        EnsureInit();
        double cost = costOverride ?? UsageStats.EstimateCostYuan(prompt, cacheHit, completion);

        var line = new StringBuilder(256);
        line.Append("{\"t\":\"")
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append("\",\"src\":\"").Append(JsonEscape(source))
            .Append("\",\"model\":\"").Append(JsonEscape(model ?? ""))
            .Append("\",\"prompt\":").Append(prompt)
            .Append(",\"hit\":").Append(cacheHit)
            .Append(",\"comp\":").Append(completion)
            .Append(",\"cost\":").Append(cost.ToString("F4", CultureInfo.InvariantCulture))
            .Append(",\"key_id\":\"").Append(JsonEscape(ApiKeyIdentity.GetKeyIdForSource(source)))
            .Append("\",\"key_hash\":\"").Append(JsonEscape(ApiKeyIdentity.GetKeyHashForSource(source)))
            .Append("\",\"billing_attribution\":\"")
            .Append(string.Equals(source, "local", StringComparison.OrdinalIgnoreCase)
                ? ApiKeyIdentity.NotApplicable
                : ApiKeyIdentity.ManualBillingCheck)
            .Append("\"")
            .Append("}\n");

        lock (_lock)
        {
            // 同步累计到内存 UsageStats（面板「消耗」可跨重启显示）
            UsageStats.Record((int)prompt, (int)cacheHit, (int)completion);

            try
            {
                File.AppendAllText(_filePath, line.ToString(), new UTF8Encoding(false));
                EnforceLimit();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[UsageLogger] 写入失败: {e.Message}");
            }
        }
    }

    /// <summary>存储上限控制：超 2MB 或 2 万行时从头部截断，保留最新</summary>
    private static void EnforceLimit()
    {
        try
        {
            var fi = new FileInfo(_filePath);
            if (fi.Length <= MAX_BYTES) return;

            // 读全部行，保留后半部分
            var lines = File.ReadAllLines(_filePath);
            if (lines.Length <= MAX_LINES / 2) return; // 行数不多但超字节：整文件重写为尾部 80%
            int keep = lines.Length - MAX_LINES / 2;
            File.WriteAllLines(_filePath, lines[keep..], new UTF8Encoding(false));
        }
        catch { /* 截断失败不阻塞主流程 */ }
    }

    /// <summary>启动时回灌历史到 UsageStats（让「消耗」面板显示跨重启累计）。调用前 EnsureInit。</summary>
    public static void LoadHistoryIntoUsageStats()
    {
        EnsureInit();
        if (_filePath == null || !File.Exists(_filePath)) return;
        try
        {
            long totalPrompt = 0, totalHit = 0, totalComp = 0, totalCalls = 0;
            foreach (var raw in File.ReadLines(_filePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var line = raw.Trim();
                int p = ExtractLong(line, "\"prompt\":");
                int h = ExtractLong(line, "\"hit\":");
                int c = ExtractLong(line, "\"comp\":");
                if (p + h + c == 0) continue;
                totalPrompt += p; totalHit += h; totalComp += c; totalCalls++;
            }
            if (totalCalls > 0)
            {
                UsageStats.LoadPersisted(totalCalls, totalPrompt, totalHit, totalComp);
                UnityEngine.Debug.Log($"[UsageLogger] 已加载历史消耗: {totalCalls} 次调用, prompt={totalPrompt}, hit={totalHit}, comp={totalComp}（跨重启累计）");
            }
        }
        catch { /* 历史加载失败不影响运行 */ }
    }

    /// <summary>按来源汇总消耗（跨重启，读日志文件）— 供「消耗」面板展示"钱花在哪"</summary>
    public static Dictionary<string, (long calls, long prompt, long hit, long comp, double cost)> SummarizeBySource()
    {
        EnsureInit();
        var result = new Dictionary<string, (long, long, long, long, double)>();
        if (_filePath == null || !File.Exists(_filePath)) return result;
        try
        {
            foreach (var raw in File.ReadLines(_filePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var line = raw.Trim();
                int srcIdx = line.IndexOf("\"src\":\"", StringComparison.Ordinal);
                if (srcIdx < 0) continue;
                int srcStart = srcIdx + 7;
                int srcEnd = line.IndexOf('"', srcStart);
                string src = srcEnd > srcStart ? line.Substring(srcStart, srcEnd - srcStart) : "?";
                long p = ExtractLong(line, "\"prompt\":");
                long h = ExtractLong(line, "\"hit\":");
                long c = ExtractLong(line, "\"comp\":");
                double cost = 0;
                int ci = line.IndexOf("\"cost\":", StringComparison.Ordinal);
                if (ci >= 0)
                {
                    int cs = ci + 7;
                    int ce = cs;
                    while (ce < line.Length && (char.IsDigit(line[ce]) || line[ce] == '.')) ce++;
                    double.TryParse(line.Substring(cs, ce - cs), NumberStyles.Float, CultureInfo.InvariantCulture, out cost);
                }
                if (!result.ContainsKey(src))
                    result[src] = (0, 0, 0, 0, 0);
                var v = result[src];
                result[src] = (v.Item1 + 1, v.Item2 + p, v.Item3 + h, v.Item4 + c, v.Item5 + cost);
            }
        }
        catch { }
        return result;
    }

    /// <summary>从 JSONL 行提取字段数值（简易解析，避免引入完整 JSON 库依赖）</summary>
    private static int ExtractLong(string line, string key)
    {
        int idx = line.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return 0;
        int start = idx + key.Length;
        int end = start;
        while (end < line.Length && char.IsDigit(line[end])) end++;
        return int.TryParse(line.Substring(start, end - start), out int v) ? v : 0;
    }

    private static string JsonEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
