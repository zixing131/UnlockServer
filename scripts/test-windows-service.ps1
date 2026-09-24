param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "UnlockServer.Wpf\bin\$Configuration"
$compiler = Join-Path $root 'packages\Microsoft.Net.Compilers.Toolset.4.12.0\tasks\net472\csc.exe'
if (!(Test-Path $compiler)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $compiler = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
    }
}
if (!$compiler -or !(Test-Path $compiler)) { throw 'A Roslyn compiler is required; build WPF first.' }
$framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$test = Join-Path $out 'WindowsServiceSmoke.exe'
& $compiler /nologo /target:exe "/out:$test" "/r:$out\UnlockServer.exe" "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\System.dll" "/r:$framework\System.Core.dll" (Join-Path $root 'tests\UnlockServer.RegressionTests\WindowsServiceSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw "Smoke test compilation failed: $LASTEXITCODE" }
& $test
if ($LASTEXITCODE -ne 0) { throw "Smoke tests failed: $LASTEXITCODE" }
