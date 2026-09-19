$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$appDir = Join-Path $projectRoot 'dist/app'
$distDir = Join-Path $projectRoot 'dist'

dotnet publish (Join-Path $projectRoot 'RapidPoint.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:DebugSymbols=false "-p:PathMap=$projectRoot=/_/RapidPoint" -o $appDir
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $appDir 'RapidPoint.exe'
foreach ($test in @('--smoke-test', '--capture-flow-test')) {
    $process = Start-Process -FilePath $exe -ArgumentList $test -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Test failed: $test (exit $($process.ExitCode))" }
}

$documents = @('README.md', 'USER_GUIDE.md', 'LICENSE', 'DOTNET-LICENSE.txt',
    'DOTNET-THIRD-PARTY-NOTICES.txt', 'WINDOWSDESKTOP-LICENSE.txt', 'macro-settings.png', 'pause-settings.png')
foreach ($document in $documents) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $appDir -Force
}
$zip = Join-Path $distDir 'RapidPoint-Windows-x64.zip'
$files = @($exe) + @($documents | ForEach-Object { Join-Path $appDir $_ })
Compress-Archive -LiteralPath $files -DestinationPath $zip -Force
Get-FileHash -LiteralPath $zip -Algorithm SHA256
Write-Host "Ready: $zip"
