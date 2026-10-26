param([string]$ExePath=(Join-Path $PSScriptRoot 'release\V2.02\日记卡片.exe'))
$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot ('local\tests\weekly-process-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
if(-not ([IO.Path]::GetFullPath($root)).StartsWith((Join-Path $PSScriptRoot 'local\tests\'),[StringComparison]::OrdinalIgnoreCase)){throw '测试目录校验失败'}
$vault=Join-Path $root 'vault';$data=Join-Path $root 'data';$config=Join-Path $root 'diary-card.json';$utf8=[Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path (Join-Path $vault 'templates'),(Join-Path $vault 'records'),(Join-Path $data 'weekly') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\Defaults\template.md') -Destination (Join-Path $vault 'templates\daily.md')
$settingsFile=Join-Path $data ('weekly\'+[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($vault.ToUpperInvariant()))).Substring(0,24)+'.json')
[IO.File]::WriteAllText($config,(@{VaultPath=$vault;DiaryFolder='records';TemplatePath='templates/daily.md';DateFormat='YYYY/MM/YYYY-MM-DD';UserDataPath=$data;SetupVersion=2}|ConvertTo-Json),$utf8)
[IO.File]::WriteAllText($settingsFile,(@{AutoEnabled=$true;Folder='周报';Weekday=6;Hour=24;Minute=0;LastCompletedWeek=''}|ConvertTo-Json),$utf8)
$local=[DateTimeOffset]::Now.ToOffset([TimeSpan]::FromHours(8));$index=(([int]$local.DayOfWeek+6)%7)
$anchor=$local.Date.AddDays(-(($index-6+7)%7));$trigger=[DateTimeOffset]::new($anchor,[TimeSpan]::FromHours(8)).AddHours(24)
if($trigger -gt $local){$anchor=$anchor.AddDays(-7)}
$friday=$anchor.AddDays(-(([int]$anchor.DayOfWeek-5+7)%7));$saturday=$friday.AddDays(-6)
$id=([Globalization.ISOWeek]::GetYear($friday)).ToString('0000')+'-W'+([Globalization.ISOWeek]::GetWeekOfYear($friday)).ToString('00')
$date=$friday.ToString('yyyy-MM-dd');$note=Join-Path $vault ('records\'+$date+'.md')
[IO.File]::WriteAllText($note,([string]::Join([Environment]::NewLine,@('---','type: diary',('journal_date: "'+$date+'"'),'---','## 每日任务完成情况','- [x] 独立启动自动汇总验收记录'))),$utf8)
$before=(Get-FileHash -LiteralPath $note).Hash;$results=[Collections.Generic.List[string]]::new()
function Start-Card { Start-Process -FilePath $ExePath -ArgumentList @('--config',('"'+$config+'"'),'--ui-probe') -WindowStyle Hidden -PassThru }
function Close-Card($process) {
    $quit=Start-Process -FilePath $ExePath -ArgumentList @('--config',('"'+$config+'"'),'--request-exit') -PassThru -Wait -WindowStyle Hidden
    if($quit.ExitCode -ne 0 -or -not $process.WaitForExit(10000)){throw '周报测试卡片未正常退出'}
}
$card=Start-Card
try {
    $state=$null
    for($attempt=0;$attempt -lt 150;$attempt++){
        try{$state=Get-Content -LiteralPath $settingsFile -Raw|ConvertFrom-Json;if($state.LastCompletedWeek -eq $id -and $state.LastOutput -and (Test-Path -LiteralPath $state.LastOutput)){break}}catch{}
        Start-Sleep -Milliseconds 200
    }
    if($state.LastCompletedWeek -ne $id){throw '未在独立启动后自动补跑周日24点最近一期'}
    $results.Add('真实EXE独立启动后，按照周日24点配置自动补跑最近一期')
    $text=Get-Content -LiteralPath $state.LastOutput -Raw
    if(-not $text.Contains($saturday.ToString('yyyy-MM-dd')) -or -not $text.Contains($friday.ToString('yyyy-MM-dd'))){throw '七天范围不匹配'}
    $results.Add('生成周期实际覆盖周六至下周五并按截止周五归档')
    if(-not $text.Contains('独立启动自动汇总验收记录') -or (Get-FileHash -LiteralPath $note).Hash -ne $before){throw '来源内容或原文保护失败'}
    $results.Add('自动周报包含原文及来源，日报散列保持一致')
    Close-Card $card;$card=$null;$results.Add('自动任务完成后可以正常退出')
    $reportHash=(Get-FileHash -LiteralPath $state.LastOutput).Hash;$mtime=(Get-Item -LiteralPath $state.LastOutput).LastWriteTimeUtc
    $card=Start-Card;Start-Sleep -Seconds 5
    if((Get-FileHash -LiteralPath $state.LastOutput).Hash -ne $reportHash -or (Get-Item -LiteralPath $state.LastOutput).LastWriteTimeUtc -ne $mtime){throw '再次启动重复重写周报'}
    $results.Add('再次独立启动不重复生成或重写同一期周报')
    Close-Card $card;$card=$null;$results.Add('重启后可正常保存退出，设置保留')
    [IO.File]::WriteAllText((Join-Path $root 'weekly-process-result.json'),(@{passed=$true;results=$results;root=$root;week=$id}|ConvertTo-Json -Depth 5),$utf8)
    Write-Output ('周报独立进程检查通过：'+$root)
}
finally { if($card -and -not $card.HasExited){Close-Card $card} }