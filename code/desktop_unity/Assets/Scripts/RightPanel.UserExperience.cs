using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

/// <summary>首启引导、帮助与本地版本中心。</summary>
public partial class RightPanel
{
    private UserExperienceState _userExperience;
    private SystemTrayManager _helpTrayManager;
    private bool _onboardingAutoStart;
    private string _onboardingName = "";
    private string _experienceStatus = "";
    private Vector2 _experienceScroll;
    private bool _onboardingExternalEditing;

    private IEnumerator ShowFirstUseHint()
    {
        // 不抢启动流程；只在真正的新数据目录中给桌宠一条轻量提示。
        yield return new WaitForSeconds(1.5f);
        if (_userExperience == null || !_userExperience.ShouldOfferOnboarding || _userExperience.firstHintShown)
            yield break;
        var bubble = FindObjectOfType<ChatBubble>();
        if (bubble != null)
            bubble.ShowMessage("初次见面：点击本体，或按 F2 打开聊天。", 7f, ChatBubble.MsgPriority.Low);
        _userExperience.MarkFirstHintShown();
    }

    private void EnsureTrayHelpSubscription()
    {
        var tray = SystemTrayManager.Instance;
        if (tray == _helpTrayManager) return;
        ReleaseTrayHelpSubscription();
        _helpTrayManager = tray;
        if (_helpTrayManager != null) _helpTrayManager.OnHelpRequested += OpenHelpFromTray;
    }

    private void ReleaseTrayHelpSubscription()
    {
        if (_helpTrayManager != null) _helpTrayManager.OnHelpRequested -= OpenHelpFromTray;
        _helpTrayManager = null;
    }

    private void OpenHelpFromTray()
    {
        if (!_isOpen)
        {
            _isOpen = true;
            _closing = false;
            _animAlpha = 1f;
        }
        OpenOnboarding(true);
    }

    private void OpenOnboarding(bool readOnlyHelp)
    {
        _prevView = PanelView.Chat;
        _currentView = PanelView.Onboarding;
        _onboardingAutoStart = _helpTrayManager != null && _helpTrayManager.AutoStartEnabled;
        _experienceStatus = readOnlyHelp ? "使用帮助：设置可随时再次修改。" : "欢迎设置可跳过，之后可从“？”或托盘帮助重新查看。";
        EndOnboardingNameEditing();
        ApplyViewSize();
    }

    private void OpenAbout()
    {
        _prevView = PanelView.Settings;
        _currentView = PanelView.About;
        _experienceStatus = "";
        ApplyViewSize();
    }

    private void DrawOnboardingSubPanel(float x, float y, float w, float h, Vector2 mp)
    {
        float contentH = Mathf.Max(h, 630f);
        _experienceScroll = GUI.BeginScrollView(new Rect(x, y, w, h), _experienceScroll,
            new Rect(0f, 0f, w - 14f, contentH), false, false, _invisibleScrollbar, _invisibleScrollbar);
        float yy = 4f;
        GUI.Label(new Rect(8f, yy, w - 32f, 28f), "三步认识桌宠", new GUIStyle(_termTitleStyle) { fontSize = 19 });
        GUI.Label(new Rect(8f, yy + 32f, w - 32f, 42f), "不用完成所有设置；随时可按 F2、点击本体，或从托盘的“使用帮助”回来查看。", _termLogDimStyle);
        yy += 84f;

        DrawGuideCard(1, "打开聊天", "点击桌宠本体，或按 F2，可打开/关闭聊天窗口。", ref yy, w);
        DrawGuideCard(2, "移动位置", "按住桌宠本体拖动，即可把她放到你习惯的屏幕位置。", ref yy, w);
        DrawGuideCard(3, "托盘菜单", "右键任务栏托盘图标，可显示、查看帮助、设置开机自启或退出。", ref yy, w);

        yy += 10f;
        GUI.Label(new Rect(8f, yy, w - 32f, 24f), "称呼（可选）", _subSectionStyle);
        GUI.Label(new Rect(8f, yy + 27f, w - 32f, 22f), "留空时，符玄仍会称呼你为“你”。", _termLogDimStyle);
        Rect nameRect = new Rect(8f, yy + 56f, Mathf.Min(360f, w - 32f), 38f);
        UiTextureFactory.DrawPixelRect(nameRect, new Color(0.12f, 0.09f, 0.20f, 0.82f));
        if (_externalRender)
        {
            GUI.Label(new Rect(nameRect.x + 10f, nameRect.y + 7f, nameRect.width - 20f, 24f),
                string.IsNullOrEmpty(_onboardingName) ? "点击填写称呼" : _onboardingName, _termLogUserStyle);
            RegisterExtHit(nameRect, BeginOnboardingNameEditing);
            if (_onboardingExternalEditing)
            {
                ExternalChatWindow.ShowInputBar(true);
                ExternalChatWindow.SetInputRect(Mathf.RoundToInt(nameRect.x), Mathf.RoundToInt(nameRect.y), Mathf.RoundToInt(nameRect.width), Mathf.RoundToInt(nameRect.height));
            }
        }
        else
        {
            GUI.SetNextControlName("onboardingName");
            _onboardingName = GUI.TextField(nameRect, _onboardingName, 40, _termInputStyle);
        }
        yy += 110f;

        bool selected = _onboardingAutoStart;
        Rect autoRect = new Rect(8f, yy, Mathf.Min(360f, w - 32f), 40f);
        if (autoRect.Contains(mp)) UiTextureFactory.DrawPixelRect(autoRect, new Color(0.50f, 0.35f, 0.80f, 0.22f));
        if (GUI.Button(autoRect, selected ? "✓ 开机自启：已开启" : "○ 开机自启：保持关闭", _subBtnStyle))
            ApplyOnboardingAutoStart(!selected);
        RegisterExtHit(autoRect, () => ApplyOnboardingAutoStart(!_onboardingAutoStart));
        GUI.Label(new Rect(8f, yy + 44f, w - 32f, 34f), "默认关闭；只有你主动选择后才会写入系统开机项。", _termLogDimStyle);
        yy += 96f;

        Rect doneRect = new Rect(8f, yy, 150f, 42f);
        Rect skipRect = new Rect(170f, yy, 150f, 42f);
        if (GUI.Button(doneRect, "完成设置", _subBtnStyle)) CompleteOnboarding(false);
        if (GUI.Button(skipRect, "暂时跳过", _termToolBtnStyle)) CompleteOnboarding(true);
        RegisterExtHit(doneRect, () => CompleteOnboarding(false));
        RegisterExtHit(skipRect, () => CompleteOnboarding(true));
        if (!string.IsNullOrEmpty(_experienceStatus))
            GUI.Label(new Rect(8f, yy + 52f, w - 32f, 40f), _experienceStatus, _termLogDimStyle);
        GUI.EndScrollView();
    }

