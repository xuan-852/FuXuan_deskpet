/// <summary>
/// 统一数据存储路径配置（唯一事实源）
/// 所有持久化数据（知识库、忆境、人格、运动记忆、便签、活动日志、日志镜像等）
/// 统一存储到 D:\DesktopPetData\，避免占用 C 盘空间，重装系统不丢失。
/// 各模块禁止自行硬编码路径，一律从这里取值（AGENTS.md 铁律）。
/// </summary>
public static class DataPathConfig
{
    /// <summary>
    /// 数据根目录。优先取环境变量 FU_XUAN_DATA（安装器/换机部署用），默认 D:\DesktopPetData（向后兼容）。
    /// 目标机若 D 盘不存在/无权限，安装器可写 FU_XUAN_DATA 指向其他盘。
    /// </summary>
    public static string DataRoot =>
        System.Environment.GetEnvironmentVariable("FU_XUAN_DATA") ?? @"D:\DesktopPetData";

    /// <summary>统一日志目录（崩溃日志 / player_log 镜像）</summary>
    public static string LogsDir => System.IO.Path.Combine(DataRoot, "logs");

    /// <summary>办公文档 / LaTeX 输出目录</summary>
    public static string DocumentsDir => System.IO.Path.Combine(DataRoot, "Documents");

    /// <summary>测试模式标记文件（存在 = IsTestMode）</summary>
    public static string TestModeFile => System.IO.Path.Combine(DataRoot, ".test_mode");

    /// <summary>测试收件箱（终端链路 UI 测试注入）</summary>
    public static string InboxFile => System.IO.Path.Combine(DataRoot, "inbox.txt");
}
