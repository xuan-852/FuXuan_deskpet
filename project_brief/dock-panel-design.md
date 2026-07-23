# 收纳盘（Dock Panel）设计方案

> 文档版本: v1.0 · 最后更新: 2026-07-22

---

## 一、需求概述

在桌面宠上增加一个**文件收纳盘**功能，允许用户从桌面/资源管理器拖放文件到收纳盘暂存，双击即可打开，解决桌面文件杂乱、临时文件无处安放的问题。

核心交互：
- 鼠标悬停 → 展开面板，显示已收纳文件图标
- 鼠标离开 2s → 自动折叠为迷你条
- 从资源管理器拖放文件到面板 → 收纳
- 双击图标 → 打开文件
- 右键菜单 → 打开位置 / 移出 / 置顶

---

## 二、模块结构

```
DesktopPet/Assets/Scripts/
├── Dock/
│   ├── DockPanel.cs              ← 面板主控（~180行）
│   ├── DockItem.cs               ← 单个文件图标（~100行）
│   ├── DockDropHandler.cs        ← WndProc 子类化拖放接收（~120行）
│   ├── DockIconProvider.cs       ← 系统图标提取（~120行）
│   ├── DockState.cs              ← 数据模型 + 持久化（~80行）
│   └── DockToggle.cs             ← 桌宠身上的开关（~30行）
├── WindowOverlay.cs              ← 已有，新增 ~45 行拖放接口
└── DragHandler.cs                ← 已有，复用点击穿透检测
```

**总计新增代码:** ~680 行
**估算工时:** 3.5 个工作日

---

## 三、各模块详细设计

---

### 3.1 DockState.cs — 数据模型 + 持久化

```csharp
[Serializable]
public class DockItemData
{
    public string path;          // 完整路径
    public string fileName;      // 显示名
    public string extension;     // .lnk .pdf .zip
    public long addedTicks;      // 添加时间
}

[Serializable]
public class DockStateData
{
    public int version = 1;
    public float panelX, panelY;      // 归一化坐标 (0~1)
    public bool collapsed;
    public List<DockItemData> items = new();
}

public static class DockState
{
    private static readonly string SavePath =
        Path.Combine(@"D:\DesktopPetData", "dock_state.json");

    public static DockStateData Load() { /* JsonUtility.FromJson */ }
    public static void Save(DockStateData state) { /* JsonUtility.ToJson */ }
    public static void Delete() { /* File.Delete */ }
}
```

**设计要点：**
- `version` 字段用于未来数据迁移
- 归一化坐标适配多分辨率
- 不存缩略图路径——启动时重新从 `DockIconProvider` 获取
- 持久化路径统一到 `D:\DesktopPetData\`（复用 `DataPathConfig.DataRoot`）

---

### 3.2 DockPanel.cs — 面板主控

```csharp
public class DockPanel : MonoBehaviour
{
    [Header("References")]
    public RectTransform dockRoot;          // 面板根
    public RectTransform collapsedBar;      // 折叠态迷你条
    public RectTransform expandedPanel;     // 展开态面板
    public Transform iconGrid;              // 图标容器 (GridLayoutGroup)
    public GameObject dockItemPrefab;       // DockItem 预制体
    public Button clearBtn;
    public Text countLabel;

    [Header("Behavior")]
    public float topMargin = 10f;
    public float rightMargin = 10f;
    public float collapseDelay = 2f;
    public int maxColumns = 4;

    // 状态
    private DockStateData state;
    private bool isExpanded;

    void Start()
    {
        state = DockState.Load() ?? new DockStateData();
        ApplyPosition();
        RebuildIconGrid();
        clearBtn.onClick.AddListener(OnClear);
        DockDropHandler.OnFilesDropped += OnFilesDropped;
    }