    private void DrawGuideCard(int index, string title, string text, ref float y, float w)
    {
        Rect rect = new Rect(8f, y, w - 32f, 66f);
        UiTextureFactory.DrawPixelRect(rect, new Color(0.20f, 0.14f, 0.33f, 0.62f));
        GUI.Label(new Rect(rect.x + 12f, rect.y + 7f, 34f, 26f), index.ToString(), new GUIStyle(_termTitleStyle) { alignment = TextAnchor.MiddleCenter });
        GUI.Label(new Rect(rect.x + 48f, rect.y + 7f, rect.width - 60f, 22f), title, _subSectionStyle);
        GUI.Label(new Rect(rect.x + 48f, rect.y + 31f, rect.width - 60f, 28f), text, _termLogDimStyle);
        y += 74f;
    }

    private void BeginOnboardingNameEditing()
    {
        if (!_externalMode) return;
        _onboardingExternalEditing = true;
        ExternalChatWindow.SetInputText(_onboardingName);
        ExternalChatWindow.ShowInputBar(true);
        ExternalChatWindow.FocusInput();
    }

    private void EndOnboardingNameEditing()
    {
        _onboardingExternalEditing = false;
        if (_externalMode && ExternalChatWindow.IsCreated) ExternalChatWindow.ShowInputBar(false);
    }

    private void ApplyOnboardingAutoStart(bool enabled)
    {
        _onboardingAutoStart = enabled;
        if (DataPathConfig.IsTestMode)
        {
            _experienceStatus = "测试模式：已记录自启选择，未写入真实注册表。";
            Debug.Log("[UserExperience] 测试模式：已记录自启选择，未写入真实注册表。");
            return;
        }
        var tray = _helpTrayManager ?? SystemTrayManager.Instance;
        if (tray == null)
        {
            _experienceStatus = "托盘尚在初始化；请稍后再次选择开机自启。";
            return;
        }
        tray.SetAutoStart(enabled);
        _experienceStatus = enabled ? "已开启开机自启。" : "已保持开机自启关闭。";
    }

    private void CompleteOnboarding(bool skipped)
    {
        EndOnboardingNameEditing();
        string name = (_onboardingName ?? "").Trim();
        if (!string.IsNullOrEmpty(name))
        {
            var preferences = FindObjectOfType<PreferencesManager>();
            if (preferences != null) preferences.SetPreference("call_me", name, "user", "首启欢迎设置");
            else Debug.LogWarning("[UserExperience] PreferencesManager 尚未就绪，未写入称呼。");
        }
        _userExperience?.MarkCompleted(skipped);
        _experienceStatus = skipped ? "已跳过欢迎设置；需要时可从帮助再次打开。" : "欢迎设置已保存。";
        _currentView = PanelView.Chat;
        ApplyViewSize();
        _inputFocused = true;
    }

    private void HandleTestOnboardingCommand(string command)
    {
        if (!ChatManager.IsTestMode) return;
        if (command == "complete") CompleteOnboarding(false);
        else if (command == "skip") CompleteOnboarding(true);
        else if (command == "autostart:on") ApplyOnboardingAutoStart(true);
        else if (command == "autostart:off") ApplyOnboardingAutoStart(false);
        else if (command.StartsWith("name:"))
        {
            _onboardingName = command.Substring("name:".Length).Trim();
            Debug.Log("[UserExperience] 测试称呼已写入等待保存状态。");
        }
        else Debug.LogWarning($"[UserExperience] 未知引导测试命令：{command}");
    }

