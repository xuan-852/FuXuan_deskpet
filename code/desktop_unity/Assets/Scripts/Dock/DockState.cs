using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 收纳盘数据模型 — 单个文件记录
/// </summary>
[Serializable]
public class DockItemData
{
    /// <summary>文件完整路径</summary>
    public string path;

    /// <summary>显示文件名（含扩展名）</summary>
    public string fileName;

    /// <summary>扩展名（小写，如 .pdf .zip）</summary>
    public string extension;

    /// <summary>添加时间（DateTime.Ticks）</summary>
    public long addedTicks;
}

/// <summary>
/// 收纳盘状态快照 — 序列化为 JSON
/// </summary>
[Serializable]
public class DockStateData
{
    public int version = 1;
    public float panelX = 0f;       // 归一化坐标 0~1（X 从右起算）
    public float panelY = 0f;       // 归一化坐标 0~1（Y 从上起算）
    public bool collapsed = true;
    public List<DockItemData> items = new();
}

/// <summary>
/// 收纳盘持久化 — JsonUtility 存取到 D:\DesktopPetData\dock_state.json
/// </summary>
public static class DockState
{
    private static readonly string SaveDir = DataPathConfig.DataRoot;
    private static readonly string SaveFile = "dock_state.json";
    private static string SavePath => Path.Combine(SaveDir, SaveFile);

    /// <summary>从磁盘加载状态，不存在或解析失败时返回默认状态</summary>
    public static DockStateData Load()
    {
        try
        {
            if (!File.Exists(SavePath))
                return new DockStateData();

            string json = File.ReadAllText(SavePath);
            DockStateData data = JsonUtility.FromJson<DockStateData>(json);
            if (data == null) return new DockStateData();

            // 版本迁移预留
            if (data.version < 1) data.version = 1;

            // 清理失效路径（文件已被删除）
            data.items.RemoveAll(item => !File.Exists(item.path));

            return data;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DockState] 加载失败: {ex.Message}");
            return new DockStateData();
        }
    }

    /// <summary>保存到磁盘</summary>
    public static void Save(DockStateData state)
    {
        try
        {
            if (!Directory.Exists(SaveDir))
                Directory.CreateDirectory(SaveDir);

            string json = JsonUtility.ToJson(state, prettyPrint: true);
            File.WriteAllText(SavePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DockState] 保存失败: {ex.Message}");
        }
    }

    /// <summary>删除状态文件</summary>
    public static void Delete()
    {
        try
        {
            if (File.Exists(SavePath))
                File.Delete(SavePath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DockState] 删除失败: {ex.Message}");
        }
    }
}
