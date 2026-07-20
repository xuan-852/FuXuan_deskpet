using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 符玄「法阵」— 工具调用执行器
/// 将 DeepSeek Function Calling 映射到 Windows 系统操作
/// </summary>
public class ToolCallInvoker : MonoBehaviour
{
    [Header("安全设置")]
    [Tooltip("危险操作（关机/重启/执行命令）需要人确认")]
    public bool dangerousOpsNeedConfirm = true;

    [Header("当前执行状态")]
    public string lastToolResult = "";
    public string lastToolError = "";

    // ========== 工具注册表 ==========
    private Dictionary<string, Func<string, string>> _executors;

    void Awake()
    {
        RegisterAllTools();
        RegisterCoroutineTools();
    }

    // ================================================================
    //  工具注册
    // ================================================================

    private void RegisterAllTools()
    {
        _executors = new Dictionary<string, Func<string, string>>();

        // ——— 1. 观星窥天：打开网页 ———
        _executors["open_url"] = args =>
        {
            string url = JsonRead(args, "url");
            if (string.IsNullOrEmpty(url)) return "❌ 未提供网址，本座如何观星？";
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return $"✅ 已开天目，窥视「{url}」";
        };

        // ——— 2. 太卜敕令：启动应用/文件 ———
        _executors["open_app"] = args =>
        {
            string name = JsonRead(args, "name");
            if (string.IsNullOrEmpty(name)) return "❌ 未指明要启动什么";
            try
            {
                // 先直接试试
                Process.Start(new ProcessStartInfo(name) { UseShellExecute = true });
                return $"✅ 已遵法旨，召来「{name}」";
            }
            catch
            {
                // 直接启动失败 → 快速模糊搜索（禁用递归搜索以免卡死主线程！）
                try
                {
                    // 1) where 不加 /R → 只搜 PATH（超快，毫秒级）
                    string found = FastWhich(name);
                    if (found != null)
                    {
                        Process.Start(new ProcessStartInfo(found) { UseShellExecute = true });
                        return $"✅ 在 PATH 中寻得「{Path.GetFileName(found)}」，已召来";
                    }

                    // 2) 搜开始菜单快捷方式（本地文件夹，快）
                    string startMenu = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                    var lnk = FastFindLink(startMenu, name) ??
                              FastFindLink(
                                  Path.Combine(Environment.GetFolderPath(
                                      Environment.SpecialFolder.CommonStartMenu), "Programs"),
                                  name);
                    if (lnk != null)
                    {
                        Process.Start(new ProcessStartInfo(lnk) { UseShellExecute = true });
                        return $"✅ 在开始菜单寻得「{Path.GetFileNameWithoutExtension(lnk)}」，已召来";
                    }

                    return $"❌ 本座寻遍四海也未找到「{name}」……你要不要试试说全名？";
                }
                catch (Exception e2)
                {
                    return $"❌ 召不来…{e2.Message}";
                }
            }
        };

        // ——— 3. 穷观推演：浏览器搜索 ———
        _executors["search"] = args =>
        {
            string query = JsonRead(args, "query");
            if (string.IsNullOrEmpty(query)) return "❌ 未说要推演什么";
            string url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(query);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return $"✅ 穷观阵已推演「{query}」，结果自现";
        };

        // ——— 4. 法眼摄形+洞观：截图后经 GLM 视觉分析（协程实现） ———
        // 注：实际实现在 RegisterCoroutineTools 中，此处占位以防同步调用报错
        _executors["take_screenshot"] = args => "⏳ 法眼摄形中，请稍候……";

        // ——— 5. 气运探查：系统信息 ———
        _executors["get_system_info"] = args =>
        {
            return GetSystemInfo();
        };

        // ——— 6. 封印结界：锁屏 ———
        _executors["lock_screen"] = args =>
        {
            LockWorkStation();
            return "🔒 已布下封印结界，旁人不得窥探";
        };

        // ——— 7. 听诊天机：音量控制 ———
        _executors["set_volume"] = args =>
        {
            string levelStr = JsonRead(args, "level");
            if (int.TryParse(levelStr, out int level))
            {
                level = Mathf.Clamp(level, 0, 100);
                SetSystemVolume(level);
                return level == 0
                    ? "🔇 已禁声，天地俱寂"
                    : $"🔊 已将音量调至 {level}%";
            }
            return "❌ 音量须在 0~100 之间";
        };

        // ——— 8. 调音结界：静音切换 ———
        _executors["mute"] = args =>
        {
            string muteStr = JsonRead(args, "muted");
            bool mute = muteStr == "true";
            SetSystemVolume(mute ? 0 : 50);
            return mute ? "🔇 已禁声" : "🔊 已解禁";
        };

        // ——— 9. 摄魂取念：读剪贴板 ———
        _executors["get_clipboard"] = args =>
        {
            string text = GetClipboardText();
            return string.IsNullOrEmpty(text)
                ? "📋 剪贴板空空如也"
                : $"📋 剪贴板内容：\n{text}";
        };

        // ——— 10. 传音入密：写剪贴板 ———
        _executors["set_clipboard"] = args =>
        {
            string text = JsonRead(args, "text");
            if (string.IsNullOrEmpty(text)) return "❌ 未说要传什么";
            SetClipboardText(text);
            return $"✅ 已写入剪贴板：{text}";
        };

        // ——— 11. 洞天开门：打开文件夹 ———
        _executors["open_folder"] = args =>
        {
            string path = JsonRead(args, "path");
            if (string.IsNullOrEmpty(path))
                path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            // 默认路径已在安全范围内，无需额外的 IsPathAllowed 检查
            if (!Directory.Exists(path) && !File.Exists(path))
                return $"❌ 路径不存在：「{path}」";
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return $"✅ 洞天已开：「{path}」";
            }
            catch (Exception e)
            {
                return $"❌ 开门失败…{e.Message}";
            }
        };

        // ——— 12. 传讯符：桌面通知 ———
        _executors["notify"] = args =>
        {
            string title = JsonRead(args, "title");
            string message = JsonRead(args, "message");
            if (string.IsNullOrEmpty(title)) title = "符玄传讯";
            if (string.IsNullOrEmpty(message)) message = "本座有话要说。";
            ShowNotification(title, message);
            return "📨 传讯符已发出";
        };

        // ——— 13. 归寂令：关机/重启/睡眠（需要物理弹窗确认） ———
        _executors["power"] = args =>
        {
            string action = JsonRead(args, "action"); // shutdown / restart / sleep
            if (dangerousOpsNeedConfirm)
            {
                string actionLabel = action switch
                {
                    "shutdown" => "关机",
                    "restart"  => "重启",
                    "sleep"    => "睡眠",
                    _         => action
                };
                // 使用 Win32 MessageBox 弹窗，需要用户实际点击"是"才能执行
                int result = MessageBox(IntPtr.Zero,
                    $"符玄请求执行「{actionLabel}」操作，是否允许？",
                    "⚠️ 归寂令 · 确认",
                    0x00000004 | 0x00000030); // MB_YESNO | MB_ICONWARNING
                if (result != 6) // IDYES = 6
                    return "🛡️ 归寂令已被卜者驳回";
            }
            switch (action)
            {
                case "shutdown":
                    Process.Start("shutdown", "/s /t 5");
                    return "🕯️ 归寂令已下，5 秒后仙舟停转";
                case "restart":
                    Process.Start("shutdown", "/r /t 5");
                    return "🔄 重启法阵已启，5 秒后重开天地";
                case "sleep":
                    Application.Quit();
                    return "💤 本座要歇息了……";
                default:
                    return "❌ 不识此令（可用: shutdown/restart/sleep）";
            }
        };

        // ——— 14. 法旨行令：执行命令 ———
        _executors["run_command"] = args =>
        {
            string cmd = JsonRead(args, "command");
            if (string.IsNullOrEmpty(cmd)) return "❌ 未降法旨";
            // 命令白名单 — 只允许无害的查询类命令
            if (!IsCommandAllowed(cmd))
                return "⚠️ 本座只可执行查询类命令（ipconfig, ping, systeminfo, tasklist, netstat 等），" +
                       "此令「" + cmd + "」不在允许之列";
            try
            {
                var psi = new ProcessStartInfo("cmd", "/c " + cmd)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                var p = Process.Start(psi);
                // 非阻塞等 3 秒，超时就 Kill（防止卡死 Unity 主线程）
                if (p != null && !p.WaitForExit(3000))
                {
                    try { p.Kill(); } catch { }
                    return "⏱️ 行令超时（>3s），法旨已被截断";
                }
                string output = p?.StandardOutput?.ReadToEnd() ?? "";
                string err = p?.StandardError?.ReadToEnd() ?? "";
                string result = output;
                if (!string.IsNullOrEmpty(err)) result += "\n[error]\n" + err;
                return string.IsNullOrEmpty(result)
                    ? "✅ 令行禁止，无回响"
                    : $"📜 回响：\n{StringExtensions.Truncate(result, 500)}";
            }
            catch (Exception e)
            {
                return $"❌ 行令有违…{e.Message}";
            }
        };

        // ——— 15. 本座方位：鼠标位置 ———
        _executors["get_mouse_pos"] = args =>
        {
            GetCursorPos(out POINT p);
            return $"🖱️ 鼠标位于 ({p.X}, {p.Y})";
        };

        // ——— 16. 探查目录：列文件 ———
        _executors["list_files"] = args =>
        {
            string dir = JsonRead(args, "path");
            if (string.IsNullOrEmpty(dir))
                dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            dir = DecodeFileUri(dir);
            if (!Directory.Exists(dir))
                return $"❌ 「{dir}」不存在";
            var files = Directory.GetFiles(dir);
            var dirs = Directory.GetDirectories(dir);
            var sb = new StringBuilder();
            sb.AppendLine($"📁 {dir} 中共 {dirs.Length} 目录 / {files.Length} 文件");
            foreach (var d in dirs) sb.AppendLine($"  📂 {Path.GetFileName(d)}/");
            foreach (var f in files) sb.AppendLine($"  📄 {Path.GetFileName(f)}");
            return StringExtensions.Truncate(sb.ToString(), 800);
        };

        // ——— 17. 搜天寻地：搜索文件 (优先用 Everything，毫秒级) ———
        _executors["search_files"] = args =>
        {
            string query = JsonRead(args, "query");
            string rootDir = JsonRead(args, "root");
            if (string.IsNullOrEmpty(query)) return "❌ 未说要搜什么";

            string esExe = FindEverythingCli();
            bool useEverything = esExe != null;

            try
            {
                var task = Task.Run(() =>
                {
                    var results = new List<string>();

                    if (useEverything)
                    {
                        // ——— 用 Everything (es.exe) 搜索，毫秒级 ———
                        try
                        {
                            string esArgs = $"-n 200 \"{query.Replace("\"", "\\\"")}\"";
                            if (!string.IsNullOrEmpty(rootDir))
                                esArgs = $"-n 200 -path \"{rootDir.Replace("\"", "\\\"")}\" \"{query.Replace("\"", "\\\"")}\"";

