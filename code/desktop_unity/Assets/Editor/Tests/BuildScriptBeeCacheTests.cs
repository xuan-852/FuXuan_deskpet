using NUnit.Framework;

/// <summary>
/// BuildScript 增量构建保护（P0 构建负载）的决策逻辑测试。
/// 要求：默认保留 Library\Bee 缓存走增量构建；仅显式 BEE_CLEAN_CACHE=1/true 才清理。
/// 旧实现（无条件每次清理 Bee）会使本文件测试失败 → 作为回归护栏。
/// </summary>
public class BuildScriptBeeCacheTests
{
    [Test]
    public void DefaultEnvironmentKeepsIncrementalCache()
    {
        // 未设置环境变量 / 空 / "0" / "false" → 都应保留缓存（增量）
        Assert.IsFalse(BuildScript.ShouldCleanBeeCache(null), "null 应保留增量");
        Assert.IsFalse(BuildScript.ShouldCleanBeeCache(""), "空串应保留增量");
        Assert.IsFalse(BuildScript.ShouldCleanBeeCache("0"), "'0' 应保留增量");
        Assert.IsFalse(BuildScript.ShouldCleanBeeCache("false"), "'false' 应保留增量");
    }

    [Test]
    public void ExplicitCleanBeeCacheFlagEnablesCleanup()
    {
        // 仅显式 "1"/"true"（大小写不敏感）才清理
        Assert.IsTrue(BuildScript.ShouldCleanBeeCache("1"), "'1' 应触发清理");
        Assert.IsTrue(BuildScript.ShouldCleanBeeCache("true"), "'true' 应触发清理");
        Assert.IsTrue(BuildScript.ShouldCleanBeeCache("TRUE"), "大小写不敏感");
    }
}
