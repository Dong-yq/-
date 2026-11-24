$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testDirectory = Join-Path $PSScriptRoot 'tests\bin'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$executable = Join-Path $testDirectory 'SalaryEngineTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /utf8output /codepage:65001 "/out:$executable" "/reference:$(Join-Path $framework 'System.Runtime.Serialization.dll')" (Join-Path $PSScriptRoot 'src\SalaryEngine.cs') (Join-Path $PSScriptRoot 'tests\SalaryEngineTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$wpf = Join-Path $framework 'WPF'
$uiReferences = @(
    "/reference:$(Join-Path $wpf 'PresentationFramework.dll')",
    "/reference:$(Join-Path $wpf 'PresentationCore.dll')",
    "/reference:$(Join-Path $wpf 'WindowsBase.dll')",
    "/reference:$(Join-Path $framework 'System.Xaml.dll')",
    "/reference:$(Join-Path $framework 'System.Runtime.Serialization.dll')"
)
$dockTest = Join-Path $testDirectory 'FloatingDockTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /utf8output /codepage:65001 /main:FloatingDockTests "/out:$dockTest" @uiReferences "/resource:$(Join-Path $PSScriptRoot 'src\MainWindow.xaml'),SalaryStopwatch.MainWindow.xaml" (Join-Path $PSScriptRoot 'src\SalaryEngine.cs') (Join-Path $PSScriptRoot 'src\FloatingDock.cs') (Join-Path $PSScriptRoot 'src\App.cs') (Join-Path $PSScriptRoot 'tests\FloatingDockTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Dock test compilation failed.' }
& $dockTest
if ($LASTEXITCODE -ne 0) { throw 'Dock tests failed.' }
$inputTest = Join-Path $testDirectory 'InputRenderingTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /utf8output /codepage:65001 "/out:$inputTest" @uiReferences (Join-Path $PSScriptRoot 'tests\InputRenderingTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Input rendering test compilation failed.' }
& $inputTest (Join-Path $PSScriptRoot 'src\MainWindow.xaml') (Join-Path $testDirectory 'input-rendering.png')
if ($LASTEXITCODE -ne 0) { throw 'Input rendering tests failed.' }
