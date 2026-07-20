using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 动作参考图管理器
///
/// 职责：
///   持久化存储每个动作的"标准参考截图"，
///   供 AI 调用 self_review 工具时做"实际 vs 标准"对比分析。
///
/// 存储位置：
///   D:\DesktopPetData\ActionRefs\<动作名>.png
///
/// 文件格式：
///   512×512 RGBA，去背景的纯模型渲染快照
/// </summary>
public static class ActionReferenceManager
{
    private static string _baseDir = null;

    private static string BaseDir
    {
        get
        {
            if (_baseDir == null)
                _baseDir = Path.Combine(DataPathConfig.DataRoot, "ActionRefs");
            return _baseDir;
        }
    }

    /// <summary>获取某个动作的参考图路径</summary>
    public static string GetRefPath(string actionName)
    {
        // 文件名净化：只保留安全字符
        string safeName = SanitizeName(actionName);
        return Path.Combine(BaseDir, safeName + ".png");
    }

    /// <summary>检查是否存在参考图</summary>
    public static bool HasReference(string actionName)
    {
        return File.Exists(GetRefPath(actionName));
    }

    /// <summary>保存参考图（从 byte[] PNG）</summary>
    public static void SaveReference(string actionName, byte[] pngBytes)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            Debug.LogWarning("[ActionRefManager] 保存参考图失败：空数据");
            return;
        }

        try
        {
            if (!Directory.Exists(BaseDir)) Directory.CreateDirectory(BaseDir);
            string path = GetRefPath(actionName);
            File.WriteAllBytes(path, pngBytes);
            Debug.Log($"[ActionRefManager] ✓ 参考图已保存: {path} ({pngBytes.Length} bytes)");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ActionRefManager] 保存参考图异常: {e.Message}");
        }
    }

    /// <summary>加载参考图为 base64 data URL</summary>
    /// <returns>data:image/png;base64,... 或 null</returns>
    public static string LoadReferenceAsDataUrl(string actionName)
    {
        string path = GetRefPath(actionName);
        if (!File.Exists(path))
        {
            Debug.Log($"[ActionRefManager] 参考图不存在: {path}");
            return null;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string base64 = Convert.ToBase64String(bytes);
            return "data:image/png;base64," + base64;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ActionRefManager] 加载参考图失败: {e.Message}");
            return null;
        }
    }

    /// <summary>删除参考图</summary>
    public static void DeleteReference(string actionName)
    {
        string path = GetRefPath(actionName);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"[ActionRefManager] 参考图已删除: {path}");
        }
    }

    /// <summary>列出所有已保存的参考图</summary>
    public static List<string> ListAllReferences()
    {
        var list = new List<string>();
        if (!Directory.Exists(BaseDir)) return list;

        foreach (string file in Directory.GetFiles(BaseDir, "*.png"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            list.Add(name);
        }
        return list;
    }

    /// <summary>文件名安全化（去空格/特殊字符）</summary>
    private static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unnamed";
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
