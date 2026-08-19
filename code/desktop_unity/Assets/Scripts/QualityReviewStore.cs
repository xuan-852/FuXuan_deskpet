using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// 质量复核用的隔离原文存储。
/// 仅在 FU_XUAN_REVIEW_EXPORT=1 且存在质量 case_id 时写入，普通运行不会保存对话原文。
/// </summary>
public static class QualityReviewStore
{
    private static readonly object SyncRoot = new object();
    private static string _filePath;

    public static bool Enabled
    {
        get
        {
            string value = Environment.GetEnvironmentVariable("FU_XUAN_REVIEW_EXPORT");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Record(string caseId, string source, string model, string input, string reply)
    {
        if (!Enabled || string.IsNullOrEmpty(caseId)) return;
        if (_filePath == null)
        {
            _filePath = Path.Combine(DataPathConfig.DataRoot, "quality_review.jsonl");
            try { Directory.CreateDirectory(DataPathConfig.DataRoot); } catch { }
        }

        var line = new StringBuilder(1024)
            .Append("{\"t\":\"")
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append("\",\"case_id\":\"").Append(JsonEscape(caseId))
            .Append("\",\"src\":\"").Append(JsonEscape(source))
            .Append("\",\"model\":\"").Append(JsonEscape(model))
            .Append("\",\"input\":\"").Append(JsonEscape(input))
            .Append("\",\"reply\":\"").Append(JsonEscape(reply))
            .Append("\"}\n")
            .ToString();

        lock (SyncRoot)
        {
            try { File.AppendAllText(_filePath, line, new UTF8Encoding(false)); }
            catch (Exception ex) { UnityEngine.Debug.LogWarning($"[QualityReviewStore] 写入失败: {ex.Message}"); }
        }
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
