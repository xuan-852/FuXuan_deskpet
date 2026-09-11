using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 首启体验的用户级状态。它与聊天记录、偏好一样保存到 DataRoot，
/// 因而发布更新替换安装目录时不会打断已经完成的选择。
/// </summary>
[Serializable]
public sealed class UserExperienceState
{
    public bool onboardingCompleted;
    public bool onboardingSkipped;
    public bool firstHintShown;
    public bool legacyUserDetected;

    [NonSerialized] private string _path;

    public bool ShouldOfferOnboarding => !legacyUserDetected && !onboardingCompleted && !onboardingSkipped;

    public static UserExperienceState Load()
    {
        var state = new UserExperienceState { _path = DataPathConfig.UserExperienceStateFile };
        try
        {
            if (File.Exists(state._path))
            {
                string json = File.ReadAllText(state._path, Encoding.UTF8);
                var loaded = JsonUtility.FromJson<UserExperienceState>(json);
                if (loaded != null)
                {
                    loaded._path = state._path;
                    return loaded;
                }
            }

            // 旧版本没有本文件。若已存在典型用户内容，将其视作既有用户，
            // 仅保留帮助入口，不用首次引导打扰。
            state.legacyUserDetected = HasEstablishedUserData(DataPathConfig.DataRoot);
            state.Save();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UserExperience] 读取首启状态失败，将保守地不自动弹出引导：{ex.Message}");
            state.legacyUserDetected = true;
        }
        return state;
    }

    public void MarkCompleted(bool skipped)
    {
        onboardingCompleted = !skipped;
        onboardingSkipped = skipped;
        Save();
    }

    public void MarkFirstHintShown()
    {
        if (firstHintShown) return;
        firstHintShown = true;
        Save();
    }

    public void Save()
    {
        try
        {
            if (!DataPathConfig.EnsureDataRoot(out string error))
            {
                Debug.LogWarning($"[UserExperience] 无法保存首启状态：{error}");
                return;
            }
            _path = string.IsNullOrEmpty(_path) ? DataPathConfig.UserExperienceStateFile : _path;
            AtomicFileWriter.WriteAllText(_path, JsonUtility.ToJson(this, true), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UserExperience] 保存首启状态失败：{ex.Message}");
        }
    }

    private static bool HasEstablishedUserData(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;
        string[] markers =
        {
            "pet_preferences.json", "pet_memory.json", "pet_personality.json",
            "motion_memory.json", "activity.json", "activity_log.json", "chat_history.json", "sessions.json"
        };
        foreach (string marker in markers)
            if (File.Exists(Path.Combine(root, marker))) return true;

        // 对旧版未知文件名保持保守：根目录已有其他 JSON 即当作老用户。
        foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (!string.Equals(Path.GetFileName(file), "ui_experience_state.json", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
