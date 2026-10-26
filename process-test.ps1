param([Parameter(Mandatory=$true)][string]$ConfigPath,[string]$ExePath=(Join-Path $PSScriptRoot 'release\V2.01\日记卡片.exe'))
$ErrorActionPreference='Stop'
$taskConfig=Get-Content -LiteralPath $ConfigPath -Raw|ConvertFrom-Json
$taskRoot=[IO.Path]::GetFullPath($taskConfig.VaultPath)
if(-not $taskRoot.StartsWith((Join-Path $PSScriptRoot 'local\tests\'),[StringComparison]::OrdinalIgnoreCase)){throw '进程验证只允许独立测试仓库。'}
$taskProbe=Join-Path $taskConfig.UserDataPath 'probe.json'
$taskResults=[Collections.Generic.List[string]]::new()
function Wait-Probe([scriptblock]$Test){
    for($taskAttempt=0;$taskAttempt -lt 40;$taskAttempt++){
        if(Test-Path -LiteralPath $taskProbe){try{$taskCurrent=Get-Content -LiteralPath $taskProbe -Raw|ConvertFrom-Json;if(&$Test $taskCurrent){return $taskCurrent}}catch{}}
        Start-Sleep -Milliseconds 200
    }
    throw '等待本程序状态报告超时。'
}
function Start-TestCard {
    Start-Process -FilePath $ExePath -ArgumentList @('--config',('"'+$ConfigPath+'"'),'--ui-probe') -PassThru -WindowStyle Hidden
}
function Close-TestCard($Process){
    $taskCommand=Start-Process -FilePath $ExePath -ArgumentList @('--config',('"'+$ConfigPath+'"'),'--request-exit') -PassThru -Wait -WindowStyle Hidden
    if(-not $Process.WaitForExit(8000)){throw '正常退出请求没有完成。'}
    if($Process.ExitCode -ne 0){throw '程序退出代码异常。'}
}
$taskFirst=Start-TestCard
try {
    $taskState=Wait-Probe {param($taskP) $taskP.pid -eq $taskFirst.Id -and $taskP.visible -and $taskP.sections -gt 0}
    $taskResults.Add('交付 EXE 独立进程启动，窗口可见，模板栏目已载入')
    $taskHash=$taskState.documentHash
    $taskBackground=$taskState.background
    $taskBackgroundOpacity=$taskState.backgroundOpacity
    $taskEdgeEnabled=$taskState.edgeAutoHide
    $taskSectionTransparency=$taskState.sectionTransparency
    $taskBackgroundKind=$taskState.backgroundKind
    $taskSecond=Start-TestCard
    if(-not $taskSecond.WaitForExit(8000)){throw '重复启动没有退出新进程。'}
    $null=Wait-Probe {param($taskP) $taskP.pid -eq $taskFirst.Id -and $taskP.ShowRequests -ge 1 -and $taskP.visible}
    $taskResults.Add('重复启动唤回原窗口，新进程退出')
    Close-TestCard $taskFirst
    $taskResults.Add('正常退出后进程结束')
    $taskRestart=Start-TestCard
    try {
        $taskState=Wait-Probe {param($taskP) $taskP.pid -eq $taskRestart.Id -and $taskP.visible}
        if($taskState.documentHash -ne $taskHash){throw '重启后文档原文散列不一致。'}
        if($taskState.theme -ne 'fluent'){throw '主题未恢复。'}
        $taskResults.Add('重新启动后正文散列一致，清透主题恢复')
        if($taskState.background -ne $taskBackground -or $taskState.backgroundOpacity -ne $taskBackgroundOpacity -or $taskState.edgeAutoHide -ne $taskEdgeEnabled){throw '重新启动后的图片背景或贴边偏好不一致。'}
        if($taskBackground -and -not $taskState.hasBackground){throw '重启后本机图片背景未成功载入。'}
        $taskResults.Add('重新启动后本机图片背景、显现度与贴边设置恢复')
        if($taskState.sectionTransparency -ne $taskSectionTransparency -or $taskState.backgroundKind -ne $taskBackgroundKind){throw '重启后栏目透明度或动态背景类型未恢复。'}
        $taskResults.Add('重新启动后栏目透明度与动态背景类型恢复')
        Close-TestCard $taskRestart
    } finally { $taskRestart.Dispose() }
    [IO.File]::WriteAllText((Join-Path $taskConfig.UserDataPath 'process-result.json'),(@{passed=$true;results=$taskResults}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
    $taskResults
} finally { $taskFirst.Dispose() }
