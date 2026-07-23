using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// 拖放接收 — WndProc 子类化拦截 WM_DROPFILES
///
/// 设计要点：
/// - WndProc 委托必须静态持活，防止 GC 回收导致崩溃
/// - 收到文件入线程安全队列，主线程 ProcessPendingDrops 处理
/// - Shutdown 时还原原始窗口过程
/// </summary>
public static class DockDropHandler
{
    #region Win32 API

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern bool DragAcceptFiles(IntPtr hWnd, bool accept);

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile,
        StringBuilder lpszFile, uint cch);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void DragFinish(IntPtr hDrop);

    // ⚠️ 32/64 位兼容：
    // - 64-bit: SetWindowLongPtrW
    // - 32-bit: SetWindowLongW（SetWindowLongPtrW 在 32-bit Dll 中不存在）
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd,
        uint msg, IntPtr wParam, IntPtr lParam);

    #endregion

    #region 常量

    private const int GWL_WNDPROC = -4;
    private const uint WM_DROPFILES = 0x0233;

    #endregion

    #region 状态

    private static IntPtr _originalWndProc = IntPtr.Zero;
    private static IntPtr _hwnd = IntPtr.Zero;
    private static bool _initialized = false;

    /// <summary>
    /// ⚠️ 必须静态持活！WndProc 由非托管代码回调，
    /// 若被 GC 回收会触发 AccessViolationException 导致进程崩溃。
    /// </summary>
    private static readonly WndProcDelegate _wndProcDelegate = WndProc;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // 线程安全文件队列
    private static readonly List<string> _pendingFiles = new();
    private static readonly object _lock = new();

    /// <summary>文件拖放事件（主线程触发，非线程安全，请勿在回调中阻塞）</summary>
    public static event Action<string[]> OnFilesDropped;

    #endregion

    #region 公共接口

    /// <summary>初始化拖放接收，子类化窗口过程</summary>
    /// <param name="windowHandle">目标窗口句柄</param>
    public static void Initialize(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            Debug.LogError("[DockDropHandler] Initialize: 窗口句柄为空");
            return;
        }

        if (_initialized)
        {
            // 如果句柄变了，重新挂载
            if (_hwnd == windowHandle)
                return;
            Shutdown();
        }

        _hwnd = windowHandle;

        // 不在此开启 DragAcceptFiles——由 DockPanel 展开/折叠控制
        // 初始状态：禁用拖放，文件穿透到桌面

        // 子类化窗口过程（32/64 兼容）
        IntPtr funcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _originalWndProc = SetWindowLongSafe(_hwnd, GWL_WNDPROC, funcPtr);

        if (_originalWndProc == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            Debug.LogError($"[DockDropHandler] SetWindowLongPtr 失败, error={err}");
            DragAcceptFiles(_hwnd, false);
            _hwnd = IntPtr.Zero;
            return;
        }

        _initialized = true;
        Debug.Log($"[DockDropHandler] 初始化成功, hWnd={_hwnd.ToInt64():X8}");

        // 初始状态强制关闭拖放（折叠态），文件穿透到桌面/其他窗口
        DragAcceptFiles(_hwnd, false);
    }

    /// <summary>每帧由 Unity 主线程调用（或放到 Update 末尾）</summary>
    public static void ProcessPendingDrops()
    {
        if (!_initialized) return;

        string[] files;
        lock (_lock)
        {
            if (_pendingFiles.Count == 0) return;
            files = _pendingFiles.ToArray();
            _pendingFiles.Clear();
        }

        try
        {
            OnFilesDropped?.Invoke(files);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DockDropHandler] 处理拖放事件异常: {ex.Message}");
        }
    }

    /// <summary>根据收纳盘展开/折叠状态，开关拖放接收</summary>
    /// <param name="accept">true=展开，可接收文件；false=折叠，穿透到桌面</param>
    public static void SetAcceptFiles(bool accept)
    {
        if (!_initialized || _hwnd == IntPtr.Zero) return;
        DragAcceptFiles(_hwnd, accept);
        Debug.Log($"[DockDropHandler] 拖放接收: {(accept ? "开启" : "关闭")}");
    }

    /// <summary>退出时还原窗口过程，释放拖放资源</summary>
    public static void Shutdown()
    {
        if (!_initialized) return;

        if (_hwnd != IntPtr.Zero && _originalWndProc != IntPtr.Zero)
        {
            SetWindowLongSafe(_hwnd, GWL_WNDPROC, _originalWndProc);
            DragAcceptFiles(_hwnd, false);
        }

        _initialized = false;
        _hwnd = IntPtr.Zero;
        _originalWndProc = IntPtr.Zero;
    }

    #endregion

    #region 32/64 兼容辅助

    /// <summary>
    /// 安全调用 SetWindowLong / SetWindowLongPtr
    /// 32-bit: 只有 SetWindowLongW, SetWindowLongPtrW 不存在
    /// 64-bit: SetWindowLongPtrW
    /// </summary>
    private static IntPtr SetWindowLongSafe(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        try
        {
            // 先试 64-bit 版本
            return SetWindowLong64(hWnd, nIndex, dwNewLong);
        }
        catch (EntryPointNotFoundException)
        {
            // 32-bit 回退
            return SetWindowLong32(hWnd, nIndex, dwNewLong);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DockDropHandler] SetWindowLong 异常: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    #endregion

    #region WndProc

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DROPFILES)
        {
            IntPtr hDrop = wParam;

            try
            {
                // 获取文件数量
                uint fileCount = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                if (fileCount == 0)
                {
                    DragFinish(hDrop);
                    return IntPtr.Zero;
                }

                string[] files = new string[fileCount];
                for (uint i = 0; i < fileCount; i++)
                {
                    StringBuilder sb = new StringBuilder(260);
                    DragQueryFile(hDrop, i, sb, 260);
                    files[i] = sb.ToString();
                }

                // 入线程安全队列
                lock (_lock)
                {
                    _pendingFiles.AddRange(files);
                }

                Debug.Log($"[DockDropHandler] WM_DROPFILES: 收到 {fileCount} 个文件");
            }
            finally
            {
                DragFinish(hDrop);
            }

            return IntPtr.Zero;
        }

        // 其他消息交由原始窗口过程处理
        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    #endregion
}
