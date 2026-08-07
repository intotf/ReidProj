<#
.SYNOPSIS
    同一人多段注册：把文件夹内多段视频（mp4/h264/hevc）批量注册为同一个成员（各段特征等权融合）。

.DESCRIPTION
    - mp4 会自动用 ffmpeg 无损转成 H264/H265 Annex-B 裸流（服务端仅支持裸流）
    - 通过 multipart 调用 POST /family/enroll-batch/{groupId}/{memberName}
    - 同名成员已存在时，新批次特征会与库内特征融合更新（EMA）
    - 合并去重请使用 scripts/merge_members.ps1 或 POST /family/merge/{groupId}

.EXAMPLE
    .\enroll_member_batch.ps1 -Folder D:\clips\laiguowei -GroupId group1 -MemberName 赖国伟
#>
param(
    [Parameter(Mandatory = $true)][string]$Folder,
    [Parameter(Mandatory = $true)][string]$GroupId,
    [Parameter(Mandatory = $true)][string]$MemberName,
    [int]$Port = 9000,
    [double]$FrameIntervalSeconds = 0.5,
    [string]$Ffmpeg = 'G:\Github\Jiulang\ReidProj\ReidFeature\bin\Debug\net10.0\win-x64\tools\ffmpeg.exe',
    [string]$TempDir = "$env:TEMP\reid_enroll"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Folder)) { throw "文件夹不存在: $Folder" }
if (-not (Test-Path -LiteralPath $Ffmpeg)) { throw "找不到 ffmpeg: $Ffmpeg" }

$Ffprobe = Join-Path (Split-Path $Ffmpeg) 'ffprobe.exe'
$videos = @(Get-ChildItem -LiteralPath $Folder -File | Where-Object { $_.Extension -in '.mp4', '.h264', '.hevc' } | Sort-Object Name)
if ($videos.Count -eq 0) { throw "文件夹内没有 mp4/h264/hevc 视频: $Folder" }

# 1. mp4 -> Annex-B 裸流（无损转封装，视频内容不变）
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null
$streams = @()
foreach ($v in $videos) {
    if ($v.Extension -eq '.mp4') {
        $codec = (& $Ffprobe -v error -select_streams v:0 -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1 $v.FullName 2>$null | Select-Object -Last 1).Trim()
        $ext = if ($codec -eq 'hevc') { 'hevc' } else { 'h264' }
        $out = Join-Path $TempDir ($v.BaseName + ".$ext")
        if ($codec -eq 'hevc') {
            & $Ffmpeg -y -v error -i $v.FullName -c:v copy -bsf:v hevc_mp4toannexb -f hevc $out 2>$null
        } else {
            & $Ffmpeg -y -v error -i $v.FullName -c:v copy -bsf:v h264_mp4toannexb -f h264 $out 2>$null
        }
        if (-not (Test-Path -LiteralPath $out)) { throw "mp4 转裸流失败: $($v.FullName)" }
        $streams += $out
        "已转换: $($v.Name) -> $([System.IO.Path]::GetFileName($out))"
    } else {
        $streams += $v.FullName
    }
}

# 2. multipart 批量注册（curl 兼容 PowerShell 5.1/7）
$encodedName = [System.Uri]::EscapeDataString($MemberName)
$url = "http://127.0.0.1:$Port/family/enroll-batch/$GroupId/$($encodedName)?frameIntervalSeconds=$FrameIntervalSeconds"
$curlArgs = @('-s', '-X', 'POST', $url)
foreach ($s in $streams) { $curlArgs += @('-F', "videos=@$s") }

$resp = & curl.exe @curlArgs 2>$null
if (-not $resp) { throw "请求失败，请确认服务已启动（端口 $Port）" }

$json = $resp | ConvertFrom-Json
""
"注册成功:"
"  成员名 : $($json.name)"
"  成员 ID: $($json.memberId)"
"  融合段数: $($json.segmentCount)"
$json.segments | ForEach-Object { "    - $($_.fileName)  track=$($_.trackId)" }
