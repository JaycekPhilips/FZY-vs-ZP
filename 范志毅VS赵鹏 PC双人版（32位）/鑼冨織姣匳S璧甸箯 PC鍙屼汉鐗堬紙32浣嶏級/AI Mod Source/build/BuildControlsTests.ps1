$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location -LiteralPath $taskRoot
$release = Join-Path $PSScriptRoot 'zhao-volley-release'
$testBuild = Join-Path $PSScriptRoot 'controls-test-build'
$testPlayer = Join-Path $PSScriptRoot 'controls-test-player'
New-Item -ItemType Directory -Path $testBuild,$testPlayer -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $testPlayer 'FanZhiYi.exe'))) {
    Copy-Item -LiteralPath 'FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe' -Destination $testPlayer
    Copy-Item -LiteralPath 'FanZhiYi_Data','MonoBleedingEdge' -Destination $testPlayer -Recurse
}
$managed = Join-Path $taskRoot 'FanZhiYi_Data\Managed'
$refs = @('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll',
    'UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll',
    'UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll') |
    ForEach-Object { '/r:' + (Join-Path $managed $_) }
$bridge = [IO.File]::ReadAllText((Join-Path $taskRoot 'AI Mod Source\GameAIMod.cs')).Replace('        menuPanel = panel;', '        ControlsTests.Boot();' + [Environment]::NewLine + '        menuPanel = panel;')
$bindings = [IO.File]::ReadAllText((Join-Path $taskRoot 'AI Mod Source\ControlBindings.cs')).Replace('return Input.GetKey(key);', 'return ControlsTests.ReadKey(key);').Replace('return Input.GetKeyDown(key);', 'return ControlsTests.ReadDown(key);')
$testBridge = Join-Path $testBuild 'GameAIMod.test.cs'
$testBindings = Join-Path $testBuild 'ControlBindings.test.cs'
[IO.File]::WriteAllText($testBridge,$bridge,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($testBindings,$bindings,[Text.UTF8Encoding]::new($false))
& 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' /nologo /target:library /optimize+ ('/out:' + (Join-Path $testBuild 'GameAIMod.dll')) $refs $testBridge $testBindings 'AI Mod Source\PlayerSkills.cs' 'AI Mod Source\PlayerMovement.cs' 'AI Mod Source\ZhaoHeader.cs' 'AI Mod Source\build\ControlsTests.cs'
if ($LASTEXITCODE -ne 0) { throw 'Controls test compilation failed' }
$testManaged = Join-Path $testPlayer 'FanZhiYi_Data\Managed'
Copy-Item -LiteralPath (Join-Path $testBuild 'GameAIMod.dll') -Destination (Join-Path $testManaged 'GameAIMod.dll') -Force
Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $testManaged 'Assembly-CSharp.dll') -Force
Write-Output 'Controls test game prepared'
