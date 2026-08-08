using System.Diagnostics;
using UnityEngine;

/// <summary>
/// 开阵·Pogget — 调用 Pogget 桌面收纳工具
///
/// 用途：当用户说「整理桌面」「打开收纳工具」「帮我收文件」「启动文件整理」
///      时，此术启动外置的 Pogget.exe 桌面收纳程序。
/// </summary>
public class PoggetTool : IPetTool
{
    /// <summary>
    /// Pogget 执行文件路径。可在 Unity Inspector 中覆盖，
    /// 默认为 d:\pogget\Pogget.exe
    /// </summary>
    public static string ExePath { get; set; } = @"d:\pogget\Pogget.exe";

    public string ToolName => "launch_pogget";
    public string ToolDescription => "打开 Pogget 桌面收纳工具，用于整理桌面文件、收纳文件到收纳盒、打开快速面板（侧边栏）窗口等。当用户说「整理桌面」「打开收纳」「帮我收文件」「启动文件整理」「把文件收起来」「打开侧边栏窗口」时调用。注意：只是打开 Pogget 窗口，不执行整理；实际整理用 pogget_agent 工具的 organize_desktop 或 add_to_container。";
    public string ToolParametersJson => "{\"type\": \"object\", \"properties\": {}}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string exe = ExePath;

        if (!System.IO.File.Exists(exe))
        {
            // 尝试在站内查找
            string alt = Application.dataPath.Replace("/Assets", "") + "/Tools/Pogget/Pogget.exe";
            if (System.IO.File.Exists(alt))
                exe = alt;
            else
                return $"❌ 未找到 Pogget：{exe}，请检查路径是否正确";
        }

        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exe)
            };
            Process.Start(psi);
            UnityEngine.Debug.Log($"[PoggetTool] 已启动 Pogget: {exe}");
            return "✅ 已为您打开桌面收纳工具 Pogget，您可以直接拖放文件到收纳盒中整理。";
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"[PoggetTool] 启动失败: {e.Message}");
            return $"❌ 启动 Pogget 失败：{e.Message}";
        }
    }

    public System.Collections.IEnumerator ExecuteAsync(string argsJson, System.Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}
