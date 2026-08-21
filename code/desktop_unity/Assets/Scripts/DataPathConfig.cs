/// <summary>
/// 统一数据存储路径配置（唯一事实源）
/// 所有持久化数据（知识库、忆境、人格、运动记忆、便签、活动日志、日志镜像等）
/// 统一存储到 D:\DesktopPetData\，避免占用 C 盘空间，重装系统不丢失。
/// 各模块禁止自行硬编码路径，一律从这里取值（AGENTS.md 铁律）。
/// </summary>
public static class DataPathConfig
{
    /// <summary>默认数据根目录。安装器和运行时共享这一事实源。</summary>
    public const string DefaultDataRoot = @"D:\DesktopPetData";

    /// <summary>
    /// 数据根目录。
    /// 既有配置优先；配置失效但默认目录已有数据时回退到默认目录，避免重装产生第二份数据。
    /// 两者都不存在时才使用配置目标或默认目录，首次启动再创建。
    /// </summary>
    public static string DataRoot => ResolveDataRoot();

    /// <summary>当前是否存在有效的用户自定义数据目录。</summary>
    public static bool HasConfiguredDataRoot
    {
        get
        {
            var configured = Normalize(System.Environment.GetEnvironmentVariable("FU_XUAN_DATA"));
            return !string.IsNullOrEmpty(configured) &&
                   System.IO.Directory.Exists(configured);
        }
    }

    /// <summary>确保唯一数据根目录存在；失败只返回错误，不阻断桌宠主流程。</summary>
    public static bool EnsureDataRoot(out string error)
    {
        error = null;
        try
        {
            var root = DataRoot;
            if (!System.IO.Directory.Exists(root))
                System.IO.Directory.CreateDirectory(root);
            return true;
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string ResolveDataRoot()
    {
        var configured = Normalize(System.Environment.GetEnvironmentVariable("FU_XUAN_DATA"));
        if (!string.IsNullOrEmpty(configured) && System.IO.Directory.Exists(configured))
            return configured;

        var defaultRoot = Normalize(DefaultDataRoot);
        if (!string.IsNullOrEmpty(configured) && System.IO.Directory.Exists(defaultRoot))
            return defaultRoot;

        return string.IsNullOrEmpty(configured) ? defaultRoot : configured;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = value.Trim().Trim('"');
        if (result.Length > 3)
            result = result.TrimEnd('\\', '/');
        return result;
    }

    /// <summary>统一日志目录（崩溃日志 / player_log 镜像）</summary>
    public static string LogsDir => System.IO.Path.Combine(DataRoot, "logs");

    /// <summary>办公文档 / LaTeX 输出目录</summary>
    public static string DocumentsDir => System.IO.Path.Combine(DataRoot, "Documents");

    /// <summary>测试模式标记文件（存在 = IsTestMode）</summary>
    public static string TestModeFile => System.IO.Path.Combine(DataRoot, ".test_mode");

    /// <summary>测试收件箱（终端链路 UI 测试注入）</summary>
    public static string InboxFile => System.IO.Path.Combine(DataRoot, "inbox.txt");
}
