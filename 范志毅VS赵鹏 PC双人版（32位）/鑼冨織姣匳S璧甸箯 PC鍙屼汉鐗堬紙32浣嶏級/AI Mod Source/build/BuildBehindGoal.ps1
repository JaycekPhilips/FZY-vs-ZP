$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location -LiteralPath $taskRoot
$release = Join-Path $PSScriptRoot 'behind-goal-release'
$testBuild = Join-Path $PSScriptRoot 'behind-goal-test-build'
New-Item -ItemType Directory -Path $release,$testBuild -Force | Out-Null
$managed = Join-Path $taskRoot 'FanZhiYi_Data\Managed'
$refs = @('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll',
    'UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll',
    'UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll') |
    ForEach-Object { '/r:' + (Join-Path $managed $_) }
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $release 'GameAIMod.dll')) $refs 'AI Mod Source\GameAIMod.cs' 'AI Mod Source\PlayerSkills.cs' 'AI Mod Source\PlayerMovement.cs' 'AI Mod Source\ZhaoHeader.cs' 'AI Mod Source\ControlBindings.cs'
if ($LASTEXITCODE -ne 0) { throw 'Production compilation failed' }
& $csc /nologo /target:exe /optimize+ ('/out:' + (Join-Path $release 'PatchGame.exe')) ('/r:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) 'AI Mod Source\PatchGame.cs'
if ($LASTEXITCODE -ne 0) { throw 'Patcher compilation failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mono.Cecil.dll') -Destination $release -Force
& (Join-Path $release 'PatchGame.exe') (Join-Path $taskRoot 'AI Mod Source\Assembly-CSharp.original.dll') (Join-Path $release 'GameAIMod.dll') (Join-Path $release 'Assembly-CSharp.dll')
if ($LASTEXITCODE -ne 0) { throw 'Game patching failed' }

$bridge = [IO.File]::ReadAllText((Join-Path $taskRoot 'AI Mod Source\GameAIMod.cs'))
$anchor = '        menuPanel = panel;'
if (-not $bridge.Contains($anchor)) { throw 'Test bootstrap anchor missing' }
$bridge = $bridge.Replace($anchor, '        BehindGoalTests.Boot();' + [Environment]::NewLine + $anchor)
$testSource = Join-Path $testBuild 'GameAIMod.test.cs'
[IO.File]::WriteAllText($testSource, $bridge, [Text.UTF8Encoding]::new($false))
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $testBuild 'GameAIMod.dll')) $refs $testSource 'AI Mod Source\PlayerSkills.cs' 'AI Mod Source\PlayerMovement.cs' 'AI Mod Source\ZhaoHeader.cs' 'AI Mod Source\ControlBindings.cs' 'AI Mod Source\build\BehindGoalTests.cs'
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
$testManaged = Join-Path $PSScriptRoot 'burst-test-player\FanZhiYi_Data\Managed'
Copy-Item -LiteralPath (Join-Path $testBuild 'GameAIMod.dll') -Destination (Join-Path $testManaged 'GameAIMod.dll') -Force
Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $testManaged 'Assembly-CSharp.dll') -Force
Write-Output 'Production and isolated test game compiled'