    private void DrawAboutSubPanel(float x, float y, float w, float h, Vector2 mp)
    {
        float contentH = Mathf.Max(h, 570f);
        _experienceScroll = GUI.BeginScrollView(new Rect(x, y, w, h), _experienceScroll,
            new Rect(0f, 0f, w - 14f, contentH), false, false, _invisibleScrollbar, _invisibleScrollbar);
        string dataRoot = DataPathConfig.DataRoot;
        string runDir = GetRunDirectory();
        string version = GetReleaseText("version.txt", Application.version);
        string notes = GetReleaseText("release-notes.txt", "开发构建：本版本的发布说明将在正式安装包中显示。");
        bool writable = CanWriteDataRoot();
        bool autostart = (_helpTrayManager ?? SystemTrayManager.Instance) != null && (_helpTrayManager ?? SystemTrayManager.Instance).AutoStartEnabled;
        float yy = 4f;
        GUI.Label(new Rect(8f, yy, w - 32f, 28f), "版本与用户数据", new GUIStyle(_termTitleStyle) { fontSize = 19 });
        yy += 42f;
        DrawAboutRow("当前版本", version, ref yy, w);
        DrawAboutRow("运行目录", runDir, ref yy, w);
        DrawAboutRow("用户数据目录", dataRoot, ref yy, w);
        DrawAboutRow("数据目录状态", writable ? "可写入" : "无法写入（请检查目录权限）", ref yy, w);
        DrawAboutRow("开机自启", autostart ? "已开启" : "已关闭", ref yy, w);
        yy += 8f;
        GUI.Label(new Rect(8f, yy, w - 32f, 48f), "更新只替换程序目录。聊天记录、偏好、记忆和设置都保存在用户数据目录，不会被覆盖。", _termLogStyle);
        yy += 62f;
        Rect openRect = new Rect(8f, yy, 150f, 40f);
        Rect copyRect = new Rect(170f, yy, 150f, 40f);
        if (GUI.Button(openRect, "打开数据文件夹", _subBtnStyle)) OpenDataFolder();
        if (GUI.Button(copyRect, "复制数据路径", _subBtnStyle)) CopyDataPath();
        RegisterExtHit(openRect, OpenDataFolder);
        RegisterExtHit(copyRect, CopyDataPath);
        yy += 58f;
        GUI.Label(new Rect(8f, yy, w - 32f, 24f), "本版本变更摘要", _subSectionStyle);
        GUI.Label(new Rect(8f, yy + 30f, w - 32f, 170f), TruncateReleaseNotes(notes), _termLogDimStyle);
        if (!string.IsNullOrEmpty(_experienceStatus)) GUI.Label(new Rect(8f, yy + 210f, w - 32f, 35f), _experienceStatus, _termLogDimStyle);
        GUI.EndScrollView();
    }

    private void DrawAboutRow(string label, string value, ref float y, float w)
    {
        GUI.Label(new Rect(8f, y, 130f, 24f), label, _subSectionStyle);
        GUI.Label(new Rect(142f, y, w - 166f, 36f), value, new GUIStyle(_termLogDimStyle) { wordWrap = true });
        y += 44f;
    }

    private static string GetRunDirectory()
    {
        try { return Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName) ?? Application.dataPath; }
        catch { return Application.dataPath; }
    }

    private static string GetReleaseText(string fileName, string fallback)
        => ReadReleaseText(GetRunDirectory(), fileName, fallback);

    /// <summary>读取随程序发布的本地文本；测试与开发构建可验证回退路径。</summary>
    public static string ReadReleaseText(string directory, string fileName, string fallback)
    {
        try
        {
            string path = Path.Combine(directory, fileName);
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[UserExperience] 读取 {fileName} 失败：{ex.Message}"); }
        return fallback;
    }

    private static bool CanWriteDataRoot()
    {
        try
        {
            if (!DataPathConfig.EnsureDataRoot(out _)) return false;
            string probe = Path.Combine(DataPathConfig.DataRoot, ".ui_write_probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static string TruncateReleaseNotes(string notes)
    {
        notes = notes.Replace("\r", "").Trim();
        return notes.Length <= 900 ? notes : notes.Substring(0, 900) + "\n…（完整说明在安装目录 release-notes.txt）";
    }

    private void OpenDataFolder()
    {
        if (DataPathConfig.IsTestMode) { _experienceStatus = "测试模式：未实际打开文件夹。"; return; }
        try
        {
            Process.Start(new ProcessStartInfo(DataPathConfig.DataRoot) { UseShellExecute = true });
            _experienceStatus = "已打开用户数据文件夹。";
        }
        catch (Exception ex) { _experienceStatus = "打开数据文件夹失败：" + ex.Message; }
    }

    private void CopyDataPath()
    {
        GUIUtility.systemCopyBuffer = DataPathConfig.DataRoot;
        _experienceStatus = "已复制用户数据路径。";
    }
}
