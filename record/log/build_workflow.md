# 标准构建流程

> 最后更新: 2026-06-28

---

## 一、概述

本项目使用 **Unity 2022.3.62t7 (Tuanjie 引擎)** 命令行批处理模式构建，构建脚本为 `Assets/Editor/BuildScript.cs`，输出到 `Build/DesktopPet.exe`。

---

## 二、环境配置

| 项目 | 路径 |
|------|------|
| **引擎路径** | `D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe` |
| **项目路径** | `D:\Unity\projects\Desktop_per_pro\code\desktop_unity\` |
| **构建输出** | `D:\Unity\projects\Desktop_per_pro\Build\DesktopPet.exe` |
| **构建日志** | `D:\Unity\projects\Desktop_per_pro\build_log6.txt` |

---

## 三、构建命令

### PowerShell 直接构建

```powershell
& "D:\Unity\editor\2022.3.62t7\Editor\Tuanjie.exe" `
    -batchmode -quit `
    -projectPath "D:\Unity\projects\Desktop_per_pro\code\desktop_unity" `
    -logFile build_log6.txt `
    -executeMethod BuildScript.BuildDesktopPet
```

> **注意**：项目路径需指向 `code\desktop_unity`（含 `Assets/` 的 Unity 项目根目录），而非项目仓库根目录。

---

## 四、构建脚本说明

`Assets/Editor/BuildScript.cs` 中的 `BuildDesktopPet()` 方法：

1. **扫描场景**：优先使用 `EditorBuildSettings.scenes` 中注册的场景，回退到 `AssetDatabase` 搜索所有 `.unity` 场景文件
2. **平台目标**：`StandaloneWindows`（Win64）
3. **输出位置**：`D:\Unity\projects\Desktop_per_pro\Build\DesktopPet.exe`
4. **退出码**：成功 `0`，失败 `1`

---

## 五、构建后操作

### 5.1 验证构建产物

```powershell
Get-Item "D:\Unity\projects\Desktop_per_pro\Build\DesktopPet.exe"
Get-Item "D:\Unity\projects\Desktop_per_pro\Build\DesktopPet_Data\Managed\Assembly-CSharp.dll"
```

### 5.2 启动运行

```powershell
Start-Process -FilePath "D:\Unity\projects\Desktop_per_pro\Build\DesktopPet.exe"
```

### 5.3 查看构建日志

```powershell
Get-Content "D:\Unity\projects\Desktop_per_pro\build_log6.txt"
```

构建日志关键字搜索：

```powershell
# 查看脚本编译结果
Select-String "CompileScripts|Assembly-CSharp" build_log6.txt

# 查看场景列表
Select-String "场景|scenes" build_log6.txt

# 查找警告/错误
Select-String "error|Error|warning|Warning" build_log6.txt

# 查看构建结果
Select-String "构建完成|Build completed|result" build_log6.txt
```

---

## 六、常见问题

### 6.1 DLL 未更新

现象：构建日志中出现 `Not rebuilding Data files -- no changes`。

原因：Unity 检测到脚本无变更，未将新 `Assembly-CSharp.dll` 复制到 `Build/` 目录。

解决方法（选一）：

**方法一：手动复制（最快）**
```powershell
Copy-Item "code\desktop_unity\Library\Bee\PlayerScriptAssemblies\Assembly-CSharp.dll" `
          "Build\DesktopPet_Data\Managed\Assembly-CSharp.dll" -Force
Copy-Item "code\desktop_unity\Library\Bee\PlayerScriptAssemblies\Assembly-CSharp.pdb" `
          "Build\DesktopPet_Data\Managed\Assembly-CSharp.pdb" -Force
```

**方法二：删除缓存后重建**
```powershell
Remove-Item "code\desktop_unity\Library\ScriptAssemblies\Assembly-CSharp.dll" -Force
Remove-Item "Build\DesktopPet_Data\Managed\Assembly-CSharp.dll" -Force
# 然后重新构建
```

### 6.2 构建过程中断

如果构建被手动中断，下次构建前需清理锁文件：

```powershell
Remove-Item "code\desktop_unity\Library\PackageCache\.lock" -Force -ErrorAction SilentlyContinue
```

---

## 七、目录结构说明

```
D:\Unity\projects\Desktop_per_pro\          # 项目仓库根目录
├── code\desktop_unity\                     # Unity 项目根目录（含 Assets/）
│   └── Assets\
│       ├── Scripts\                        # C# 业务脚本
│       ├── Editor\                         # 编辑器/构建脚本
│       │   └── BuildScript.cs              # 构建入口
│       ├── Resources\                      # Resources 资源
│       ├── StreamingAssets\                # 流式资源（Live2D 模型）
│       └── Live2D\                         # Cubism SDK
├── Build\                                  # 构建输出目录
│   ├── DesktopPet.exe                      # 可执行文件
│   └── DesktopPet_Data\                    # 数据目录
├── record\                                 # 项目记录
│   ├── Latex\                              # LaTeX 文档（技术文档）
│   └── log\                                # 项目日志
│       └── build_workflow.md               # 本文件
├── build_log6.txt                          # 最近一次构建日志
└── build_temp.cmd                          # 构建批处理
```
