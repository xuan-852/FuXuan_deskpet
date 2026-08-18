using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 为运行时成本日志生成不包含完整密钥的 API Key 身份标识。
/// 官方账单无法由桌宠实时读取时，使用该标识核对一批日志的运行环境。
/// </summary>
public static class ApiKeyIdentity
{
    public const string ManualBillingCheck = "manual_platform_check_required";
    public const string NotApplicable = "not_applicable";

    /// <summary>返回可人工核对的短标识，例如 sk-3707b*****52ca。</summary>
    public static string GetKeyId(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return "missing";
        if (apiKey.Length <= 11) return "configured_short";

        string prefix = apiKey.Substring(0, Math.Min(7, apiKey.Length));
        string suffix = apiKey.Substring(apiKey.Length - 4, 4);
        return prefix + "*****" + suffix;
    }

    /// <summary>返回不可逆 SHA-256 指纹，便于跨日志核对同一密钥。</summary>
    public static string GetKeyHash(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return "";

        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
            var builder = new StringBuilder(bytes.Length * 2 + 7);
            builder.Append("sha256:");
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }
    }

    /// <summary>按 usage source 选择对应的云端密钥身份。</summary>
    public static string GetKeyIdForSource(string source)
    {
        if (string.Equals(source, "local", StringComparison.OrdinalIgnoreCase))
            return "local";
        return GetKeyId(IsGlmSource(source) ? ChatConfig.GlmApiKey : ChatConfig.ApiKey);
    }

    /// <summary>按 usage source 选择对应的云端密钥指纹。</summary>
    public static string GetKeyHashForSource(string source)
    {
        if (string.Equals(source, "local", StringComparison.OrdinalIgnoreCase))
            return "";
        return GetKeyHash(IsGlmSource(source) ? ChatConfig.GlmApiKey : ChatConfig.ApiKey);
    }

    public static bool IsGlmSource(string source)
    {
        return string.Equals(source, "glm", StringComparison.OrdinalIgnoreCase);
    }
}
