$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$experimentRoot = Join-Path $taskRoot '实验版（模型75%）'
$source = Join-Path $experimentRoot 'AI Mod Source'
$release = Join-Path $PSScriptRoot 'scale-release'
$testBuild = Join-Path $PSScriptRoot 'scale-test-build'
$testPlayer = Join-Path $PSScriptRoot 'scale-test-player'
New-Item -ItemType Directory -Path $release,$testBuild,$testPlayer -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $testPlayer 'FanZhiYi.exe'))) {
    foreach ($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe')) { Copy-Item -LiteralPath (Join-Path $experimentRoot $name) -Destination $testPlayer }
    foreach ($name in @('FanZhiYi_Data','MonoBleedingEdge')) { Copy-Item -LiteralPath (Join-Path $experimentRoot $name) -Destination $testPlayer -Recurse }
}
$managed = Join-Path $taskRoot 'FanZhiYi_Data\Managed'
$refs = @('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.ImageConversionModule.dll') | ForEach-Object { '/r:' + (Join-Path $managed $_) }
$sources = @('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ControlBindings.cs','ExperimentScale.cs') | ForEach-Object { Join-Path $source $_ }
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $release 'GameAIMod.dll')) $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Experimental production compilation failed' }
& $csc /nologo /target:exe /optimize+ ('/out:' + (Join-Path $release 'PatchGame.exe')) ('/r:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) (Join-Path $source 'PatchGame.cs')
if ($LASTEXITCODE -ne 0) { throw 'Patcher compilation failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mono.Cecil.dll') -Destination $release -Force
& (Join-Path $release 'PatchGame.exe') (Join-Path $source 'Assembly-CSharp.original.dll') (Join-Path $release 'GameAIMod.dll') (Join-Path $release 'Assembly-CSharp.dll')
if ($LASTEXITCODE -ne 0) { throw 'Game patching failed' }
$bridge = [IO.File]::ReadAllText((Join-Path $source 'GameAIMod.cs')).Replace('        menuPanel = panel;', '        ScaleTests.Boot();' + [Environment]::NewLine + '        menuPanel = panel;')
$bindings = [IO.File]::ReadAllText((Join-Path $source 'ControlBindings.cs')).Replace('return Input.GetKey(key);','return ScaleTests.ReadKey(key);').Replace('return Input.GetKeyDown(key);','return ScaleTests.ReadDown(key);')
[IO.File]::WriteAllText((Join-Path $testBuild 'GameAIMod.cs'),$bridge,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $testBuild 'ControlBindings.cs'),$bindings,[Text.UTF8Encoding]::new($false))
$testSources = $sources | Where-Object { (Split-Path $_ -Leaf) -notin @('GameAIMod.cs','ControlBindings.cs') }
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $testBuild 'GameAIMod.dll')) $refs $testSources (Join-Path $testBuild 'GameAIMod.cs') (Join-Path $testBuild 'ControlBindings.cs') (Join-Path $PSScriptRoot 'ScaleTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Experimental test compilation failed' }
Copy-Item -LiteralPath (Join-Path $testBuild 'GameAIMod.dll') -Destination (Join-Path $testPlayer 'FanZhiYi_Data\Managed\GameAIMod.dll') -Force
Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $testPlayer 'FanZhiYi_Data\Managed\Assembly-CSharp.dll') -Force
'Experimental production and isolated test build prepared'
