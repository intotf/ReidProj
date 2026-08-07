param(
    [int]$Port = 9000,
    [string]$StreamDir = "G:\Github\Jiulang\ReidProj\ReidFeature\testrun\streams",
    [string]$OutCsv = "G:\Github\Jiulang\ReidProj\ReidFeature\testrun\results.csv",
    [double]$FrameInterval = 0.5,
    [float]$WCloth = 0.30,
    [float]$WHead = 0.30,
    [float]$WBody = 0.30,
    [float]$WGait = 0.10,
    [float]$HighConfidenceThreshold = 0.965
)

$files = Get-ChildItem -LiteralPath $StreamDir -File | Where-Object { $_.Extension -in '.h264', '.hevc' } | Sort-Object Name
$rows = @()
foreach ($f in $files) {
    $name = $f.Name
    $expected = if ($name -like 'D*') { 'stranger' } else { 'member' }
    $url = "http://127.0.0.1:$Port/recognize/group1?frameIntervalSeconds=$FrameInterval&wCloth=$WCloth&wHead=$WHead&wBody=$WBody&wGait=$WGait&highConfidenceThreshold=$HighConfidenceThreshold"
    $body = [System.IO.File]::ReadAllBytes($f.FullName)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $status = 0; $resp = ''
    try {
        $r = Invoke-WebRequest -Uri $url -Method Post -Body $body -ContentType 'application/octet-stream' -UseBasicParsing -TimeoutSec 300
        $status = $r.StatusCode
        $resp = $r.Content
    } catch {
        $status = -1
        $resp = $_.Exception.Message
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    }
    $sw.Stop()
    $json = $null
    if ($resp -and $resp.Trim().StartsWith('{')) { try { $json = $resp | ConvertFrom-Json } catch {} }
    $returnedName = if ($json) { [string]$json.name } else { '' }
    $score = if ($json) { [double]$json.score } else { -1.0 }
    $cloth = if ($json) { [double]$json.clothScore } else { -1.0 }
    $head  = if ($json) { [double]$json.headScore } else { -1.0 }
    $bodyS = if ($json) { [double]$json.bodyScore } else { -1.0 }
    $gait  = if ($json) { [double]$json.gaitScore } else { -1.0 }
    $hit = if ($expected -eq 'stranger') {
        if ($returnedName -eq 'stranger') { 'TN' } else { 'FP' }
    } else {
        if ($returnedName -ne '' -and $returnedName -ne 'stranger') { 'TP' } else { 'FN' }
    }
    $rows += [PSCustomObject]@{
        File = $name; Expected = $expected; Result = $hit; Name = $returnedName;
        Score = $score; Cloth = $cloth; Head = $head; Body = $bodyS; Gait = $gait;
        Http = $status; Ms = [int]$sw.Elapsed.TotalMilliseconds
    }
    "{0,-50} exp={1,-8} {2,-2} | name={3,-14} score={4:N4} cloth={5:N4} head={6:N4} body={7:N4} gait={8:N4} | {9}ms" -f `
        $name, $expected, $hit, $returnedName, $score, $cloth, $head, $bodyS, $gait, [int]$sw.Elapsed.TotalMilliseconds
}
$rows | Export-Csv -LiteralPath $OutCsv -NoTypeInformation -Encoding UTF8
"Saved: $OutCsv"
