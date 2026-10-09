param([ValidateSet('Original','Experiment')][string]$Edition='Original',[ValidateSet('Gameplay','Controls','Boundary','Defense','Goals')][string]$Suite='Gameplay')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$gameRoot = if ($Edition -eq 'Experiment') { Join-Path $taskRoot '实验版（模型75%）' } else { $taskRoot }
$source = Join-Path $gameRoot 'AI Mod Source'
$release = Join-Path $PSScriptRoot ('power-' + $Edition.ToLower() + '-release')
$testBuild = Join-Path $PSScriptRoot ('power-' + $Edition.ToLower() + '-' + $Suite.ToLower() + '-build')
$testPlayer = Join-Path $PSScriptRoot ('power-test-player-' + $Edition.ToLower())
New-Item -ItemType Directory -Path $release,$testBuild,$testPlayer -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $testPlayer 'FanZhiYi.exe'))) {
    foreach ($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer }
    foreach ($name in @('FanZhiYi_Data','MonoBleedingEdge')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer -Recurse }
}
$managed = Join-Path $taskRoot 'FanZhiYi_Data\Managed'
$refs = @('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.ImageConversionModule.dll') | ForEach-Object { '/r:' + (Join-Path $managed $_) }
$sources = @('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ControlBindings.cs','PowerShot.cs') | ForEach-Object { Join-Path $source $_ }
if ($Edition -eq 'Experiment') { $sources += Join-Path $source 'ExperimentScale.cs' }
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $release 'GameAIMod.dll')) $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Production compilation failed' }
& $csc /nologo /target:exe /optimize+ ('/out:' + (Join-Path $release 'PatchGame.exe')) ('/r:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) (Join-Path $source 'PatchGame.cs')
if ($LASTEXITCODE -ne 0) { throw 'Patcher compilation failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mono.Cecil.dll') -Destination $release -Force
& (Join-Path $release 'PatchGame.exe') (Join-Path $source 'Assembly-CSharp.original.dll') (Join-Path $release 'GameAIMod.dll') (Join-Path $release 'Assembly-CSharp.dll')
if ($LASTEXITCODE -ne 0) { throw 'Game patching failed' }
$testClass = switch ($Suite) { 'Gameplay' { 'PowerUpdateTests' } 'Controls' { 'ControlsTests' } 'Boundary' { 'BehindGoalTests' } 'Defense' { 'ZhaoVolleyTests' } 'Goals' { 'GoalHeightTests' } }
$bridge = [IO.File]::ReadAllText((Join-Path $source 'GameAIMod.cs')).Replace('        menuPanel = panel;',('        ' + $testClass + '.Boot();' + [Environment]::NewLine + '        menuPanel = panel;'))
$bindings = [IO.File]::ReadAllText((Join-Path $source 'ControlBindings.cs'))
if ($Suite -in @('Gameplay','Controls')) { $bindings = $bindings.Replace('return Input.GetKey(key);',('return ' + $testClass + '.ReadKey(key);')).Replace('return Input.GetKeyDown(key);',('return ' + $testClass + '.ReadDown(key);')) }
if ($Suite -eq 'Defense') { $bridge = $bridge.Replace('        if (controller == null) return false;', '        if (!ZhaoVolleyTests.UseAI) return false;' + [Environment]::NewLine + '        if (controller == null) return false;') }
[IO.File]::WriteAllText((Join-Path $testBuild 'GameAIMod.cs'),$bridge,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $testBuild 'ControlBindings.cs'),$bindings,[Text.UTF8Encoding]::new($false))
$testSources = $sources | Where-Object { (Split-Path $_ -Leaf) -notin @('GameAIMod.cs','ControlBindings.cs') }
if ($Suite -eq 'Gameplay') {
    $skills = [IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('        bool power = PowerShot.OnBallCollision(collision);', '        PowerUpdateTests.BeforeContact(collision);' + [Environment]::NewLine + '        bool power = PowerShot.OnBallCollision(collision);' + [Environment]::NewLine + '        PowerUpdateTests.AfterContact(collision, power);')
    [IO.File]::WriteAllText((Join-Path $testBuild 'PlayerSkills.cs'),$skills,[Text.UTF8Encoding]::new($false))
    $testSources = $testSources | Where-Object { (Split-Path $_ -Leaf) -ne 'PlayerSkills.cs' }
    $testSources += Join-Path $testBuild 'PlayerSkills.cs'
}
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $testBuild 'GameAIMod.dll')) $refs $testSources (Join-Path $testBuild 'GameAIMod.cs') (Join-Path $testBuild 'ControlBindings.cs') (Join-Path $PSScriptRoot ($testClass + '.cs'))
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
Copy-Item -LiteralPath (Join-Path $testBuild 'GameAIMod.dll') -Destination (Join-Path $testPlayer 'FanZhiYi_Data\Managed\GameAIMod.dll') -Force
Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $testPlayer 'FanZhiYi_Data\Managed\Assembly-CSharp.dll') -Force
$taskLog = Join-Path $PSScriptRoot ('power-' + $Edition.ToLower() + '-' + $Suite.ToLower() + '.log')
$taskProcess = Start-Process -FilePath (Join-Path $testPlayer 'FanZhiYi.exe') -WorkingDirectory $testPlayer -ArgumentList @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"' + $taskLog + '"')) -WindowStyle Hidden -PassThru
while (-not $taskProcess.WaitForExit(30000)) { Write-Output ($Edition + ' ' + $Suite + ' verification running') }
Write-Output ('Exit: ' + $taskProcess.ExitCode)
Select-String -LiteralPath $taskLog -Pattern '(POWER|BOUNDARY|CONTROLS|DEFENSE|GOALHEIGHT) (FAIL|COMPLETE)|Exception:|TIMEOUT|GotoState'
if ($taskProcess.ExitCode -ne 0) { throw 'Runtime checks failed' }