    void Update()
    {
        // 悬停检测：鼠标是否在 dockRoot 范围内
        // ⚠️ 多屏注意：用 DragHandler 的 AABB 检测代替 RectTransformUtility
        bool hovering = IsMouseOverDock();

        if (hovering)
        {
            if (!isExpanded) Expand();
            mouseLeaveTimestamp = 0f;
        }
        else if (isExpanded)
        {
            if (mouseLeaveTimestamp == 0f)
                mouseLeaveTimestamp = Time.time;
            else if (Time.time - mouseLeaveTimestamp > collapseDelay)
                Collapse();
        }

        ApplyPosition();
    }

    void Expand() { /* 展开动画 → isExpanded=true */ }
    void Collapse() { /* 折叠动画 → isExpanded=false, mouseLeaveTimestamp=0 */ }
    void ApplyPosition() { /* 右上角定位 */ }
    void RebuildIconGrid() { /* 清除旧 icon → 遍历 items → 实例化 DockItem */ }
    void OnClear() { /* 确认 → 清空 items → 保存 */ }

    public void AddItem(string filePath)
    {
        // 防重复 → 创建 DockItemData → 插入 state.items
        // → RebuildIconGrid() → DockState.Save()
    }

    private bool IsMouseOverDock()
    {
        // 复用 DragHandler 的 AABB 模式，不用 RectTransformUtility
        // 在多屏下更稳定
        Vector2 mousePos = Input.mousePosition;
        Rect rect = dockRoot.rect;
        return mousePos.x >= dockRoot.position.x &&
               mousePos.x <= dockRoot.position.x + rect.width &&
               mousePos.y >= dockRoot.position.y &&
               mousePos.y <= dockRoot.position.y + rect.height;
    }

    void OnApplicationQuit() { DockState.Save(state); }
}
```

**设计要点：**
- 纯 Unity Canvas 内 UI，不依赖额外窗口/进程
- 悬停检测用 AABB 代替 `RectTransformUtility`（多屏兼容）
- 延迟 2s 折叠防误触
- 每帧刷新位置以应对分辨率变化

---

### 3.3 DockItem.cs — 单个文件图标

```csharp
public class DockItem : MonoBehaviour
{
    public Image iconImage;
    public Text fileNameLabel;

    private DockItemData data;

    public void Init(DockItemData itemData)
    {
        data = itemData;

        // 获取系统图标（按扩展名缓存）
        DockIconProvider.GetIcon(data.path, 48, sprite =>
        {
            iconImage.sprite = sprite;
        });

        fileNameLabel.text = TruncateName(Path.GetFileNameWithoutExtension(data.path), 12);

        // 双击打开
        // 右键菜单
    }

    void OpenFile()
    {
        System.Diagnostics.Process.Start(new ProcessStartInfo(data.path)
        {
            UseShellExecute = true
        });
    }

    private string TruncateName(string name, int maxLen)
    {
        return name.Length > maxLen ? name[..(maxLen - 2)] + ".." : name;
    }
}
```

**设计要点：**
- 文件名超 12 字符自动截断
- `Process.Start` + `UseShellExecute=true` 支持任意文件类型
- 图标异步回调加载

---

### 3.4 DockDropHandler.cs — WndProc 子类化拖放接收

```csharp
public static class DockDropHandler
{
    // ── P/Invoke ──
    [DllImport("user32.dll")]
    private static extern bool DragAcceptFiles(IntPtr hWnd, bool accept);

