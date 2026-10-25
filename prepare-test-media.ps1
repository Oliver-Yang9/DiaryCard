$ErrorActionPreference='Stop'
$taskDestination=Join-Path $PSScriptRoot 'test-assets\flower.mp4'
if(Test-Path -LiteralPath $taskDestination){Write-Output 'MP4 测试素材已存在。';return}
New-Item -ItemType Directory -Path (Split-Path $taskDestination -Parent) -Force|Out-Null
Invoke-WebRequest -Uri 'https://developer.mozilla.org/shared-assets/videos/flower.mp4' -OutFile $taskDestination -UseBasicParsing
Write-Output 'MDN MP4 示例已下载，仅用于本机播放验收，已由 .gitignore 排除。'