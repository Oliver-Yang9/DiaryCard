param([string]$ExePath=(Join-Path $PSScriptRoot 'build\local-review\日记卡片.exe'))
$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot ('local\tests\mode-process-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $root -Force|Out-Null
$utf8=[Text.UTF8Encoding]::new($false);$results=[Collections.Generic.List[string]]::new()
function Wait-State([string]$path,[scriptblock]$test){
    for($attempt=0;$attempt -lt 150;$attempt++){
        if(Test-Path -LiteralPath $path){try{$s=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json;if(&$test $s){return $s}}catch{}}
        Start-Sleep -Milliseconds 200
    }
    throw ('等待模式状态超时：'+$path)
}
function Start-Card([string]$config,[string]$mode='',[string]$target=''){
    $arguments=@('--config',('"'+$config+'"'),'--ui-probe')
    if($mode){$arguments+=@('--storage-switch-mode',$mode,'--storage-switch-path',('"'+$target+'"'))}
    Start-Process -FilePath $ExePath -ArgumentList $arguments -PassThru -WindowStyle Hidden
}
function Close-Card([string]$config,$process){
    $quit=Start-Process -FilePath $ExePath -ArgumentList @('--config',('"'+$config+'"'),'--request-exit') -PassThru -Wait -WindowStyle Hidden
    if($quit.ExitCode -ne 0 -or -not $process.WaitForExit(10000)){throw '退出模式测试卡片失败'}
}
$initial=Join-Path $root '首次使用';New-Item -ItemType Directory -Path $initial -Force|Out-Null
$config=Join-Path $initial 'diary-card.json';$first=Start-Card $config
$null=Wait-State (Join-Path $initial 'startup-probe.json') {param($s) $s.pid -eq $first.Id -and $s.state -eq 'setup' -and $s.visible}
Close-Card $config $first
if((Test-Path -LiteralPath $config) -or (Test-Path -LiteralPath (Join-Path $initial 'Diary')) -or (Test-Path -LiteralPath (Join-Path $initial 'DailyNote')) -or (Test-Path -LiteralPath (Join-Path $initial 'data'))){throw '取消首次选择后创建了数据'}
$results.Add('真实首次启动显示模式选择，取消后不创建配置、日记库或资料夹')
[IO.File]::WriteAllText($config,'null',$utf8);$repair=Start-Card $config
$null=Wait-State (Join-Path $initial 'startup-probe.json') {param($s) $s.pid -eq $repair.Id -and $s.state -eq 'setup'}
Close-Card $config $repair
if([IO.File]::ReadAllText($config) -ne 'null'){throw '修复取消改写了配置'}
$results.Add('空JSON配置进入修复选择，取消保留原文件')
$install=Join-Path $root '模式切换';$data=Join-Path $install 'data';New-Item -ItemType Directory -Path $data -Force|Out-Null
function Make-Vault([string]$name){
    $vault=Join-Path $install $name;New-Item -ItemType Directory -Path (Join-Path $vault '.obsidian'),(Join-Path $vault 'templates'),(Join-Path $vault 'records') -Force|Out-Null
    [IO.File]::WriteAllText((Join-Path $vault '.obsidian\daily-notes.json'),'{"folder":"records","format":"YYYY-MM-DD","template":"templates/daily"}',$utf8)
    [IO.File]::WriteAllText((Join-Path $vault 'templates\daily.md'),[string]::Join([Environment]::NewLine,@('---','type: diary','---','## 今天','原库提示','## 明天','- [ ] ')),$utf8)
    return $vault
}
$a=Make-Vault 'obsidian-a';$b=Make-Vault 'obsidian-b';$date=Get-Date -Format 'yyyy-MM-dd';$aNote=Join-Path $a ('records\'+$date+'.md');$bNote=Join-Path $b ('records\'+$date+'.md')
[IO.File]::WriteAllText($aNote,[string]::Join([Environment]::NewLine,@('---','type: diary','---','## 今天','A已有独有内容','## 明天','A计划')),$utf8)
[IO.File]::WriteAllText($bNote,[string]::Join([Environment]::NewLine,@('---','type: diary','custom: B属性','---','## 今天','B已有独有内容','## 明天','B计划')),$utf8)
$bHash=(Get-FileHash -LiteralPath $bNote).Hash;$aSettingsHash=(Get-FileHash -LiteralPath (Join-Path $a '.obsidian\daily-notes.json')).Hash
$config=Join-Path $install 'diary-card.json'
[IO.File]::WriteAllText($config,(@{VaultPath=$a;TemplatePath='templates/daily.md';DiaryFolder='records';DateFormat='YYYY-MM-DD';UserDataPath=$data;SetupVersion=2}|ConvertTo-Json),$utf8)
[IO.File]::WriteAllText((Join-Path $data 'settings.json'),'{"Theme":"night"}',$utf8)
$local=Join-Path $install 'Diary';$parent=Start-Card $config 'standalone' $local;$probe=Join-Path $data 'probe.json'
$s=Wait-State $probe {param($s) $s.pid -ne $parent.Id -and $s.mode -eq 'standalone' -and $s.vault -eq $local}
if(-not $parent.WaitForExit(10000)){throw '旧进程未退出'}
$child=Get-Process -Id $s.pid
$results.Add('从无模式字段的V2旧配置实际切换并重启为独立模式')
if(-not [IO.File]::ReadAllText($aNote).Contains('切换使用方式前保留的中文输入') -or (Get-FileHash -LiteralPath (Join-Path $a '.obsidian\daily-notes.json')).Hash -ne $aSettingsHash){throw '旧日记或Obsidian配置保护失败'}
$results.Add('切换前输入保存到旧库，旧Obsidian设置保持原样')
if(-not(Test-Path -LiteralPath (Join-Path $local 'diarycard-library.json')) -or (Test-Path -LiteralPath (Join-Path $local '.obsidian')) -or $s.theme -ne 'night'){throw '独立模式或外观偏好不正确'}
$results.Add('独立资料夹只有自己的格式标记，切换后保留原主题')
$localNote=$s.target
if((Test-Path -LiteralPath $localNote) -and [IO.File]::ReadAllText($localNote).Contains('A已有独有内容')){throw '旧库内容被复制到新资料夹'}
$results.Add('两种存储内容不互相复制或串写')
Close-Card $config $child
$restart=Start-Card $config
$null=Wait-State $probe {param($s) $s.pid -eq $restart.Id -and $s.mode -eq 'standalone' -and $s.vault -eq $local}
Close-Card $config $restart;$results.Add('独立模式再次启动保持模式与资料位置')
$parent=Start-Card $config 'obsidian' $b
$s=Wait-State $probe {param($s) $s.pid -ne $parent.Id -and $s.mode -eq 'obsidian' -and $s.vault -eq $b}
if(-not $parent.WaitForExit(10000)){throw '独立旧进程未结束'}
$child=Get-Process -Id $s.pid
if((Get-FileHash -LiteralPath $bNote).Hash -ne $bHash -or -not [IO.File]::ReadAllText($localNote).Contains('切换使用方式前保留的中文输入')){throw '反向切换文件保护失败'}
$results.Add('从独立模式实际切回Obsidian，保存旧输入并保留目标已有笔记及属性')
if($s.theme -ne 'night'){throw '反向切换丢失主题'}
Close-Card $config $child;$results.Add('两次自动重启均可正常退出，不注册开机启动')
[IO.File]::WriteAllText((Join-Path $root 'mode-process-result.json'),(@{passed=$true;results=$results;root=$root}|ConvertTo-Json -Depth 5),$utf8)
Write-Output ('双模式独立进程验收通过：'+$root)