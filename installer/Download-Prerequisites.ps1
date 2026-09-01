$ErrorActionPreference = 'Stop'

$target = Join-Path $PSScriptRoot 'prerequisites'
New-Item -ItemType Directory -Force -Path $target | Out-Null

$files = @(
    @{ Name = 'windowsdesktop-runtime-8.0.30-win-x64.exe'; Url = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.30/windowsdesktop-runtime-8.0.30-win-x64.exe' },
    @{ Name = 'WindowsAppRuntimeInstall-x64-2.3.1.exe'; Url = 'https://download.microsoft.com/download/cbb0f858-7923-48f0-bc2e-ba8ae187b65c/WindowsAppRuntimeInstall-x64.exe' }
)

foreach ($file in $files) {
    $path = Join-Path $target $file.Name
    if (-not (Test-Path -LiteralPath $path)) {
        Invoke-WebRequest -Uri $file.Url -OutFile $path
    }
}
