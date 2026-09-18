# 上传微信小游戏 CDN 目录到腾讯云 COS，并按后缀自动设置 Content-Type。
# 无后缀的 AssetBundle / 首包二进制一律 application/octet-stream，不必在控制台手改。
#
# 依赖：先安装 coscli
#   https://cloud.tencent.com/document/product/436/63144
# 并执行一次：coscli config  （填 SecretId/SecretKey/桶地域）
#
# 用法（在仓库根目录）:
#   powershell -File tools/UploadWxGameCdn.ps1
#   powershell -File tools/UploadWxGameCdn.ps1 -LocalDir "wxoutput/webgl" -CosPrefix "WxGame"
#
# 桶名与 CDN 对应关系（按你当前 MiniGameConfig）:
#   https://cardwxgame-1319599505.cos.ap-shanghai.myqcloud.com/WxGame
#   → Bucket: cardwxgame-1319599505   Region: ap-shanghai   Prefix: WxGame

param(
    [string]$LocalDir = "wxoutput/webgl",
    [string]$Bucket = "cardwxgame-1319599505",
    [string]$Region = "ap-shanghai",
    [string]$CosPrefix = "WxGame",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $root $LocalDir))) {
    # 允许从仓库根或 Card 外层调用
    $alt = Join-Path (Get-Location) $LocalDir
    if (Test-Path -LiteralPath $alt) {
        $localFull = (Resolve-Path $alt).Path
    } else {
        throw "本地目录不存在: $LocalDir"
    }
} else {
    $localFull = (Resolve-Path (Join-Path $root $LocalDir)).Path
}

function Get-ContentType([string]$filePath) {
    $name = [IO.Path]::GetFileName($filePath)
    $ext = [IO.Path]::GetExtension($filePath).ToLowerInvariant()

    switch ($ext) {
        ".txt"   { return "text/plain; charset=utf-8" }
        ".json"  { return "application/json; charset=utf-8" }
        ".html"  { return "text/html; charset=utf-8" }
        ".js"    { return "application/javascript; charset=utf-8" }
        ".css"   { return "text/css; charset=utf-8" }
        ".wasm"  { return "application/wasm" }
        ".data"  { return "application/octet-stream" }
        ".unityweb" { return "application/octet-stream" }
        ".bin"   { return "application/octet-stream" }
        ".bundle"{ return "application/octet-stream" }
        ".manifest" { return "text/plain; charset=utf-8" }
        ".png"   { return "image/png" }
        ".jpg"   { return "image/jpeg" }
        ".jpeg"  { return "image/jpeg" }
        ".webp"  { return "image/webp" }
        ".mp3"   { return "audio/mpeg" }
        ".wav"   { return "audio/wav" }
        ".br"    { return "application/octet-stream" }
        default {
            # 无后缀 AB（game/ui/textures…）或未知后缀 → 二进制
            if ([string]::IsNullOrEmpty($ext)) {
                return "application/octet-stream"
            }
            return "application/octet-stream"
        }
    }
}

$coscli = Get-Command coscli -ErrorAction SilentlyContinue
if (-not $coscli) {
    Write-Host @"
未找到 coscli。请二选一：

1) 安装 coscli 后重跑本脚本（推荐，自动带 Content-Type）
   https://cloud.tencent.com/document/product/436/63144

2) 用 COSBrowser 设一次即可（不用每次改）：
   传输设置 → Content-Type → 无扩展名文件使用 application/octet-stream
   然后照常拖拽上传 wxoutput/webgl 到桶路径 /$CosPrefix/

说明：首包 atob 二次启动问题已用「首包走 data-package 分包」处理（见 game.json）。
注意：AB 仍走 CDN 自动缓存（__GAME_FILE_CACHE），开发者工具二次启动读缓存仍可能
触发 coverRes/atob 报错——这是工具模拟器 base64 桥接的问题，真机不受影响，
遇到后在开发者工具「清缓存 → 全部清除」即可恢复。本脚本只是省事、类型更干净。
"@
    exit 1
}

$bucketUri = "cos://$Bucket-$Region/$CosPrefix"
Write-Host "Local : $localFull"
Write-Host "Remote: $bucketUri/"
Write-Host ""

# coscli sync 对单文件 Content-Type 支持因版本而异；改为逐文件 cp 并带 meta。
$files = Get-ChildItem -LiteralPath $localFull -Recurse -File
$i = 0
foreach ($f in $files) {
    $i++
    $rel = $f.FullName.Substring($localFull.Length).TrimStart('\', '/')
    $relUnix = $rel.Replace('\', '/')
    $ct = Get-ContentType $f.FullName
    $dest = "$bucketUri/$relUnix"

    Write-Host "[$i/$($files.Count)] $relUnix  ($ct)"
    if ($DryRun) {
        continue
    }

    # -H 设置对象元数据 Content-Type（coscli v1）
    & coscli cp $f.FullName $dest -H "Content-Type: $ct"
    if ($LASTEXITCODE -ne 0) {
        # 旧版 coscli 可能不支持 -H，回退普通上传
        Write-Warning "带 Content-Type 上传失败，回退普通 cp: $relUnix"
        & coscli cp $f.FullName $dest
        if ($LASTEXITCODE -ne 0) {
            throw "上传失败: $relUnix"
        }
    }
}

Write-Host ""
Write-Host "完成。CDN 根路径应类似:"
Write-Host "  https://$Bucket.cos.$Region.myqcloud.com/$CosPrefix/"
Write-Host "请确认 MiniGameConfig.CDN / DATA_CDN 与此一致。"
