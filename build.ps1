param([string]$DotnetPath,[string]$OutputDirectory=(Join-Path $PSScriptRoot 'release'))
$ErrorActionPreference = 'Stop'
$taskProject = $PSScriptRoot
if (-not $DotnetPath) {
    $taskBundled = Join-Path $taskProject 'local\build\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $taskBundled) { $DotnetPath = $taskBundled }
    else { $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
}
$env:DOTNET_CLI_HOME=Join-Path $taskProject 'local\build\cli'
$env:NUGET_PACKAGES=Join-Path $taskProject 'local\build\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$taskSdk = & $DotnetPath --list-sdks
if (-not ($taskSdk -match '^10\.')) { throw '需要 .NET 10 SDK；只有桌面运行时不能构建。请传入 -DotnetPath 或安装官方 SDK。' }
$taskCsproj = Join-Path $taskProject 'src\DailyDiaryCard.csproj'
& $DotnetPath restore $taskCsproj -r win-x64 --configfile (Join-Path $taskProject 'NuGet.Config') --source 'https://api.nuget.org/v3/index.json' -p:SelfContained=true -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw '还原运行时包失败。' }
$taskRelease = [IO.Path]::GetFullPath($OutputDirectory)
& $DotnetPath publish $taskCsproj --no-restore -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $taskRelease
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
Copy-Item -LiteralPath (Join-Path $taskRelease 'DailyDiaryCard.exe') -Destination (Join-Path $taskRelease '日记卡片.exe') -Force
Remove-Item -LiteralPath (Join-Path $taskRelease 'DailyDiaryCard.exe')
if (-not (Test-Path -LiteralPath (Join-Path $taskRelease 'diary-card.json'))) { Copy-Item -LiteralPath (Join-Path $taskProject 'diary-card.example.json') -Destination (Join-Path $taskRelease 'diary-card.json') }
Copy-Item -LiteralPath (Join-Path $taskProject 'README.md') -Destination (Join-Path $taskRelease '使用说明.md') -Force
if (Test-Path -LiteralPath (Join-Path $taskProject '验收结果.md')) { Copy-Item -LiteralPath (Join-Path $taskProject '验收结果.md') -Destination $taskRelease -Force }
if (Test-Path -LiteralPath (Join-Path $taskProject 'verification')) { Copy-Item -LiteralPath (Join-Path $taskProject 'verification') -Destination $taskRelease -Recurse -Force }
$taskRuntime = Get-Content -LiteralPath (Join-Path $taskProject 'src\bin\Release\net10.0-windows\win-x64\DailyDiaryCard.runtimeconfig.json') -Raw | ConvertFrom-Json
$taskPackageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
$taskLicenses = Join-Path $taskRelease 'licenses'
New-Item -ItemType Directory -Path $taskLicenses -Force | Out-Null
foreach ($taskFramework in $taskRuntime.runtimeOptions.includedFrameworks) {
    $taskPackage = Join-Path $taskPackageRoot ($taskFramework.name.ToLowerInvariant()+'.runtime.win-x64\'+$taskFramework.version)
    foreach ($taskLicenseName in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        $taskLicenseFile = Join-Path $taskPackage $taskLicenseName
        if (Test-Path -LiteralPath $taskLicenseFile) { Copy-Item -LiteralPath $taskLicenseFile -Destination (Join-Path $taskLicenses ($taskFramework.name+'-'+$taskLicenseName)) -Force }
    }
}
$taskExeHash=(Get-FileHash -LiteralPath (Join-Path $taskRelease '日记卡片.exe') -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $taskRelease 'SHA256SUMS.txt'),($taskExeHash+'  日记卡片.exe'+[Environment]::NewLine),[Text.UTF8Encoding]::new($false))
Write-Output ('可双击运行：' + (Join-Path $taskRelease '日记卡片.exe'))
