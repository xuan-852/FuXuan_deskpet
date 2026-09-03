using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public class BuildScript
{
    /// <summary>
    /// 完整构建 — 输出可执行文件到 Build/
    /// </summary>
    /// <summary>
    /// 决定是否清理 Library\Bee 缓存（增量构建保护）。
    /// 默认【保留】缓存走增量，仅显式 BEE_CLEAN_CACHE=1/true 才清理。
    /// 抽成纯函数便于单测，防止「每次全量重编译」回归。
    /// </summary>
    public static bool ShouldCleanBeeCache(string value)
    {
        return string.Equals(value, "1", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase);
    }

    public static void BuildDesktopPet()
    {
        // ★ P0 构建负载保护：Library\Bee 缓存清理改为显式开关。
        // 默认【保留】缓存走增量构建（避免每次全量重编译导致 CPU 打满/热保护）；
        // 仅当 build.ps1 显式传 -CleanBeeCache（设置 BEE_CLEAN_CACHE=1）时才清一次，
        // 用于确认缓存损坏/构建异常后的强制全量重建。
        bool cleanBeeCache = ShouldCleanBeeCache(
            System.Environment.GetEnvironmentVariable("BEE_CLEAN_CACHE"));
        string beeDir = "Library/Bee";
        if (cleanBeeCache && Directory.Exists(beeDir))
        {
            Debug.Log("[BuildScript] 清理 Bee 缓存（显式 -CleanBeeCache）");
            Directory.Delete(beeDir, true);
        }
        else
        {
            Debug.Log("[BuildScript] 保留 Bee 缓存，增量构建（需全量请在 build.ps1 加 -CleanBeeCache）");
        }

        // ★ 用硬编码绝对路径
        string buildDir = @"D:\Unity\projects\Desktop_per_pro\Build";
        string buildPath = Path.Combine(buildDir, "DesktopPet.exe");

        if (!Directory.Exists(buildDir))
            Directory.CreateDirectory(buildDir);

        // 查找所有场景
        string[] scenes = EditorBuildSettingsScene.GetActiveSceneList(
            EditorBuildSettings.scenes);

        if (scenes == null || scenes.Length == 0)
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene");
            if (guids.Length > 0)
            {
                scenes = new string[guids.Length];
                for (int i = 0; i < guids.Length; i++)
                    scenes[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            }
        }

        if (scenes == null || scenes.Length == 0)
        {
            Debug.LogError("[BuildScript] 找不到任何场景!");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[BuildScript] 场景: {string.Join(", ", scenes)}");
        Debug.Log($"[BuildScript] 输出路径: {buildPath}");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = buildPath,
            targetGroup = BuildTargetGroup.Standalone,
            target = BuildTarget.StandaloneWindows,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        Debug.Log($"[BuildScript] 构建完成: {summary.result}, 耗时 {summary.totalTime.TotalSeconds:F1}s");

        if (summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
        else
            EditorApplication.Exit(0);
    }

    /// <summary>
    /// 快速编译验证 — 检查脚本编译 + ToolRegistry 初始化 + JSON Schema 合法性
    /// 由 build.ps1 -Quick 调用
    /// </summary>
    public static void VerifyCompile()
    {
        Debug.Log("[BuildScript] 编译验证模式启动...");

        // 1. 初始化 ToolRegistry（触发反射发现所有工具）
        Debug.Log("[BuildScript] 正在初始化 ToolRegistry...");
        ToolRegistry.Initialize();
        Debug.Log($"[BuildScript] ToolRegistry 已加载 {ToolRegistry.ToolCount} 个工具");

        // 2. 收集所有工具
        var allTools = new List<IPetTool>();
        var syncDict = typeof(ToolRegistry)
            .GetField("_syncTools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var asyncDict = typeof(ToolRegistry)
            .GetField("_asyncTools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (syncDict?.GetValue(null) is Dictionary<string, IPetTool> sync)
            allTools.AddRange(sync.Values);
        if (asyncDict?.GetValue(null) is Dictionary<string, IPetTool> async)
            allTools.AddRange(async.Values);

        // 3. 逐个验证 JSON Schema 合法性
        int passCount = 0;
        int failCount = 0;
        var errors = new List<string>();

        foreach (var tool in allTools.OrderBy(t => t.ToolName))
        {
            string json = tool.ToolParametersJson;
            try
            {
                var parsed = JObject.Parse(json);

                // 额外的健康检查：必须有 type 字段，且为 "object"
                string typeVal = parsed["type"]?.ToString();
                if (typeVal != "object")
                    errors.Add($"[{tool.ToolName}] type 应为 'object'，实际为 '{typeVal}'");

                // properties 如果存在，必须是 JObject
                if (parsed["properties"] != null && parsed["properties"]?.Type != JTokenType.Object)
                    errors.Add($"[{tool.ToolName}] properties 必须是对象");

                passCount++;
            }
            catch (System.Exception ex)
            {
                failCount++;
                errors.Add($"[{tool.ToolName}] JSON 解析失败: {ex.Message}");
            }
        }

        // 4. 报告结果
        Debug.Log($"[BuildScript] Schema 验证完成: {passCount} 通过, {failCount} 失败");
        if (errors.Count > 0)
        {
            foreach (var err in errors)
                Debug.LogError($"[BuildScript] {err}");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("[BuildScript] 编译验证模式 — 全部通过 ✓");
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// 运行所有 Editor 测试的入口点。
    /// 实际通过 Unity 命令行 -runTests 参数调用，
    /// 此方法仅用于前置准备和日志。
    /// </summary>
    public static void RunAllTests()
    {
        Debug.Log("[BuildScript] Editor 测试模式启动...");
        Debug.Log("[BuildScript] 测试运行由 Test Runner 框架管理（-runTests 参数）");
        // 此方法作为 -runTests 前的准备工作，保持简单
        EditorApplication.Exit(0);
    }
}
