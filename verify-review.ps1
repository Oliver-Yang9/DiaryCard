param([Parameter(Mandatory=$true)][string]$ExePath,[string]$TemplatePath=(Join-Path $PSScriptRoot 'src\Defaults\template.md'),[string]$SourceVault='')
$ErrorActionPreference='Stop'
$version=(Get-Item -LiteralPath $ExePath).VersionInfo.ProductVersion
$dual=Test-Path -LiteralPath (Join-Path $PSScriptRoot 'src\StandaloneService.cs')
$tests=Join-Path $PSScriptRoot ('local\tests\acceptance-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$proof=Join-Path $PSScriptRoot ('verification\'+$version)
New-Item -ItemType Directory -Path $tests,$proof -Force|Out-Null
$roots=[ordered]@{Version=$version;Exe=$ExePath;Tests=$tests}
$modes=@('--startup-tests','--startup-ui-smoke','--setup-tests','--setup-ui-smoke','--weekly-tests','--weekly-ui-smoke')
if($dual){$modes=@('--standalone-tests','--library-tests','--mode-ui-smoke','--library-ui-smoke')+$modes}
foreach($mode in $modes){
    $name=$mode.Substring(2);$arguments=@($mode,'--test-root',('"'+(Join-Path $tests $name)+'"'))
    if($SourceVault -and $mode -eq '--weekly-tests'){$arguments+=@('--weekly-source-vault',('"'+$SourceVault+'"'))}
    $run=Start-Process -FilePath $ExePath -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
    if($run.ExitCode -ne 0){throw ('检查失败：'+$mode+'，报告目录：'+$tests)}
    Write-Output ('通过：'+$mode)
}
$log=@(& (Join-Path $PSScriptRoot 'verify.ps1') -ExePath $ExePath -TemplatePath $TemplatePath);$log|Write-Output
$verify=[regex]::Match([string]::Join([Environment]::NewLine,$log),'报告与截图：([^\r\n]+)').Groups[1].Value.Trim()
if(-not $verify){throw '未生成完整回归报告'};$roots.Verify=$verify
if($dual){
    $log=@(& (Join-Path $PSScriptRoot 'mode-process-test.ps1') -ExePath $ExePath);$log|Write-Output
    $modePath=[regex]::Match([string]::Join([Environment]::NewLine,$log),'验收通过：([^\r\n]+)').Groups[1].Value.Trim()
    if(-not $modePath){throw '未生成双模式进程报告'};$roots.ModeProcess=$modePath
}
$log=@(& (Join-Path $PSScriptRoot 'setup-process-test.ps1') -ExePath $ExePath);$log|Write-Output
$setup=[regex]::Match([string]::Join([Environment]::NewLine,$log),'验收通过：([^\r\n]+)').Groups[1].Value.Trim()
if(-not $setup){throw '未生成切库进程报告'};$roots.SetupProcess=$setup
& (Join-Path $PSScriptRoot 'process-test.ps1') -ExePath $ExePath -ConfigPath (Join-Path $verify 'ui\ui-config.json')
$log=@(& (Join-Path $PSScriptRoot 'weekly-process-test.ps1') -ExePath $ExePath);$log|Write-Output
$weekly=[regex]::Match([string]::Join([Environment]::NewLine,$log),'检查通过：([^\r\n]+)').Groups[1].Value.Trim()
if(-not $weekly){throw '未生成周报进程报告'};$roots.WeeklyProcess=$weekly
$animation=Get-Content -LiteralPath (Join-Path $verify 'ui\local\ui-verification\animation-result.json') -Raw|ConvertFrom-Json
if(-not $animation.passed -or $animation.closeDurationMs -ne 400 -or $animation.openDurationMs -ne 360){throw '提速后的动画未通过实测'}
[IO.File]::WriteAllText((Join-Path $proof 'test-roots.json'),($roots|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
Write-Output ('当前审核文件全部通过：'+$version)
$roots|ConvertTo-Json