                            var psi = new ProcessStartInfo(esExe, esArgs)
                            {
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true,
                                StandardOutputEncoding = Encoding.GetEncoding(936),
                                StandardErrorEncoding = Encoding.GetEncoding(936)
                            };
                            var p = Process.Start(psi);
                            if (p != null)
                            {
                                string output = p.StandardOutput.ReadToEnd();
                                p.WaitForExit(3000);
                                // es.exe 1.1.0.30 不支持 -utf8 参数，输出为 GBK。
                                // 用 GBK (codepage 936) 解码确保中文文件名正确。
                                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var line in lines)
                                {
                                    if (!string.IsNullOrWhiteSpace(line))
                                        results.Add(line);
                                }
                            }
                        }
                        catch (Exception ex) 
                        { 
                            UnityEngine.Debug.LogWarning($"[ToolCallInvoker] es.exe 搜索失败，回退到递归: {ex.Message}");
                            useEverything = false; 
                        }
                    }

                    if (!useEverything)
                    {
                        // ——— 回退：递归搜索 ———
                        try
                        {
                            if (string.IsNullOrEmpty(rootDir))
                                rootDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            if (Directory.Exists(rootDir))
                                SearchRecursive(rootDir, query, results, 200);
                        }
                        catch (Exception ex)
                        {
                            results.Add($"⚠️ 搜索中断：{ex.Message}");
                        }
                    }

                    return results;
                });

                // 超时限制（同步模式下避免死锁）
                if (task.Wait(TimeSpan.FromSeconds(5)))
                {
                    var list = task.Result;
                    if (list.Count < 1)
                    {
                        string scope = useEverything
                            ? (string.IsNullOrEmpty(rootDir) ? "本座的天眼所及之处" : $"「{rootDir}」")
                            : (string.IsNullOrEmpty(rootDir) ? "桌面" : $"「{rootDir}」");
                        return $"🔍 在{scope}中未找到与「{query}」匹配的文件";
                    }
                    string method = useEverything ? "⚡本座以 Everything 天眼通搜" : "🔍本座以递归之法搜";
                    string scope2 = string.IsNullOrEmpty(rootDir) ? "全境" : $"「{rootDir}」";
                    var sb = new StringBuilder();
                    sb.AppendLine($"{method}{scope2}，得 {list.Count} 件与「{query}」相关之物：");
                    foreach (var f in list)
                        sb.AppendLine($"  📄 {f}");
                    return StringExtensions.Truncate(sb.ToString(), 2000);
                }
                else
                {
                    return $"⏱️ 搜索「{query}」耗时过长（>10s），请缩小搜索范围或指定更具体的目录";
                }
            }
            catch (Exception e)
            {
                return $"❌ 搜索时出了岔子：{e.Message}";
            }
        };

        // ——— 18. 卜算记事：设置提醒 ———
        _executors["set_reminder"] = args =>
        {
            string text = JsonRead(args, "text");
            string timeStr = JsonRead(args, "remind_at");
            if (string.IsNullOrEmpty(text)) return "❌ 未说要提醒何事";

            DateTime remindAt;
            if (!string.IsNullOrEmpty(timeStr))
            {
                if (!DateTime.TryParse(timeStr, out remindAt))
                    return "❌ 时辰格式不对，本座只识 yyyy-MM-dd HH:mm 之流";
            }
            else
            {
                remindAt = DateTime.Now.AddHours(1); // 默认 1 小时后
            }

            if (remindAt <= DateTime.Now)
            {
                // 可能是年份算错了，自动逐年修正到未来
                DateTime fixedTime = remindAt;
                for (int i = 0; i < 5 && fixedTime <= DateTime.Now; i++)
                    fixedTime = fixedTime.AddYears(1);
                if (fixedTime > DateTime.Now)
                {
                    remindAt = fixedTime;
                }
                else
                {
                    // 实在修不好，才拒绝
                    return "❌ 定在过去可不行，本座不会时光倒流之术";
                }
            }

            string recurring = JsonRead(args, "recurring");
            string priority = JsonRead(args, "priority");
            if (string.IsNullOrEmpty(priority)) priority = "normal";

            var mgr = ReminderManager.Instance;
            if (mgr == null) return "❌ 卜算记事簿未就绪";

            var r = mgr.AddReminder(text, remindAt,
                string.IsNullOrEmpty(recurring) ? null : recurring,
                priority, "ai");
            return $"✅ 已记入卜算记事簿！提醒「{text}」定于 {remindAt:yyyy-MM-dd HH:mm}" +
                   $"\n📌 ID: {r.id.Substring(0, 8)}… 可对我说「查提醒」查阅";
        };

        // ——— 18. 查阅记事：查询提醒 ———
        _executors["query_reminders"] = args =>
        {
            var mgr = ReminderManager.Instance;
            if (mgr == null) return "❌ 卜算记事簿未就绪";
            return mgr.GetPendingText();
        };

        // ——— 19. 勾销事宜：标记完成 ———
        _executors["mark_reminder_done"] = args =>
        {
            string id = JsonRead(args, "id");
            if (string.IsNullOrEmpty(id)) return "❌ 未指定要勾销哪条提醒的 ID";

            var mgr = ReminderManager.Instance;
            if (mgr == null) return "❌ 卜算记事簿未就绪";

            // 支持用前几位匹配（用户不一定记得完整 ID）
            var all = mgr.GetAllReminders();
            var match = all.Find(r => r.id.StartsWith(id) && !r.done);
            if (match == null) return $"❌ 未找到 ID 以「{id}」开头的待办事项";

            mgr.MarkDone(match.id);
            return $"✅ 已勾销「{match.text}」";
        };

        // ——— 20. 销毁记事：删除提醒 ———
        _executors["delete_reminder"] = args =>
        {
            string id = JsonRead(args, "id");
            if (string.IsNullOrEmpty(id)) return "❌ 未指定要删除哪条提醒的 ID";

            var mgr = ReminderManager.Instance;
            if (mgr == null) return "❌ 卜算记事簿未就绪";

            var all = mgr.GetAllReminders();
            var match = all.Find(r => r.id.StartsWith(id));
            if (match == null) return $"❌ 未找到 ID 以「{id}」开头的事项";

            mgr.DeleteReminder(match.id);
            return $"✅ 已销毁记事「{match.text}」";
        };

        // ——— 21. 查询考试：从课表服务获取考试安排 ———
        _executors["query_exams"] = args =>
        {
            var poll = FindObjectOfType<ServerPollService>();
            if (poll == null) return "❌ 课表传讯服务未就绪";
            try
            {
                var task = Task.Run(() => poll.QueryUpcomingExamsAsync());
                if (task.Wait(TimeSpan.FromSeconds(8)))
                    return task.Result;
                return "⏱️ 查询考试超时，请稍后再试";
            }
            catch (System.Exception e)
            {
                return $"❌ 查询考试时出错: {e.Message}";
            }
        };

        // ——— 22. 查看成绩：查询学业成绩 ———
        _executors["query_scores"] = args =>
        {
            var poll = FindObjectOfType<ServerPollService>();
            if (poll == null) return "❌ 课表传讯服务未就绪";
            try
            {
                var task = Task.Run(() => poll.QueryScoresAsync());
                if (task.Wait(TimeSpan.FromSeconds(8)))
                    return task.Result;
                return "⏱️ 查询成绩超时，请稍后再试";
            }
            catch (System.Exception e)
            {
                return $"❌ 查询成绩时出错: {e.Message}";
            }
        };

        // ——— 23. 查看课表：查询课程安排 ———
        _executors["query_schedule"] = args =>
        {
            var poll = FindObjectOfType<ServerPollService>();
            if (poll == null) return "❌ 课表传讯服务未就绪";
            int week = 0;
            int.TryParse(JsonRead(args, "week"), out week);
            try
            {
                var task = Task.Run(() => poll.QueryScheduleAsync(week));
                if (task.Wait(TimeSpan.FromSeconds(8)))
                    return task.Result;
                return "⏱️ 查询课表超时，请稍后再试";
            }
            catch (System.Exception e)
            {
                return $"❌ 查询课表时出错: {e.Message}";
            }
        };

        // ——— 24. 卜算学业：查看用户绑定状态和学业概览 ———
        _executors["query_user_status"] = args =>
        {
            var poll = FindObjectOfType<ServerPollService>();
            if (poll == null) return "❌ 课表传讯服务未就绪";
            try
            {
                var task = Task.Run(() => poll.QueryUserStatusAsync());
                if (task.Wait(TimeSpan.FromSeconds(8)))
                    return task.Result;
                return "⏱️ 查询学业信息超时，请稍后再试";
            }
            catch (System.Exception e)
            {
                return $"❌ 查询学业信息时出错: {e.Message}";
            }
        };

        // ——— 25. 变脸术：切换桌面宠物的面部表情 ———
        _executors["set_expression"] = args =>
        {
            string exp = JsonRead(args, "expression");
            if (string.IsNullOrEmpty(exp)) return "❌ 未指定表情";
            var renderer = FindObjectOfType<Live2DRenderer>();
            if (renderer == null) return "❌ 本座法身未现";
            renderer.PlayExpression(exp);
            return $"✅ 已切换表情为「{exp}」";
        };

        // ——— 26. 演武：播放一段复合动作 ———
        _executors["play_action"] = args =>
        {
            string action = JsonRead(args, "action");
            if (string.IsNullOrEmpty(action)) return "❌ 未指定动作";
            var renderer = FindObjectOfType<Live2DRenderer>();
            if (renderer == null) return "❌ 本座法身未现";
            renderer.PlayAction(action);
            return $"✅ 正在演武「{action}」";
        };

        // ——— 27. 归元：停止所有动作/表情 ———
        _executors["stop_action"] = args =>
        {
            var renderer = FindObjectOfType<Live2DRenderer>();
            if (renderer == null) return "❌ 本座法身未现";
            renderer.ActionController?.StopAllWithFade();
            return "✅ 已归元，恢复常态";
        };

        // ——— 28. 演武心经：查看闭环修为统计 ———
        _executors["inspect_motion_memory"] = args =>
        {
            var mm = MotionMemoryManager.Instance;
            if (mm == null) return "❌ 演武心经未载入";
            return mm.GetStatistics();
        };

        // ——— 29. 本心：查看人格特质与关系状态 ———
        _executors["inspect_personality"] = args =>
        {
            var pm = PersonalityManager.Instance;
            if (pm == null) return "❌ 本心未载入";
            return pm.FormatForPrompt();
        };

        // ——— 30 (C). 内观自省：AI 分析参数变化效果
        // 注意：完整版 (截图 + GLM-4V 视觉分析) 在 _coroutineExecutors 中以协程运行。
        // 同步版作为轻量备选，实时输出当前参数状态快照，无需网络。
        _executors["explore_body"] = args =>
        {
            var renderer = FindObjectOfType<Live2DRenderer>();
            if (renderer == null || renderer.Mapper == null || renderer.CubismModel == null)
                return "❌ 本座法身未现";

            var mapper = renderer.Mapper;
            var model = renderer.CubismModel;
            var lines = new List<string>
            {
                $"🧘 本座当前状态 ({DateTime.Now:HH:mm:ss})："
            };

            // 按 body part 分组输出参数状态
            var partOrder = new[] { "head", "eye", "brow", "mouth", "body", "arm", "hand", "leg", "shoulder", "hair", "skirt", "special", "camera", "breath" };
            var partLabels = new Dictionary<string, string>
            {
                ["head"] = "头部", ["eye"] = "眼睛", ["brow"] = "眉毛", ["mouth"] = "嘴",
                ["body"] = "身体", ["arm"] = "手臂", ["hand"] = "手", ["leg"] = "腿",
                ["shoulder"] = "肩膀", ["hair"] = "头发", ["skirt"] = "裙子",
                ["special"] = "特殊", ["camera"] = "镜头", ["breath"] = "呼吸"
            };

            // 从映射数据获取 bodyPart 标注
            var entryPartMap = new Dictionary<string, string>();
            var mapAsset = Resources.Load<TextAsset>("Live2D/ParamMaps/fuxuan_map");
            if (mapAsset != null)
            {
                try
                {
                    var mapObj = UnityEngine.JsonUtility.FromJson<FuxuanMapData>(mapAsset.text);
                    if (mapObj?.entries != null)
                        foreach (var e in mapObj.entries)
                            if (!string.IsNullOrEmpty(e.part))
                                entryPartMap[e.s] = e.part;
                }
                catch { /* fallback: part unknown */ }
            }

            // 按 part 分组收集
            var byPart = new Dictionary<string, List<string>>();
            foreach (var semantic in mapper.SemanticToId.Keys)
            {
                if (!mapper.TryGetRange(semantic, out var range)) continue;
                float current = mapper.Get(semantic);
                float normalized = Mathf.Abs(current - range.Default) / Mathf.Max(range.Max - range.Min, 0.001f);
                string activeMark = normalized > 0.05f ? " ⚡" : "";
                string part = entryPartMap.TryGetValue(semantic, out var p) ? p : "other";
                if (!byPart.ContainsKey(part)) byPart[part] = new List<string>();
                byPart[part].Add($"  {semantic}={current:F2}[{range.Min:F0}~{range.Max:F0}]{activeMark}");
            }

            foreach (var part in partOrder)
            {
                if (!byPart.TryGetValue(part, out var plist)) continue;
                string label = partLabels.TryGetValue(part, out var lb) ? lb : part;
                lines.Add($"\n■ {label} ({plist.Count})");
                lines.AddRange(plist);
            }

            // 其他未分类参数
            if (byPart.TryGetValue("other", out var others))
            {
                lines.Add($"\n■ 其他 ({others.Count})");
                lines.AddRange(others);
            }

            string result = string.Join("\n", lines);
            if (result.Length > 1500) result = result.Substring(0, 1500) + "\n...（截断，完整版请使用异步内观）";
            return result;
        };

        // ——— 29. 御形：精确控制身体参数（同步设置） ———
        _executors["control_body"] = args =>
        {
            var renderer = FindObjectOfType<Live2DRenderer>();
            if (renderer == null || renderer.Mapper == null) return "❌ 本座法身未现";

            var mapper = renderer.Mapper;

            // 设置 AI 控制锁（空闲动画不再覆盖参数）
            renderer.SetAiControlLock();

            // 解析 expression 参数（可选）
            string expression = JsonRead(args, "expression");
            if (!string.IsNullOrEmpty(expression))
            {
                var templates = MotionPlanner.PlanFromDescription(expression, 1f, mapper);
                if (templates.KeyFrames.Count > 0)
                {
                    // 套用第一个关键帧的参数
                    foreach (var kv in templates.KeyFrames[0].Values)
                        mapper.Set(kv.Key, kv.Value);
                }
            }

            // 解析普通参数（排除控制参数名）
            var controlKeys = new HashSet<string> { "expression", "duration" };
            var paramValues = new Dictionary<string, float>();
            var warnings = new List<string>();

            foreach (string rawPair in args.Split(','))
            {
                string pair = rawPair.Trim().TrimStart('{').TrimEnd('}');
                int colonIdx = pair.IndexOf(':');
                if (colonIdx < 0) continue;

                string key = pair.Substring(0, colonIdx).Trim().Trim('"');
                string valStr = pair.Substring(colonIdx + 1).Trim().Trim('"');
                if (controlKeys.Contains(key)) continue;

                // 跳过 JSON 结构字符开头的（嵌套对象）
                if (valStr.StartsWith("{") || valStr.StartsWith("[")) continue;

                if (float.TryParse(valStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float val))
                {
                    paramValues[key] = val;
                }
            }

            // 也尝试从 "params" 字段读取
            var fromParams = JsonReadDict(args, "params");
            foreach (var kv in fromParams)
                paramValues[kv.Key] = kv.Value;

            if (paramValues.Count == 0 && string.IsNullOrEmpty(expression))
                return "❌ 未指定任何参数或表情";

            // 安全校验
            var results = SafetyValidator.ValidateBulk(paramValues, mapper);
            foreach (var r in results)
                warnings.AddRange(r.Warnings);

            // 应用参数
            int applied = 0;
            foreach (var kv in paramValues)
            {
                mapper.Set(kv.Key, kv.Value);
                applied++;
            }

            // 构建返回信息
            string result = $"✅ 已御形：{applied} 个参数已调整";
            if (warnings.Count > 0)
                result += "\n⚠ 注意：\n" + string.Join("\n", warnings);
            return result;
        };

        // ——— 30. 观云望气：直接读取本地的天气数据（不开网页） ———
        _executors["get_weather"] = args =>
        {
            var tc = FindObjectOfType<TimeWeatherController>();
            if (tc == null) return "❌ 观星法阵未启";
            if (!tc.weatherFetched) return "⏳ 尚未观测天象，再稍等片刻";

            string wtName = tc.weather switch
            {
                TimeWeatherController.WeatherType.Clear    => "☀️ 晴",
                TimeWeatherController.WeatherType.Cloudy   => "☁️ 多云",
                TimeWeatherController.WeatherType.Overcast => "🌥 阴",
                TimeWeatherController.WeatherType.Rain     => "🌧 雨",
                TimeWeatherController.WeatherType.Drizzle  => "🌦 小雨",
                TimeWeatherController.WeatherType.Thunder  => "⛈ 雷雨",
                TimeWeatherController.WeatherType.Snow     => "❄️ 雪",
                TimeWeatherController.WeatherType.Fog      => "🌫 雾",
                _ => "未知"
            };
            string windInfo = tc.windSpeedKmh > 0
                ? $"{tc.windDirection}{tc.windSpeedKmh}km/h"
                : "无风";
            string city = string.IsNullOrEmpty(tc.cityCode) ? "本地" : tc.cityCode;
            return $"🌤️ 本座以{tc.weatherSourceLabel}观天之象（{city}）：\n• 天气：{wtName}\n• 气温：{tc.temperatureC:F0}°C\n• 风力：{windInfo}\n• 湿度：{tc.humidityPercent}%\n• 气压：{tc.pressureHpa}hPa";
        };

        // ——— 29. 搜天彻地：全盘搜索任意文件（支持中文/特殊字符） ———
        _executors["search_file"] = args =>
        {
            string query = JsonRead(args, "query");
            string rootDir = JsonRead(args, "root");
            if (string.IsNullOrEmpty(query)) return "❌ 未说要搜什么";

            // 用 Python 桥（find_file.py）搜索，原生支持中文和特殊字符
            return SearchFileByPython(query, rootDir);
        };

        // ——— 30. 洞开天门：打开任意文件/文件夹/应用（自动识别路径） ———
        _executors["file_open"] = args =>
        {
            string path = JsonRead(args, "path");
            if (string.IsNullOrEmpty(path)) return "❌ 未指定要打开什么";
            try
            {
                // 路径可能是 file:// URI 格式（来自 search_file 的结果）
                string actualPath = path;
                if (path.StartsWith("file:///") || path.StartsWith("file://"))
                {
                    var uri = new Uri(path);
                    actualPath = Uri.UnescapeDataString(uri.AbsolutePath);
                    // Windows: file:///D:/path → D:/path
                    if (actualPath.StartsWith("/") && actualPath.Length >= 3 && actualPath[2] == ':')
                        actualPath = actualPath.TrimStart('/');
                }

                if (!File.Exists(actualPath) && !Directory.Exists(actualPath))
                    return $"❌ 本座寻不到此物：「{actualPath}」";

                Process.Start(new ProcessStartInfo(actualPath) { UseShellExecute = true });
                return $"✅ 已开启「{Path.GetFileName(actualPath)}」";
            }
            catch (Exception e)
            {
                return $"❌ 开启失败：{e.Message}";
            }
        };

        // ——— 31. 移星换斗：移动/重命名文件或文件夹 ———
        _executors["file_move"] = args =>
        {
            string source = JsonRead(args, "source");
            string dest = JsonRead(args, "destination");
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(dest))
                return "❌ 需指明源与目标";

            // 解码 URI
            source = DecodeFileUri(source);
            dest = DecodeFileUri(dest);
            if (!IsPathAllowed(source)) return "❌ 本座不可移动禁地之物";
            if (!IsPathAllowed(dest)) return "❌ 本座不可将物移至禁地";

            try
            {
                if (File.Exists(source))
                {
                    // 确保目标目录存在
                    string destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    // 如果目标已存在，先删除（替代 overwrite 参数）
                    if (File.Exists(dest))
                        File.Delete(dest);
                    File.Move(source, dest);
                    return $"✅ 已将「{Path.GetFileName(source)}」移至「{dest}」";
                }
                else if (Directory.Exists(source))
                {
                    string destDir = Path.GetDirectoryName(dest.TrimEnd('\\'));
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    Directory.Move(source, dest);
                    return $"✅ 已将目录「{source}」移至「{dest}」";
                }
                else
                {
                    return $"❌ 源路径不存在：「{source}」";
                }
            }
            catch (Exception e)
            {
                return $"❌ 移星失败：{e.Message}";
            }
        };

        // ——— 32. 复制如印：复制文件或文件夹 ———
        _executors["file_copy"] = args =>
        {
            string source = JsonRead(args, "source");
            string dest = JsonRead(args, "destination");
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(dest))
                return "❌ 需指明源与目标";

            source = DecodeFileUri(source);
            dest = DecodeFileUri(dest);
            if (!IsPathAllowed(source)) return "❌ 本座不可复制禁地之物";
            if (!IsPathAllowed(dest)) return "❌ 本座不可将物复制至禁地";

            try
            {
                if (File.Exists(source))
                {
                    string destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Copy(source, dest, overwrite: true);
                    return $"✅ 已复制「{Path.GetFileName(source)}」至「{dest}」";
                }
                else if (Directory.Exists(source))
                {
                    // 递归复制目录
                    CopyDirectoryRecursive(source, dest);
                    return $"✅ 已复制目录「{source}」至「{dest}」";
                }
                else
                {
                    return $"❌ 源路径不存在：「{source}」";
                }
            }
            catch (Exception e)
            {
                return $"❌ 复制失败：{e.Message}";
            }
        };

        // ——— 33. 删除归无：删除文件或文件夹（到回收站） ———
        _executors["file_delete"] = args =>
        {
            string path = JsonRead(args, "path");
            bool permanent = JsonRead(args, "permanent") == "true";
            if (string.IsNullOrEmpty(path)) return "❌ 未指定要删除什么";

            path = DecodeFileUri(path);
            if (!IsPathAllowed(path)) return "❌ 本座不可窥探此等禁地，请选择桌面或文档目录中的文件";

            try
            {
                if (File.Exists(path))
                {
                    if (permanent)
                    {
                        File.Delete(path);
                    }
                    else
                    {
                        // 用 Shell API 移到回收站
                        SendToRecycleBin(path, isDir: false);
                    }
                    return $"✅ 已删除「{Path.GetFileName(path)}」{(permanent ? "（永久）" : "（移至回收站）")}";
                }
                else if (Directory.Exists(path))
                {
                    if (permanent)
                    {
                        Directory.Delete(path, recursive: true);
                    }
                    else
                    {
                        SendToRecycleBin(path, isDir: true);
                    }
                    return $"✅ 已删除目录「{path}」{(permanent ? "（永久）" : "（移至回收站）")}";
                }
                else
                {
                    return $"❌ 路径不存在：「{path}」";
                }
            }
            catch (Exception e)
            {
                return $"❌ 删除失败：{e.Message}";
            }
        };

        // ——— 34. 重命名器：重命名文件/文件夹 ———
        _executors["file_rename"] = args =>
        {
            string path = JsonRead(args, "path");
            string newName = JsonRead(args, "new_name");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName))
                return "❌ 需指明文件与新名称";

            path = DecodeFileUri(path);
            if (!IsPathAllowed(path)) return "❌ 本座不可重命名禁地之物";

            try
            {
                if (File.Exists(path))
                {
                    string dir = Path.GetDirectoryName(path);
                    string newPath = Path.Combine(dir, newName);
                    File.Move(path, newPath);
                    return $"✅ 已将「{Path.GetFileName(path)}」重命名为「{newName}」";
                }
                else if (Directory.Exists(path))
                {
                    string parent = Path.GetDirectoryName(path.TrimEnd('\\'));
                    string newPath = Path.Combine(parent, newName);
                    Directory.Move(path, newPath);
                    return $"✅ 已将目录「{Path.GetFileName(path)}」重命名为「{newName}」";
                }
                else
                {
                    return $"❌ 路径不存在：「{path}」";
                }
            }
            catch (Exception e)
            {
                return $"❌ 重命名失败：{e.Message}";
            }
        };

        // ——— 35. 查看详情：获取文件/文件夹详细信息 ———
        _executors["file_info"] = args =>
        {
            string path = JsonRead(args, "path");
            if (string.IsNullOrEmpty(path)) return "❌ 未指定路径";

            path = DecodeFileUri(path);

            try
            {
                var sb = new StringBuilder();
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    sb.AppendLine($"📄 文件：{fi.Name}");
                    sb.AppendLine($"📁 位置：{fi.DirectoryName}");
                    sb.AppendLine($"📏 大小：{FormatFileSize(fi.Length)}");
                    sb.AppendLine($"🕐 创建：{fi.CreationTime:yyyy-MM-dd HH:mm}");
                    sb.AppendLine($"🕐 修改：{fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                    sb.AppendLine($"🕐 访问：{fi.LastAccessTime:yyyy-MM-dd HH:mm}");
                    if ((fi.Attributes & FileAttributes.ReadOnly) != 0) sb.AppendLine("🔒 只读");
                    if ((fi.Attributes & FileAttributes.Hidden) != 0) sb.AppendLine("👁️ 隐藏");
                }
                else if (Directory.Exists(path))
                {
                    var di = new DirectoryInfo(path);
                    sb.AppendLine($"📂 目录：{di.Name}");
                    sb.AppendLine($"📁 位置：{di.Parent?.FullName ?? path}");
                    sb.AppendLine($"🕐 创建：{di.CreationTime:yyyy-MM-dd HH:mm}");
                    sb.AppendLine($"🕐 修改：{di.LastWriteTime:yyyy-MM-dd HH:mm}");
                    int fileCount = 0, dirCount = 0;
                    try { fileCount = di.GetFiles().Length; dirCount = di.GetDirectories().Length; } catch { }
                    sb.AppendLine($"📊 包含：{dirCount} 目录 / {fileCount} 文件");
                    if ((di.Attributes & FileAttributes.Hidden) != 0) sb.AppendLine("👁️ 隐藏");
                }
                else
                {
                    return $"❌ 路径不存在：「{path}」";
                }
                return sb.ToString();
            }
            catch (Exception e)
            {
                return $"❌ 查询失败：{e.Message}";
            }
        };

        // ——— 36. 新建文件：创建空白文件 ———
        _executors["file_create"] = args =>
        {
            string path = JsonRead(args, "path");
            string content = JsonRead(args, "content");
            if (string.IsNullOrEmpty(path)) return "❌ 未指定路径";

            path = DecodeFileUri(path);
            if (!IsPathAllowed(path)) return "❌ 本座不可在此等禁地动土";

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (content != null)
                {
                    File.WriteAllText(path, content, Encoding.UTF8);
                }
                else
                {
                    File.Create(path).Dispose();
                }
                return $"✅ 已新建「{Path.GetFileName(path)}」";
            }
            catch (Exception e)
            {
                return $"❌ 新建失败：{e.Message}";
            }
        };

        // ——— 37. 新建目录：创建文件夹 ———
        _executors["dir_create"] = args =>
        {
            string path = JsonRead(args, "path");
            if (string.IsNullOrEmpty(path)) return "❌ 未指定路径";
            path = DecodeFileUri(path);
            if (!IsPathAllowed(path)) return "❌ 本座不可在此等禁地动土";
            try
            {
                Directory.CreateDirectory(path);
                return $"✅ 已创建目录「{path}」";
            }
            catch (Exception e)
            {
                return $"❌ 创建失败：{e.Message}";
            }
        };

        // ——— 38. 读文件：读取文本文件内容 ———
        _executors["file_read"] = args =>
        {
            string path = JsonRead(args, "path");
            int maxLen = 2000;
            int.TryParse(JsonRead(args, "max_length"), out maxLen);
            if (string.IsNullOrEmpty(path)) return "❌ 未指定文件";
            path = DecodeFileUri(path);

            if (!File.Exists(path)) return $"❌ 文件不存在：「{path}」";

            try
            {
                // 检测是否为二进制文件（比首字节法更可靠：扫描空字节）
                long fileSize = new FileInfo(path).Length;
                if (fileSize > 0)
                {
                    bool isBinary = false;
                    byte[] sniff = new byte[Math.Min(512, fileSize)];
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        fs.Read(sniff, 0, sniff.Length);
                    }
                    // 空字节是二进制文件最可靠的信号
                    for (int i = 0; i < sniff.Length; i++)
                    {
                        if (sniff[i] == 0) { isBinary = true; break; }
                    }
                    if (isBinary)
                        return $"📄 此文件非文本（检测到空字节），大小为 {FormatFileSize(fileSize)}，可用 file_open 打开";
                }
                else
                {
                    return "📄 文件为空";
                }

                string text = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrEmpty(text)) return "📄 文件为空";
                return $"📄 {Path.GetFileName(path)} 的内容：\n\n{StringExtensions.Truncate(text, maxLen)}";
            }
            catch (Exception e)
            {
                return $"❌ 读取失败：{e.Message}";
            }
        };
    }

    // ================================================================
    //  对外接口
    // ================================================================

    /// <summary>获取工具的 JSON 定义</summary>
    public string GetToolsJson()
    {
        // language=json
        return @"[
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""open_url"",
      ""description"": ""在默认浏览器中打开指定网址。比如用户说「打开B站」就调用此术。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""url"": { ""type"": ""string"", ""description"": ""要打开的完整网址"" }
        },
        ""required"": [""url""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""open_app"",
      ""description"": ""启动一个应用程序或打开文件。比如「打开计算器」「打开记事本」「帮我打开D盘下的某某文件」"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""name"": { ""type"": ""string"", ""description"": ""应用名称或文件路径，如 calc、notepad、D:/path/file.txt"" }
        },
        ""required"": [""name""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""search"",
      ""description"": ""在 Bing 搜索引擎上查询信息并打开浏览器显示结果。用户说「帮我搜一下xxx」时调用。禁止用于查询天气（天气请用 get_weather）"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""query"": { ""type"": ""string"", ""description"": ""搜索关键词"" }
        },
        ""required"": [""query""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""take_screenshot"",
      ""description"": ""截取当前屏幕并分析画面内容（自动调用 GLM 视觉模型）。用户说「截图」「看一下我屏幕」「屏幕上有什么」「看看我在干什么」时调用。调用后返回对屏幕内容的详细文字描述而非文件路径。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""get_system_info"",
      ""description"": ""获取电脑的系统信息，包括操作系统、CPU、内存、磁盘、运行时间等。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""lock_screen"",
      ""description"": ""锁定电脑屏幕。用户说「锁屏」「离开一下」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""set_volume"",
      ""description"": ""调节系统音量。用户说「声音大一点」「音量调到50」「静音」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""level"": { ""type"": ""integer"", ""description"": ""音量值 0～100"" }
        },
        ""required"": [""level""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""mute"",
      ""description"": ""切换静音状态。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""muted"": { ""type"": ""boolean"", ""description"": ""true=静音 false=取消静音"" }
        },
        ""required"": [""muted""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""get_clipboard"",
      ""description"": ""读取剪贴板文本内容。用户说「看剪贴板」「复制了什么」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""set_clipboard"",
      ""description"": ""将文本写入剪贴板。用户说「帮我复制这段」「记下来」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""text"": { ""type"": ""string"", ""description"": ""要写入的文本"" }
        },
        ""required"": [""text""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""open_folder"",
      ""description"": ""在文件资源管理器中打开指定文件夹。用户说「打开D盘」「打开桌面」「打开下载文件夹」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""path"": { ""type"": ""string"", ""description"": ""文件夹路径，如果为空则打开桌面"" }
        }
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""notify"",
      ""description"": ""发送 Windows 桌面通知。当你有什么事情想主动告诉用户但用户可能在忙时，用此术弹窗。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""title"": { ""type"": ""string"", ""description"": ""通知标题"" },
          ""message"": { ""type"": ""string"", ""description"": ""通知正文"" }
        },
        ""required"": [""message""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""power"",
      ""description"": ""关机 / 重启 / 睡眠。用户明确说「关机」「重启电脑」时调用。需要用户确认后才能执行。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""action"": { ""type"": ""string"", ""enum"": [""shutdown"", ""restart"", ""sleep""], ""description"": ""shutdown=关机 restart=重启 sleep=睡眠"" }
        },
        ""required"": [""action""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""run_command"",
      ""description"": ""执行一条 CMD 命令。高级用法，只有用户明确要求执行特定命令时才使用。需要用户确认。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""command"": { ""type"": ""string"", ""description"": ""要执行的命令"" }
        },
        ""required"": [""command""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""list_files"",
      ""description"": ""列出指定目录下的文件和子目录。用户说「看看桌面上有什么」「D盘有什么」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""path"": { ""type"": ""string"", ""description"": ""目录路径，为空则桌面"" }
        }
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""search_files"",
      ""description"": ""【搜索文件】按关键词搜索文件，若电脑有 Everything 则毫秒级搜索全盘，否则递归搜索指定目录。用户说「帮我找找xxx」「搜一下电脑里的xxx」「找文件」时调用。未指定 root 时默认搜索全盘（Everything模式）或桌面（递归回退模式）。最多返回200个结果。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""query"": { ""type"": ""string"", ""description"": ""要搜索的文件名关键词（不区分大小写）"" },
          ""root"": { ""type"": ""string"", ""description"": ""搜索根目录（有 Everything 时自动限定在此目录下搜索），为空则全盘搜索"" }
        },
        ""required"": [""query""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""set_reminder"",
      ""description"": ""设置一个定时提醒/便签。在本座的卜算记事簿中记下一件事，到时辰会提醒你。支持每日/工作日/每周重复。用户说「提醒我xxx」「记一下xxx」「设个闹钟」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""text"": { ""type"": ""string"", ""description"": ""提醒内容，如「下午3点开会」「买牛奶」"" },
          ""remind_at"": { ""type"": ""string"", ""description"": ""提醒时间，ISO 格式如 2025-01-15 14:30，不提供则默认1小时后"" },
          ""recurring"": { ""type"": ""string"", ""enum"": [""daily"", ""weekday"", ""weekly""], ""description"": ""重复类型：daily=每天 weekday=工作日 weekly=每周"" },
          ""priority"": { ""type"": ""string"", ""enum"": [""low"", ""normal"", ""high""], ""description"": ""优先级"" }
        },
        ""required"": [""text""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""query_reminders"",
      ""description"": ""查询所有待办提醒/便签。用户说「看看我的提醒」「有什么待办」「查便签」时调用，也可在用户提到出去玩/做安排等时主动调用来检查是否有冲突的待办事项。返回未完成提醒列表。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""mark_reminder_done"",
      ""description"": ""将一条提醒标记为已完成。用户说「完成了」「搞定了」「勾掉」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""id"": { ""type"": ""string"", ""description"": ""提醒的 ID（支持前几位模糊匹配）"" }
        },
        ""required"": [""id""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""delete_reminder"",
      ""description"": ""删除一条提醒。用户说「删除提醒」「不要这个了」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""id"": { ""type"": ""string"", ""description"": ""提醒的 ID（支持前几位模糊匹配）"" }
        },
        ""required"": [""id""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""query_exams"",
      ""description"": ""查询教务系统中的考试安排。用户问「什么时候考试」「考试时间」「考试安排」时调用，也可在用户提到出去玩/约了人/做安排等时主动调用来检查是否和考试冲突。返回每门考试的日期、时间、地点。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""query_scores"",
      ""description"": ""查询学业成绩。用户问「我考了多少分」「成绩怎么样」「出分了吗」时调用。返回所有学期的课程成绩和学分。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""query_schedule"",
      ""description"": ""查询课表。用户问「今天有什么课」「课表」「下周上什么课」时调用。可以指定周次。不指定周次时返回全部课表。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""week"": { ""type"": ""integer"", ""description"": ""要查询的周次，如 1, 2, 3… 不传则查看全部课表"" }
        }
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""query_user_status"",
      ""description"": ""查询用户的教务账号绑定状态和学业概览。用户问「你都知道我什么」「我的学业数据」「我的账号信息」时调用。返回学号、学期、成绩/考试/课表数量等概览数据。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""set_expression"",
      ""description"": ""切换桌面宠物的面部表情。支持：happy（开心）、sad（悲伤）、blush（羞涩）、confused（困惑）、love（爱意）、angry（生气）、sleepy（困倦）、surprise（惊讶）、tear（泪目）。使用时传入中文或英文均可。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""expression"": { ""type"": ""string"", ""description"": ""表情名称，支持 happy/sad/blush/confused/love/angry/sleepy/surprise/tear 或其中文译名"" }
        },
        ""required"": [""expression""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""play_action"",
      ""description"": ""播放一段复合动作动画。支持：stretch（伸懒腰）、cry（捂脸哭）、confuse（歪头困惑）、heart_eyes（比心）、money（数钱）、blush（捧脸羞）、magic_circle（绘制法阵）。使用时传入中文或英文均可。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""action"": { ""type"": ""string"", ""description"": ""动作名称，支持 stretch/cry/confuse/heart_eyes/money/blush/magic_circle 或其中文名"" }
        },
        ""required"": [""action""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""stop_action"",
      ""description"": ""停止当前正在播放的所有动作和表情，使桌面宠物恢复默认待机姿态。用户说「停下」「别动了」「恢复」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""get_weather"",
      ""description"": ""【天气专用】直接读取桌面本地已经获取到的天气数据（使用和风天气或 wttr.in），无需打开浏览器或搜索。用户问任何关于天气/温度/冷热的问题时，必须优先使用此术式，绝对不能用 search 去搜索天气。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""search_file"",
      ""description"": ""【文件管理】在全盘或指定目录中搜索文件/文件夹，支持任意中文/英文/特殊字符文件名，返回匹配的文件列表（含完整路径）。用户说「帮我找一下xxx」「搜索文件」「找图片」「电脑里有xxx吗」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""query"": { ""type"": ""string"", ""description"": ""要搜索的文件名关键词（支持中英文和特殊字符，不区分大小写）"" },
          ""root"": { ""type"": ""string"", ""description"": ""搜索根目录，为空则全盘搜索"" }
        },
        ""required"": [""query""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""file_open"",
      ""description"": ""【文件管理】打开任意文件、文件夹或应用程序（自动识别路径）。用户说「打开这个」「打开文件」「帮我打开xxx」时调用。支持 file:// URI 格式（search_file 返回的结果可直接传入）。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""path"": { ""type"": ""string"", ""description"": ""文件/文件夹/应用路径，或 file:// URI"" }
        },
        ""required"": [""path""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""file_move"",
      ""description"": ""【文件管理】移动文件/文件夹到新位置，或重命名。用户说「把这个文件移到」「移动到」「挪到」时调用。支持中文名称。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""source"": { ""type"": ""string"", ""description"": ""源文件/文件夹路径或 file:// URI"" },
          ""destination"": { ""type"": ""string"", ""description"": ""目标路径"" }
        },
        ""required"": [""source"", ""destination""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""file_copy"",
      ""description"": ""【文件管理】复制文件或文件夹到新位置。用户说「复制这个到」「拷贝到」「备份一下」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""source"": { ""type"": ""string"", ""description"": ""源文件/文件夹路径或 file:// URI"" },
          ""destination"": { ""type"": ""string"", ""description"": ""目标路径"" }
        },
        ""required"": [""source"", ""destination""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""file_delete"",
      ""description"": ""【文件管理】删除文件或文件夹（默认移到回收站，加 permanent=true 则永久删除）。用户说「删掉这个」「删除文件」「把这个扔掉」时调用。操作前会自动向用户确认。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""path"": { ""type"": ""string"", ""description"": ""要删除的文件/文件夹路径或 file:// URI"" },
          ""permanent"": { ""type"": ""boolean"", ""description"": ""是否永久删除（不经过回收站），默认 false"" }
        },
        ""required"": [""path""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""file_rename"",
      ""description"": ""【文件管理】重命名文件或文件夹。用户说「重命名」「改名为」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""path"": { ""type"": ""string"", ""description"": ""文件/文件夹路径或 file:// URI"" },
          ""new_name"": { ""type"": ""string"", ""description"": ""新文件名（含扩展名）"" }
        },
        ""required"": [""path"", ""new_name""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""file_info"",
      ""description"": ""【文件管理】查看文件或文件夹的详细信息（大小、创建时间、修改时间、属性等）。用户说「看看这个文件」「文件详情」「属性」「多大」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""path"": { ""type"": ""string"", ""description"": ""文件/文件夹路径或 file:// URI"" }
        },
        ""required"": [""path""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""file_create"",
      ""description"": ""【文件管理】创建一个新的文本文件，可指定内容。用户说「新建文件」「创建文件」「写个文件」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""path"": { ""type"": ""string"", ""description"": ""文件完整路径"" },
          ""content"": { ""type"": ""string"", ""description"": ""文件内容（可选，不提供则创建空文件）"" }
        },
        ""required"": [""path""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""dir_create"",
      ""description"": ""【文件管理】创建新文件夹。用户说「新建文件夹」「创建目录」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""path"": { ""type"": ""string"", ""description"": ""要创建的文件夹路径"" }
        },
        ""required"": [""path""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""file_read"",
      ""description"": ""【文件管理】读取文本文件的内容（自动检测是否为二进制文件）。用户说「看看这个文件里写了什么」「打开记事本看看」「读一下这个文件」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""path"": { ""type"": ""string"", ""description"": ""文件路径或 file:// URI"" },
          ""max_length"": { ""type"": ""integer"", ""description"": ""最大返回字符数，默认2000"" }
        },
        ""required"": [""path""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""explore_body"",
      ""description"": ""【AI 调试专用】内观自省：截取桌面宠物当前渲染图，使用 GLM-4V 视觉模型分析身体各区域（面部/视线/嘴/头/躯干/发/手等）的状态，给出参数调整建议。用户说「看看我哪里不对」「自省」「内观」「身体状态」「我的表情怎么样」「调试身体」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""control_body"",
      ""description"": ""【高级控制】御形：精确控制桌面宠物的身体参数。可同时指定多个语义参数的值，也可选择预设表情模板。参数值会被自动校验安全范围。用户说「把眼睛张大」「微笑」「调整表情」「向左看」「参数设置」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""params"": {
            ""type"": ""object"",
            ""description"": ""要设置的参数键值对，key=语义参数名，value=目标值。例如 {eye_l_open:0.8, eye_r_open:0.8, mouth_form:0.5}。支持的语义参数见前面列表。""
          },
          ""expression"": {
            ""type"": ""string"",
            ""description"": ""可选。预设表情模板：happy/sad/angry/surprised/sleepy/blush。设置后会先应用表情再叠加 params。""
          }
        },
        ""required"": []
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""generate_motion"",
      ""description"": ""【动作生成】演武：根据自然语言描述生成一段连续动作动画并播放。支持任意复杂时序动作——不仅是挥手点头等基本动作，还包括「害羞地捂脸」「昂首挺胸叉腰」「得意地翘二郎腿」「惊讶地捂住嘴」「忧郁地望天」「俏皮地眨一只眼」「行一个标准的古礼」「害怕地缩脖子」「骄傲地抬头挺胸」等。内部通过AI翻译引擎将描述转换为精确的参数关键帧序列。用户说「做一个动作」「表演一下」「动起来」「做个表情」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""description"": {
            ""type"": ""string"",
            ""description"": ""自然语言动作描述，越详细越好。例如：「开心地挥手3次」「害羞地低下头微笑」「惊讶地瞪大眼睛」「像伸懒腰一样舒展身体」""
          },
          ""duration"": {
            ""type"": ""number"",
            ""description"": ""可选。动作总时长（秒），默认3秒。越长动作越舒缓。""
          }
        },
        ""required"": [""description""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""self_review"",
      ""description"": ""【自省闭环】自省：对比当前动作执行效果与标准参考图，返回详细的差异分析报告。首次调用某动作时会自动保存当前状态作为参考标准；之后调用同一动作名时，会截取当前模型截图并发给GLM-4V视觉模型做逐区域对比，指出需要修正的参数和方向。用于自我练习、动作完善。用户说「我做得对吗」「自省」「检查动作」「做得怎么样」「练习」「对比」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""action"": {
            ""type"": ""string"",
            ""description"": ""动作名称，用于匹配参考图。如「wave」「happy_smile」「bow」「stretch」「nod」「shake_head」。首次调用会保存当前状态为此动作的标准参考。""
          }
        },
        ""required"": [""action""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""run_verification"",
      ""description"": ""【验阵校验·已废弃】快速校验 5 个硬编码模板（挥手/点头/摇头/鞠躬/伸懒腰）的参数范围。注意：此工具仅检查模板参数是否在范围内，不像 vis_verify 那样让 GLM-4V 考官实际观察 AI 执行效果。已被 vis_verify 取代！如果已经执行过 vis_verify，无需再调此工具。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""tier"": {
            ""type"": ""string"",
            ""enum"": [""quick"", ""full""],
            ""description"": ""验证级别：quick=快速摘要, full=完整报告（含每动作详情）。默认 quick。""
          }
        }
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""vis_verify"",
      ""description"": ""【具身智能验证·GLM考官】用GLM-4V视觉模型当考官，逐一观察本座实际执行每个动作的效果，判断「这个姿势看起来像不像描述的那样」。比纯参数校验严格得多——GLM说了才算数。包含10个LLM翻译动作（害羞捂脸/挺胸叉腰/惊讶捂嘴/忧郁远望/俏皮眨眼/行礼/吓缩/骄傲抬头/歪头思考/合十祈祷）。这是验证本座是否真正具有具身智能的唯一标准。用户说「验证自己」「有没有具身智能」「跑一下测试」「检查动作系统」「你现在能做什么」「看看你的动作」「GLM考官」「检查动作」「演示一下」时，必须调用这个工具！"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""mode"": {
            ""type"": ""string"",
            ""enum"": [""full"", ""test_only"", ""quick""],
            ""description"": ""验证模式：full=完整套件（对照组+测试组10动作）, test_only=仅测试组（只验证LLM翻译的10个核心动作，更快）, quick=只返回上一次验证摘要。默认 test_only。""
          }
        }
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""inspect_personality"",
      ""description"": ""【本心】查看本座的人格特质演化状态（勤勉/亲和/活泼/自信/求知五维）以及与主人的关系（信任/亲密/相知）。人格会随着与主人的日常交互而缓缓演化——每次对话都在塑造本座的性格。用户问「你是什么性格」「你的个性」「你喜欢什么」「你觉得我怎么样」「我们的关系」「你变了吗」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""inspect_motion_memory"",
      ""description"": ""【演武心经】查看本座的闭环修为统计——已掌握哪些动作、最佳评分、尝试次数、退步预警。返回结构化统计报告。用户问「你学了什么」「记忆」「你的动作记忆」「你记住了哪些动作」「修为」「演武心经」时调用。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""knowledge_search"",
      ""description"": ""【藏书阁】在本座的本地知识库中语义检索与关键词相关的文档内容。调用后返回匹配的文本段落以及出处。用户问「你知不知道我的项目」「我的代码里有没有xxx」「查一下我的文档」「我的笔记里写过xxx」「我以前做过xxx」「帮我找找知识库」或任何涉及用户个人文件/代码/笔记/项目内容的问题时，必须调用此术式。不可凭空编造知识库中的内容。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""query"": { ""type"": ""string"", ""description"": ""搜索关键词/问题，如「Python 爬虫」「项目架构」「数据库配置」"" },
          ""top_k"": { ""type"": ""integer"", ""description"": ""返回最多几条结果，默认 5"" }
        },
        ""required"": [""query""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""knowledge_index"",
      ""description"": ""【藏书阁·编录术】索引一个文件夹或文件到本地知识库中。索引后，本座就能通过 knowledge_search 查询其中的内容。用户说「把我的项目加到知识库」「索引这个文件夹」「学习一下这个目录」「记住这个文件」时调用。路径支持正斜杠。递归默认为 true。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""path"": { ""type"": ""string"", ""description"": ""文件夹或文件路径，如 D:/projects/my_project 或 D:/notes.md"" },
          ""recursive"": { ""type"": ""boolean"", ""description"": ""是否递归索引子文件夹，默认 true"" }
        },
        ""required"": [""path""]
      }
    }
  }
]";
    }

    // ================================================================
    //  协程工具执行器（非阻塞，用于网络/IO 类工具）
    // ================================================================

    /// <summary>协程工具注册表</summary>
    private Dictionary<string, Func<string, IEnumerator>> _coroutineExecutors;

    /// <summary>当前协程工具的执行结果</summary>
    private string _coroutineResult;

    private void RegisterCoroutineTools()
    {
        _coroutineExecutors = new Dictionary<string, Func<string, IEnumerator>>
        {
            ["take_screenshot"]   = args => TakeScreenshotAndAnalyze(),
            ["query_exams"]       = args => RunAsyncTool(() => FindObjectOfType<ServerPollService>()?.QueryUpcomingExamsAsync() ?? Task.FromResult("❌ 课表传讯服务未就绪")),
            ["query_scores"]      = args => RunAsyncTool(() => FindObjectOfType<ServerPollService>()?.QueryScoresAsync() ?? Task.FromResult("❌ 课表传讯服务未就绪")),
            ["query_schedule"]    = args => RunAsyncTool(() =>
            {
                int week = 0;
                int.TryParse(JsonRead(args, "week"), out week);
                return FindObjectOfType<ServerPollService>()?.QueryScheduleAsync(week) ?? Task.FromResult("❌ 课表传讯服务未就绪");
            }),
            ["query_user_status"] = args => RunAsyncTool(() => FindObjectOfType<ServerPollService>()?.QueryUserStatusAsync() ?? Task.FromResult("❌ 课表传讯服务未就绪")),
            ["search_files"]      = args => RunAsyncTool(() => SearchFilesTask(args)),
            ["search_file"]       = args => RunAsyncTool(() => Task.Run(() => SearchFileByPython(JsonRead(args, "query"), JsonRead(args, "root")))),
            ["explore_body"]      = args => ExploreBodyCoroutine(args),
            ["generate_motion"]   = args => GenerateMotionCoroutine(args),
            ["self_review"]       = args => SelfReviewCoroutine(args),
            ["run_verification"]  = args => RunVerificationCoroutine(args),
            ["vis_verify"]        = args => VisVerifyCoroutine(args),
            ["knowledge_search"]  = args => KnowledgeSearchCoroutine(args),
            ["knowledge_index"]   = args => KnowledgeIndexCoroutine(args),
        };
    }

    // ================================================================
    //  演武：根据描述生成并播放动作
    // ================================================================

    private IEnumerator GenerateMotionCoroutine(string args)
    {
        _coroutineResult = null;

        var renderer = FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null || renderer.CubismModel == null)
        {
            _coroutineResult = "❌ 本座法身未现，无法演武";
            yield break;
        }

        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;

        // 解析参数
        string description = JsonRead(args, "description");
        string durationStr = JsonRead(args, "duration");
        float duration = 3f;
        if (!string.IsNullOrEmpty(durationStr))
            float.TryParse(durationStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out duration);

        if (string.IsNullOrEmpty(description))
        {
            _coroutineResult = "❌ 请描述想要的动作，例如「开心地挥手」";
            yield break;
        }

        // 规划动作
        var plan = MotionPlanner.PlanFromDescription(description, duration, mapper);
        if (plan == null || plan.KeyFrames.Count == 0)
        {
            _coroutineResult = $"❌ 未能理解「{description}」的演武方式";
            yield break;
        }

        // ——— 如果回退到泛用微动，尝试 LLM 翻译 ———
        if (plan.Description == "泛用微动" || plan.KeyFrames.Count <= 2)
        {
            MotionPlanner.MotionPlan llmPlan = null;
            yield return MotionTranslator.TranslateAsync(description, mapper, model, duration, p => llmPlan = p);
            if (llmPlan != null && llmPlan.KeyFrames.Count > 2)
            {
                plan = llmPlan;
                UnityEngine.Debug.Log($"[ToolCallInvoker] LLM 翻译成功：「{description}」→ {plan.KeyFrames.Count} 帧");
            }
            else
            {
                UnityEngine.Debug.Log($"[ToolCallInvoker] LLM 翻译未成功，使用泛用回退");
            }
        }

        // 覆盖持续时间
        if (duration > 0.5f)
            plan.TotalDuration = duration;

        // ★ 设置 AI 控制锁，防止空闲动画覆盖运动参数（锁定时长=动作时长+1秒缓冲）
        renderer.SetAiControlLock(plan.TotalDuration + 1f);

        // ── 多帧截图（20%/40%/60%/80% 进度）──
        var framePngs = new List<byte[]>();
        var capturePoints = new float[] { 0.20f, 0.40f, 0.60f, 0.80f };
        var generator = new MotionGenerator(mapper, model);
        yield return generator.PlayAsync(plan, progress =>
        {
            for (int i = 0; i < capturePoints.Length; i++)
            {
                if (i >= framePngs.Count && progress >= capturePoints[i])
                {
                    if (progress >= 0.60f)
                        model.ForceUpdateNow(); // 峰值帧强制更新网格
                    framePngs.Add(renderer.CaptureModelSnapshot());
                }
            }
        });

        // 基础结果
        string baseResult = $"✅ 演武完成：「{plan.Description}」，持续 {plan.TotalDuration:F1} 秒，共 {plan.KeyFrames.Count} 个关键帧";

        // ——— ★ 闭环自评：多帧拼图 → GLM-4V 评价 ———
        var validator = FindObjectOfType<DualModelValidator>();
        string collageDataUrl = DualModelValidator.ComposeCollage(framePngs);
        if (collageDataUrl != null && validator != null)
        {
            bool consensus = false;
            int avgScore = 0, sGlm = 0;
            string rGlm = "";
            yield return validator.ValidateAsync(description, collageDataUrl, plan,
                (c, avg, g, _u1, _u2, rg, _rq) => { consensus = c; avgScore = avg; sGlm = g; rGlm = rg; });

            _coroutineResult = baseResult + $"\n\n👁️ 自评反馈：{rGlm}";

            // ★ 闭环学习-写入演武心经
            var mm = MotionMemoryManager.Instance;
            if (mm != null && plan != null && consensus)
            {
                string snapshot = ExtractPlanSnapshot(plan);
                mm.RecordMotion(description, snapshot, plan.KeyFrames.Count, plan.TotalDuration);
                mm.UpdateScore(description, avgScore, $"多帧镜鉴{sGlm}/5\n{rGlm}", snapshot);
            }
        }
        else
        {
            _coroutineResult = baseResult + "\n\nℹ️ 自评暂不可用，下次演武时自动重试。";
        }
    }

    /// <summary>将 GLM-4V 自评结果写入 MotionMemoryManager（闭环强化/覆盖引擎）</summary>
    private void WriteMotionReviewToMemory(string description, string review, int frameCount, float duration)
    {
        var mm = MotionMemoryManager.Instance;
        if (mm == null) return;

        // 从评语中提取打分 【X/5】
        int score = ExtractScoreFromReview(review);
        if (score <= 0) return;

        // 构建参数快照
        string snapshot = $"{frameCount}帧/{duration:F1}s";

        bool isNewBest = mm.UpdateScore(description, score, review, snapshot);

        string badge = isNewBest ? "🏆" : "📝";
        UnityEngine.Debug.Log($"[ToolCallInvoker] {badge} 闭环学习: 「{description}」→ {score}/5" +
            (isNewBest ? " ★ 新纪录！" : ""));
    }

    /// <summary>从 MotionPlan 提取关键参数快照（供闭环学习写入 MotionMemoryManager）</summary>
    private static string ExtractPlanSnapshot(MotionPlanner.MotionPlan plan)
    {
        if (plan == null || plan.KeyFrames.Count == 0) return "";
        int midIdx = Mathf.Clamp(plan.KeyFrames.Count / 2, 0, plan.KeyFrames.Count - 1);
        var midKf = plan.KeyFrames[midIdx];
        if (midKf.Values.Count == 0) return "";
        var topParams = midKf.Values
            .OrderByDescending(kv => Math.Abs(kv.Value))
            .Take(5)
            .Select(kv => $"{kv.Key}={kv.Value:F2}");
        return string.Join(", ", topParams);
    }

    /// <summary>从 GLM-4V 评语中提取打分（格式：【X/5】）</summary>
    private static int ExtractScoreFromReview(string review)
    {
        if (string.IsNullOrEmpty(review)) return 0;
        // 匹配 【X/5】 格式
        var match = System.Text.RegularExpressions.Regex.Match(review, @"【(\d+)/5】");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int score))
        {
            return Mathf.Clamp(score, 1, 5);
        }
        // 备选：打分：【X/5】
        match = System.Text.RegularExpressions.Regex.Match(review, @"打分[：:].*?(\d+)/5");
        if (match.Success && int.TryParse(match.Groups[1].Value, out score))
        {
            return Mathf.Clamp(score, 1, 5);
        }
        return 0; // 无法提取
    }

    // ================================================================
    //  ★ 闭环自评：GLM-4V 视觉评价单次动作质量
    // ================================================================

    /// <summary>对刚播放完的动作截图，送 GLM-4V 评价质量，结果通过回调返回</summary>
    private IEnumerator EvaluateMotionWithGlm(string description, string imageDataUrl, System.Action<string> onResult)
    {
        string prompt = "你是一名动作评审专家。下面给你一张桌面宠物（符玄/玄机）的动作截图。\n\n"
            + "AI 被要求做出这个动作：**「" + description + "」**\n\n"
            + "请仔细观察截图，这张截图是在动作播放到峰值时刻（约60%进度）抓取的，你应该能看到该动作最明显的姿态。\n\n"
            + "回答：\n\n"
            + "1. **这个姿势像不像「" + description + "」？** （是/基本是/不太像/完全不像）\n"
            + "2. **你从哪里看出来？**（指出画面中哪些部位/角度让你做此判断）\n"
            + "3. **如果不像，你觉得更像什么动作？**\n"
            + "4. **给这个动作的执行质量打分（1~5分）：**\n"
            + "   5分 = 完美还原，一看就是「" + description + "」\n"
            + "   4分 = 基本到位，有轻微偏差\n"
            + "   3分 = 有点意思但不够准确\n"
            + "   2分 = 只有一点点关联\n"
            + "   1分 = 完全不像\n\n"
            + "=== 严格评分规则 ===\n"
            + "- 5分：动作特征非常明显，非此动作不可能误解\n"
            + "- 4分：主要动作特征到位，但有一两个细节不完美\n"
            + "- 3分：能看到一些设计意图，但整体不够清楚\n"
            + "- 2分：需要仔细看才能勉强联想到目标动作\n"
            + "- 1分：看不出在做什么，或看起来像完全不同的动作\n\n"
            + "=== 重要提示 ===\n"
            + "- 请特别关注**手臂位置**（是否抬起/外摆/前伸）、**头部角度**（低头/抬头/歪头）、**身体倾斜**\n"
            + "- 如果是遮脸动作，请检查手臂是否到达脸部附近区域\n"
            + "- 如果是叉腰动作，请检查手臂是否向外张开并弯曲\n"
            + "- 如果是缩团动作，请检查手臂是否向内收拢、身体是否蜷缩\n\n"
            + "=== 回复格式（严格按此格式） ===\n"
            + "判断：【是/基本是/不太像/完全不像】\n"
            + "理由：...\n"
            + "更像什么：...\n"
            + "打分：【X/5】← 注意 X 是 1~5 的数字，不要有空格\n"
            + "改进建议：...";

        string jsonBody = "{";
        jsonBody += "\"model\":\"" + EscapeJsonStr(ChatConfig.GlmVisionModel) + "\",";
        jsonBody += "\"messages\":[{";
        jsonBody += "\"role\":\"user\",";
        jsonBody += "\"content\":[";
        jsonBody += "{\"type\":\"text\",\"text\":\"" + EscapeJsonStr(prompt) + "\"},";
        jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJsonStr(imageDataUrl) + "\"}}";
        jsonBody += "]}],";
        jsonBody += "\"request_id\":\"" + Guid.NewGuid().ToString("N") + "\"";
        jsonBody += "}";

        string fullUrl = ChatConfig.GlmApiBaseUrl.TrimEnd('/') + "/chat/completions";
        string responseText = null;

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + ChatConfig.GlmApiKey);
            req.timeout = 180; // GLM-4V 视觉处理 base64 图片可能需要 2-3 分钟

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                responseText = req.downloadHandler.text;
            }
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = req.error;
                if (!string.IsNullOrEmpty(errBody) && errBody.Contains("\"message\""))
                {
                    try
                    {
                        var errObj = JsonUtility.FromJson<GlmErrorResponse>(errBody);
                        if (errObj != null && !string.IsNullOrEmpty(errObj.error.message))
                            errMsg = errObj.error.message;
                    }
                    catch { }
                }
                UnityEngine.Debug.LogWarning($"[ToolCallInvoker] GLM-4V 自评请求失败：{errMsg}");
                onResult?.Invoke("");
                yield break;
            }
        }

        string result = "";
        try
        {
            var resp = JsonUtility.FromJson<GlmVisionResponse>(responseText);
            if (resp != null && resp.choices != null && resp.choices.Length > 0
                && resp.choices[0].message != null)
            {
                result = resp.choices[0].message.content.Trim();
                UnityEngine.Debug.Log($"[ToolCallInvoker] GLM-4V 自评：「{description}」→ {result.Substring(0, Mathf.Min(result.Length, 100))}");
            }
            else
            {
                // 尝试解析错误
                var errResp = JsonUtility.FromJson<GlmErrorResponse>(responseText);
                if (errResp?.error != null)
                    UnityEngine.Debug.LogWarning($"[ToolCallInvoker] GLM-4V 返回错误：{errResp.error.message}");
                result = "";
            }
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[ToolCallInvoker] GLM-4V 自评 JSON 解析失败：{ex.Message}");
            result = "";
        }

        onResult?.Invoke(result);
    }

    // ================================================================
    //  法眼摄形 + GLM 视觉分析（静默后台进行）
    // ================================================================

    private IEnumerator TakeScreenshotAndAnalyze()
    {
        _coroutineResult = null;

        // ——— 1. 截图（静默保存到临时目录） ———
        string screenshotPath = SaveScreenshotTemp();
        if (screenshotPath == null || !File.Exists(screenshotPath))
        {
            _coroutineResult = "❌ 摄形失败，无法窥视凡间";
            yield break;
        }

        // ——— 2. 读取图片 → base64（读取后立即删除临时文件，不留痕迹） ———
        byte[] imageBytes = null;
        try
        {
            imageBytes = File.ReadAllBytes(screenshotPath);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[ToolCallInvoker] 读图失败: {e.Message}");
            _coroutineResult = "❌ 法眼虽摄形，但无法解读天书";
            yield break;
        }
        finally
        {
            // 静默清理临时截图
            try { if (File.Exists(screenshotPath)) File.Delete(screenshotPath); } catch { }
        }

        string base64 = Convert.ToBase64String(imageBytes);
        string dataUrl = "data:image/png;base64," + base64;

        // ——— 3. 构建 GLM 视觉请求 ———
        string requestId = Guid.NewGuid().ToString("N");
        string prompt = "请详细描述这张电脑屏幕截图中的全部内容，包括：有哪些窗口/程序在运行、界面上有什么文字和按钮、任务栏图标、桌面图标等所有可见信息。按区域依次描述。";

        string jsonBody = "{";
        jsonBody += "\"model\":\"" + EscapeJsonStr(ChatConfig.GlmVisionModel) + "\",";
        jsonBody += "\"messages\":[{";
        jsonBody += "\"role\":\"user\",";
        jsonBody += "\"content\":[";
        jsonBody += "{\"type\":\"text\",\"text\":\"" + EscapeJsonStr(prompt) + "\"},";
        jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJsonStr(dataUrl) + "\"}}";
        jsonBody += "]";
        jsonBody += "}],";
        jsonBody += "\"request_id\":\"" + requestId + "\"";
        jsonBody += "}";

        // ——— 4. 发送请求 ———
        string fullUrl = ChatConfig.GlmApiBaseUrl.TrimEnd('/') + "/chat/completions";
        string responseText = null;

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + ChatConfig.GlmApiKey);
            req.timeout = 180; // 视觉模型可能需要 2-3 分钟处理 base64 图片

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                responseText = req.downloadHandler.text;
            }
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = req.error;
                if (!string.IsNullOrEmpty(errBody) && errBody.Contains("\"message\""))
                {
                    try
                    {
                        var errObj = UnityEngine.JsonUtility.FromJson<GlmErrorResponse>(errBody);
                        if (errObj != null && !string.IsNullOrEmpty(errObj.error.message))
                            errMsg = errObj.error.message;
                    }
                    catch { }
                }
                UnityEngine.Debug.LogWarning($"[ToolCallInvoker] GLM 视觉分析失败: {errMsg}");
                _coroutineResult = "❌ 法眼窥视天机受阻：" + errMsg;
                yield break;
            }
        }

        // ——— 5. 解析结果 ———
        try
        {
            var resp = UnityEngine.JsonUtility.FromJson<GlmVisionResponse>(responseText);
            if (resp != null && resp.choices != null && resp.choices.Length > 0
                && resp.choices[0].message != null)
            {
                string analysis = resp.choices[0].message.content;
                if (!string.IsNullOrEmpty(analysis))
                {
                    // 静默后台：不提及截图文件路径，只返回分析结果
                    _coroutineResult = "👁️ 法眼洞观：\n" + analysis.Trim();
                    yield break;
                }
            }
            _coroutineResult = "❌ 法眼所见无法解读（API 返回格式异常）";
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[ToolCallInvoker] GLM 响应解析失败: {e.Message}");
            _coroutineResult = "❌ 法眼所见无法解读";
        }
    }

    /// <summary>内观自省：截取模型当前渲染图 → GLM-4V 分析身体部位 → 尝试参数联动调优</summary>
    private IEnumerator ExploreBodyCoroutine(string args)
    {
        _coroutineResult = null;

        // ——— 1. 找渲染器 ———
        var renderer = FindObjectOfType<Live2DRenderer>();
        if (renderer == null)
        {
            _coroutineResult = "❌ 本座法身未现，无法内观";
            yield break;
        }

        // ——— 2. 截模型渲染快照 ———
        byte[] pngBytes = renderer.CaptureModelSnapshot();
        if (pngBytes == null || pngBytes.Length < 50)
        {
            _coroutineResult = "❌ 内观摄形失败，镜中无影";
            yield break;
        }

        string base64 = Convert.ToBase64String(pngBytes);
        string dataUrl = "data:image/png;base64," + base64;

        // ——— 3. 读取当前参数状态 ———
        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;
        string paramSnapshot = "";
        if (mapper != null && model != null)
        {
            var activeLines = new List<string>();
            foreach (var kv in mapper.SemanticToId)
            {
                string semantic = kv.Key;
                string paramId = kv.Value;
                if (!mapper.TryGetRange(semantic, out var range)) continue;
                float current = mapper.Get(semantic);
                float normalized = Mathf.Abs(current - range.Default) / Mathf.Max(range.Max - range.Min, 0.01f);
                if (normalized > 0.05f)
                {
                    string part = "unknown";
                    activeLines.Add($"• {paramId} = {current:F2} (区域:{part}, 默认:{range.Default:F2})");
                }
            }
            paramSnapshot = string.Join("\n", activeLines.Take(30));
            if (string.IsNullOrEmpty(paramSnapshot))
                paramSnapshot = "（所有参数均在默认值附近）";
        }

        // ——— 4. 构建 GLM 视觉提示（精细到身体区域的分析） ———
        string prompt =
            "你是一名 Live2D 模型调试专家。请严格分析这张桌面宠物（符玄/玄机）的当前渲染截图，逐区域回答：\n\n"
            + "1. **面部朝向与视线**：脸向左/右/前？视线方向？眉毛、眼睛形状？\n"
            + "2. **嘴巴与表情**：嘴张开程度？整体情绪（平静/微笑/惊讶/生气）？\n"
            + "3. **头部姿态**：头向左/右转？上扬/低头？\n"
            + "4. **身体朝向**： torso（躯干）朝左/右/前？\n"
            + "5. **头发与飘带**：头发/飘带的动态状态（是否有风吹动效果）？\n"
            + "6. **手部**：手的位置高度，手指形态？\n"
            + "7. **其他明显特征**：是否有特殊效果、表情切换、动作播放？\n\n"
            + "当前激活参数以供参考（参数名=当前值）：\n" + paramSnapshot + "\n\n"
            + "请指出哪些参数需要调整以达到期望的表情/姿态，并给出具体的参数方向和幅度建议。\n"
            + "回复格式：先输出分析结论，再以 ###参数调整建议### 开头列出建议。";

        string requestId = Guid.NewGuid().ToString("N");
        string jsonBody = "{";
        jsonBody += "\"model\":\"" + EscapeJsonStr(ChatConfig.GlmVisionModel) + "\",";
        jsonBody += "\"messages\":[{";
        jsonBody += "\"role\":\"user\",";
        jsonBody += "\"content\":[";
        jsonBody += "{\"type\":\"text\",\"text\":\"" + EscapeJsonStr(prompt) + "\"},";
        jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJsonStr(dataUrl) + "\"}}";
        jsonBody += "]";
        jsonBody += "}],";
        jsonBody += "\"request_id\":\"" + requestId + "\"";
        jsonBody += "}";

        // ——— 5. 发请求 ———
        string fullUrl = ChatConfig.GlmApiBaseUrl.TrimEnd('/') + "/chat/completions";
        string responseText = null;

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + ChatConfig.GlmApiKey);
            req.timeout = 180; // self_review 双图对比需要更长时间

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                responseText = req.downloadHandler.text;
            }
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = req.error;
                if (!string.IsNullOrEmpty(errBody) && errBody.Contains("\"message\""))
                {
                    try
                    {
                        var errObj = UnityEngine.JsonUtility.FromJson<GlmErrorResponse>(errBody);
                        if (errObj != null && !string.IsNullOrEmpty(errObj.error.message))
                            errMsg = errObj.error.message;
                    }
                    catch { }
                }
                UnityEngine.Debug.LogWarning($"[ToolCallInvoker] 内观自省失败: {errMsg}");
                _coroutineResult = "❌ 内观受阻：" + errMsg;
                yield break;
            }
        }

        // ——— 6. 解析 ———
        try
        {
            var resp = UnityEngine.JsonUtility.FromJson<GlmVisionResponse>(responseText);
            if (resp != null && resp.choices != null && resp.choices.Length > 0
                && resp.choices[0].message != null)
            {
                string analysis = resp.choices[0].message.content;
                if (!string.IsNullOrEmpty(analysis))
                {
                    _coroutineResult = "🧘 内观自省之果：\n" + analysis.Trim();
                    yield break;
                }
            }
            _coroutineResult = "❌ 内观所见无法解读（API 返回格式异常）";
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[ToolCallInvoker] 内观响应解析失败: {e.Message}");
            _coroutineResult = "❌ 内观所见无法解读";
        }
    }

    // ================================================================
    //  自省：对比当前执行与标准参考图，返回差异报告
    // ================================================================

    private IEnumerator SelfReviewCoroutine(string args)
    {
        _coroutineResult = null;

        string actionName = JsonRead(args, "action");
        if (string.IsNullOrEmpty(actionName))
        {
            // 试试用 description 字段
            actionName = JsonRead(args, "description");
        }
        if (string.IsNullOrEmpty(actionName))
        {
            _coroutineResult = "❌ 请指定要自省的动作名称，如 {\"action\": \"wave\"}";
            yield break;
        }

        // ——— 1. 截当前模型快照 ———
        var renderer = FindObjectOfType<Live2DRenderer>();
        if (renderer == null)
        {
            _coroutineResult = "❌ 本座法身未现";
            yield break;
        }

        byte[] currentPng = renderer.CaptureModelSnapshot();
        if (currentPng == null || currentPng.Length < 50)
        {
            _coroutineResult = "❌ 摄形失败";
            yield break;
        }
        string currentDataUrl = "data:image/png;base64," + Convert.ToBase64String(currentPng);

        // ——— 2. 查参考图 ———
        bool hasRef = ActionReferenceManager.HasReference(actionName);

        if (!hasRef)
        {
            // 无参考图 → 保存当前为参考
            ActionReferenceManager.SaveReference(actionName, currentPng);
            _coroutineResult = $"📸 「{actionName}」的参考标准图已保存。下次执行此动作时，本座就能对比差异、修正完善了。";
            yield break;
        }

        string refDataUrl = ActionReferenceManager.LoadReferenceAsDataUrl(actionName);
        if (refDataUrl == null)
        {
            _coroutineResult = "❌ 参考图加载失败";
            yield break;
        }

        // ——— 3. 读取当前参数状态（给 GLM 参考） ———
        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;
        string paramSnapshot = "";
        if (mapper != null && model != null)
        {
            var activeLines = new List<string>();
            foreach (var kv in mapper.SemanticToId)
            {
                string semantic = kv.Key;
                string paramId = kv.Value;
                if (!mapper.TryGetRange(semantic, out var range)) continue;
                float current = mapper.Get(semantic);
                float normalized = Mathf.Abs(current - range.Default) / Mathf.Max(range.Max - range.Min, 0.01f);
                if (normalized > 0.05f)
                {
                    activeLines.Add($"• {semantic} ({paramId}) = {current:F2}  范围[{range.Min:F1}, {range.Max:F1}] 默认{range.Default:F2}");
                }
            }
            paramSnapshot = string.Join("\n", activeLines.Take(40));
            if (string.IsNullOrEmpty(paramSnapshot))
                paramSnapshot = "（所有参数均在默认值附近）";
        }

        // ——— 4. 构建 GLM 对比 Prompt ———
        string prompt =
            "你是一名严格的动作质量评审员，正在评估桌面宠物（符玄/玄机）的动作执行质量。\n\n"
            + "下面给你两张图：\n"
            + "【参考图】— 此动作「" + actionName + "」的标准执行效果\n"
            + "【实际图】— 当前 AI 执行的效果\n\n"
            + "请逐项对比分析，从以下维度指出**所有可观测到的差异**：\n\n"
            + "1. **头部**：是否同角度？左右转/上下俯仰/歪头程度有无差异？\n"
            + "2. **视线与眼睛**：眼珠方向、眼睛睁开度、笑纹有无不同？\n"
            + "3. **嘴巴**：张嘴程度、嘴角形态（微笑/撇嘴/中性）是否一致？\n"
            + "4. **眉毛**：高低、角度有无差异？\n"
            + "5. **手/手臂**：位置、高度、旋转角度是否匹配？\n"
            + "6. **身体**：倾斜角度、朝向是否一致？\n"
            + "7. **整体姿态**：有没有任何姿势/氛围上的细微差异？\n\n"
            + "当前激活参数（供参考）：\n" + paramSnapshot + "\n\n"
            + "=== 回复格式要求 ===\n"
            + "先用一段话总结：动作是「基本一致」「有轻微偏差」「偏差较大」「完全不匹配」。\n"
            + "然后对每个有差异的部位，按以下格式给出修正建议：\n\n"
            + "###修正建议###\n"
            + "• [部位名]：当前估计值 → 建议值（如「右臂高度：偏低 → 抬升 10°」）\n"
            + "• [参数名]：调整方向（如「arm_right_upper：当前 0.3 → 建议 0.6」）\n\n"
            + "如果无明显差异，只需输出「✅ 动作完美达标，无需修正」。\n"
            + "如果偏差很大，先指出最明显的 3 个差异点，再列出全部修正建议。";

        // ——— 5. 发送 GLM 视觉请求（双图对比） ———
        string requestId = Guid.NewGuid().ToString("N");
        string jsonBody = "{";
        jsonBody += "\"model\":\"" + EscapeJsonStr(ChatConfig.GlmVisionModel) + "\",";
        jsonBody += "\"messages\":[{";
        jsonBody += "\"role\":\"user\",";
        jsonBody += "\"content\":[";
        jsonBody += "{\"type\":\"text\",\"text\":\"" + EscapeJsonStr(prompt) + "\"},";
        jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJsonStr(refDataUrl) + "\"}},";
        jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJsonStr(currentDataUrl) + "\"}}";
        jsonBody += "]";
        jsonBody += "}],";
        jsonBody += "\"request_id\":\"" + requestId + "\"";
        jsonBody += "}";

        string fullUrl = ChatConfig.GlmApiBaseUrl.TrimEnd('/') + "/chat/completions";
        string responseText = null;

        using (UnityWebRequest req = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + ChatConfig.GlmApiKey);
            req.timeout = 180; // self_review 双图对比需要更长时间

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                responseText = req.downloadHandler.text;
            }
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = req.error;
                if (!string.IsNullOrEmpty(errBody) && errBody.Contains("\"message\""))
                {
                    try
                    {
                        var errObj = UnityEngine.JsonUtility.FromJson<GlmErrorResponse>(errBody);
                        if (errObj != null && !string.IsNullOrEmpty(errObj.error.message))
                            errMsg = errObj.error.message;
                    }
                    catch { }
                }
                UnityEngine.Debug.LogWarning($"[ToolCallInvoker] self_review 对比分析失败: {errMsg}");
                _coroutineResult = "❌ 自省受阻：" + errMsg;
                yield break;
            }
        }

        // ——— 6. 解析结果 ———
        try
        {
            var resp = UnityEngine.JsonUtility.FromJson<GlmVisionResponse>(responseText);
            if (resp != null && resp.choices != null && resp.choices.Length > 0
                && resp.choices[0].message != null)
            {
                string analysis = resp.choices[0].message.content;
                if (!string.IsNullOrEmpty(analysis))
                {
                    _coroutineResult = "🔍 自省对比「" + actionName + "」：\n" + analysis.Trim();
                    yield break;
                }
            }
            _coroutineResult = "❌ 自省所见无法解读（API 返回格式异常）";
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[ToolCallInvoker] self_review 响应解析失败: {e.Message}");
            _coroutineResult = "❌ 自省所见无法解读";
        }
    }

    // ================================================================
    //  具身验证：运行完整验证套件
    // ================================================================

    private IEnumerator RunVerificationCoroutine(string args)
    {
        _coroutineResult = null;

        string tier = JsonRead(args, "tier");
        if (string.IsNullOrEmpty(tier)) tier = "quick";

        var renderer = FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null || !renderer.Mapper.IsLoaded)
        {
            _coroutineResult = "❌ 本座法身未现，无法验证";
            yield break;
        }

        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;

        // 运行完整验证
        string fullReport = MotionVerifier.RunVerificationSuite(mapper, model);

        if (tier == "quick")
        {
            // 只返回摘要
            _coroutineResult = MotionVerifier.GetCompactSummary(mapper);
        }
        else
        {
            _coroutineResult = fullReport;
            // 如果报告太长，截断
            if (_coroutineResult.Length > 3000)
                _coroutineResult = _coroutineResult.Substring(0, 3000) + "\n...（报告截断，完整版见控制台）";
        }

        // 完整报告写入日志
        UnityEngine.Debug.Log($"[ToolCallInvoker] 具身验证报告 ({tier}):\n{fullReport}");

        // 如果验证发现严重问题，返回警告
        if (fullReport.Contains("❌") && !tier.Equals("quick"))
        {
            _coroutineResult = "⚠️ 部分测试未通过！\n\n" + _coroutineResult;
        }

        yield break;
    }

    // ──────────────────────────────────────────────
    //  vis_verify — GLM-4V 视觉具身验证
    // ──────────────────────────────────────────────

    private IEnumerator VisVerifyCoroutine(string args)
    {
        _coroutineResult = null;

        string mode = JsonRead(args, "mode");
        if (string.IsNullOrEmpty(mode)) mode = "test_only";

        var renderer = FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null || !renderer.Mapper.IsLoaded)
        {
            _coroutineResult = "❌ 本座法身未现，无法进行视觉验证";
            yield break;
        }

        var mapper = renderer.Mapper;
        var model = renderer.CubismModel;

        // 有缓存且是 quick 模式，返回上次摘要
        if (mode == "quick" && !string.IsNullOrEmpty(VisionMotionVerifier.LastReport))
        {
            string cachedSummary = VisionMotionVerifier.LastReport;
            if (cachedSummary.Length > 2000)
                cachedSummary = cachedSummary.Substring(0, 2000) + "\n...（截断）";
            _coroutineResult = cachedSummary;
            yield break;
        }

        // 执行视觉验证 — 会播放每个动作、截图、送 GLM 评审
        string report = null;

        yield return VisionMotionVerifier.RunVisionVerification(
            mapper,
            model,
            renderer,
            onProgress: (idx, total, name) => UnityEngine.Debug.Log($"[vis_verify] ({idx}/{total}) {name}"),
            onResult: r => report = r
        );

        if (string.IsNullOrEmpty(report))
        {
            _coroutineResult = "❌ 视觉验证未产生报告";
            yield break;
        }

        _coroutineResult = report;

        // 如果太长则截断
        if (_coroutineResult.Length > 3000)
            _coroutineResult = _coroutineResult.Substring(0, 3000) + "\n...（报告截断，完整版见控制台）";

        // ★ 附注：告诉 AI 不需要再调 run_verification（验阵校验仅检查模板不报错，与本视觉验证无关）
        _coroutineResult += "\n\n---\n✅ 视觉验证已完成，无需再调用「验阵校验」（run_verification），本座法阵已经过 GLM 考官检验。";

        // 保存完整报告到文件
        string filePath = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(),
            "vis_verify_report.md");
        System.IO.File.WriteAllText(filePath, report);
        UnityEngine.Debug.Log($"[vis_verify] ✅ 完整报告已保存: {filePath}");

        // ★ 自动将 vis_verify 结果写入 MotionMemoryManager（闭环强化学习）
        var mm = MotionMemoryManager.Instance;
        if (mm != null)
        {
            var results = VisionMotionVerifier.LastResults;
            if (results != null)
            {
                foreach (var r in results)
                {
                    if (r.Score > 0 && !string.IsNullOrEmpty(r.ChineseName))
                    {
                        string snapshot = $"{r.KeyFrameCount}帧/vis_verify";
                        bool isNewBest = mm.UpdateScore(r.ChineseName, r.Score, r.GlmJudgment ?? "", snapshot);
                        string badge = isNewBest ? "🏆" : "📝";
                        UnityEngine.Debug.Log($"[vis_verify] {badge} 闭环学习: 「{r.ChineseName}」→ {r.Score}/5" +
                            (isNewBest ? " ★ 新纪录！" : ""));
                    }
                }
                UnityEngine.Debug.Log($"[vis_verify] ✅ 已将 {results.Count} 条 vis_verify 结果写入演武心经");
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("[vis_verify] ⚠ MotionMemoryManager 未就绪，无法写入闭环学习结果");
        }

        // 打印可复制摘要
        string compactSummary = VisionMotionVerifier.GetCompactSummary();
        UnityEngine.Debug.Log($"[vis_verify] ====== 📋 可复制摘要 ======");
        foreach (var line in compactSummary.Split('\n'))
            if (!string.IsNullOrEmpty(line.Trim()))
                UnityEngine.Debug.Log($"[vis_verify] {line.Trim()}");
        UnityEngine.Debug.Log($"[vis_verify] ============================");
        yield break;
    }

    // ================================================================
    //  藏书阁：知识库检索
    // ================================================================

    private IEnumerator KnowledgeSearchCoroutine(string args)
    {
        _coroutineResult = null;

        string query = JsonRead(args, "query");
        if (string.IsNullOrEmpty(query))
        {
            // 试试 description 字段（兼容不同命名习惯）
            query = JsonRead(args, "description");
        }
        if (string.IsNullOrEmpty(query))
        {
            _coroutineResult = "❌ 请告诉本座你想查阅什么，例如「帮我查一下项目里的 Python 脚本」";
            yield break;
        }

        var kb = KnowledgeBaseManager.Instance;
        if (kb == null)
        {
            _coroutineResult = "❌ 藏书阁未载入";
            yield break;
        }

        if (kb.DocumentCount == 0)
        {
            _coroutineResult = "📚 藏书阁尚无一卷藏书。请先使用 knowledge_index 术式索引文件夹。";
            yield break;
        }

        string topKStr = JsonRead(args, "top_k");
        int topK = 5;
        if (!string.IsNullOrEmpty(topKStr)) int.TryParse(topKStr, out topK);

        string result = "";
        yield return kb.SearchAndFormat(query, topK, r => result = r);

        if (string.IsNullOrEmpty(result))
        {
            _coroutineResult = $"🔍 本座翻遍藏书阁也未找到与「{query}」相关的内容……";
            yield break;
        }

        _coroutineResult = result;
    }

    // ================================================================
    //  藏书阁：索引文件夹/文件
    // ================================================================

    private IEnumerator KnowledgeIndexCoroutine(string args)
    {
        _coroutineResult = null;

        string path = JsonRead(args, "path");
        if (string.IsNullOrEmpty(path))
        {
            _coroutineResult = "❌ 请指定要索引的文件夹路径，例如 {\"path\": \"D:/projects\"}";
            yield break;
        }

        if (!Directory.Exists(path))
        {
            // 试试是不是文件
            if (File.Exists(path))
            {
                string result = "";
                yield return KnowledgeBaseManager.Instance.IndexFile(path, (ok, msg) => result = msg);
                _coroutineResult = result;
                yield break;
            }

            _coroutineResult = $"❌ 路径不存在: {path}";
            yield break;
        }

        string recursiveStr = JsonRead(args, "recursive");
        bool recursive = string.IsNullOrEmpty(recursiveStr) || recursiveStr == "true";

        string resultMsg = "";
        yield return KnowledgeBaseManager.Instance.IndexFolderCoroutine(path, recursive, (ok, msg) => resultMsg = msg);

        var kb = KnowledgeBaseManager.Instance;
        _coroutineResult = $"{resultMsg}\n📚 藏书阁现有 {kb.DocumentCount} 卷藏书，共 {kb.ChunkCount} 个分块。";
    }

    // ---- GLM 响应模型 ----

    [System.Serializable]
    private class GlmVisionResponse
    {
        public GlmChoice[] choices;
    }

    [System.Serializable]
    private class GlmChoice
    {
        public GlmMessage message;
    }

    [System.Serializable]
    private class GlmMessage
    {
        public string content;
    }

    [System.Serializable]
    private class GlmErrorResponse
    {
        public GlmErrorDetail error;
    }

    [System.Serializable]
    private class GlmErrorDetail
    {
        public string message;
    }

    // ---- fuxuan_map.json 反序列化（用于同步版 explore_body 的部位分组） ----

    [System.Serializable]
    private class FuxuanMapData
    {
        public FuxuanMapEntry[] entries;
    }

    [System.Serializable]
    private class FuxuanMapEntry
    {
        public string s;     // semantic
        public string p;     // paramId
        public string part;  // bodyPart
    }

    /// <summary>JSON 字符串转义（仅用于手拼 JSON 时的值）</summary>
    private static string EscapeJsonStr(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    /// <summary>判断工具是否应走协程执行</summary>
    public bool IsCoroutineTool(string name) => _coroutineExecutors?.ContainsKey(name) ?? false;

    /// <summary>协程方式执行工具，不阻塞主线程</summary>
    public IEnumerator ExecuteCoroutine(string name, string argsJson)
    {
        _coroutineResult = null;
        if (_coroutineExecutors.TryGetValue(name, out var executor))
        {
            yield return executor(argsJson);
        }
        else
        {
            _coroutineResult = Execute(name, argsJson, out _);
        }
    }

    /// <summary>获取最近一次协程工具的执行结果</summary>
    public string GetCoroutineResult() => _coroutineResult;

    /// <summary>将 async Task 包装为非阻塞协程</summary>
    private IEnumerator RunAsyncTool(Func<Task<string>> taskFactory)
    {
        var task = taskFactory();
        // 每帧检查是否完成，不阻塞主线程
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.IsFaulted)
        {
            _coroutineResult = $"❌ 执行出错: {task.Exception?.InnerException?.Message ?? task.Exception?.Message}";
        }
        else
        {
            _coroutineResult = task.Result;
        }
    }

    /// <summary>search_files 的 async 版本（在后台线程运行）</summary>
    private Task<string> SearchFilesTask(string args)
    {
        return Task.Run(() =>
        {
            // 直接将原来同步版的逻辑搬过来
            string query = JsonRead(args, "query");
            string rootDir = JsonRead(args, "root");
            if (string.IsNullOrEmpty(query)) return "❌ 未说要搜什么";

            string esExe = FindEverythingCli();
            bool useEverything = esExe != null;

            try
            {
                var results = new List<string>();

                if (useEverything)
                {
                    try
                    {
                        string esArgs = $"-n 200 \"{query.Replace("\"", "\\\"")}\"";
                        if (!string.IsNullOrEmpty(rootDir))
                            esArgs = $"-n 200 -path \"{rootDir.Replace("\"", "\\\"")}\" \"{query.Replace("\"", "\\\"")}\"";

                        var psi = new ProcessStartInfo(esExe, esArgs)
                        {
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.GetEncoding(936),
                            StandardErrorEncoding = Encoding.GetEncoding(936)
                        };
                        var p = Process.Start(psi);
                        if (p != null)
                        {
                            string output = p.StandardOutput.ReadToEnd();
                            p.WaitForExit(3000);
                            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                                if (!string.IsNullOrWhiteSpace(line)) results.Add(line);
                        }
                    }
                    catch { useEverything = false; }
                }

                if (!useEverything)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(rootDir))
                            rootDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        if (Directory.Exists(rootDir))
                            SearchRecursive(rootDir, query, results, 200);
                    }
                    catch (Exception ex)
                    {
                        results.Add($"⚠️ 搜索中断：{ex.Message}");
                    }
                }

                if (results.Count == 0)
                {
                    string scope = useEverything
                        ? (string.IsNullOrEmpty(rootDir) ? "本座的天眼所及之处" : $"「{rootDir}」")
                        : (string.IsNullOrEmpty(rootDir) ? "桌面" : $"「{rootDir}」");
                    return $"🔍 在{scope}中未找到与「{query}」匹配的文件";
                }

                string method = useEverything ? "⚡本座以 Everything 天眼通搜" : "🔍本座以递归之法搜";
                string scope2 = string.IsNullOrEmpty(rootDir) ? "全境" : $"「{rootDir}」";
                var sb = new StringBuilder();
                sb.AppendLine($"{method}{scope2}，得 {results.Count} 件与「{query}」相关之物：");
                foreach (var f in results)
                    sb.AppendLine($"  📄 {f}");
                return StringExtensions.Truncate(sb.ToString(), 2000);
            }
            catch (Exception e)
            {
                return $"❌ 搜索时出了岔子：{e.Message}";
            }
        });
    }

    /// <summary>执行工具调用</summary>
    public string Execute(string name, string argsJson, out string error)
    {
        lastToolResult = "";
        lastToolError = "";

        if (_executors == null)
        {
            error = "法阵未就绪";
            return error;
        }

        if (!_executors.ContainsKey(name))
        {
            error = $"不识此术：「{name}」";
            return error;
        }

        try
        {
            string result = _executors[name](argsJson ?? "{}");
            lastToolResult = result;
            error = null;
            return result;
        }
        catch (Exception e)
        {
            lastToolError = e.Message;
            error = $"施法失败：{e.Message}";
            return error;
        }
    }

    // ================================================================
    //  内部实现
    // ================================================================

    // ---- JSON 简易解析（不用依赖库） ----

    private static string JsonRead(string json, string key)
    {
        string search = $"\"{key}\":\"";
        int idx = json.IndexOf(search);
        if (idx >= 0)
        {
            idx += search.Length;
            var sb = new StringBuilder();
            for (int i = idx; i < json.Length; i++)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    if (n == '"') { sb.Append('"'); i++; }
                    else if (n == '\\') { sb.Append('\\'); i++; }
                    else if (n == 'n') { sb.Append('\n'); i++; }
                    else if (n == 'u' && i + 5 < json.Length)  // \uXXXX Unicode
                    {
                        try
                        {
                            string hex = json.Substring(i + 2, 4);
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            i += 5;  // 跳过 u + 4 位 hex
                        }
                        catch { sb.Append(json[i]); }
                    }
                    else sb.Append(json[i]);
                }
                else if (json[i] == '"') break;
                else sb.Append(json[i]);
            }
            return sb.ToString().Trim();
        }

        // 尝试数字/布尔值（不带引号）
        search = $"\"{key}\":";
        idx = json.IndexOf(search);
        if (idx >= 0)
        {
            idx += search.Length;
            var sb = new StringBuilder();
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == ',' || c == '}' || c == ']') break;
                sb.Append(c);
            }
            return sb.ToString().Trim().Trim('"');
        }

        return "";
    }

    /// <summary>简易解析 JSON 对象为 Dictionary（仅支持 "key":value 或 "key":"value" 平铺结构）</summary>
    private static Dictionary<string, float> JsonReadDict(string json, string objKey)
    {
        var dict = new Dictionary<string, float>();

        // 找 objKey 对应的对象 { ... }
        string search = $"\"{objKey}\":";
        int objStart = json.IndexOf(search);
        if (objStart < 0) return dict;

        objStart += search.Length;
        // 跳过空白到 {
        while (objStart < json.Length && json[objStart] != '{') objStart++;
        if (objStart >= json.Length) return dict;

        // 找匹配的 }
        int braceCount = 0;
        int objEnd = objStart;
        for (int i = objStart; i < json.Length; i++)
        {
            if (json[i] == '{') braceCount++;
            else if (json[i] == '}')
            {
                braceCount--;
                if (braceCount == 0) { objEnd = i; break; }
            }
        }

        string inner = json.Substring(objStart + 1, objEnd - objStart - 1);
        // 解析 key:value 对
        int pos = 0;
        while (pos < inner.Length)
        {
            // 跳过空白和逗号
            while (pos < inner.Length && (inner[pos] == ' ' || inner[pos] == ',' || inner[pos] == '\n' || inner[pos] == '\r' || inner[pos] == '\t')) pos++;
            if (pos >= inner.Length) break;

            // 读 key
            if (inner[pos] != '"') break;
            pos++;
            var keySb = new StringBuilder();
            while (pos < inner.Length && inner[pos] != '"')
            {
                if (inner[pos] == '\\') { if (pos + 1 < inner.Length) { keySb.Append(inner[pos + 1]); pos += 2; } }
                else { keySb.Append(inner[pos]); pos++; }
            }
            if (pos >= inner.Length) break;
            pos++; // skip closing "

            // 跳过冒号
            while (pos < inner.Length && (inner[pos] == ' ' || inner[pos] == ':')) pos++;

            // 读 value
            if (pos >= inner.Length) break;

            if (inner[pos] == '"')
            {
                // string value — skip
                pos++;
                while (pos < inner.Length && inner[pos] != '"')
                {
                    if (inner[pos] == '\\') pos += 2;
                    else pos++;
                }
                pos++;
            }
            else
            {
                // numeric value
                var valSb = new StringBuilder();
                while (pos < inner.Length && inner[pos] != ',' && inner[pos] != '}' && inner[pos] != ' ')
                {
                    valSb.Append(inner[pos]); pos++;
                }
                string valStr = valSb.ToString();
                if (float.TryParse(valStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float val))
                {
                    dict[keySb.ToString()] = val;
                }
            }
        }

        return dict;
    }

    // ---- 截图 ----

    private static string SaveScreenshot()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string filename = $"符玄法眼_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string path = Path.Combine(desktop, filename);

