# 虚拟机验收操作指南（阶段 4）— VirtualBox 路线

> 目的：在**干净系统**上完整验证安装包（installer-plan §八 12 项），
> 覆盖本机测不出的场景：缺 VC++ / 无 Node / 无 Python / 无 D 盘 / 无既有环境变量。
> 预估耗时：准备 1 小时 + 验收 1 小时（快照可复用，之后每次验收 < 30 分钟）。

---

## 第 0 步：确认本机条件

- 本机 CPU 32 核（实测），VirtualBox 完全跑得动；内存请开任务管理器看「可用内存」——
  **建议 ≥ 8GB 可用**（分给 VM 4GB；要测 Ollama 则分 8GB）。
- 若 VirtualBox 启动 VM 报 VT-x 不可用：进 BIOS 开启 Intel VT-x / AMD-V。

## 第 1 步：下载两个东西

| 软件 | 下载 | 说明 |
|------|------|------|
| **VirtualBox**（免费）| oracle.com/virtualbox → Windows 版 | 7.x 自带 Win11 所需 TPM 模拟 |
| **Windows ISO** | microsoft.com/software-download/windows11 → 下载 ISO | 或 Windows 10 ISO；装完不激活也能用（评估模式）|

## 第 2 步：创建虚拟机

1. VirtualBox → 新建：
   - 名称 `FuXuanTest`，类型 Windows，版本 Windows 10/11 x64
   - 内存 **4096 MB**（测 Ollama 用 8192），CPU **4 核**
   - 硬盘 **40 GB** VDI 动态分配
2. 设置 → 系统 → 勾选「启用 EFI」+ **TPM 2.0**（VirtualBox 7：系统→TPM→选择 v2.0，Win11 必需）
3. 设置 → 存储 → 挂载下载的 ISO → 启动 → 正常装 Windows（跳过产品密钥，选「我没有产品密钥」）
4. 装完系统后：**设备 → 安装增强功能（Guest Additions）**——共享剪贴板/拖拽/共享文件夹都需要它
5. **重要：打快照** → 虚拟机菜单「快照」→ 生成 → 命名 `干净基线`（以后每次验收都从这恢复，可重复测）

## 第 3 步：把文件拷进虚拟机

三种方式任选：
- **共享文件夹**（推荐）：设置 → 共享文件夹 → 添加本机 `D:\Unity\projects\Desktop_per_pro\installer\dist\`（只读）→ VM 内 `\\VBOXSVR\dist` 访问
- 或拖拽（Guest Additions 后支持）
- 或放 U 盘/网盘

需要拷进去的：
```
FuXuanSetup-1.0.0.exe          ← 安装器（113MB）
```

## 第 4 步：VM 内完整安装（人工验收向导）

1. 双击 `FuXuanSetup-1.0.0.exe` → UAC 允许
2. 走完整向导：语言 → 许可 → **组件勾选**（建议全勾，含 Ollama/TeX，验证组件下载安装）→
   安装目录（默认 `C:\Program Files\FuXuan`）→ **数据目录**（默认 `D:\DesktopPetData`；
   若 VM 无 D 盘，改到 `C:\DesktopPetData`，验证 FU_XUAN_DATA 逻辑）→
   **API 密钥页**（填一个临时 DeepSeek Key；没有就留空，聊天检查会如实报告）→ 安装执行
3. 观察：VC++ 静默装 / 网关启动 / Ollama 拉模型（数 GB，可取消勾选加速）/ NSSM 服务注册
4. 完成页勾选启动桌宠 → 确认能跑

## 第 5 步：自动验收（跑内置脚本）

在 VM 里打开 CMD，执行（安装器已内置验收脚本 + 便携 Node）：

```bat
"C:\Program Files\FuXuan\bridge\node\node.exe" "C:\Program Files\FuXuan\extras\acceptance\verify-acceptance.cjs"
```

预期输出：自动项 **全部 PASS**（安装产物 / 环境变量 / 桥服务 / /health / 桌宠启动 /
聊天真实回复 / 工具调用 / PPT 生成 / PDF 提取）。若数据目录改过，加参数：
`--dir "C:\Program Files\FuXuan"`。

## 第 6 步：手动 6 项（脚本末尾列出）

| # | 项 | 做法 |
|---|----|------|
| 2 | openclaw_task 审批 | 对桌宠说「帮我查一下 B 站更新」→ 审批弹窗 → 放行 → 返回结果 |
| 9 | compile_latex | 若勾选了 TeX：让桌宠编译一个 LaTeX 文档出 PDF |
| 10 | 重启自启 | 重启 VM → 确认桥服务自动运行（任务管理器服务） + 桌宠自启 |
| 11 | 卸载 | 控制面板卸载 → 弹窗选「是」→ 确认数据目录保留、服务/环境变量清理 |
| 12 | 升级 | 再装一次新版本安装器 → 确认旧数据完好 |
| UX | 向导体验 | 全程无卡死、无异常弹窗 |

## 第 7 步：收尾

- 验收记录：把 PASS/FAIL 结果截图或存文本，更新 `docs/installer-plan.md` §八 清单打勾
- 虚拟机可删除（数据在快照里）；或保留快照下次复测

---

## 备选：云 Windows 虚拟机（更省事，约 ¥2-5/小时）

不想装 VirtualBox / 下载 ISO 的话：腾讯云或阿里云开一台 **Windows Server 按量计费**
轻量实例（2 核 4G 即可），远程桌面连上去，把安装器传上去跑同样的第 4~6 步，
测完**销毁实例**（按小时计费）。好处是绝对干净、不用折腾本地虚拟化；坏处是花钱 + 网络下载组件慢。

## 常见坑

- **Win11 装不上**：多半是 TPM/EFI 没开（VirtualBox 7 设置里开 TPM 2.0 + EFI）
- **VM 里没网**：默认 NAT 即可联网；组件下载（Ollama/TeX/VC++）都需要网络
- **拖不进文件**：先装 Guest Additions（设备菜单）
- **Ollama 太慢**：验收时可不勾选（聊天/工具不依赖它）
- **端口 19876 冲突**：本机 PM2 桥与本机测试安装会撞端口；**VM 是独立机器无此问题**，
  本机 GUI 试装时记得测完卸载
