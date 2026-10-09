param([ValidateSet('Original','Experiment')][string]$Edition='Original',[ValidateSet('Gameplay','Controls','Boundary','Defense','Goals','Magnetic','Contest','AI','Drop','Corner')][string]$Suite='Gameplay',[switch]$Baseline)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$gameRoot = if ($Edition -eq 'Experiment') { Join-Path $taskRoot '实验版（模型75%）' } else { $taskRoot }
$source = Join-Path $gameRoot 'AI Mod Source'
$release = Join-Path $PSScriptRoot ('burst-range-' + $Edition.ToLower() + '-release')
$testBuild = Join-Path $PSScriptRoot ('burst-range-' + $Edition.ToLower() + '-' + $Suite.ToLower() + '-build')
$testPlayer = Join-Path $PSScriptRoot ('burst-range-test-player-' + $Edition.ToLower())
New-Item -ItemType Directory -Path $release,$testBuild,$testPlayer -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $testPlayer 'FanZhiYi.exe'))) {
    foreach ($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer }
    foreach ($name in @('FanZhiYi_Data','MonoBleedingEdge')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer -Recurse }
}
$managed = Join-Path $taskRoot 'FanZhiYi_Data\Managed'
$refs = @('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.ImageConversionModule.dll') | ForEach-Object { '/r:' + (Join-Path $managed $_) }
$sources = @('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ControlBindings.cs','PowerShot.cs','MagneticFoot.cs') | ForEach-Object { Join-Path $source $_ }
if ($Edition -eq 'Experiment') { $sources += Join-Path $source 'ExperimentScale.cs' }
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $release 'GameAIMod.dll')) $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Production compilation failed' }
& $csc /nologo /target:exe /optimize+ ('/out:' + (Join-Path $release 'PatchGame.exe')) ('/r:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) (Join-Path $source 'PatchGame.cs')
if ($LASTEXITCODE -ne 0) { throw 'Patcher compilation failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mono.Cecil.dll') -Destination $release -Force
& (Join-Path $release 'PatchGame.exe') (Join-Path $source 'Assembly-CSharp.original.dll') (Join-Path $release 'GameAIMod.dll') (Join-Path $release 'Assembly-CSharp.dll')
if ($LASTEXITCODE -ne 0) { throw 'Game patching failed' }
$testClass = switch ($Suite) { 'Gameplay' { 'BurstRangeTests' } 'Controls' { 'ControlsTests' } 'Boundary' { 'BehindGoalTests' } 'Defense' { 'ZhaoVolleyTests' } 'Goals' { 'GoalHeightTests' } 'Magnetic' { 'MagneticFootTests' } 'Contest' { 'GroundContestTests' } 'AI' { 'AIContestAirTests' } 'Drop' { 'SemiAutoDropTests' } 'Corner' { 'BehindGoalTests' } }
$bridge = [IO.File]::ReadAllText((Join-Path $source 'GameAIMod.cs')).Replace('        menuPanel = panel;',('        ' + $testClass + '.Boot();' + [Environment]::NewLine + '        menuPanel = panel;'))
if ($Suite -eq 'Corner') { $bridge = $bridge.Replace('BehindGoalTests.Boot();','BehindGoalTests.CornerOnly = true; BehindGoalTests.Boot();') }
if ($Baseline) {
    $before = Get-ChildItem -LiteralPath $source -Directory -Filter 'backup-before-burst-range-*' | Sort-Object Name | Select-Object -Last 1
    $baselineBridge = [IO.File]::ReadAllText((Join-Path $before.FullName 'GameAIMod.cs'))
    $ceilingHelper = [regex]::Match([IO.File]::ReadAllText((Join-Path $source 'GameAIMod.cs')),'(?s)    public static float FindGoalCeiling.*?(?=    private void FixedUpdate)').Value
    $baselineBridge = $baselineBridge.Replace('    public bool TryScoreCrossing()', $ceilingHelper + '    public bool TryScoreCrossing()')
    $bridge = $baselineBridge.Replace('        menuPanel = panel;', '        BehindGoalTests.CornerOnly = true; BehindGoalTests.Boot();' + [Environment]::NewLine + '        menuPanel = panel;')
}
$bindings = [IO.File]::ReadAllText((Join-Path $source 'ControlBindings.cs'))
if ($Suite -in @('Gameplay','Controls','Magnetic','Contest','AI','Drop')) { $bindings = $bindings.Replace('return Input.GetKey(key);',('return ' + $testClass + '.ReadKey(key);')).Replace('return Input.GetKeyDown(key);',('return ' + $testClass + '.ReadDown(key);')) }
if ($Suite -in @('Defense','AI')) { $bridge = $bridge.Replace('        if (controller == null) return false;', ('        if (!' + $testClass + '.UseAI) return false;') + [Environment]::NewLine + '        if (controller == null) return false;') }
[IO.File]::WriteAllText((Join-Path $testBuild 'GameAIMod.cs'),$bridge,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $testBuild 'ControlBindings.cs'),$bindings,[Text.UTF8Encoding]::new($false))
$testSources = $sources | Where-Object { (Split-Path $_ -Leaf) -notin @('GameAIMod.cs','ControlBindings.cs') }
if ($Suite -eq 'Gameplay') {
    $skills = [IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('        bool power = PowerShot.OnBallCollision(collision);', '        BurstRangeTests.BeforeContact(collision);' + [Environment]::NewLine + '        bool power = PowerShot.OnBallCollision(collision);' + [Environment]::NewLine + '        BurstRangeTests.AfterContact(collision, power);')
    [IO.File]::WriteAllText((Join-Path $testBuild 'PlayerSkills.cs'),$skills,[Text.UTF8Encoding]::new($false))
    $testSources = $testSources | Where-Object { (Split-Path $_ -Leaf) -ne 'PlayerSkills.cs' }
    $testSources += Join-Path $testBuild 'PlayerSkills.cs'
}
if ($Suite -eq 'Contest') {
    $skills = [IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('        lastTackle = Time.time;', '        GroundContestTests.OnTackle(controller, opponent.controller, aerial, throughBall);' + [Environment]::NewLine + '        lastTackle = Time.time;')
    [IO.File]::WriteAllText((Join-Path $testBuild 'PlayerSkills.cs'),$skills,[Text.UTF8Encoding]::new($false))
    $testSources = $testSources | Where-Object { (Split-Path $_ -Leaf) -ne 'PlayerSkills.cs' }
    $testSources += Join-Path $testBuild 'PlayerSkills.cs'
}
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $testBuild 'GameAIMod.dll')) $refs $testSources (Join-Path $testBuild 'GameAIMod.cs') (Join-Path $testBuild 'ControlBindings.cs') (Join-Path $PSScriptRoot ($testClass + '.cs'))
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
Copy-Item -LiteralPath (Join-Path $testBuild 'GameAIMod.dll') -Destination (Join-Path $testPlayer 'FanZhiYi_Data\Managed\GameAIMod.dll') -Force
Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $testPlayer 'FanZhiYi_Data\Managed\Assembly-CSharp.dll') -Force
$logSuite = $Suite.ToLower() + $(if ($Baseline) { '-baseline' } else { '' })
$taskLog = Join-Path $PSScriptRoot ('burst-range-' + $Edition.ToLower() + '-' + $logSuite + '.log')
$taskProcess = Start-Process -FilePath (Join-Path $testPlayer 'FanZhiYi.exe') -WorkingDirectory $testPlayer -ArgumentList @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"' + $taskLog + '"')) -WindowStyle Hidden -PassThru
while (-not $taskProcess.WaitForExit(30000)) { Write-Output ($Edition + ' ' + $Suite + ' verification running') }
Write-Output ('Exit: ' + $taskProcess.ExitCode)
Select-String -LiteralPath $taskLog -Pattern '(BURSTRANGE|AIMATCH|CONTEST|MAGNETIC|POWER|BOUNDARY|CONTROLS|DEFENSE|GOALHEIGHT|DROP) (FAIL|COMPLETE)|Exception:|TIMEOUT|GotoState'
if ($taskProcess.ExitCode -ne 0) { throw 'Runtime checks failed' }
