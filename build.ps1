param([string]$OutputName = '薪资秒表.exe')
$ErrorActionPreference = 'Stop'
$workspace = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Requires Windows with .NET Framework 4.x.' }
$output = Join-Path $workspace $OutputName
$arguments = @(
    '/nologo', '/target:winexe', '/optimize+', '/utf8output', '/codepage:65001',
    "/out:$output",
    "/win32manifest:$(Join-Path $workspace 'src\app.manifest')",
    "/resource:$(Join-Path $workspace 'src\MainWindow.xaml'),SalaryStopwatch.MainWindow.xaml",
    "/reference:$(Join-Path $wpf 'PresentationFramework.dll')",
    "/reference:$(Join-Path $wpf 'PresentationCore.dll')",
    "/reference:$(Join-Path $wpf 'WindowsBase.dll')",
    "/reference:$(Join-Path $framework 'System.Xaml.dll')",
    "/reference:$(Join-Path $framework 'System.Runtime.Serialization.dll')",
    (Join-Path $workspace 'src\SalaryEngine.cs'),
    (Join-Path $workspace 'src\FloatingDock.cs'),
    (Join-Path $workspace 'src\App.cs')
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $output"