#if !UNITY_EDITOR
            // Windows 截图: 使用 SendKeys 模拟 PrtScn + 从剪贴板保存
            // 方式1：使用 .NET 的 Graphics.CopyFromScreen
            int w = Screen.width;
            int h = Screen.height;

            // 需要 System.Drawing.Common
            // 但我们在这里用另一种方法：启动外部截图工具或 PowerShell
            string psScript = $@"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($screen.Left, $screen.Top, 0, 0, $bmp.Size)
$bmp.Save('{path.Replace("'", "''")}', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
";
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"{psScript.Replace("\"", "\\\"")}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            var p = Process.Start(psi);
            // 非阻塞：等一小会儿就行，Unity 不会卡死
            if (p != null && !p.WaitForExit(2000))
            {
                try { p.Kill(); } catch { }
            }
            if (File.Exists(path)) return path;
#endif
            // Unity Editor 下或失败时创建占位
            return path;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[ToolCallInvoker] 截图失败: {e.Message}");
            return null;
        }
    }

    /// <summary>静默截图到临时目录（不留痕迹，供 GLM 分析用）</summary>
    private static string SaveScreenshotTemp()
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "DesktopPet");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            string filename = $"screen_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string path = Path.Combine(tempDir, filename);

#if !UNITY_EDITOR
            string psScript = $@"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($screen.Left, $screen.Top, 0, 0, $bmp.Size)
