param([string]$ExePath = (Join-Path $PSScriptRoot 'release\日记卡片.exe'), [string]$TemplatePath = (Join-Path $PSScriptRoot 'review-assets\复盘模板.md'), [string]$TaskFixturePath = '')
$ErrorActionPreference='Stop'
if(-not(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'test-assets\flower.mp4'))){throw 'MP4 验收素材未准备，请先运行 .\prepare-test-media.ps1，或按 test-assets/README.md 手动准备。'}
$taskRoot=Join-Path $PSScriptRoot ('local\tests\verify-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
$taskCore=Join-Path $taskRoot 'core'
$taskRun=Start-Process -FilePath $ExePath -ArgumentList @('--self-test','--test-root',('"'+$taskCore+'"'),'--template',('"'+$TemplatePath+'"')) -WindowStyle Hidden -PassThru -Wait
if($taskRun.ExitCode -ne 0){throw ('文件测试失败，详见 '+(Join-Path $taskCore 'verification.json'))}
$taskUi=Join-Path $taskRoot 'ui'
New-Item -ItemType Directory -Path (Join-Path $taskUi '.obsidian'),(Join-Path $taskUi '模板') -Force|Out-Null
Copy-Item -LiteralPath $TemplatePath -Destination (Join-Path $taskUi '模板\复盘.md')
[IO.File]::WriteAllText((Join-Path $taskUi '.obsidian\daily-notes.json'),'{"folder":"日记","format":"YYYY/MM/YYYY-MM-DD","template":"模板/复盘"}',[Text.UTF8Encoding]::new($false))
$taskConfig=Join-Path $taskUi 'ui-config.json'
[IO.File]::WriteAllText($taskConfig,(@{VaultPath=$taskUi;UserDataPath=(Join-Path $taskUi 'local')}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$taskArgs=@('--config',('"'+$taskConfig+'"'),'--ui-smoke','--media-fixtures',('"'+(Join-Path $PSScriptRoot 'test-assets')+'"'))
if($TaskFixturePath){$taskArgs+=@('--task-fixture',('"'+$TaskFixturePath+'"'))}
$taskRun=Start-Process -FilePath $ExePath -ArgumentList $taskArgs -WindowStyle Hidden -PassThru -Wait
$taskUiResult=Join-Path $taskUi 'local\ui-verification\ui-result.json'
if(-not(Test-Path -LiteralPath $taskUiResult)){throw ('没有生成界面验证报告，请检查 '+$taskUi)}
$taskReport=Get-Content -LiteralPath $taskUiResult -Raw|ConvertFrom-Json
if(-not $taskReport.passed){throw ('界面测试失败，详见 '+$taskUiResult)}
Write-Output ('全部通过。报告与截图：'+$taskRoot)
