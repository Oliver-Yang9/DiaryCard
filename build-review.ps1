param([string]$DotnetPath='', [string]$OutputDirectory=(Join-Path $PSScriptRoot 'review\local-preview'), [string]$ReuseReviewDirectory='', [string]$LiveTemplatePath='')
$ErrorActionPreference='Stop'
$taskProject=[IO.Path]::GetFullPath($PSScriptRoot)
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskRelease=Join-Path $taskProject 'release'
if($taskOutput -eq $taskRelease -or $taskOutput.StartsWith($taskRelease+'\',[StringComparison]::OrdinalIgnoreCase)){throw '审核构建不能输出到正式 release 目录。'}
if(-not $DotnetPath){
  $taskBundled=Join-Path $taskProject 'local\build\dotnet\dotnet.exe'
  if(Test-Path -LiteralPath $taskBundled){$DotnetPath=$taskBundled}else{$DotnetPath=(Get-Command dotnet -ErrorAction Stop).Source}
}
$env:DOTNET_CLI_HOME=Join-Path $taskProject 'local\build\cli'
$env:NUGET_PACKAGES=Join-Path $taskProject 'local\build\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$taskApp=Join-Path $taskOutput 'app'
$taskVault=Join-Path $taskOutput '审核仓库'
$taskData=Join-Path $taskOutput '审核数据'
if($ReuseReviewDirectory){
  $taskReuse=[IO.Path]::GetFullPath($ReuseReviewDirectory)
  if(-not $taskReuse.StartsWith((Join-Path $taskProject 'review')+'\',[StringComparison]::OrdinalIgnoreCase)){throw '只能沿用本项目 review 目录下的审核数据。'}
  $taskVault=Join-Path $taskReuse '审核仓库'
  $taskData=Join-Path $taskReuse '审核数据'
  if(-not(Test-Path -LiteralPath $taskVault -PathType Container) -or -not(Test-Path -LiteralPath $taskData -PathType Container)){throw '原审核仓库或数据目录不存在。'}
}
New-Item -ItemType Directory -Path $taskApp,(Join-Path $taskVault '.obsidian'),(Join-Path $taskVault '模板'),$taskData -Force | Out-Null
& $DotnetPath restore (Join-Path $taskProject 'src\DailyDiaryCard.csproj') -r win-x64 --configfile (Join-Path $taskProject 'NuGet.Config') --source 'https://api.nuget.org/v3/index.json' -p:SelfContained=true -p:PublishSingleFile=true
if($LASTEXITCODE -ne 0){throw '审核构建还原失败。'}
& $DotnetPath publish (Join-Path $taskProject 'src\DailyDiaryCard.csproj') --no-restore -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $taskApp
if($LASTEXITCODE -ne 0){throw '审核构建失败。'}
Copy-Item -LiteralPath (Join-Path $taskApp 'DailyDiaryCard.exe') -Destination (Join-Path $taskApp '日记卡片-审核版.exe') -Force
Remove-Item -LiteralPath (Join-Path $taskApp 'DailyDiaryCard.exe')
$taskUtf8=[Text.UTF8Encoding]::new($false)
$taskTemplate=Join-Path $taskVault '模板\复盘.md'
if(-not $ReuseReviewDirectory -and -not(Test-Path -LiteralPath $taskTemplate)){
  $taskOriginal=Join-Path $taskProject 'review-assets\复盘模板.md'
  if(-not(Test-Path -LiteralPath $taskOriginal)){$taskOriginal=Join-Path $taskProject 'review-assets\复盘模板.md'}
  Copy-Item -LiteralPath $taskOriginal -Destination $taskTemplate
}
if(-not $ReuseReviewDirectory -and -not(Test-Path -LiteralPath (Join-Path $taskVault '.obsidian\daily-notes.json'))){[IO.File]::WriteAllText((Join-Path $taskVault '.obsidian\daily-notes.json'),'{"folder":"日记","format":"YYYY/MM/YYYY-MM-DD","template":"模板/复盘"}',$taskUtf8)}
$taskConfig=if($ReuseReviewDirectory){@{VaultPath=$taskVault;UserDataPath=$taskData}}else{@{VaultPath='..\审核仓库';UserDataPath='..\审核数据'}}
if($LiveTemplatePath){
  $taskLive=[IO.Path]::GetFullPath($LiveTemplatePath)
  if(-not(Test-Path -LiteralPath $taskLive -PathType Leaf)){throw '真实模板文件不存在。'}
  $taskConfig['TemplatePath']=$taskLive
  $taskSha=[Security.Cryptography.SHA256]::Create()
  try{$taskKey=([BitConverter]::ToString($taskSha.ComputeHash([Text.Encoding]::UTF8.GetBytes($taskLive.ToUpperInvariant())))).Replace('-','').Substring(0,24)}finally{$taskSha.Dispose()}
  $taskTemplateCache=Join-Path $taskData ('templates\'+$taskKey+'.md')
  if(-not(Test-Path -LiteralPath $taskTemplateCache) -and (Test-Path -LiteralPath $taskTemplate)){
    New-Item -ItemType Directory -Path (Split-Path $taskTemplateCache -Parent) -Force|Out-Null
    Copy-Item -LiteralPath $taskTemplate -Destination $taskTemplateCache
  }
}
[IO.File]::WriteAllText((Join-Path $taskApp 'diary-card.json'),($taskConfig|ConvertTo-Json),$taskUtf8)
$taskToday=[DateTime]::Now
$taskRelative='日记\'+$taskToday.ToString('yyyy')+'\'+$taskToday.ToString('MM')+'\'+$taskToday.ToString('yyyy-MM-dd')+'.md'
$taskNote=Join-Path $taskVault $taskRelative
if(-not $ReuseReviewDirectory -and -not(Test-Path -LiteralPath $taskNote)){
  New-Item -ItemType Directory -Path (Split-Path $taskNote -Parent) -Force | Out-Null
  $taskText=[IO.File]::ReadAllText($taskTemplate).Replace('{{date:YYYY-MM-DD}}',$taskToday.ToString('yyyy-MM-dd'))
  $taskText=$taskText.Replace('<!-- TASKS:START -->',"<!-- TASKS:START -->`r`n- [x] 试用本地图片背景`r`n- [ ] 把卡片拖到屏幕边缘`r`n- [ ] 鼠标移入边缘，展开继续记录")
  [IO.File]::WriteAllText($taskNote,$taskText,$taskUtf8)
}
$taskImage=Join-Path $taskProject 'review-assets\sample-background.png'
if(-not $ReuseReviewDirectory -and (Test-Path -LiteralPath $taskImage) -and -not(Test-Path -LiteralPath (Join-Path $taskData 'settings.json'))){
  $taskName=(Get-FileHash -LiteralPath $taskImage -Algorithm SHA256).Hash.ToLowerInvariant()+'.png'
  New-Item -ItemType Directory -Path (Join-Path $taskData 'backgrounds') -Force|Out-Null
  Copy-Item -LiteralPath $taskImage -Destination (Join-Path $taskData ('backgrounds\'+$taskName))
  [IO.File]::WriteAllText((Join-Path $taskData 'settings.json'),(@{Theme='fluent';Width=520;Height=760;BackgroundImage=$taskName;BackgroundOpacity=0.8;EdgeAutoHide=$true}|ConvertTo-Json),$taskUtf8)
}
Copy-Item -LiteralPath (Join-Path $taskProject 'REVIEW.md') -Destination (Join-Path $taskOutput '审核说明.md') -Force
if(Test-Path -LiteralPath (Join-Path $taskProject 'release\licenses')){Copy-Item -LiteralPath (Join-Path $taskProject 'release\licenses') -Destination $taskApp -Recurse -Force}
$taskHash=(Get-FileHash -LiteralPath (Join-Path $taskApp '日记卡片-审核版.exe') -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $taskOutput 'SHA256SUMS.txt'),($taskHash+'  app/日记卡片-审核版.exe'+[Environment]::NewLine),$taskUtf8)
Write-Output ('审核候选已生成，正式 release 未更新：'+(Join-Path $taskApp '日记卡片-审核版.exe'))
