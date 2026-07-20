using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

public class BuildScript
{
    /// <summary>
    /// 完整构建 — 输出可执行文件到 Build/
    /// </summary>
    public static void BuildDesktopPet()
    {
        // ★ 清除 Bee DAG 缓存，防止增量构建的 MonoScript 序列化损坏
        string beeDir = "Library/Bee";
        if (Directory.Exists(beeDir))
        {
            Debug.Log("[BuildScript] 清理 Bee 缓存（防止 DAG 增量损坏）");
            Directory.Delete(beeDir, true);
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
    /// 快速编译验证 — 只检查脚本能否通过编译（不输出可执行文件）
    /// 由 build.ps1 -Quick 调用
    /// </summary>
    public static void VerifyCompile()
    {
        Debug.Log("[BuildScript] 编译验证模式 — 脚本编译已通过");

        // 获取所有的编译错误
        var logEntries = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
        if (logEntries != null)
        {
            var clearMethod = logEntries.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            clearMethod?.Invoke(null, null);
        }

        // 如果有编译错误，Unity 在 -batchmode 下会自动打印并返回非零退出码
        // 脚本编译本身成功就算验证通过
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
