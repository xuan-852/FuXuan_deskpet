# ============================================================
# 创建 Win11 验收虚拟机（VirtualBox 7+，命令行版）
# 前置：VirtualBox 已安装 + Win11 ISO 已下载
# 用法：powershell -ExecutionPolicy Bypass -File create-win11-vm.ps1
#       -IsoPath D:\VirtualBox\Win11.iso 可覆盖默认 ISO 路径
# 默认路径全部指向 D:\VirtualBox（C 盘空间紧张，D 盘 200G 充足）
# ============================================================
param(
    [string]$VmName = "FuXuanWin11",
    [string]$IsoPath = "D:\VirtualBox\Win11.iso",
    [string]$VmDir = "D:\VirtualBox\VMs",
    [int]$MemoryMB = 4096,
    [int]$Cpus = 4,
    [int]$DiskMB = 40960
)

$ErrorActionPreference = "Stop"
$vbm = "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe"
if (-not (Test-Path $vbm)) { Write-Host "[ERROR] 未找到 VBoxManage，请先安装 VirtualBox 7+" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $IsoPath)) { Write-Host "[ERROR] 未找到 ISO: $IsoPath" -ForegroundColor Red; exit 1 }

function VBox([string]$argsLine) { Write-Host "> VBoxManage $argsLine"; & $vbm $argsLine.Split(' ') | Out-Null; if ($LASTEXITCODE -ne 0) { Write-Host "[WARN] VBoxManage 返回 $LASTEXITCODE: $argsLine" -ForegroundColor Yellow } }

Write-Host "== 1/7 删除旧 VM（如存在）=="
VBox "unregistervm $VmName --delete"

Write-Host "== 2/7 创建 VM（Win11_64, 4G 内存, 4 核, EFI+TPM2.0，机器文件在 D:\VirtualBox\VMs）=="
New-Item -ItemType Directory -Path $VmDir -Force | Out-Null
VBox "createvm --name $VmName --ostype Windows11_64 --register --basefolder `"$VmDir`""
VBox "modifyvm $VmName --memory $MemoryMB --cpus $Cpus --firmware efi --tpm-type 2.0"
VBox "modifyvm $VmName --graphicscontroller vmsvga --vram 128"
VBox "modifyvm $VmName --vrde on --vrdeport 33891"
VBox "modifyvm $VmName --audio none --usb off"
VBox "modifyvm $VmName --nic1 nat"

Write-Host "== 3/7 创建 40G 动态虚拟硬盘（D:\VirtualBox\VMs）=="
New-Item -ItemType Directory -Path $VmDir -Force | Out-Null
$vdi = Join-Path $VmDir "$VmName\$VmName.vdi"
VBox "createmedium disk --filename `"$vdi`" --size $DiskMB --format VDI"

Write-Host "== 4/7 挂载 SATA 控制器 + 硬盘 =="
VBox "storagectl $VmName --name SATA --add sata --controller IntelAhci"
VBox "storageattach $VmName --storagectl SATA --port 0 --device 0 --type hdd --medium `"$vdi`""

Write-Host "== 5/7 挂载 Win11 ISO =="
VBox "storageattach $VmName --storagectl SATA --port 1 --device 0 --type dvddrive --medium `"$IsoPath`""

Write-Host "== 6/7 挂载 autounattend 应答文件（虚拟光驱）=="
$aa = Join-Path $PSScriptRoot "autounattend.xml"
if (Test-Path $aa) { VBox "storageattach $VmName --storagectl SATA --port 2 --device 0 --type dvddrive --medium `"$aa`"" } else { Write-Host "[WARN] 未找到 autounattend.xml，Windows 安装将需要手动点击" -ForegroundColor Yellow }

Write-Host "== 7/7 开机顺序：光驱优先 =="
VBox "modifyvm $VmName --boot1 dvd --boot2 disk"

Write-Host ""
Write-Host "[OK] VM 创建完成：$VmName"
Write-Host "启动方式（无头安装，后台跑）："
Write-Host "  & `"$vbm`" startvm $VmName --type headless"
Write-Host "查看进度（VNC 端口 33891）：VirtualBox 管理器双击 VM，或 VNC 客户端连 127.0.0.1:33891"
Write-Host "安装完成（自动装 30-40 分钟）后：用户 FuXuan / 密码 Passw0rd!，自动登录"
