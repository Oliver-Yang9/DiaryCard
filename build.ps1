param([string]$DotnetPath='', [string]$OutputDirectory=(Join-Path $PSScriptRoot 'build\local-review'))
$ErrorActionPreference='Stop'
if(-not $DotnetPath){
    $taskBundled=Join-Path (Split-Path $PSScriptRoot -Parent) 'local\build\dotnet\dotnet.exe'
    if(Test-Path -LiteralPath $taskBundled){$DotnetPath=$taskBundled}else{$DotnetPath=(Get-Command dotnet -ErrorAction Stop).Source}
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot 'local\cli'
if(-not $env:NUGET_PACKAGES){
    $taskCache=Join-Path (Split-Path $PSScriptRoot -Parent) 'local\build\packages'
    $env:NUGET_PACKAGES=if(Test-Path -LiteralPath $taskCache){$taskCache}else{Join-Path $PSScriptRoot 'local\packages'}
}
& $DotnetPath restore (Join-Path $PSScriptRoot 'src\DailyDiaryCard.csproj') -r win-x64 --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --source 'https://api.nuget.org/v3/index.json' -p:SelfContained=true -p:PublishSingleFile=true
if($LASTEXITCODE -ne 0){throw '还原失败'}
& $DotnetPath publish (Join-Path $PSScriptRoot 'src\DailyDiaryCard.csproj') --no-restore -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $OutputDirectory
if($LASTEXITCODE -ne 0){throw '构建失败'}
Copy-Item -LiteralPath (Join-Path $OutputDirectory 'DailyDiaryCard.exe') -Destination (Join-Path $OutputDirectory '日记卡片.exe') -Force
Remove-Item -LiteralPath (Join-Path $OutputDirectory 'DailyDiaryCard.exe')
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'README.md')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $OutputDirectory '使用说明.md') -Force}
foreach($name in @('LICENSE','CHANGELOG.md','diary-card.example.json')){
    $file=Join-Path $PSScriptRoot $name
    if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination $OutputDirectory -Force}
}
$weeklyGuide=Join-Path $PSScriptRoot 'docs\weekly-report.md'
if(Test-Path -LiteralPath $weeklyGuide){
    New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'docs') -Force|Out-Null
    Copy-Item -LiteralPath $weeklyGuide -Destination (Join-Path $OutputDirectory 'docs\weekly-report.md') -Force
}
Write-Output ('本地审核程序：'+(Join-Path $OutputDirectory '日记卡片.exe'))