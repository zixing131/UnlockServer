param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$WithProvider
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent
$packages = Join-Path $root 'packages'

function Restore-Package([string]$id, [string]$version) {
    $destination = Join-Path $packages "$id.$version"
    if (!(Test-Path $destination)) {
        New-Item -ItemType Directory -Force $packages | Out-Null
        $zip = Join-Path ([IO.Path]::GetTempPath()) ("$id-" + [guid]::NewGuid() + '.zip')
        try {
            $lower = $id.ToLowerInvariant()
            Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$lower/$version/$lower.$version.nupkg" -OutFile $zip -UseBasicParsing
            Expand-Archive $zip $destination
        } finally { Remove-Item $zip -ErrorAction SilentlyContinue }
    }
    return $destination
}

$null = Restore-Package 'InTheHand.Net.Bluetooth' '4.0.30'
$arguments = @((Join-Path $root 'UnlockServer.Wpf\UnlockServer.Wpf.csproj'), '/t:Rebuild', "/p:Configuration=$Configuration", '/v:minimal', '/nologo')
$msbuild = (Get-Command MSBuild.exe -ErrorAction SilentlyContinue).Source
if (!$msbuild) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    }
}
if (!$msbuild) {
    if ($WithProvider) { throw 'The native x64 Provider requires Visual Studio with the v143 C++ toolchain.' }
    # Build WPF with the installed Framework targets plus a modern C# compiler.
    # This fallback does not install VS or change registry/system configuration.
    $msbuild = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
    $sdkTools = "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools"
    if (!(Test-Path $msbuild) -or !(Test-Path "$sdkTools\TlbImp.exe")) {
        throw 'Install Visual Studio .NET desktop build tools and the .NET Framework 4.8 targeting pack.'
    }
    $compiler = Restore-Package 'Microsoft.Net.Compilers.Toolset' '4.12.0'
    $arguments += @("/p:CscToolPath=$compiler\tasks\net472", "/p:TargetFrameworkSDKToolsDirectory=$sdkTools", '/p:LangVersion=latest')
}
if ($WithProvider) {
    & $msbuild (Join-Path $root 'UnlockServer.Provider\UnlockServer.Provider.vcxproj') /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Provider build failed: $LASTEXITCODE" }
}
& $msbuild @arguments
if ($LASTEXITCODE -ne 0) { throw "WPF build failed: $LASTEXITCODE" }
Write-Host "Built: $root\UnlockServer.Wpf\bin\$Configuration\UnlockServer.exe"
if (!$WithProvider) { Write-Host 'WPF only. Use -WithProvider to include the native x64 Credential Provider (VS v143 required).' }
