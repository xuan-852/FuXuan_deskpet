# 标准构建流程

> 最后更新: 2026-08-02（N38，按代码真相审计核对，内容基本准确）

---

## 一、概述

使用 **Tuanjie 2022.3.62t7 (Unity 衍生版)** 命令行批处理构建，输出到 `Build/DesktopPet.exe`。

---

## 二、环境配置

| 项目 | 路径 |
|------|------|
| 引擎 | `D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe` |
| 项目 | `D:\Unity\projects\Desktop_per_pro\code\desktop_unity\` |
| 输出 | `D:\Unity\projects\Desktop_per_pro\Build\DesktopPet.exe` |
| 构建脚本 | `Assets/Editor/BuildScript.cs` — `BuildDesktopPet()` |

---

## 三、构建方法

### 方法 1: VS Code Task (推荐)

`build-current` task — 自动处理 Subst + CD + 构建。

### 方法 2: PowerShell build.ps1

```powershell
.\build.ps1                    # 完整构建
.\build.ps1 -Quick             # 仅编译验证
.\build.ps1 -RunTests          # Editor 测试
.\build.ps1 -UnityExe "D:\..." # 指定引擎
.\build.ps1 -LogFile log.txt   # 指定日志
```

### 方法 3: CMD build.cmd

```cmd
build.cmd
```

### 方法 4: 手动 PowerShell

```powershell
# ⚠️ 关键：必须先 CD 到项目目录，否则 Tuanjie 路径拼接 Bug
Set-Location "D:\Unity\projects\Desktop_per_pro\code\desktop_unity"
& "D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe" `
    -batchmode -quit `
    -projectPath . `
    -logFile build_log.txt `
    -executeMethod BuildScript.BuildDesktopPet
```

---

## 四、构建流程

1. `BuildScript.BuildDesktopPet()` 扫描场景
2. 平台 `StandaloneWindows` (Win64)
3. 输出到 `Build/DesktopPet.exe`
4. 成功返回 0，失败返回 1

---

## 五、构建后验证

```powershell
Get-Item ".\Build\DesktopPet.exe"
Get-Item ".\Build\DesktopPet_Data\Managed\Assembly-CSharp.dll"
Start-Process ".\Build\DesktopPet.exe"
```

### 日志分析

```powershell
Select-String "error|Error|warning|Warning" build_log.txt
Select-String "CompileScripts|Assembly-CSharp" build_log.txt
Select-String "Build completed|result" build_log.txt
```

---

## 六、关键注意事项

| 问题 | 说明 | 解决 |
|------|------|------|
| **🔴 Tuanjie 路径拼接 Bug** | 绝对路径 `-projectPath "D:\..."` 时 `.` 被解析为空 | 必须 cd 到项目目录 + `-projectPath .` |
| **编辑器实例冲突** | 另一个 Tuanjie 进程占用 | `Stop-Process -Name "Tuanjie"` |
| **exe 被锁** | 正在运行的 DesktopPet 占用构建输出 | `Stop-Process -Name "DesktopPet"` |
| **DLL 未更新** | `Not rebuilding Data files -- no changes` | 手动复制 `Library/Bee/PlayerScriptAssemblies/` 到 `Build/` |
| **锁残留** | 手动中断构建后 | `Remove-Item "Library/PackageCache/.lock"` |

---

## 七、构建 Pipeline

```
代码修改
  → build.ps1 -Quick (编译验证, ~30s)
    → 通过 → build.ps1 (完整构建, ~5min)
      → 通过 → 验证 Build/DesktopPet.exe
        → 启动测试
```

---

## 八、启动流程

```
DesktopPet.exe
  → Mutex 单例检查
  → WindowOverlay: DWM 透明窗口
  → PetConfig / PetMemory 加载 (D:\DesktopPetData\)
  → Live2DRenderer: 模型加载 (StreamingAssets/Live2D/Fuxuan，资源加载失败降级)
  → ChatManager: API 初始化 (DeepSeek + GLM + Ollama)
  → ServerPollService: 开始轮询
  → IdleChatGenerator: 预生成闲话
  → MotionAgent: 开始决策循环
  → ReminderManager: 提醒调度（气泡/Toast/Server酱³）
  → PerformanceMonitor: 开始监控
  → SystemTray: 托盘图标
  → 就绪
```
