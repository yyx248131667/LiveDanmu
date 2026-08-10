# 发布清理脚本 - 在 dotnet publish 后运行
# 用法: .\publish-clean.ps1

$pubDir = Join-Path $PSScriptRoot "publish-standalone"

if (-not (Test-Path $pubDir)) {
    $pubDir = Join-Path (Get-Location) "publish-standalone"
}

if (-not (Test-Path $pubDir)) {
    Write-Host "[ERROR] publish-standalone directory not found!" -ForegroundColor Red
    exit 1
}

Write-Host "[Clean] $pubDir"

# 删除运行时文件
@("WebView2Data","LiveDanmuDesktop.exe.WebView2","logs","cookie_config.yaml","live_config.json","LiveDanmuDesktop.pdb") | ForEach-Object {
    $p = Join-Path $pubDir $_
    if (Test-Path $p) {
        Remove-Item $p -Recurse -Force
        Write-Host "  [DEL] $_" -ForegroundColor Yellow
    }
}

# Cookie 检查
$found = $false
Get-ChildItem $pubDir -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*cookie*" } | ForEach-Object {
    Write-Host "  [WARN] Cookie: $($_.Name)" -ForegroundColor Red
    $found = $true
}
if (-not $found) { Write-Host "  [OK] No cookies" -ForegroundColor Green }

# 大小统计
$size = 0
Get-ChildItem $pubDir -Recurse -ErrorAction SilentlyContinue | Where-Object { -not $_.PSIsContainer } | ForEach-Object { $size += $_.Length }
Write-Host "[Done] $([math]::Round($size/1MB,1)) MB" -ForegroundColor Cyan
