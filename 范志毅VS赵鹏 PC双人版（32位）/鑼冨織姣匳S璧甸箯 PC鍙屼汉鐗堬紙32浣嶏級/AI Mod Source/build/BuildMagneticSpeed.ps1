param([ValidateSet('Original','Experiment','Win64')][string]$Edition='Original',[ValidateSet('Gameplay','Controls','Boundary','Defense','Goals','Magnetic','Contest','AI','Drop','Corner','Balance')][string]$Suite='Gameplay',[switch]$Baseline)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$gameRoot = if ($Edition -eq 'Experiment') { Join-Path $taskRoot '实验版（模型75%）' } else { $taskRoot }
$source = Join-Path $gameRoot 'AI Mod Source'
$originalAssembly = Join-Path $source 'Assembly-CSharp.original.dll'
if ($Edition -eq 'Win64') {
    $source = Join-Path $taskRoot 'AI Mod Source'
    $workspace = Split-Path (Split-Path $taskRoot -Parent) -Parent
    $gameRoot = (Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（64位）') -Directory -Force | Select-Object -First 1).FullName
    $originalAssembly = Join-Path $gameRoot 'AI Mod Source\Assembly-CSharp.original.dll'
}
$release = Join-Path $PSScriptRoot ('magnetic-speed-' + $Edition.ToLower() + '-release')
$testBuild = Join-Path $PSScriptRoot ('magnetic-speed-' + $Edition.ToLower() + '-' + $Suite.ToLower() + '-build')
$testPlayer = Join-Path $PSScriptRoot ('rescue-balance-test-player-' + $Edition.ToLower())
New-Item -ItemType Directory -Path $release,$testBuild,$testPlayer -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $testPlayer 'FanZhiYi.exe'))) {
        if ($Edition -eq 'Win64') {
        Copy-Item -LiteralPath (Join-Path $gameRoot 'D.exe') -Destination (Join-Path $testPlayer 'FanZhiYi.exe')
        foreach ($name in @('UnityPlayer.dll','UnityCrashHandler64.exe')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer }
    } else {
        foreach ($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer }
    }
    foreach ($name in @('FanZhiYi_Data','MonoBleedingEdge')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer -Recurse }
}
$managed = Join-Path $gameRoot 'FanZhiYi_Data\Managed'
$refs = @('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.ImageConversionModule.dll') | ForEach-Object { '/r:' + (Join-Path $managed $_) }
$sources = @('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ControlBindings.cs','PowerShot.cs','MagneticFoot.cs') | ForEach-Object { Join-Path $source $_ }
if ($Edition -eq 'Experiment') { $sources += Join-Path $source 'ExperimentScale.cs' }
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $release 'GameAIMod.dll')) $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Production compilation failed' }
& $csc /nologo /target:exe /optimize+ ('/out:' + (Join-Path $release 'PatchGame.exe')) ('/r:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) (Join-Path $source 'PatchGame.cs')
if ($LASTEXITCODE -ne 0) { throw 'Patcher compilation failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mono.Cecil.dll') -Destination $release -Force
& (Join-Path $release 'PatchGame.exe') $originalAssembly (Join-Path $release 'GameAIMod.dll') (Join-Path $release 'Assembly-CSharp.dll')
if ($LASTEXITCODE -ne 0) { throw 'Game patching failed' }
$testClass = switch ($Suite) { 'Gameplay' { 'BurstRangeTests' } 'Controls' { 'ControlsTests' } 'Boundary' { 'BehindGoalTests' } 'Defense' { 'ZhaoVolleyTests' } 'Goals' { 'GoalHeightTests' } 'Magnetic' { 'MagneticSpeedTests' } 'Contest' { 'KnockbackTests' } 'AI' { 'AIContestAirTests' } 'Drop' { 'ThighInterceptTests' } 'Balance' { 'RescueBalanceTests' } 'Corner' { 'BehindGoalTests' } }
$bridge = [IO.File]::ReadAllText((Join-Path $source 'GameAIMod.cs')).Replace('        menuPanel = panel;',('        ' + $testClass + '.Boot();' + [Environment]::NewLine + '        menuPanel = panel;'))
if ($Suite -eq 'Corner') { $bridge = $bridge.Replace('BehindGoalTests.Boot();','BehindGoalTests.CornerOnly = true; BehindGoalTests.Boot();') }
if ($Baseline) {
    $before = Get-ChildItem -LiteralPath $source -Directory -Filter 'backup-before-magnetic-speed-*' | Sort-Object Name | Select-Object -Last 1
    $baselineBridge = [IO.File]::ReadAllText((Join-Path $before.FullName 'GameAIMod.cs'))
    $ceilingHelper = [regex]::Match([IO.File]::ReadAllText((Join-Path $source 'GameAIMod.cs')),'(?s)    public static float FindGoalCeiling.*?(?=    private void FixedUpdate)').Value
    $baselineBridge = $baselineBridge.Replace('    public bool TryScoreCrossing()', $ceilingHelper + '    public bool TryScoreCrossing()')
    $bridge = $baselineBridge.Replace('        menuPanel = panel;', '        BehindGoalTests.CornerOnly = true; BehindGoalTests.Boot();' + [Environment]::NewLine + '        menuPanel = panel;')
}
$bindings = [IO.File]::ReadAllText((Join-Path $source 'ControlBindings.cs'))
if ($Suite -in @('Gameplay','Controls','Magnetic','Contest','AI','Drop','Balance')) { $bindings = $bindings.Replace('return Input.GetKey(key);',('return ' + $testClass + '.ReadKey(key);')).Replace('return Input.GetKeyDown(key);',('return ' + $testClass + '.ReadDown(key);')) }
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
    $skills = [IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('        lastTackle = Time.time;', '        KnockbackTests.OnTackle(controller, opponent.controller, aerial, throughBall);' + [Environment]::NewLine + '        lastTackle = Time.time;')
    [IO.File]::WriteAllText((Join-Path $testBuild 'PlayerSkills.cs'),$skills,[Text.UTF8Encoding]::new($false))
    $testSources = $testSources | Where-Object { (Split-Path $_ -Leaf) -ne 'PlayerSkills.cs' }
    $testSources += Join-Path $testBuild 'PlayerSkills.cs'
}
if ($Suite -eq 'Balance') {
    $power = [IO.File]::ReadAllText((Join-Path $source 'PowerShot.cs')).Replace('        ball.AddForce(ball.velocity * ((HeaderSpeedMultiplier - 1f) * ball.mass), ForceMode2D.Impulse);', '        Vector2 beforeHeader = ball.velocity; ball.AddForce(ball.velocity * ((HeaderSpeedMultiplier - 1f) * ball.mass), ForceMode2D.Impulse); RescueBalanceTests.HeaderScaled(controller, beforeHeader, ball.velocity);')
    $skills = [IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('                PowerShot.CompleteHeader(controller);','                RescueBalanceTests.AssistedHeader(target, ball.velocity); PowerShot.CompleteHeader(controller);').Replace('        s.retreatUntil = -1f; PowerShot.CompleteHeader(player); return true;','        RescueBalanceTests.RescueStrike(s.ball.velocity); s.retreatUntil = -1f; PowerShot.CompleteHeader(player); return true;').Replace('        bool power = PowerShot.OnBallCollision(collision);','        RescueBalanceTests.Contact(collision); bool power = PowerShot.OnBallCollision(collision);')
    [IO.File]::WriteAllText((Join-Path $testBuild 'PowerShot.cs'),$power,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $testBuild 'PlayerSkills.cs'),$skills,[Text.UTF8Encoding]::new($false))
    $testSources = $testSources | Where-Object { (Split-Path $_ -Leaf) -notin @('PlayerSkills.cs','PowerShot.cs') }
    $testSources += Join-Path $testBuild 'PlayerSkills.cs'; $testSources += Join-Path $testBuild 'PowerShot.cs'
}
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $testBuild 'GameAIMod.dll')) $refs $testSources (Join-Path $testBuild 'GameAIMod.cs') (Join-Path $testBuild 'ControlBindings.cs') (Join-Path $PSScriptRoot ($testClass + '.cs'))
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
Copy-Item -LiteralPath (Join-Path $testBuild 'GameAIMod.dll') -Destination (Join-Path $testPlayer 'FanZhiYi_Data\Managed\GameAIMod.dll') -Force
Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $testPlayer 'FanZhiYi_Data\Managed\Assembly-CSharp.dll') -Force
$logSuite = $Suite.ToLower() + $(if ($Baseline) { '-baseline' } else { '' })
$taskLog = Join-Path $PSScriptRoot ('magnetic-speed-' + $Edition.ToLower() + '-' + $logSuite + '.log')
$taskProcess = Start-Process -FilePath (Join-Path $testPlayer 'FanZhiYi.exe') -WorkingDirectory $testPlayer -ArgumentList @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"' + $taskLog + '"')) -WindowStyle Hidden -PassThru
$runStarted=Get-Date
while (-not $taskProcess.WaitForExit(30000)) { if(((Get-Date)-$runStarted).TotalSeconds -gt 150){$taskProcess.Kill();throw "Isolated runtime timed out"}; Write-Output ($Edition + ' ' + $Suite + ' verification running') }
Write-Output ('Exit: ' + $taskProcess.ExitCode)
Select-String -LiteralPath $taskLog -Pattern '(BURSTRANGE|AIMATCH|CONTEST|MAGNETIC|POWER|BOUNDARY|CONTROLS|DEFENSE|GOALHEIGHT|DROP|BALANCE) (FAIL|COMPLETE)|Exception:|TIMEOUT|GotoState'
if ($taskProcess.ExitCode -ne 0) { throw 'Runtime checks failed' }
