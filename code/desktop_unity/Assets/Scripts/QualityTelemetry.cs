using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// 本地/模板/云端质量遥测。
///
/// 只记录路由和结果指标，不记录用户原文、动作描述、完整回复、密钥或参数快照。
/// 文件位置：DataPathConfig.DataRoot/quality_log.jsonl。
/// </summary>
public static class QualityTelemetry
{
    private const long MAX_BYTES = 2 * 1024 * 1024;
    private const int MAX_LINES = 20000;
    private const int MAX_REASON_LENGTH = 100;
    private const int MAX_CASE_ID_LENGTH = 64;

    private static readonly object _lock = new object();
    private static string _filePath;
    private static string _caseId = "";

    public static void EnsureInit()
    {
        if (_filePath != null) return;
        _filePath = Path.Combine(DataPathConfig.DataRoot, "quality_log.jsonl");
        SetCaseId(Environment.GetEnvironmentVariable("FU_XUAN_CASE_ID"));
        try { Directory.CreateDirectory(DataPathConfig.DataRoot); } catch { }
    }

    public static string FilePath => _filePath;
    public static string CurrentCaseId => _caseId;

    /// <summary>
    /// 设置当前质量测试案例编号。只接受短标识，不接受用户原文；空值清除编号。
    /// 例如：chat_001、motion_wave_03。
    /// </summary>
    public static void SetCaseId(string caseId)
    {
        if (string.IsNullOrEmpty(caseId))
        {
            _caseId = "";
            return;
        }

        var builder = new StringBuilder(Math.Min(caseId.Length, MAX_CASE_ID_LENGTH));
        foreach (char c in caseId.Trim())
        {
            if (builder.Length >= MAX_CASE_ID_LENGTH) break;
            builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
        }
        _caseId = builder.ToString();
    }

    public static void RecordChat(
        string source, string model, bool success, bool accepted,
        long latencyMs, string reason, int replyChars, bool toolCall, string caseId = null)
    {
        Record(new QualityEvent
        {
            Task = "chat",
            Source = source,
            Model = model,
            Success = success,
            Accepted = accepted,
            ParseValid = success,
            SafetyValid = true,
            Score = -1,
            LatencyMs = latencyMs,
            ReplyChars = replyChars,
            Keyframes = -1,
            ToolCall = toolCall,
            Reason = reason,
            CaseId = string.IsNullOrEmpty(caseId) ? CurrentCaseId : caseId
        });
    }

    /// <summary>
    /// 记录回答内容五维评分。只记录分数和短原因，不记录输入或完整回答。
    /// </summary>
    public static void RecordChatQuality(
        string source, string model, ReplyQualityJudge.Result result, string caseId = null)
    {
        if (result == null) return;
        Record(new QualityEvent
        {
            Task = "chat_quality",
            Source = source,
            Model = model,
            Success = result.AverageScore >= 0,
            Accepted = result.AverageScore >= 3,
            ParseValid = result.AverageScore >= 0,
            SafetyValid = true,
            Score = result.AverageScore,
            LatencyMs = 0,
            ReplyChars = -1,
            Keyframes = -1,
            ToolCall = false,
            Reason = result.Reason,
            CaseId = string.IsNullOrEmpty(caseId) ? CurrentCaseId : caseId,
            JudgeProvider = result.Provider,
            JudgePersona = result.Persona,
            JudgeMemory = result.Memory,
            JudgeTime = result.Time,
            JudgeRelevance = result.Relevance,
            JudgeConstraint = result.Constraint
        });
    }

    public static void RecordMotionDecision(
        string source, string model, bool success, bool parseValid,
        bool accepted, long latencyMs, string reason)
    {
        Record(new QualityEvent
        {
            Task = "motion_decision",
            Source = source,
            Model = model,
            Success = success,
            Accepted = accepted,
            ParseValid = parseValid,
            SafetyValid = true,
            Score = -1,
            LatencyMs = latencyMs,
            ReplyChars = -1,
            Keyframes = -1,
            ToolCall = false,
            Reason = reason,
            CaseId = CurrentCaseId
        });
    }

    public static void RecordMotionTranslation(
        string source, string model, bool success, bool parseValid,
        bool accepted, long latencyMs, int keyframes, string reason)
    {
        Record(new QualityEvent
        {
            Task = "motion_translation",
            Source = source,
            Model = model,
            Success = success,
            Accepted = accepted,
            ParseValid = parseValid,
            SafetyValid = success,
            Score = -1,
            LatencyMs = latencyMs,
            ReplyChars = -1,
            Keyframes = keyframes,
            ToolCall = false,
            Reason = reason,
            CaseId = CurrentCaseId
        });
    }

