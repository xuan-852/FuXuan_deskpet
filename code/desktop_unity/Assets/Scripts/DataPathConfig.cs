/// <summary>
/// 统一数据存储路径配置
/// 所有持久化数据（知识库、忆境、人格、运动记忆、便签、活动日志等）
/// 统一存储到 D:\DesktopPetData\，避免占用 C 盘空间，重装系统不丢失。
/// </summary>
public static class DataPathConfig
{
    /// <summary>数据根目录</summary>
    public static string DataRoot => @"D:\DesktopPetData";
}