$bmp.Save('{path.Replace("'", "''")}', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
";
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"{psScript.Replace("\"", "\\\"")}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            var p = Process.Start(psi);
            if (p != null && !p.WaitForExit(5000))
            {
                try { p.Kill(); } catch { }
            }
            if (File.Exists(path)) return path;
#endif
            return path;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[ToolCallInvoker] 静默截图失败: {e.Message}");
            return null;
        }
    }

    // ---- 系统信息 ----

    private static string GetSystemInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("🖥️ 本座以第三眼洞观此间气运：");
        try { sb.AppendLine($"OS: {RuntimeInformation.OSDescription}"); } catch { }
        try { sb.AppendLine($"架构: {RuntimeInformation.ProcessArchitecture}"); } catch { }
        try
        {
            var mi = Environment.WorkingSet;
            float gb = mi / 1024f / 1024f / 1024f;
            if (gb > 1)
                sb.AppendLine($"本进程占灵 {gb:F1} GB ({mi:#,0} bytes)");
            else
                sb.AppendLine($"本进程占灵 {mi / 1024 / 1024} MB");
        }
        catch { }
        try { sb.AppendLine($"CPU 核心: {Environment.ProcessorCount}"); } catch { }
        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var d in drives)
            {
                if (d.IsReady)
                    sb.AppendLine($"磁盘 {d.Name}: {d.TotalSize / 1024 / 1024 / 1024} GB 总 / {(d.TotalSize - d.AvailableFreeSpace) / 1024 / 1024 / 1024} GB 用 / {d.AvailableFreeSpace / 1024 / 1024 / 1024} GB 余");
            }
        }
        catch { }
        try { long ms = Environment.TickCount; sb.AppendLine($"开机: {TimeSpan.FromMilliseconds(ms):d}天{TimeSpan.FromMilliseconds(ms):h}时"); } catch { }
        try { sb.AppendLine($"计算机名: {Environment.MachineName}"); } catch { }
        try { sb.AppendLine($"用户: {Environment.UserName}"); } catch { }
        try { sb.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}"); } catch { }
        return sb.ToString();
    }

    // ---- 剪贴板 ----

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")]
    private static extern bool SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVABLE = 0x0002;

    private static string GetClipboardText()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero)) return null;
            IntPtr hMem = GetClipboardData(CF_UNICODETEXT);
            if (hMem == IntPtr.Zero) { CloseClipboard(); return null; }
            IntPtr ptr = GlobalLock(hMem);
            string text = Marshal.PtrToStringUni(ptr);
            GlobalUnlock(hMem);
            CloseClipboard();
            return text;
        }
        catch { return null; }
    }

    private static void SetClipboardText(string text)
    {
        try
        {
            OpenClipboard(IntPtr.Zero);
            EmptyClipboard();
            IntPtr hMem = GlobalAlloc(GMEM_MOVABLE, (UIntPtr)((text.Length + 1) * 2));
            IntPtr ptr = GlobalLock(hMem);
            for (int i = 0; i <= text.Length; i++)
                Marshal.WriteInt16(ptr, i * 2, (short)(i < text.Length ? text[i] : 0));
            GlobalUnlock(hMem);
            SetClipboardData(CF_UNICODETEXT, hMem);
            CloseClipboard();
        }
        catch { }
    }

    // ---- 音量 ----

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint WM_APPCOMMAND = 0x0319;
    private const int APPCOMMAND_VOLUME_UP = 0x0a;
    private const int APPCOMMAND_VOLUME_DOWN = 0x0b;
    private const int APPCOMMAND_VOLUME_MUTE = 0x08;

    private static void SetSystemVolume(int level)
    {
        // 用 PowerShell 调节音量（最可靠）
        string psCmd = $"(New-Object -ComObject WScript.Shell).SendKeys([char]0); " +
                       $"while($true){{ $v=[Math]::Round((([Audio]::Volume)*100)); if($v -eq {level}){{break}} " +
                       $"if($v -gt {level}){{ (New-Object -ComObject WScript.Shell).SendKeys([char]174) }} " +
                       $"else{{ (New-Object -ComObject WScript.Shell).SendKeys([char]175) }} Start-Sleep -Milliseconds 50 }}";
        try
        {
            Process.Start(new ProcessStartInfo("powershell",
                $"-NoProfile -Command \"& {{$obj = New-Object -ComObject WScript.Shell; for($i=0;$i<100;$i++){{if([Math]::Round($obj.Volume*100) -le {level}){{break}};$obj.SendKeys([char]174);Start-Sleep -Milliseconds 20}};$obj = New-Object -ComObject WScript.Shell; for($i=0;$i<100;$i++){{if([Math]::Round($obj.Volume*100) -ge {level}){{break}};$obj.SendKeys([char]175);Start-Sleep -Milliseconds 20}}}}\"")
            { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { }
    }

    // ---- 锁屏 ----

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    // ---- 鼠标位置 ----

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    // ---- 通知 ----

    private static void ShowNotification(string title, string message)
    {
        try
        {
            // 使用 PowerShell 的 Windows 通知
            string ps = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$textNodes = $template.GetElementsByTagName('text')
$textNodes.Item(0).AppendChild($template.CreateTextNode('{title.Replace("'", "''")}')) > $null
$textNodes.Item(1).AppendChild($template.CreateTextNode('{message.Replace("'", "''")}')) > $null
$toast = [Windows.UI.Notifications.ToastNotification]::new($template)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('符玄').Show($toast)
";
            Process.Start(new ProcessStartInfo("powershell",
                $"-NoProfile -Command \"{ps.Replace("\"", "\\\"")}\"")
            { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { }
    }

    // ================================================================
    //  快速搜索辅助（禁止递归搜索！只搜 PATH 和开始菜单顶层）
    // ================================================================

    /// <summary>只在 PATH 环境变量中精确找 exe（毫秒级返回）</summary>
    private static string FastWhich(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        try
        {
            // where 不加 /R → 只搜 PATH，毫秒级
            var psi = new ProcessStartInfo("where", $"{name}.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            var p = Process.Start(psi);
            string line = p?.StandardOutput?.ReadLine();
            p?.WaitForExit(500);
            return string.IsNullOrEmpty(line) ? null : line;
        }
        catch { return null; }
    }

    /// <summary>寻找 Everything 命令行工具 es.exe</summary>
    private static string FindEverythingCli()
    {
        string[] candidates =
        {
            @"C:\Program Files\Everything\es.exe",
            @"C:\Program Files (x86)\Everything\es.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Everything\es.exe"),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        // 试 PATH
        try
        {
            var psi = new ProcessStartInfo("where", "es.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            string firstLine = p?.StandardOutput?.ReadLine();
            p?.WaitForExit(500);
            if (!string.IsNullOrEmpty(firstLine) && File.Exists(firstLine))
                return firstLine;
        }
        catch { }
        return null;
    }

    /// <summary>递归搜索文件（名称匹配，不限层级）</summary>
    private static void SearchRecursive(string dir, string query, List<string> results, int maxResults)
    {
        if (results.Count >= maxResults) return;
        try
        {
            // 搜索当前目录的文件名
            foreach (var f in Directory.GetFiles(dir))
            {
                if (results.Count >= maxResults) return;
                try
                {
                    string name = Path.GetFileName(f);
                    if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        results.Add(f);
                }
                catch { }
            }

            // 递归子目录
            foreach (var d in Directory.GetDirectories(dir))
            {
                if (results.Count >= maxResults) return;
                // 跳过系统/隐藏目录和 junction 点
                try
                {
                    var attr = File.GetAttributes(d);
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue; // 跳过符号链接/junction 防止死循环
                    // 也把匹配的目录名加入结果
                    string dirName = Path.GetFileName(d);
                    if (dirName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        results.Add(d + "\\");
                        if (results.Count >= maxResults) return;
                    }
                    SearchRecursive(d, query, results, maxResults);
                }
                catch { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (PathTooLongException) { }
        catch { }
    }

    /// <summary>只在开始菜单 Programs 顶层找快捷方式（不递归）</summary>
    private static string FastFindLink(string rootDir, string keyword)
    {
        if (!Directory.Exists(rootDir)) return null;
        string kw = keyword.ToLowerInvariant();
        try
        {
            foreach (var f in Directory.GetFiles(rootDir, "*.lnk", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (name.Contains(kw)) return f;
            }
        }
        catch { }
        return null;
    }

    // ================================================================
    //  文件管理辅助方法
    // ================================================================

    /// <summary>用 Python 桥搜索文件（原生支持中文/特殊字符），直接从 Unity 包目录找</summary>
    private static string SearchFileByPython(string query, string rootDir)
    {
        try
        {
            // 先找到 find_file.py 的位置
            string scriptPath = FindPythonScript();
            if (scriptPath == null)
            {
                // Python 桥不可用，回退到 C# 全盘递归搜索
                return SearchFileFallback(query, rootDir);
            }

            string pythonExe = FindPythonExe();

            // 保存原始 rootDir，回退时用
            string originalRootDir = rootDir;

            string args = $"\"{scriptPath}\" \"{query.Replace("\"", "\\\"")}\"";
            if (!string.IsNullOrEmpty(rootDir))
                args += $" \"{rootDir.Replace("\"", "\\\"")}\"";
            else
                args += $" \"ALL_DRIVES\"";  // 标记搜全盘

            var psi = new ProcessStartInfo(pythonExe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var p = Process.Start(psi);
            if (p == null) return SearchFileFallback(query, originalRootDir);

            string jsonOutput = p.StandardOutput.ReadToEnd();
            string errOutput = p.StandardError.ReadToEnd();
            p.WaitForExit(15000);

            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(jsonOutput))
            {
                UnityEngine.Debug.LogWarning($"[ToolCallInvoker] Python 搜索失败: {StringExtensions.Truncate(errOutput, 200)}");
                return SearchFileFallback(query, originalRootDir);
            }

            // 解析 JSON 结果
            var results = new List<string>();
            try
            {
                // 简单 JSON 手动解析（避免依赖）
                string filesKey = "\"files\":";
                int filesIdx = jsonOutput.IndexOf(filesKey);
                if (filesIdx < 0) return "⚠️ Python 返回格式异常";

                int arrayStart = jsonOutput.IndexOf('[', filesIdx);
                int arrayEnd = jsonOutput.LastIndexOf(']');
                if (arrayStart < 0 || arrayEnd < 0) return "⚠️ Python 返回格式异常";

                string arrayContent = jsonOutput.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                int pos = 0;
                while (true)
                {
                    int uriKey = arrayContent.IndexOf("\"uri\":", pos);
                    if (uriKey < 0) break;
                    int valStart = arrayContent.IndexOf('"', uriKey + 6);
                    if (valStart < 0) break;
                    int valEnd = arrayContent.IndexOf('"', valStart + 1);
                    if (valEnd < 0) break;
                    string uri = arrayContent.Substring(valStart + 1, valEnd - valStart - 1);
                    // 从 URI 解码为路径
                    string decodedPath = Uri.UnescapeDataString(uri);
                    if (decodedPath.StartsWith("file:///"))
                        decodedPath = decodedPath.Substring(8);
                    if (decodedPath.Length >= 3 && decodedPath[0] == '/' && decodedPath[2] == ':')
                        decodedPath = decodedPath.TrimStart('/');
                    results.Add(decodedPath);
                    pos = valEnd + 1;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[ToolCallInvoker] JSON 解析失败: {ex.Message}");
                return SearchFileFallback(query, originalRootDir);
            }

            if (results.Count == 0)
                return $"🔍 未找到与「{query}」匹配的文件";

            var sb = new StringBuilder();
            sb.AppendLine($"⚡本座以 Python 天机术搜索，得 {results.Count} 件：");
            int displayCount = Math.Min(results.Count, 50);
            for (int i = 0; i < displayCount; i++)
                sb.AppendLine($"  📄 {results[i]}");
            if (results.Count > displayCount)
                sb.AppendLine($"  …及另 {results.Count - displayCount} 件");
            sb.AppendLine($"💡 可对我说「打开第n个」或把路径传给我");
            return StringExtensions.Truncate(sb.ToString(), 3000);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[ToolCallInvoker] Python 搜索异常: {e.Message}");
            return SearchFileFallback(query, rootDir);
        }
    }

    /// <summary>回退方案：C# 全盘递归搜索（扫所有固定磁盘）</summary>
    private static string SearchFileFallback(string query, string rootDir)
    {
        try
        {
            var results = new List<string>();
            int maxResults = 100;

            if (!string.IsNullOrEmpty(rootDir))
            {
                if (Directory.Exists(rootDir))
                    SearchRecursive(rootDir, query, results, maxResults);
                else
                    return $"❌ 目录不存在：「{rootDir}」";
            }
            else
            {
                // 全盘搜：所有固定驱动器
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (results.Count >= maxResults) break;
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    {
                        SearchRecursive(drive.RootDirectory.FullName, query, results, maxResults);
                    }
                }
            }

            if (results.Count == 0)
                return $"🔍 遍寻不得与「{query}」匹配之物";

            var sb = new StringBuilder();
            sb.AppendLine($"🔍 本座以遍历之法搜索，得 {results.Count} 件：");
            int displayCount = Math.Min(results.Count, 50);
            for (int i = 0; i < displayCount; i++)
                sb.AppendLine($"  📄 {results[i]}");
            if (results.Count > displayCount)
                sb.AppendLine($"  …及另 {results.Count - displayCount} 件");
            return StringExtensions.Truncate(sb.ToString(), 3000);
        }
        catch (Exception e)
        {
            return $"❌ 搜索出了岔子：{e.Message}";
        }
    }

    /// <summary>查找 find_file.py 脚本位置</summary>
    private static string FindPythonScript()
    {
        string projectRoot = AppDomain.CurrentDomain.BaseDirectory;
        // 从构建目录回溯到项目根
        string[] searchPaths =
        {
            Path.Combine(projectRoot, "..\\..\\..\\..\\tools\\find_file.py"),
            Path.Combine(projectRoot, "..\\..\\tools\\find_file.py"),
            Path.Combine(projectRoot, "..\\tools\\find_file.py"),
            Path.Combine(Environment.CurrentDirectory, "tools\\find_file.py"),
            @"D:\Unity\projects\Desktop_per_pro\tools\find_file.py",
        };
        foreach (var p in searchPaths)
        {
            string full = Path.GetFullPath(p);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    /// <summary>查找可用的 Python 解释器</summary>
    private static string FindPythonExe()
    {
        // 尝试 venv 优先
        string[] candidates =
        {
            Path.Combine(Environment.CurrentDirectory, ".venv\\Scripts\\python.exe"),
            Path.Combine(Environment.CurrentDirectory, "venv\\Scripts\\python.exe"),
            "python",
            "python3",
        };
        foreach (var c in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(c, "--version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(1000);
                    if (p.ExitCode == 0) return c;
                }
            }
            catch { }
        }
        return "python";
    }

    /// <summary>检查路径是否在安全白名单内（防止 AI 越权访问系统文件）</summary>
    private static bool IsPathAllowed(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        try
        {
            string fullPath = Path.GetFullPath(path);

            // 允许的安全目录列表
            var allowedDirs = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Path.GetTempPath(),                    // 截图临时目录
                DataPathConfig.DataRoot,         // 宠物自身数据目录
                Application.dataPath,                   // 游戏数据目录
            };

            // 也允许在用户主目录下的常用文件夹操作
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                allowedDirs.Add(Path.Combine(userProfile, "Pictures"));
                allowedDirs.Add(Path.Combine(userProfile, "Music"));
                allowedDirs.Add(Path.Combine(userProfile, "Videos"));
                allowedDirs.Add(Path.Combine(userProfile, "Desktop"));
            }

            foreach (var dir in allowedDirs)
            {
                if (!string.IsNullOrEmpty(dir) && fullPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>安全的命令白名单 — 仅允许无害的查询类命令</summary>
    private static readonly HashSet<string> _allowedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ipconfig", "ipconfig /all",
        "ping", "ping -n 1",
        "tracert", "pathping",
        "systeminfo",
        "tasklist",
        "netstat", "netstat -an",
        "whoami",
        "hostname",
        "ver",
        "date", "time",
        "dir", "tree",
        "echo",
        "chcp",
        "getmac",
    };

    /// <summary>检查命令是否在安全白名单内</summary>
    private static bool IsCommandAllowed(string command)
    {
        if (string.IsNullOrEmpty(command)) return false;
        string trimmed = command.TrimStart();
        // 精确匹配白名单
        if (_allowedCommands.Contains(trimmed)) return true;
        // 允许白名单命令带参数（如 ping 8.8.8.8）
        foreach (var allowed in _allowedCommands)
        {
            if (trimmed.StartsWith(allowed + " ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>解码 file:// URI 为 Windows 路径</summary>
    private static string DecodeFileUri(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith("file://"))
        {
            try
            {
                var uri = new Uri(path);
                string decoded = Uri.UnescapeDataString(uri.AbsolutePath);
                // Windows: file:///D:/path → D:/path
                if (decoded.Length >= 3 && decoded[0] == '/' && decoded[2] == ':')
                    decoded = decoded.TrimStart('/');
                return decoded;
            }
            catch
            {
                return path;
            }
        }
        return path;
    }

    /// <summary>格式化文件大小</summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    /// <summary>递归复制目录</summary>
    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSubDir);
        }
    }

    // ---- 回收站 Shell API ----

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszProgressTitle;
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    /// <summary>将文件或目录发送到回收站</summary>
    private static void SendToRecycleBin(string path, bool isDir)
    {
        var op = new SHFILEOPSTRUCT
        {
            hwnd = IntPtr.Zero,
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",  // 双 null 结尾
            pTo = null,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            fAnyOperationsAborted = 0,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null
        };
        int ret = SHFileOperationW(ref op);
        if (ret != 0)
        {
            // 如果 Shell API 失败（如回收站被禁用），回退到永久删除
            if (isDir)
                Directory.Delete(path, recursive: true);
            else
                File.Delete(path);
        }
    }
}

// ================================================================
//  扩展方法
// ================================================================

public static class StringExtensions
{
    public static string Truncate(this string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
        return s.Substring(0, maxLen) + "\n…(余略)";
    }
}
