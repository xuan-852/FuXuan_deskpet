using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 测试模式运行时输入模拟器。
///
/// 命令从 DataPathConfig.InboxFile 进入，由 RightPanel 轮询调用。
/// 这里只模拟桌宠自身的输入语义，不调用 OS 鼠标 API，因此不会移动开发者真实鼠标。
/// </summary>
public static class RuntimeInputSimulator
{
    private const string SIM_PREFIX = "@@sim:";
    private const string INPUT_PREFIX = "@@input:";

    /// <summary>
    /// 处理 @@sim / @@input 命令。返回 true 表示该文本已被识别，不能继续进入聊天。
    /// 所有模拟命令都要求 .test_mode，避免生产 inbox 被当成调试遥控入口。
    /// </summary>
    public static bool TryHandle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string body;
        if (raw.StartsWith(SIM_PREFIX, StringComparison.OrdinalIgnoreCase))
            body = raw.Substring(SIM_PREFIX.Length).Trim();
        else if (raw.StartsWith(INPUT_PREFIX, StringComparison.OrdinalIgnoreCase))
            body = raw.Substring(INPUT_PREFIX.Length).Trim();
        else
            return false;

        if (!ChatManager.IsTestMode)
        {
            Debug.LogWarning("[TestInbox] 模拟输入已忽略：仅测试模式允许 @@sim/@@input");
            return true;
        }

        // 节日主题是像素符玄/UI 的本地状态，不依赖 DragHandler；必须在查找拖拽组件前处理。
        if (body.StartsWith("holiday:", StringComparison.OrdinalIgnoreCase))
        {
            string theme = body.Substring("holiday:".Length).Trim();
            string result;
            bool ok = HolidayThemeRuntime.TrySetTheme(theme, out result);
            if (ok) Debug.Log("[TestInbox] " + result);
            else Debug.LogWarning("[TestInbox] " + result);
            return true;
        }

        if (body.Equals("holidays", StringComparison.OrdinalIgnoreCase))
        {
            string result;
            HolidayThemeRuntime.TrySetTheme("list", out result);
            Debug.Log("[TestInbox] " + result);
            return true;
        }

        DragHandler drag = UnityEngine.Object.FindObjectOfType<DragHandler>();
        if (drag == null)
        {
            Debug.LogWarning("[TestInbox] 模拟输入失败：未找到 DragHandler");
            return true;
        }

        if (body.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log("[TestInbox] 模拟输入状态: " + drag.GetDebugState());
            return true;
        }

        if (body.StartsWith("screenshot", StringComparison.OrdinalIgnoreCase))
        {
            string suffix = body.Length > "screenshot".Length
                ? body.Substring("screenshot".Length).TrimStart(':', ' ', '\t')
                : "drag";
            string safeName = SanitizeScreenshotName(suffix);
            string dir = System.IO.Path.Combine(DataPathConfig.DataRoot, "test_screenshots");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir,
                $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[TestInbox] screenshot queued: " + path);
            return true;
        }

        if (body.Equals("reset", StringComparison.OrdinalIgnoreCase)
            || body.Equals("release", StringComparison.OrdinalIgnoreCase))
        {
            drag.ResetInputSimulation();
            return true;
        }

        if (body.StartsWith("click:", StringComparison.OrdinalIgnoreCase))
        {
            string value = body.Substring("click:".Length).Trim();
            Vector2 point;
            if (!TryResolvePoint(value, drag, out point))
            {
                WarnFormat("click", value, "x,y 或 center");
                return true;
            }
            drag.SimulateClick(point);
            return true;
        }

        if (body.StartsWith("drag:", StringComparison.OrdinalIgnoreCase))
        {
            string value = body.Substring("drag:".Length).Trim();
            Vector2 start;
            Vector2 end;
            int steps;
            if (TryParseOffsetDrag(value, drag, out start, out end, out steps)
                || TryParseAbsoluteDrag(value, drag, out start, out end, out steps))
            {
                drag.SimulateDrag(start, end, steps);
            }
            else
            {
                WarnFormat("drag", value, "x1,y1->x2,y2[,steps] 或 offset:dx,dy[,steps]");
            }
            return true;
        }

        Debug.LogWarning("[TestInbox] 未知模拟输入: " + body
            + "（支持 status/reset/release/holiday:list/holiday:status/holiday:cn_new_year/holiday:off/holiday:auto/click:x,y/click:center/drag:x1,y1->x2,y2[,steps]/drag:offset:dx,dy[,steps]）");
        return true;
    }

    private static bool TryParseOffsetDrag(string value, DragHandler drag,
        out Vector2 start, out Vector2 end, out int steps)
    {
        start = Vector2.zero;
        end = Vector2.zero;
        steps = 12;
        if (!value.StartsWith("offset:", StringComparison.OrdinalIgnoreCase)) return false;

        string[] parts = value.Substring("offset:".Length).Split(',');
        float dx;
        float dy;
        if (parts.Length < 2 || !TryFloat(parts[0], out dx) || !TryFloat(parts[1], out dy)) return false;
        if (parts.Length >= 3 && !int.TryParse(parts[2].Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out steps)) return false;

        Rect rect = drag.GetDebugPetRect();
        start = rect.center;
        end = start + new Vector2(dx, dy);
        return true;
    }

    private static bool TryParseAbsoluteDrag(string value, DragHandler drag,
        out Vector2 start, out Vector2 end, out int steps)
    {
        start = Vector2.zero;
        end = Vector2.zero;
        steps = 12;
        string[] ends = value.Split(new[] { "->" }, StringSplitOptions.None);
        if (ends.Length != 2) return false;
        if (!TryResolvePoint(ends[0].Trim(), drag, out start)) return false;

        string[] endParts = ends[1].Split(',');
        if (endParts.Length < 2) return false;
        float x;
        float y;
        if (!TryFloat(endParts[0], out x) || !TryFloat(endParts[1], out y)) return false;
        if (endParts.Length >= 3 && !int.TryParse(endParts[2].Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out steps)) return false;
        end = new Vector2(x, y);
        return true;
    }

    private static bool TryResolvePoint(string value, DragHandler drag, out Vector2 point)
    {
        point = Vector2.zero;
        if (value.Equals("center", StringComparison.OrdinalIgnoreCase))
        {
            point = drag.GetDebugPetRect().center;
            return true;
        }

        string[] parts = value.Split(',');
        float x;
        float y;
        if (parts.Length != 2 || !TryFloat(parts[0], out x) || !TryFloat(parts[1], out y)) return false;
        point = new Vector2(x, y);
        return true;
    }

    private static bool TryFloat(string raw, out float value)
    {
        return float.TryParse(raw.Trim(), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value);
    }

    private static string SanitizeScreenshotName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "drag";
        var chars = raw.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
                chars[i] = '_';
        }
        string result = new string(chars).Trim('_');
        return string.IsNullOrEmpty(result) ? "drag" : result;
    }

    private static void WarnFormat(string command, string value, string expected)
    {
        Debug.LogWarning($"[TestInbox] {command} 参数格式错误: {value}（应为 {expected}）");
    }
}
