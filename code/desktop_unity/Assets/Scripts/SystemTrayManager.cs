using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 系统托盘管理 — 托盘图标、隐藏/恢复、开机自启
///
/// 功能：
/// - 在 Windows 系统托盘创建图标
/// - 左键/双击 = 切换显示/隐藏
/// - 右键 = 弹出菜单（显示/隐藏、开机自启、退出）
/// - 开机自启通过 HKCU\...\Run 注册表管理
/// - 创建隐藏消息窗口接收托盘回调
///
/// 用法：
/// 1. 挂到 DesktopPet 同一 GameObject，或让 DesktopPet 自动添加
/// 2. 在 DesktopPet.Start() 中调用 Initialize(mainWindowHandle)
/// 3. 程序退出时自动清理托盘图标
/// </summary>
public class SystemTrayManager : MonoBehaviour
{
    #region Win32 P/Invoke — 结构体

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;    // LPCWSTR — 自动封送为指针
        public string lpszClassName;   // LPCWSTR
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    // 窗口 Proc 委托类型（必须存为字段防 GC）
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    #endregion

    #region Win32 P/Invoke — 常量

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;

    private const uint NIF_MESSAGE = 0x0001;
    private const uint NIF_ICON = 0x0002;
    private const uint NIF_TIP = 0x0004;
    private const uint NIF_SHOWTIP = 0x0080;

    private const uint NIS_HIDDEN = 0x0001;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint WM_USER = 0x0400;
    private const uint WM_TRAYICON = WM_USER + 100;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;

    private const uint SW_HIDE = 0;
    private const uint SW_SHOW = 5;
    private const uint SW_SHOWNA = 8;

    // 右键菜单项 ID
    private const uint MENU_SHOW = 1001;
    private const uint MENU_AUTOSTART = 1002;
    private const uint MENU_QUIT = 1003;

    private const uint MF_STRING = 0;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_UNCHECKED = 0;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_BYPOSITION = 0x0400;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_LEFTALIGN = 0x0000;

    #endregion

    #region Win32 P/Invoke — DllImport

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, uint nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags,
        uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags,
        int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
        out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetCurrentProcess();