    public static void RecordMotionValidation(
        string source, string model, bool success, bool accepted,
        long latencyMs, int score, string reason, string caseId = null)
    {
        Record(new QualityEvent
        {
            Task = "motion_validation",
            Source = source,
            Model = model,
            Success = success,
            Accepted = accepted,
            ParseValid = success,
            SafetyValid = success,
            Score = score,
            LatencyMs = latencyMs,
            ReplyChars = -1,
            Keyframes = -1,
            ToolCall = false,
            Reason = reason,
            CaseId = string.IsNullOrEmpty(caseId) ? CurrentCaseId : caseId
        });
    }

    /// <summary>供 EditMode 测试检查日志协议；不写文件。</summary>
    public static string BuildJsonLine(QualityEvent e, DateTime timestamp)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));

        var line = new StringBuilder(400);
        line.Append("{\"t\":\"")
            .Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append("\",\"task\":\"").Append(JsonEscape(e.Task))
            .Append("\",\"src\":\"").Append(JsonEscape(e.Source))
            .Append("\",\"model\":\"").Append(JsonEscape(e.Model))
            .Append("\",\"case_id\":\"").Append(JsonEscape(e.CaseId))
            .Append("\",\"ok\":").Append(e.Success ? "true" : "false")
            .Append(",\"accepted\":").Append(e.Accepted ? "true" : "false")
            .Append(",\"parse\":").Append(e.ParseValid ? "true" : "false")
            .Append(",\"safe\":").Append(e.SafetyValid ? "true" : "false")
            .Append(",\"score\":").Append(e.Score)
            .Append(",\"latency_ms\":").Append(Math.Max(0, e.LatencyMs))
            .Append(",\"reply_chars\":").Append(e.ReplyChars)
            .Append(",\"keyframes\":").Append(e.Keyframes)
            .Append(",\"tool_call\":").Append(e.ToolCall ? "true" : "false")
            .Append(",\"judge_provider\":\"").Append(JsonEscape(e.JudgeProvider))
            .Append("\",\"judge_persona\":").Append(e.JudgePersona)
            .Append(",\"judge_memory\":").Append(e.JudgeMemory)
            .Append(",\"judge_time\":").Append(e.JudgeTime)
            .Append(",\"judge_relevance\":").Append(e.JudgeRelevance)
            .Append(",\"judge_constraint\":").Append(e.JudgeConstraint)
            .Append(",\"reason\":\"").Append(JsonEscape(NormalizeReason(e.Reason)))
            .Append("\"}\n");
        return line.ToString();
    }

    public sealed class QualityEvent
    {
        public string Task;
        public string Source;
        public string Model;
        public string CaseId;
        public bool Success;
        public bool Accepted;
        public bool ParseValid;
        public bool SafetyValid;
        public int Score;
        public long LatencyMs;
        public int ReplyChars;
        public int Keyframes;
        public bool ToolCall;
        public string Reason;
        public string JudgeProvider = "";
        public int JudgePersona = -1;
        public int JudgeMemory = -1;
        public int JudgeTime = -1;
        public int JudgeRelevance = -1;
        public int JudgeConstraint = -1;
    }

    private static void Record(QualityEvent e)
    {
        EnsureInit();
        if (string.IsNullOrEmpty(e.CaseId)) e.CaseId = CurrentCaseId;
        string line = BuildJsonLine(e, DateTime.Now);
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_filePath, line, new UTF8Encoding(false));
                EnforceLimit();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[QualityTelemetry] 写入失败: {ex.Message}");
            }
        }
    }

    private static void EnforceLimit()
    {
        try
        {
            var fi = new FileInfo(_filePath);
            if (fi.Length <= MAX_BYTES) return;

            string[] lines = File.ReadAllLines(_filePath);
            if (lines.Length <= MAX_LINES / 2) return;
            int keepFrom = lines.Length - MAX_LINES / 2;
            File.WriteAllLines(_filePath, lines[keepFrom..], new UTF8Encoding(false));
        }
        catch { /* 遥测失败不得阻塞桌宠 */ }
    }

    private static string NormalizeReason(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return "";
        string clean = reason.Replace("\r", " ").Replace("\n", " ");
        return clean.Length <= MAX_REASON_LENGTH
            ? clean
            : clean.Substring(0, MAX_REASON_LENGTH);
    }

    private static string JsonEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}
