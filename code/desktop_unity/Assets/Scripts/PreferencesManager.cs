using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// 符玄「心之所向」— 主人偏好结构化存储（P4.2）
///
/// 设计目标：
///   ▸ 持久化记录主人的明确偏好与观察推断（如称呼、作息、口味、禁用词等）
///   ▸ 与记忆（PetMemory 事件流）不同：偏好是「去重、可覆盖、常驻」的结构化条目
///   ▸ 数据文件 pet_preferences.json（DataPathConfig.DataRoot 下）
///   ▸ 每次 SystemPrompt 注入最新偏好摘要，让符玄始终记得
///
/// 条目字段：
///   key         —— 偏好键（snake_case，如 "call_me" / "sleep_time"）
///   value       —— 偏好值（字符串）
///   source      —— 来源：user=主人明确告知 / infer=符玄观察推断
///   note        —— 备注（可选）
///   updatedAt   —— 更新时间 yyyy-MM-dd HH:mm
///
/// 容量控制：超过 MaxEntries 时淘汰最旧条目（保持 prompt 精简）
/// </summary>
[Serializable]
public class PreferenceEntry
{
    public string key = "";
    public string value = "";
    public string source = "user";   // user / infer
    public string note = "";
    public string updatedAt = "";
}

[Serializable]
public class PreferencesData
{
    public List<PreferenceEntry> entries = new List<PreferenceEntry>();
}

public class PreferencesManager : MonoBehaviour
{
    [Header("◈ 偏好存储配置")]
    [Tooltip("最大保留条目数（超出淘汰最旧）")]
    public int maxEntries = 50;

    private PreferencesData _data = new PreferencesData();
    public static PreferencesManager Instance { get; private set; }

    private string FilePath => Path.Combine(DataPathConfig.DataRoot, "pet_preferences.json");

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    // ==================================================================
    //  公开接口 — CRUD
    // ==================================================================

    /// <summary>新增或覆盖一条偏好（同 key 更新 value/source/note）</summary>
    public void SetPreference(string key, string value, string source = "user", string note = "")
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return;

        key = key.Trim();
        value = value.Trim();
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var existing = _data.entries.FirstOrDefault(e => e.key == key);
        if (existing != null)
        {
            existing.value = value;
            existing.source = string.IsNullOrEmpty(source) ? existing.source : source;
            existing.note = string.IsNullOrEmpty(note) ? existing.note : note;
            existing.updatedAt = now;
        }
        else
        {
            _data.entries.Add(new PreferenceEntry
            {
                key = key,
                value = value,
                source = string.IsNullOrEmpty(source) ? "user" : source,
                note = note,
                updatedAt = now
            });
        }

        TrimToMax();
        Save();
        Debug.Log($"[PreferencesManager] 💖 偏好已记录: {key} = {value}（来源: {source}）");
    }

    /// <summary>获取指定偏好值，不存在返回 null</summary>
    public string GetPreference(string key)
    {
        var entry = _data.entries.FirstOrDefault(e => e.key == key);
        return entry?.value;
    }

    /// <summary>删除一条偏好，返回是否删除成功</summary>
    public bool RemovePreference(string key)
    {
        int removed = _data.entries.RemoveAll(e => e.key == key);
        if (removed > 0)
        {
            Save();
            Debug.Log($"[PreferencesManager] 🗑 偏好已删除: {key}");
            return true;
        }
        return false;
    }

    /// <summary>获取全部偏好条目（只读副本）</summary>
    public List<PreferenceEntry> GetAllPreferences()
    {
        return new List<PreferenceEntry>(_data.entries);
    }

    /// <summary>偏好总数</summary>
    public int Count => _data.entries.Count;

    /// <summary>淘汰最旧条目（按 updatedAt 升序，保留最新 maxEntries 条）</summary>
    private void TrimToMax()
    {
        if (_data.entries.Count <= maxEntries) return;
        int excess = _data.entries.Count - maxEntries;
        var ordered = _data.entries
            .OrderBy(e => e.updatedAt)
            .ThenBy(e => e.key)
            .ToList();
        for (int i = 0; i < excess; i++)
            _data.entries.Remove(ordered[i]);
    }

    // ==================================================================
    //  为 SystemPrompt 构建偏好摘要
    // ==================================================================

    /// <summary>生成偏好摘要，注入 ChatManager SystemPrompt（空列表返回空串）</summary>
    public string FormatForPrompt()
    {
        if (_data.entries.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n【本座谨记 · 主人偏好】");
        sb.AppendLine("（以下为主人明示或本座观察所得的偏好，与主人相处时务必遵行）");

        // 按更新时间倒序展示（最新在前）
        var ordered = _data.entries
            .OrderByDescending(e => e.updatedAt)
            .ToList();

        foreach (var e in ordered)
        {
            string tag = e.source == "infer" ? "（本座观之）" : "";
            string notePart = string.IsNullOrEmpty(e.note) ? "" : $" — {e.note}";
            sb.AppendLine($"  ✦ {e.key}: {e.value}{tag}{notePart}");
        }

        sb.AppendLine("（若主人纠正某项偏好，即刻更新，莫要再犯。）");
        return sb.ToString();
    }

    // ==================================================================
    //  持久化
    // ==================================================================

    public void Save()
    {
        try
        {
            string json = JsonUtility.ToJson(_data, prettyPrint: true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[PreferencesManager] ❌ 保存失败: {e.Message}");
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                Save();
                return;
            }
            string json = File.ReadAllText(FilePath);
            var loaded = JsonUtility.FromJson<PreferencesData>(json);
            if (loaded != null)
            {
                _data = loaded;
                TrimToMax();
                Debug.Log($"[PreferencesManager] ✅ 偏好已载入，共{_data.entries.Count}条");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PreferencesManager] ❌ 载入失败: {e.Message}");
        }
    }
}