    // ---- 注册表 API (代替 Microsoft.Win32.Registry，Unity 不支持) ----
    private const uint KEY_SET_VALUE = 0x0002;
    private const uint KEY_QUERY_VALUE = 0x0001;
    private const uint REG_SZ = 1;
    private const uint KEY_WOW64_64KEY = 0x0100;

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int RegOpenKeyEx(
        IntPtr hKey, string lpSubKey, uint ulOptions, uint samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int RegSetValueEx(
        IntPtr hKey, string lpValueName, uint Reserved, uint dwType,
        byte[] lpData, int cbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int RegQueryValueEx(
        IntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType,
        byte[] lpData, ref int lpcbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int RegDeleteValue(IntPtr hKey, string lpValueName);

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    private static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));

    #endregion

    // ===================================================================
    // ===== 🎛️ 调参区 =====
    // ===================================================================
    [Header("托盘设置")]
    [Tooltip("鼠标悬停托盘图标时的提示文字")]
    public string trayTooltip = "符玄桌面宠物";
    [Tooltip("开机自启注册表项名称")]
    public string autoStartRegName = "DesktopPet";
    [Tooltip("调试日志")]
    public bool debugLog = true;
    // ===================================================================

    /// <summary>
    /// 托盘是否已初始化完毕（可用）
    /// </summary>
    public bool IsReady => _initialized && _mainWindowHandle != IntPtr.Zero;

    // 内部状态
    private IntPtr _trayHwnd = IntPtr.Zero;     // 隐藏消息窗口句柄
    private IntPtr _mainWindowHandle = IntPtr.Zero; // Unity 主窗口句柄
    private IntPtr _hIcon = IntPtr.Zero;         // 托盘图标
    private bool _initialized = false;
    private bool _minimizedToTray = false;
    private bool _autoStartEnabled = false;
    private bool _showMenuRequested = false;     // 右键菜单请求（线程安全）

    // 窗口 Proc 委托（必须存字段，否则 GC 会回收导致崩溃）
    private WndProcDelegate _wndProcDelegate;

    // 单例
    private static SystemTrayManager _instance;

    /// <summary>是否已隐藏到托盘</summary>
    public bool IsMinimizedToTray => _minimizedToTray;

    /// <summary>开机自启当前状态</summary>
    public bool AutoStartEnabled => _autoStartEnabled;

    /// <summary>退出请求事件（通知主程序关闭）</summary>
    public event Action OnQuitRequested;

    /// <summary>显示/隐藏切换事件</summary>
    public event Action<bool> OnVisibilityChanged;

    // 引用 WindowOverlay（用于恢复时重新应用 DWM 透明）
    private WindowOverlay _windowOverlay;

    // ================================================================

    /// <summary>更新托盘悬浮提示文字（可用于显示提醒数量）</summary>
    public void UpdateTooltip(string tip)
    {
        trayTooltip = tip;
        if (!_initialized || _trayHwnd == IntPtr.Zero) return;

        NOTIFYICONDATA nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
            hWnd = _trayHwnd,
            uID = 0,
            uFlags = NIF_TIP | NIF_SHOWTIP,
            szTip = tip
        };
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private void Awake()
    {
        if (_instance != null)
        {
            Destroy(this);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 读取当前开机自启状态
        _autoStartEnabled = ReadAutoStartRegistry();
        Log($"开机自启状态: {_autoStartEnabled}");
    }

    #region 公开接口

    /// <summary>
    /// 初始化托盘管理器（在获取到主窗口句柄后调用）
    /// </summary>
    /// <param name="mainWindowHandle">Unity 主窗口句柄</param>
    public void Initialize(IntPtr mainWindowHandle)
    {
        if (_initialized || mainWindowHandle == IntPtr.Zero) return;
        _mainWindowHandle = mainWindowHandle;
        Log($"初始化托盘，主窗口句柄: {mainWindowHandle.ToInt64():X8}");

        try
        {
            CreateHiddenWindow();
            _hIcon = ExtractAppIcon();
            AddTrayIcon();
            _initialized = true;
            Log("✅ 托盘管理器初始化完成");
        }
        catch (Exception ex)
        {
            LogError($"托盘初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 隐藏窗口到托盘
    /// </summary>
    public void MinimizeToTray()
    {
        if (_minimizedToTray || _mainWindowHandle == IntPtr.Zero) return;

        _minimizedToTray = true;
        ShowWindow(_mainWindowHandle, SW_HIDE);
        OnVisibilityChanged?.Invoke(false);
        Log("隐藏到托盘");
    }

    /// <summary>
    /// 从托盘恢复窗口
    /// </summary>
    public void RestoreFromTray()
    {
        if (!_minimizedToTray || _mainWindowHandle == IntPtr.Zero) return;

        _minimizedToTray = false;

        // 先显示窗口（SW_HIDE 后必须用 SW_SHOW 确保真正显示）
        ShowWindow(_mainWindowHandle, SW_SHOW);

        // ★ 恢复后重新应用 DWM 玻璃层透明 — SW_HIDE 会丢失 DWM 效果
        //    ApplyNow() 会重新设置 DWM 透明 + 移除边框 + 置顶
        if (_windowOverlay == null)
            _windowOverlay = FindObjectOfType<WindowOverlay>();
        if (_windowOverlay != null)
        {
            _windowOverlay.ApplyNow();
        }

        SetForegroundWindow(_mainWindowHandle);
        OnVisibilityChanged?.Invoke(true);
        Log("从托盘恢复");
    }

    /// <summary>
    /// 切换显示/隐藏
    /// </summary>
    public void ToggleVisibility()
    {
        if (_minimizedToTray)
            RestoreFromTray();
        else
            MinimizeToTray();
    }

    /// <summary>
    /// 设置开机自启（通过 Win32 注册表 API，Unity 友好）
    /// </summary>
    /// <param name="enabled">是否启用</param>
    public void SetAutoStart(bool enabled)
    {
        try
        {
            string regPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

            if (enabled)
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string value = $"\"{exePath}\"";
                WriteRegistryString(HKEY_CURRENT_USER, regPath, autoStartRegName, value);
                Log($"✅ 已设置开机自启: {value}");
            }
            else
            {
                DeleteRegistryValue(HKEY_CURRENT_USER, regPath, autoStartRegName);
                Log("已取消开机自启");
            }

            _autoStartEnabled = enabled;
        }
        catch (Exception ex)
        {
            LogError($"设置开机自启失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 退出程序（清理后发送退出信号）
    /// </summary>
    public void Quit()
    {
        Log("用户通过托盘菜单退出程序");
        RemoveTrayIcon();
        OnQuitRequested?.Invoke();
    }

    #endregion

    #region 隐藏窗口 & 托盘图标

    /// <summary>
    /// 创建隐藏消息窗口（接收 Shell_NotifyIcon 回调）
    /// </summary>
    private void CreateHiddenWindow()
    {
        // 用进程 ID 保证类名唯一
        string className = "DesktopPetTrayMsgWindow_" + Process.GetCurrentProcess().Id;

        _wndProcDelegate = TrayWndProc;

        WNDCLASS wc = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = className
        };

        ushort regResult = RegisterClass(ref wc);
        if (regResult == 0)
        {
            int err = Marshal.GetLastWin32Error();
            // ERROR_CLASS_ALREADY_EXISTS=1410：类已存在，可以继续
            if (err == 1410)
            {
                Log("隐藏窗口类已存在（上次注册遗留），继续使用");
            }
            else
            {
                LogError($"注册隐藏窗口类失败, error={err}（仍尝试创建窗口）");
            }
        }

        // 创建隐藏消息窗口
        _trayHwnd = CreateWindowEx(
            0, className, "TrayMsgWindow",
            0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        if (_trayHwnd == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            // 如果类已注册但 CreateWindowEx 仍失败，可能是跨线程问题
            // 最后一次尝试：用 FindWindow 找现有窗口
            IntPtr existing = FindWindow(className, null);
            if (existing != IntPtr.Zero)
            {
                _trayHwnd = existing;
                Log($"找到现有隐藏窗口: {_trayHwnd.ToInt64():X8}");
            }
            else
            {
                throw new Exception($"创建隐藏窗口失败, error={err}");
            }
        }
        else
        {
            Log($"隐藏消息窗口已创建: {_trayHwnd.ToInt64():X8}");
        }
    }

    /// <summary>
    /// 从当前 exe 提取图标
    /// </summary>
    private IntPtr ExtractAppIcon()
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;

            uint result = ExtractIconEx(exePath, 0, out IntPtr largeIcon, out IntPtr smallIcon, 1);
            if (result > 0 && largeIcon != IntPtr.Zero)
            {
                Log($"已提取程序图标");
                return largeIcon;
            }
        }
        catch (Exception ex)
        {
            LogError($"提取图标失败: {ex.Message}");
        }

        // 保底：用系统默认应用图标
        return LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }

    /// <summary>
    /// 添加托盘图标
    /// </summary>
    private void AddTrayIcon()
    {
        if (_trayHwnd == IntPtr.Zero || _hIcon == IntPtr.Zero)
        {
            LogError("添加托盘图标失败: 窗口句柄或图标为空");
            return;
        }

        NOTIFYICONDATA nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
            hWnd = _trayHwnd,
            uID = 0,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = trayTooltip
        };

        bool ok = Shell_NotifyIcon(NIM_ADD, ref nid);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            throw new Exception($"Shell_NotifyIcon(NIM_ADD) 失败, error={err}");
        }

        // 设置版本（启用 NIN_* 回调行为）
        nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref nid);

        Log("✅ 托盘图标已添加");
    }

    /// <summary>
    /// 移除托盘图标并释放资源
    /// </summary>
    public void RemoveTrayIcon()
    {
        if (_trayHwnd != IntPtr.Zero)
        {
            NOTIFYICONDATA nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _trayHwnd,
                uID = 0
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        if (_trayHwnd != IntPtr.Zero)
        {
            DestroyWindow(_trayHwnd);
            _trayHwnd = IntPtr.Zero;
        }

        _initialized = false;
        Log("托盘图标已移除");
    }

    #endregion

    #region 托盘消息处理

    /// <summary>
    /// 隐藏窗口的消息处理函数
    /// </summary>
    private IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            uint mouseMsg = (uint)lParam;
            switch (mouseMsg)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    // 左键单击/双击 → 切换显示/隐藏
                    ExecuteOnMainThread(ToggleVisibility);
                    break;

                case WM_RBUTTONUP:
                    // 右键 → 下一帧显示弹出菜单
                    _showMenuRequested = true;
                    break;
            }
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// 在 Unity 主线程执行操作（从 WndProc 回调调用）
    /// </summary>
    private void ExecuteOnMainThread(System.Action action)
    {
        _pendingActions.Enqueue(action);
    }

    // 主线程待执行队列（线程安全）
    private readonly System.Collections.Concurrent.ConcurrentQueue<System.Action> _pendingActions =
        new System.Collections.Concurrent.ConcurrentQueue<System.Action>();

    private void Update()
    {
        // 执行从 WndProc 来的待处理事件
        while (_pendingActions.TryDequeue(out System.Action action))
        {
            try { action(); }
            catch (Exception ex) { LogError($"执行待处理事件失败: {ex.Message}"); }
        }

        // 处理右键菜单请求（必须在主线程调用 TrackPopupMenu）
        if (_showMenuRequested && _trayHwnd != IntPtr.Zero)
        {
            _showMenuRequested = false;
            ShowTrayContextMenu();
        }
    }

    /// <summary>
    /// 显示托盘右键菜单
    /// </summary>
    private void ShowTrayContextMenu()
    {
        if (_trayHwnd == IntPtr.Zero) return;

        // 必须置 foreground，否则菜单可能不显示
        SetForegroundWindow(_trayHwnd);

        GetCursorPos(out POINT pos);

        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        // — 菜单项 —
        InsertMenu(hMenu, 0, MF_BYPOSITION | MF_STRING, MENU_SHOW,
            _minimizedToTray ? "显示 (S)" : "隐藏 (H)");

        InsertMenu(hMenu, 1, MF_BYPOSITION | MF_SEPARATOR, 0, null);

        InsertMenu(hMenu, 2, MF_BYPOSITION | MF_STRING |
            (_autoStartEnabled ? MF_CHECKED : MF_UNCHECKED),
            MENU_AUTOSTART, "开机自启 (A)");

        InsertMenu(hMenu, 3, MF_BYPOSITION | MF_SEPARATOR, 0, null);

        InsertMenu(hMenu, 4, MF_BYPOSITION | MF_STRING, MENU_QUIT, "退出 (Q)");

        // 同步获取用户选择（阻塞）
        uint cmd = TrackPopupMenu(hMenu,
            TPM_RETURNCMD | TPM_LEFTALIGN,
            pos.x, pos.y, 0, _trayHwnd, IntPtr.Zero);

        DestroyMenu(hMenu);

        // 处理选择的命令
        switch (cmd)
        {
            case MENU_SHOW:
                ToggleVisibility();
                break;
            case MENU_AUTOSTART:
                SetAutoStart(!_autoStartEnabled);
                break;
            case MENU_QUIT:
                Quit();
                break;
        }
    }

    #endregion

    #region 开机自启 (Win32 Registry API)

    private bool ReadAutoStartRegistry()
    {
        try
        {
            string regPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            string val = ReadRegistryString(HKEY_CURRENT_USER, regPath, autoStartRegName);
            return !string.IsNullOrEmpty(val);
        }
        catch (Exception ex)
        {
            LogError($"读取开机自启注册表失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 读取注册表字符串值
    /// </summary>
    private string ReadRegistryString(IntPtr hKeyRoot, string subKey, string valueName)
    {
        IntPtr hKey = IntPtr.Zero;
        int result = RegOpenKeyEx(hKeyRoot, subKey, 0, KEY_QUERY_VALUE, out hKey);
        if (result != 0 || hKey == IntPtr.Zero)
            return null;

        try
        {
            uint type = 0;
            int cbData = 0;
            // 先查大小
            RegQueryValueEx(hKey, valueName, IntPtr.Zero, out type, null, ref cbData);
            if (cbData <= 0)
                return null;

            byte[] buffer = new byte[cbData];
            RegQueryValueEx(hKey, valueName, IntPtr.Zero, out type, buffer, ref cbData);

            if (type == REG_SZ)
            {
                // 去掉末尾 null 终止符
                string s = System.Text.Encoding.UTF8.GetString(buffer);
                int nullIdx = s.IndexOf('\0');
                if (nullIdx >= 0) s = s.Substring(0, nullIdx);
                return s;
            }
            return null;
        }
        finally
        {
            RegCloseKey(hKey);
        }
    }

    /// <summary>
    /// 写入注册表字符串值
    /// </summary>
    private void WriteRegistryString(IntPtr hKeyRoot, string subKey, string valueName, string value)
    {
        IntPtr hKey = IntPtr.Zero;
        // KEY_SET_VALUE 就足够写入现有的键；但 Run 键已存在，所以不需要 CREATE_SUB_KEY
        int result = RegOpenKeyEx(hKeyRoot, subKey, 0, KEY_SET_VALUE, out hKey);
        if (result != 0 || hKey == IntPtr.Zero)
            throw new Exception($"RegOpenKeyEx 失败, error={result}");

        try
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(value + '\0');
            result = RegSetValueEx(hKey, valueName, 0, REG_SZ, data, data.Length);
            if (result != 0)
                throw new Exception($"RegSetValueEx 失败, error={result}");
        }
        finally
        {
            RegCloseKey(hKey);
        }
    }

    /// <summary>
    /// 删除注册表值
    /// </summary>
    private void DeleteRegistryValue(IntPtr hKeyRoot, string subKey, string valueName)
    {
        IntPtr hKey = IntPtr.Zero;
        int result = RegOpenKeyEx(hKeyRoot, subKey, 0, KEY_SET_VALUE, out hKey);
        if (result != 0 || hKey == IntPtr.Zero)
            return; // 键不存在，忽略

        try
        {
            RegDeleteValue(hKey, valueName);
        }
        finally
        {
            RegCloseKey(hKey);
        }
    }

    #endregion

    #region Unity 生命周期

    private void OnApplicationQuit()
    {
        RemoveTrayIcon();
    }

    private void OnDestroy()
    {
        RemoveTrayIcon();
        if (_instance == this) _instance = null;
    }

    #endregion

    #region 日志

    private void Log(string msg)
    {
        if (debugLog) UnityEngine.Debug.Log($"[SystemTrayManager] {msg}");
    }

    private void LogError(string msg)
    {
        UnityEngine.Debug.LogError($"[SystemTrayManager] {msg}");
    }

    #endregion
}