    [DllImport("shell32.dll")]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile,
        StringBuilder lpszFile, uint cch);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr hDrop);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd,
        uint msg, IntPtr wParam, IntPtr lParam);

    // ── 常量 ──
    private const int GWL_WNDPROC = -4;
    private const uint WM_DROPFILES = 0x0233;

    // ── 状态 ──
    private static IntPtr _originalWndProc;
    private static IntPtr _hwnd;

    // ⚠️ 必须持活防止 GC 回收 WndProc 委托
    private static readonly WndProcDelegate _wndProcDelegate = WndProc;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // 线程安全队列
    private static readonly List<string> _pendingFiles = new();
    private static readonly object _lock = new();

    public static event Action<string[]> OnFilesDropped;

    public static void Initialize(IntPtr windowHandle)
    {
        _hwnd = windowHandle;
        DragAcceptFiles(_hwnd, true);

        _originalWndProc = SetWindowLongPtr64(_hwnd, GWL_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DROPFILES)
        {
            IntPtr hDrop = wParam;
            uint fileCount = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            string[] files = new string[fileCount];

            for (uint i = 0; i < fileCount; i++)
            {
                StringBuilder sb = new(260);
                DragQueryFile(hDrop, i, sb, 260);
                files[i] = sb.ToString();
            }
            DragFinish(hDrop);

            lock (_lock)
            {
                _pendingFiles.AddRange(files);
            }
            return IntPtr.Zero;
        }

        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>每帧由 Unity 主线程调用</summary>
    public static void ProcessPendingDrops()
    {
        string[] files;
        lock (_lock)
        {
            if (_pendingFiles.Count == 0) return;
            files = _pendingFiles.ToArray();
            _pendingFiles.Clear();
        }
        OnFilesDropped?.Invoke(files);
    }

    /// <summary>退出时还原 WndProc</summary>
    public static void Shutdown()
    {
        if (_hwnd != IntPtr.Zero && _originalWndProc != IntPtr.Zero)
        {
            SetWindowLongPtr64(_hwnd, GWL_WNDPROC, _originalWndProc);
        }
        DragAcceptFiles(_hwnd, false);
    }
}
```

**设计要点：**
- `EntryPoint = "SetWindowLongPtrW"` 兼容 32/64 位
- `_wndProcDelegate` 静态持活防止 GC 回收
- 线程安全队列：`lock` + 主线程 `ProcessPendingDrops`
- `Shutdown()` 还原原始窗口过程

---

### 3.5 DockIconProvider.cs — 系统图标提取

> ⚠️ **Tuanjie 2022.3 (Unity) 没有 System.Drawing**，不能用 `Icon.FromHandle()`。
> 改用纯 Win32 P/Invoke 方案：`SHGetFileInfo` + 手动提取像素。

```csharp
public static class DockIconProvider
{
    // ── P/Invoke ──
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initData);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint startScan,
        uint cScanLines, IntPtr lpvBits, ref BITMAPINFO lpbmi, uint usage);

    // ── 结构体 ──
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;

    // ── 缓存 ──
    private static readonly Dictionary<string, Sprite> _cache = new();

    public static void GetIcon(string filePath, int size, Action<Sprite> callback)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        // 缓存命中
        if (_cache.TryGetValue(ext, out Sprite cached))
        {
            callback(cached);
            return;
        }

        // 提取系统图标
        SHFILEINFO shfi = new();
        IntPtr result = SHGetFileInfo(filePath, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_LARGEICON);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
        {
            callback(null);
            return;
        }

        Texture2D tex = ExtractTextureFromIcon(shfi.hIcon, size);
        DestroyIcon(shfi.hIcon);

        if (tex != null)
        {
            Sprite sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            _cache[ext] = sprite;
            callback(sprite);
        }
        else
        {
            callback(null);
        }
    }

    private static Texture2D ExtractTextureFromIcon(IntPtr hIcon, int size)
    {
        // HICON → BITMAP (通过 GetIconInfo + GetDIBits 提取像素)
        // 返回 Texture2D (RGBA32)
        // ...
    }
}
```

**V1 Fallback（图标系统稳定前）：** 按扩展名映射颜色块

| 类型 | 显示 |
|------|------|
| `.pdf` | 🔴 `PDF` |
| `.zip/.rar/.7z` | 🟡 `ZIP` |
| `.exe/.msi` | 🔵 `EXE` |
| `.txt/.md/.log` | ⚪ `TXT` |
| `.jpg/.png/.gif` | 🟢 `IMG` |
| `.doc/.docx/.xlsx` | 🔵 `DOC` |
| 默认 | ⚫ `FIL` |

---

### 3.6 DockToggle.cs — 桌宠开关

```csharp
public class DockToggle : MonoBehaviour
{
    public DockPanel dockPanel;
    public GameObject bagIcon;     // 桌宠身上的背包图标
    private bool isVisible = true;

