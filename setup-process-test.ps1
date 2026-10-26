param([string]$ExePath=(Join-Path $PSScriptRoot 'release\V2.02\日记卡片.exe'))
$ErrorActionPreference='Stop'
$taskRoot=Join-Path $PSScriptRoot ('local\tests\switch-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskRoot -Force|Out-Null
$taskResults=[Collections.Generic.List[string]]::new()
$taskUtf8=[Text.UTF8Encoding]::new($false)
function Wait-FileState([string]$Path,[scriptblock]$Test){
    for($taskAttempt=0;$taskAttempt -lt 150;$taskAttempt++){
        if(Test-Path -LiteralPath $Path){try{$taskState=Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json;if(&$Test $taskState){return $taskState}}catch{}}
        Start-Sleep -Milliseconds 200
    }
    throw ('等待程序自身状态超时：'+$Path)
}
function Start-Card([string]$Config,[string]$Switch=''){
    $taskArgs=@('--config',('"'+$Config+'"'),'--ui-probe')
    if($Switch){$taskArgs+=@('--settings-switch-test',('"'+$Switch+'"'))}
    Start-Process -FilePath $ExePath -ArgumentList $taskArgs -PassThru -WindowStyle Hidden
}
function Close-Card([string]$Config){
    $taskQuit=Start-Process -FilePath $ExePath -ArgumentList @('--config',('"'+$Config+'"'),'--request-exit') -WindowStyle Hidden -PassThru -Wait
    if($taskQuit.ExitCode -ne 0){throw '退出请求失败'}
}
$taskInitial=Join-Path $taskRoot '首次启动 中文目录';New-Item -ItemType Directory -Path $taskInitial -Force|Out-Null
$taskConfig=Join-Path $taskInitial 'diary-card.json';$taskFirst=Start-Card $taskConfig
$null=Wait-FileState (Join-Path $taskInitial 'startup-probe.json') {param($s) $s.pid -eq $taskFirst.Id -and $s.state -eq 'setup' -and $s.visible}
$taskResults.Add('无配置的独立进程首次显示设置向导')
Close-Card $taskConfig
if(-not $taskFirst.WaitForExit(10000) -or $taskFirst.ExitCode -ne 0){throw '取消向导未能正常退出'}
if((Test-Path -LiteralPath $taskConfig) -or (Test-Path -LiteralPath (Join-Path $taskInitial 'DailyNote'))){throw '取消后仍创建了仓库或配置'}
$taskResults.Add('取消首次向导后没有创建DailyNote或配置')
[IO.File]::WriteAllText($taskConfig,'{ invalid',$taskUtf8);$taskBadHash=(Get-FileHash -LiteralPath $taskConfig).Hash;$taskRepair=Start-Card $taskConfig
$null=Wait-FileState (Join-Path $taskInitial 'startup-probe.json') {param($s) $s.pid -eq $taskRepair.Id -and $s.state -eq 'setup'}
Close-Card $taskConfig
if(-not $taskRepair.WaitForExit(10000) -or (Get-FileHash -LiteralPath $taskConfig).Hash -ne $taskBadHash){throw '配置修复取消未保留原文件'}
$taskResults.Add('无效配置可重新进入向导，取消后原配置不变')
$taskInstall=Join-Path $taskRoot '自动切库 中文目录';New-Item -ItemType Directory -Path $taskInstall -Force|Out-Null
function Make-Vault([string]$Name){
    $taskVault=Join-Path $taskInstall $Name
    New-Item -ItemType Directory -Path (Join-Path $taskVault '.obsidian'),(Join-Path $taskVault 'templates'),(Join-Path $taskVault 'records') -Force|Out-Null
    [IO.File]::WriteAllText((Join-Path $taskVault '.obsidian\daily-notes.json'),'{"folder":"records","format":"YYYY-MM-DD","template":"templates/custom"}',$taskUtf8)
    [IO.File]::WriteAllText((Join-Path $taskVault 'templates\custom.md'),"## 今天`n提示`n## 明天`n- [ ] 下一步`n",$taskUtf8)
    return $taskVault
}
$taskA=Make-Vault 'vault-a';$taskB=Make-Vault 'vault-b';$taskToday=(Get-Date -Format 'yyyy-MM-dd')+'.md';$taskANote=Join-Path $taskA ('records\'+$taskToday);$taskBNote=Join-Path $taskB ('records\'+$taskToday)
[IO.File]::WriteAllText($taskANote,"## 今天`nA已有正文`n## 明天`nA计划`n",$taskUtf8)
[IO.File]::WriteAllText($taskBNote,"---`ncustom: B已有属性`n---`n## 今天`nB已有正文`n## 明天`n- [x] B已完成`n",$taskUtf8)
$taskBBefore=(Get-FileHash -LiteralPath $taskBNote).Hash
$taskData=Join-Path $taskInstall 'data';$taskConfig=Join-Path $taskInstall 'diary-card.json'
[IO.File]::WriteAllText($taskConfig,(@{VaultPath=$taskA;TemplatePath='templates/custom.md';DiaryFolder='records';DateFormat='YYYY-MM-DD';UserDataPath=$taskData;SetupVersion=2}|ConvertTo-Json),$taskUtf8)
$taskParent=Start-Card $taskConfig $taskB
$taskProbe=Join-Path $taskData 'probe.json'
$taskChildState=Wait-FileState $taskProbe {param($s) $s.pid -ne $taskParent.Id -and $s.vault -eq $taskB -and $s.visible}
if(-not $taskParent.WaitForExit(10000) -or $taskParent.ExitCode -ne 0){throw '旧进程未正常结束'}
$taskResults.Add('设置保存后旧进程结束，实际自动启动新进程并显示目标库')
if(-not([IO.File]::ReadAllText($taskANote).Contains('进程切库前必须保存的中文'))){throw '旧输入未保存'}
$taskResults.Add('真实切库前的未保存中文已写入旧库')
if((Get-FileHash -LiteralPath $taskBNote).Hash -ne $taskBBefore){throw '目标库已有笔记被改动'}
$taskResults.Add('自动重启后目标已有笔记逐字保留，旧内容未复制过去')
$taskSaved=Get-Content -LiteralPath $taskConfig -Raw|ConvertFrom-Json
$taskResolved=[IO.Path]::GetFullPath($taskSaved.VaultPath,$taskInstall)
if($taskResolved -ne $taskB){throw '新仓库地址未持久保存'}
$taskResults.Add('新仓库地址已经写入程序配置')
$taskChild=Get-Process -Id $taskChildState.pid;Close-Card $taskConfig
if(-not $taskChild.WaitForExit(10000)){throw '新进程未能正常退出'}
$taskRestart=Start-Card $taskConfig
$null=Wait-FileState $taskProbe {param($s) $s.pid -eq $taskRestart.Id -and $s.vault -eq $taskB -and $s.sections -eq 2}
Close-Card $taskConfig
if(-not $taskRestart.WaitForExit(10000)){throw '重启进程退出失败'}
$taskResults.Add('再次独立启动继续使用新库和目标模板')
[IO.File]::WriteAllText((Join-Path $taskRoot 'setup-process-result.json'),(@{passed=$true;results=$taskResults;root=$taskRoot}|ConvertTo-Json -Depth 5),$taskUtf8)
Write-Output ('首次向导／切库进程验收通过：'+$taskRoot)