# 下载开源 Live2D 动作样本数据
# shizuku (Cubism 2.1, .mtn) + haru (Cubism 4, .motion3.json)

$BASE = "https://raw.githubusercontent.com/guansss/pixi-live2d-display/master/test/assets"
$OUT = "D:\Unity\projects\Desktop_per_pro\scripts\param_mapper\samples"
$ErrorActionPreference = "SilentlyContinue"

# ===== 1. Shizuku (Cubism 2) - .mtn 动作文件 =====
$shizuku_motions = @(
    "flickHead_00.mtn", "flickHead_01.mtn", "flickHead_02.mtn",
    "idle_00.mtn", "idle_01.mtn", "idle_02.mtn",
    "pinchIn_00.mtn", "pinchIn_01.mtn", "pinchIn_02.mtn",
    "pinchOut_00.mtn", "pinchOut_01.mtn", "pinchOut_02.mtn",
    "shake_00.mtn", "shake_01.mtn", "shake_02.mtn",
    "tapBody_00.mtn", "tapBody_01.mtn", "tapBody_02.mtn"
)

$dir_mtn = "$OUT\shizuku_mtn"
if (!(Test-Path $dir_mtn)) { New-Item -ItemType Directory -Path $dir_mtn -Force | Out-Null }

foreach ($f in $shizuku_motions) {
    $url = "$BASE/shizuku/motions/$f"
    $outfile = "$dir_mtn/$f"
    if (!(Test-Path $outfile)) {
        try {
            Invoke-WebRequest -Uri $url -OutFile $outfile -UseBasicParsing
            Write-Host "  ✅ $f" -ForegroundColor Green
        } catch {
            Write-Host "  ❌ $f : $_" -ForegroundColor Red
        }
    } else {
        Write-Host "  ⏭️ $f (已存在)" -ForegroundColor Yellow
    }
}

# ===== 2. Haru (Cubism 4) - .motion3.json 动作文件 =====
$haru_motions = @(
    "haru_g_idle.motion3.json",
    "haru_g_m05.motion3.json",
    "haru_g_m07.motion3.json",
    "haru_g_m14.motion3.json",
    "haru_g_m15.motion3.json"
)

$dir_m3 = "$OUT\haru_motion3"
if (!(Test-Path $dir_m3)) { New-Item -ItemType Directory -Path $dir_m3 -Force | Out-Null }

foreach ($f in $haru_motions) {
    $url = "$BASE/haru/motion/$f"
    $outfile = "$dir_m3/$f"
    if (!(Test-Path $outfile)) {
        try {
            Invoke-WebRequest -Uri $url -OutFile $outfile -UseBasicParsing
            Write-Host "  ✅ $f" -ForegroundColor Green
        } catch {
            Write-Host "  ❌ $f : $_" -ForegroundColor Red
        }
    } else {
        Write-Host "  ⏭️ $f (已存在)" -ForegroundColor Yellow
    }
}

# ===== 3. 模型 JSON 参考文件 =====
$refs = @{
    "shizuku.model.json" = "$BASE/shizuku/shizuku.model.json"
    "haru_greeter_t03.model3.json" = "$BASE/haru/haru_greeter_t03.model3.json"
}
$dir_ref = "$OUT\references"
if (!(Test-Path $dir_ref)) { New-Item -ItemType Directory -Path $dir_ref -Force | Out-Null }

foreach ($kv in $refs.GetEnumerator()) {
    $outfile = "$dir_ref/$($kv.Key)"
    if (!(Test-Path $outfile)) {
        try {
            Invoke-WebRequest -Uri $kv.Value -OutFile $outfile -UseBasicParsing
            Write-Host "  ✅ $($kv.Key)" -ForegroundColor Green
        } catch {
            Write-Host "  ❌ $($kv.Key) : $_" -ForegroundColor Red
        }
    } else {
        Write-Host "  ⏭️ $($kv.Key) (已存在)" -ForegroundColor Yellow
    }
}

Write-Host "`n🎯 下载完成!" -ForegroundColor Cyan
Write-Host "   MTN 文件: $(@($shizuku_motions | Where-Object {Test-Path "$dir_mtn/$_"}).Count) 个" -ForegroundColor Cyan
Write-Host "   Motion3 JSON: $(@($haru_motions | Where-Object {Test-Path "$dir_m3/$_"}).Count) 个" -ForegroundColor Cyan