    void Start() { bagIcon.SetActive(true); }

    void OnMouseDown()
    {
        isVisible = !isVisible;
        dockPanel.gameObject.SetActive(isVisible);
        bagIcon.SetActive(isVisible);
    }
}
```

---

## 四、WindowOverlay.cs 修改点

| 位置 | 改动 |
|------|------|
| `Start()` 末尾 | 调用 `DockDropHandler.Initialize(_hwnd)` |
| `Update()` 末尾 | 调用 `DockDropHandler.ProcessPendingDrops()` |
| `OnApplicationQuit()` | 调用 `DockDropHandler.Shutdown()` |

实际新增代码：~45 行

---

## 五、数据流

```
[用户从桌面拖文件]
       │
       ▼
WindowOverlay (Windows 消息线程)
 └─ WndProc 拦截 WM_DROPFILES
    └─ DragQueryFile 提取路径列表
       └─ 入 pendingFiles 队列
              │
              ▼ (下一帧)
DockDropHandler.ProcessPendingDrops() (Unity 主线程)
       │
       ▼
DockPanel.AddItem(path)
       │
       ▼
DockIconProvider.GetIcon(path) → 从缓存/提取图标
│      ▼
└→ DockItem.Init(icon, name) → 挂到 iconGrid
       │
       ▼
DockState.Save() → D:\DesktopPetData\dock_state.json
```

---

## 六、UI 交互设计

```
常态（右上角）              悬停展开
 ┌─────┐              ┌──────────────────────┐
 │ 📎 3│  ← 迷你条    │ 📎 收纳盘 (3项) [🗑️] │
 └─────┘              │ ┌──┐ ┌──┐ ┌──┐      │
                      │ │📄│ │🗜│ │🔗│      │
                      │ └──┘ └──┘ └──┘      │
                      │ [清空]               │
                      └──────────────────────┘
```

**交互规则：**
- 默认折叠为右上角迷你条（仅图标 + 数量）
- 鼠标悬停 → 展开为完整面板
- 鼠标离开 2s → 自动折叠
- 从桌面拖文件到面板 → 收纳
- 双击图标 → 打开文件
- 右键 → 菜单：[打开位置] [移出]
- 清空按钮 → 确认后移除所有

---

## 七、关键技术风险与对策

| 风险 | 等级 | 对策 |
|------|------|------|
| Unity 不含 System.Drawing | 🔴 高 | 纯 Win32 P/Invoke 替代方案 |
| WndProc 委托被 GC 回收 | 🟡 中 | 静态字段持活 |
| 多屏下鼠标坐标不一致 | 🟡 中 | AABB 检测代替 RectTransformUtility |
| SetWindowLongPtr 32/64 兼容 | 🟢 低 | EntryPoint 指定 W Unicode 版本 |
| 图标提取跨 Unity 版本 | 🟢 低 | V1 用颜色块 fallback，稳定后再切 |
| Unity DomainReload 重置静态 | 🟢 低 | 每次重新挂载 WndProc |

---

## 八、交付清单

```
Dock/
├── DockPanel.cs          ~180行   主控逻辑
├── DockItem.cs           ~100行   单个图标
├── DockDropHandler.cs    ~120行   WndProc 拖放
├── DockIconProvider.cs   ~120行   系统图标提取
├── DockState.cs           ~80行   数据 + 持久化
└── DockToggle.cs          ~30行   开关

WindowOverlay.cs 修改     ~45行    挂载拖放接口
总计新增代码             ~680行
预估工时                 3.5天 (不含测试)
```
