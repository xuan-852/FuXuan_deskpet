# Win11 验收虚拟机 — 快速启动手册

本目录文件（已入库）：
| 文件 | 作用 |
|------|------|
| `create-win11-vm.ps1` | 命令行创建 VM（4G 内存/4 核/40G 盘/EFI+TPM2.0/VRDE）|
| `autounattend.xml` | Win11 无人值守应答文件（自动分区/装系统/建用户 FuXuan）|
| `vm-acceptance-guide.md`（上一级）| 完整验收流程 |

## 你需要做的（约 20 分钟，只做一次）

1. **下载 VirtualBox 7.x**：https://www.virtualbox.org/wiki/Downloads → "Windows hosts"
   （必须 7.x，6.x 没有 TPM 支持，Win11 装不了）
2. **下载 Win11 ISO**：https://www.microsoft.com/zh-cn/software-download/windows11 → 选"下载 Windows 11 磁盘映像(ISO)" → 选中文（简体）
   （约 6GB）
3. 把两个文件放到一个文件夹，**默认建议 `D:\vm-setup\`**（ISO 命名为 `Win11.iso` 或告诉我实际路径）
4. **双击安装 VirtualBox**（一路下一步，UAC 允许）→ 装完告诉我

## 之后交给 AI 代理

在项目根目录执行：
```powershell
powershell -ExecutionPolicy Bypass -File installer\vm-setup\create-win11-vm.ps1 -IsoPath D:\vm-setup\Win11.iso
```
会创建 VM → 挂 ISO + autounattend → 启动无头安装（30-40 分钟自动装完）→
自动登录用户 `FuXuan` / 密码 `Passw0rd!`（本机 VM 专用，非项目密钥）。

## 验收步骤（装完后）

同 `installer\vm-acceptance-guide.md`：传 `FuXuanSetup-1.0.0.exe` 进 VM →
安装 → 跑 `verify-acceptance.cjs`（VM 里应**全 PASS**，尤其补上云上没验成的 `/health` 与聊天项）。

## 注意事项

- 磁盘：Win11 ISO(6G) + VM 虚拟盘(40G) 需要约 50GB 空闲，先确认磁盘空间
- VRDE 端口 33891：可用 VNC 客户端连 `127.0.0.1:33891` 看安装进度（VirtualBox 管理器双击 VM 也行）
- autounattend 关闭了 UAC（EnableLUA=0），验收完如需日常用可在 VM 里重新开启
