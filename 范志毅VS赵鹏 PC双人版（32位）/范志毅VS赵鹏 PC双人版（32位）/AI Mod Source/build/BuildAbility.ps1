param([ValidateSet('Original','Experiment','Win64')][string]$Edition='Original',[ValidateSet('Gameplay','Controls','Boundary','Defense','Goals','Magnetic','Contest','AI','Drop','Corner','Balance','Strategy','SelfPlay','Rolling','FanStrategy','Ability','Visual','AbilityContest','AbilitySelfPlay','AIPressure','ShotRescue','SkillSelfPlay','DownIntercept','CombatFlow','Startup','CannonTotal','Lottery','AerialTempo','HeaderLob','SkillRework','BicycleFlow','FormlessSafety','SkillOdds','FormlessSelfPlay')][string]$Suite='Gameplay',[switch]$Baseline,[int]$GoalTarget=2000,[ValidateRange(1,64)][int]$SelfPlaySpeed=24,[switch]$PrepareSelfPlay,[string]$SourceOverride)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workspace = Split-Path (Split-Path $taskRoot -Parent) -Parent
$gameRoot = if ($Edition -eq 'Experiment') { Join-Path $workspace '范志毅VS赵鹏 实验版（模型75%）' } else { $taskRoot }
$source = Join-Path $gameRoot 'AI Mod Source'
if($SourceOverride){$source=(Resolve-Path -LiteralPath $SourceOverride).Path}
$originalAssembly = Join-Path $source 'Assembly-CSharp.original.dll'
if ($Edition -eq 'Win64') {
    $source = Join-Path $taskRoot 'AI Mod Source'
    $workspace = Split-Path (Split-Path $taskRoot -Parent) -Parent
    $gameRoot = (Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（64位）') -Directory -Force | Select-Object -First 1).FullName
    $originalAssembly = Join-Path $gameRoot 'AI Mod Source\Assembly-CSharp.original.dll'
}
$release = Join-Path $PSScriptRoot ('ability-' + $Edition.ToLower() + $(if ($Baseline) { '-baseline' } else { '' }) + '-release')
$testBuild = Join-Path $PSScriptRoot ('ability-' + $Edition.ToLower() + '-' + $Suite.ToLower() + '-build')
$testPlayer = Join-Path $PSScriptRoot ('ability-test-player-' + $Edition.ToLower() + $(if ($Baseline) { '-baseline' } else { '' }))
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
foreach($asset in @('球场背景.png')){$assetPath=Join-Path $gameRoot $asset;if(Test-Path -LiteralPath $assetPath){Copy-Item -LiteralPath $assetPath -Destination $testPlayer -Force}}
if ($Baseline) { $source = (Get-ChildItem -LiteralPath $source -Directory -Filter 'backup-before-fan-ai-20*' | Sort-Object Name | Select-Object -Last 1).FullName }
$managed = Join-Path $gameRoot 'FanZhiYi_Data\Managed'
$refs = @('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.JSONSerializeModule.dll') | ForEach-Object { '/r:' + (Join-Path $managed $_) }
$sources = @('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ControlBindings.cs','PowerShot.cs','MagneticFoot.cs','AbilityMode.cs','MenuBackdrop.cs','LanMultiplayer.cs') | ForEach-Object { Join-Path $source $_ }
if ($Edition -eq 'Experiment') { $sources += Join-Path $source 'ExperimentScale.cs' }
$policyText = ($sources + (Join-Path $source 'PatchGame.cs') | ForEach-Object { (Split-Path $_ -Leaf) + ':' + (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }) -join '|';
$policySha=[Security.Cryptography.SHA256]::Create();$policyHash=[BitConverter]::ToString($policySha.ComputeHash([Text.Encoding]::UTF8.GetBytes($policyText))).Replace('-','');$policySha.Dispose();
[IO.File]::WriteAllText((Join-Path $release 'policy-hash.txt'),$policyHash);
$csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:library /optimize+ ('/out:' + (Join-Path $release 'GameAIMod.dll')) $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Production compilation failed' }
& $csc /nologo /target:exe /optimize+ ('/out:' + (Join-Path $release 'PatchGame.exe')) ('/r:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) (Join-Path $source 'PatchGame.cs')
if ($LASTEXITCODE -ne 0) { throw 'Patcher compilation failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mono.Cecil.dll') -Destination $release -Force
& (Join-Path $release 'PatchGame.exe') $originalAssembly (Join-Path $release 'GameAIMod.dll') (Join-Path $release 'Assembly-CSharp.dll')
if ($LASTEXITCODE -ne 0) { throw 'Game patching failed' }
$testClass = switch ($Suite) { 'Gameplay' { 'BurstRangeTests' } 'Controls' { 'ControlsTests' } 'Boundary' { 'BehindGoalTests' } 'Defense' { 'ZhaoVolleyTests' } 'Goals' { 'GoalHeightTests' } 'Magnetic' { 'MagneticSpeedTests' } 'Contest' { 'KnockbackTests' } 'AI' { 'AIContestAirTests' } 'Drop' { 'DownInterceptTests' } 'Balance' { 'RescueBalanceTests' } 'Strategy' { 'ZhaoStrategyTests' } 'FanStrategy' { 'FanStrategyTests' } 'AIPressure' { 'AIPressureTests' } 'AbilityContest' { 'AbilityContestTests' } 'AbilitySelfPlay' { 'AbilitySelfPlayTests' } 'ShotRescue' { 'ShotRescueTests' } 'SkillSelfPlay' { 'SkillSelfPlayTests' } 'DownIntercept' { 'DownInterceptTests' } 'CombatFlow' { 'CombatFlowTests' } 'Startup' { 'StartupTests' } 'CannonTotal' { 'CannonTotalTests' } 'Lottery' { 'CannonTotalTests' } 'AerialTempo' { 'CannonTotalTests' } 'HeaderLob' { 'CannonTotalTests' } 'SkillRework' { 'SkillReworkTests' } 'BicycleFlow' { 'SkillReworkTests' } 'FormlessSafety' { 'SkillReworkTests' } 'SkillOdds' { 'SkillReworkTests' } 'FormlessSelfPlay' { 'FormlessSelfPlayTests' } 'Ability' { 'AbilityModeTests' } 'Visual' { 'SkillVisualTests' } 'SelfPlay' { 'FanSelfPlayTests' } 'Rolling' { 'RollingRescueTests' } 'Corner' { 'BehindGoalTests' } }
$bridge = [IO.File]::ReadAllText((Join-Path $source 'GameAIMod.cs')).Replace('        menuPanel = panel;',('        ' + $testClass + '.Boot();' + [Environment]::NewLine + '        menuPanel = panel;'))
if ($Suite -eq 'Corner') { $bridge = $bridge.Replace('BehindGoalTests.Boot();','BehindGoalTests.CornerOnly = true; BehindGoalTests.Boot();') }
if($Suite -eq 'SkillOdds'){$bridge=$bridge.Replace('SkillReworkTests.Boot();','SkillReworkTests.OddsOnly=true;SkillReworkTests.Boot();')}
if($Suite -eq 'FormlessSafety'){$bridge=$bridge.Replace('SkillReworkTests.Boot();','SkillReworkTests.SafetyOnly=true;SkillReworkTests.Boot();')}
if($Suite -eq 'BicycleFlow'){$bridge=$bridge.Replace('SkillReworkTests.Boot();','SkillReworkTests.MotionOnly=true;SkillReworkTests.Boot();')}
if($Suite -eq 'HeaderLob'){$bridge=$bridge.Replace('CannonTotalTests.Boot();','CannonTotalTests.LobOnly=true;CannonTotalTests.Boot();')}
if($Suite -eq 'AerialTempo'){$bridge=$bridge.Replace('CannonTotalTests.Boot();','CannonTotalTests.TempoOnly=true;CannonTotalTests.Boot();')}
if($Suite -eq 'Lottery'){$bridge=$bridge.Replace('CannonTotalTests.Boot();','CannonTotalTests.PreviewOnly=true;CannonTotalTests.Boot();')}
if ($Suite -in @('SelfPlay','AbilitySelfPlay','AIPressure','ShotRescue','SkillSelfPlay','DownIntercept','CombatFlow','Startup','CannonTotal','Lottery','AerialTempo','HeaderLob','SkillRework','BicycleFlow','FormlessSafety','SkillOdds','FormlessSelfPlay')) { $bridge = $bridge.Replace('        string name = controller.gameObject.name;','        return controller.name == '+[char]34+'Fan'+[char]34+' || controller.name == '+[char]34+'Zhao'+[char]34+';'+[Environment]::NewLine+'        string name = controller.gameObject.name;') }
$bindings = [IO.File]::ReadAllText((Join-Path $source 'ControlBindings.cs'))
if ($Suite -in @('Gameplay','Controls','Magnetic','Contest','AI','Drop','Balance','Strategy','SelfPlay','Rolling','FanStrategy','Ability','Visual','AbilityContest','AbilitySelfPlay','AIPressure','ShotRescue','SkillSelfPlay','DownIntercept','CombatFlow','Startup','CannonTotal','Lottery','AerialTempo','HeaderLob','SkillRework','BicycleFlow','FormlessSafety','SkillOdds','FormlessSelfPlay')) { $bindings = $bindings.Replace('return Input.GetKey(key);',('return ' + $testClass + '.ReadKey(key);')).Replace('return Input.GetKeyDown(key);',('return ' + $testClass + '.ReadDown(key);')) }
if ($Suite -in @('Defense','AI','Strategy','SelfPlay','FanStrategy','Ability','Visual','AbilityContest','AbilitySelfPlay','AIPressure','ShotRescue','SkillSelfPlay','DownIntercept','CombatFlow','Startup','CannonTotal','Lottery','AerialTempo','HeaderLob','SkillRework','BicycleFlow','FormlessSafety','SkillOdds','FormlessSelfPlay')) { $bridge = $bridge.Replace('        if (controller == null) return false;', ('        if (!' + $testClass + '.UseAI) return false;') + [Environment]::NewLine + '        if (controller == null) return false;') }
[IO.File]::WriteAllText((Join-Path $testBuild 'GameAIMod.cs'),$bridge,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $testBuild 'ControlBindings.cs'),$bindings,[Text.UTF8Encoding]::new($false))
$testSources = $sources | Where-Object { (Split-Path $_ -Leaf) -notin @('GameAIMod.cs','ControlBindings.cs') }
if($Suite -eq 'FormlessSelfPlay'){
 $power=[IO.File]::ReadAllText((Join-Path $source PowerShot.cs)).Replace('s.lastBicycle=Time.time;if(UnityEngine.Random.value<.5f)','FormlessSelfPlayTests.Attempt();s.lastBicycle=Time.time;if(UnityEngine.Random.value<.5f)').Replace('s.bicycleUntil=Time.time+.9f;','FormlessSelfPlayTests.Started();s.bicycleUntil=Time.time+.9f;').Replace('PlayerSkills.CancelShotFlights();BuildPlayer.CancelFlights();PowerShot.CompleteHeader(s.player);','FormlessSelfPlayTests.Shot(s.player,bicycle,quality);PlayerSkills.CancelShotFlights();BuildPlayer.CancelFlights();PowerShot.CompleteHeader(s.player);')
 [IO.File]::WriteAllText((Join-Path $testBuild PowerShot.cs),$power,[Text.UTF8Encoding]::new($false));$testSources=$testSources|Where-Object {(Split-Path $_ -Leaf) -ne 'PowerShot.cs'};$testSources+=Join-Path $testBuild PowerShot.cs
}
if($Suite -in @('SkillRework','BicycleFlow','FormlessSafety','SkillOdds')){
 $ability=[IO.File]::ReadAllText((Join-Path $source AbilityMode.cs)).Replace('void Launch(Vector2 velocity){','void Launch(Vector2 velocity){SkillReworkTests.Launched(controller,velocity,ball.velocity);')
 $skills=[IO.File]::ReadAllText((Join-Path $source PlayerSkills.cs)).Replace('Vector2 target = CannonVelocity(direction,ball.velocity);','Vector2 target = CannonVelocity(direction,ball.velocity);SkillReworkTests.Launched(controller,target,ball.velocity);')
 $power=[IO.File]::ReadAllText((Join-Path $source PowerShot.cs)).Replace('s.bicycleUntil=Time.time+.9f;','SkillReworkTests.Started();s.bicycleUntil=Time.time+.9f;').Replace('poseTargets[i].SetValue(stick,poseAngles[i]);','SkillReworkTests.PoseStep(player,i,before,poseAngles[i],dt);poseTargets[i].SetValue(stick,poseAngles[i]);').Replace('s.armedUntil=-1f;if(bicycle)s.bicycleStruck=true;ShotCloseup.End(s.player);','SkillReworkTests.Forgiven(s.player,bicycle,quality,shot);s.armedUntil=-1f;if(bicycle)s.bicycleStruck=true;ShotCloseup.End(s.player);')
 foreach($entry in @(@{name='AbilityMode.cs';code=$ability},@{name='PlayerSkills.cs';code=$skills},@{name='PowerShot.cs';code=$power})){[IO.File]::WriteAllText((Join-Path $testBuild $entry.name),$entry.code,[Text.UTF8Encoding]::new($false))}
 $testSources=$testSources|Where-Object {(Split-Path $_ -Leaf) -notin @('AbilityMode.cs','PlayerSkills.cs','PowerShot.cs')};foreach($name in @('AbilityMode.cs','PlayerSkills.cs','PowerShot.cs')){$testSources+=Join-Path $testBuild $name}
}
if ($Suite -eq 'ShotRescue') {
 $ability=[IO.File]::ReadAllText((Join-Path $source 'AbilityMode.cs')).Replace('void Launch(Vector2 velocity){','void Launch(Vector2 velocity){ShotRescueTests.Launched(controller,velocity);').Replace('p.interceptUntil=-1f;p.interceptPending=false;p.Contact(collision);','ShotRescueTests.Contacted(collision);p.interceptUntil=-1f;p.interceptPending=false;p.Contact(collision);').Replace('Vector2 target=ShotTarget(level);bool weird=','Vector2 target=ShotTarget(level);ShotRescueTests.Aimed(controller,target);bool weird=')
 [IO.File]::WriteAllText((Join-Path $testBuild 'AbilityMode.cs'),$ability,[Text.UTF8Encoding]::new($false))
 $testSources=$testSources|Where-Object {(Split-Path $_ -Leaf) -ne 'AbilityMode.cs'}
 $testSources+=Join-Path $testBuild 'AbilityMode.cs'
}
if ($Suite -eq 'SkillSelfPlay') {
 $ability=[IO.File]::ReadAllText((Join-Path $source 'AbilityMode.cs')).Replace('leafCurve=new LeafShotProfile','SkillSelfPlayTests.Leaf();leafCurve=new LeafShotProfile').Replace('rescueUntil=Time.time+3.2f;','SkillSelfPlayTests.Rescue();rescueUntil=Time.time+3.2f;').Replace('void CannonHeader(){','void CannonHeader(){SkillSelfPlayTests.Cannon();').Replace('if(lob){cannonTarget.x-=','if(lob){SkillSelfPlayTests.Lobbed();cannonTarget.x-=')
 $skills=[IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('Vector2 target = CannonVelocity(direction,ball.velocity);','SkillSelfPlayTests.Cannon();Vector2 target = CannonVelocity(direction,ball.velocity);').Replace('curveProfile=new LeafShotProfile','SkillSelfPlayTests.Leaf();curveProfile=new LeafShotProfile').Replace('s.retreatUntil = Time.time + 2.6f;','SkillSelfPlayTests.Rescue();s.retreatUntil = Time.time + 2.6f;')
 [IO.File]::WriteAllText((Join-Path $testBuild 'AbilityMode.cs'),$ability,[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllText((Join-Path $testBuild 'PlayerSkills.cs'),$skills,[Text.UTF8Encoding]::new($false))
 $testSources=$testSources|Where-Object {(Split-Path $_ -Leaf) -notin @('AbilityMode.cs','PlayerSkills.cs')}
 $testSources+=Join-Path $testBuild 'AbilityMode.cs';$testSources+=Join-Path $testBuild 'PlayerSkills.cs'
}
if ($Suite -eq 'Ability') {
    $ability = [IO.File]::ReadAllText((Join-Path $source 'AbilityMode.cs')).Replace('void Launch(Vector2 velocity){','void Launch(Vector2 velocity){AbilityModeTests.Launched(controller,velocity);').Replace('p.interceptUntil=-1f;p.interceptPending=false;p.Contact(collision);','AbilityModeTests.Contacted(collision);p.interceptUntil=-1f;p.interceptPending=false;p.Contact(collision);').Replace('Vector2 target=ShotTarget(level);bool weird=','Vector2 target=ShotTarget(level);AbilityModeTests.Aimed(controller,target);bool weird=')
    [IO.File]::WriteAllText((Join-Path $testBuild 'AbilityMode.cs'),$ability,[Text.UTF8Encoding]::new($false))
    $testSources = $testSources | Where-Object { (Split-Path $_ -Leaf) -ne 'AbilityMode.cs' }
    $testSources += Join-Path $testBuild 'AbilityMode.cs'
}
if ($Suite -in @('DownIntercept','Drop')) {
 $skills=[IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('        bool power = PowerShot.OnBallCollision(collision);','        DownInterceptTests.Contacted(collision); bool power = PowerShot.OnBallCollision(collision);')
 [IO.File]::WriteAllText((Join-Path $testBuild 'PlayerSkills.cs'),$skills,[Text.UTF8Encoding]::new($false))
 $testSources=$testSources|Where-Object {(Split-Path $_ -Leaf) -ne 'PlayerSkills.cs'}
 $testSources+=Join-Path $testBuild 'PlayerSkills.cs'
}
if ($Suite -in @('CannonTotal','Lottery','AerialTempo','HeaderLob')) {
 $ability=[IO.File]::ReadAllText((Join-Path $source 'AbilityMode.cs')).Replace('void Launch(Vector2 velocity){','void Launch(Vector2 velocity){CannonTotalTests.Launched(controller,velocity,ball.velocity);').Replace('UnityEngine.Random.value<chance','CannonTotalTests.WantLob(chance)').Replace('if(lob){cannonTarget.x-=','if(lob){CannonTotalTests.Lobbed(controller);cannonTarget.x-=')
 $skills=[IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('Vector2 target = CannonVelocity(direction,ball.velocity);','Vector2 target = CannonVelocity(direction,ball.velocity);CannonTotalTests.Launched(controller,target,ball.velocity);')
 $power=[IO.File]::ReadAllText((Join-Path $source 'PowerShot.cs')).Replace('AbilityMode.CannonWorldContact(collision);','CannonTotalTests.WorldContact(collision);AbilityMode.CannonWorldContact(collision);')
 foreach($entry in @(@{name='AbilityMode.cs';code=$ability},@{name='PlayerSkills.cs';code=$skills},@{name='PowerShot.cs';code=$power})){[IO.File]::WriteAllText((Join-Path $testBuild $entry.name),$entry.code,[Text.UTF8Encoding]::new($false))}
 $testSources=$testSources|Where-Object {(Split-Path $_ -Leaf) -notin @('AbilityMode.cs','PlayerSkills.cs','PowerShot.cs')};foreach($name in @('AbilityMode.cs','PlayerSkills.cs','PowerShot.cs')){$testSources+=Join-Path $testBuild $name}
}
if ($Suite -eq 'CombatFlow') {
 $ability=[IO.File]::ReadAllText((Join-Path $source 'AbilityMode.cs')).Replace('void Launch(Vector2 velocity){','void Launch(Vector2 velocity){CombatFlowTests.Applied(controller);CombatFlowTests.Launched(controller,velocity);')
 $skills=[IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('Vector2 target = CannonVelocity(direction,ball.velocity);','CombatFlowTests.Applied(controller); Vector2 target = CannonVelocity(direction,ball.velocity);CombatFlowTests.Launched(controller,target);')
 $power=[IO.File]::ReadAllText((Join-Path $source 'PowerShot.cs')).Replace('headerUntil=-1f;Vector2 target=OrdinaryHeaderVelocity(controller,ball.velocity);','headerUntil=-1f;Vector2 target=OrdinaryHeaderVelocity(controller,ball.velocity);CombatFlowTests.Ordinary(controller,target,ball.velocity);')
 foreach($entry in @(@{name='AbilityMode.cs';code=$ability},@{name='PlayerSkills.cs';code=$skills},@{name='PowerShot.cs';code=$power})){[IO.File]::WriteAllText((Join-Path $testBuild $entry.name),$entry.code,[Text.UTF8Encoding]::new($false))}
 $testSources=$testSources|Where-Object {(Split-Path $_ -Leaf) -notin @('AbilityMode.cs','PlayerSkills.cs','PowerShot.cs')}
 foreach($name in @('AbilityMode.cs','PlayerSkills.cs','PowerShot.cs')){$testSources+=Join-Path $testBuild $name}
}
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
if ($Suite -eq 'SelfPlay') {
    $skills = [IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('        animator.SetTrigger(action);','        FanSelfPlayTests.Action(player, action); animator.SetTrigger(action);').Replace('{ s.retreatUntil = Time.time + 2.6f;', '{ FanSelfPlayTests.RescueStarted(); s.retreatUntil = Time.time + 2.6f;').Replace('s.retreatUntil = -1f; PowerShot.CompleteHeader(player); return true;','FanSelfPlayTests.RescueStrike(); s.retreatUntil = -1f; PowerShot.CompleteHeader(player); return true;').Replace('PowerShot.CompleteHeader(controller);','FanSelfPlayTests.Contact(controller.name); PowerShot.CompleteHeader(controller);')
    [IO.File]::WriteAllText((Join-Path $testBuild 'PlayerSkills.cs'),$skills,[Text.UTF8Encoding]::new($false))
    $testSources = $testSources | Where-Object { (Split-Path $_ -Leaf) -ne 'PlayerSkills.cs' }
    $testSources += Join-Path $testBuild 'PlayerSkills.cs'
}
if ($Suite -eq 'Strategy') {
    $skills = [IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('        animator.SetTrigger(action);','        ZhaoStrategyTests.Action(player,action); animator.SetTrigger(action);')
    [IO.File]::WriteAllText((Join-Path $testBuild 'PlayerSkills.cs'),$skills,[Text.UTF8Encoding]::new($false))
    $testSources = $testSources | Where-Object { (Split-Path $_ -Leaf) -ne 'PlayerSkills.cs' }
    $testSources += Join-Path $testBuild 'PlayerSkills.cs'
}
if ($Suite -eq 'FanStrategy') {
    $skills=[IO.File]::ReadAllText((Join-Path $source 'PlayerSkills.cs')).Replace('        animator.SetTrigger(action);','        FanStrategyTests.Action(player,action); animator.SetTrigger(action);').Replace('                HighlightBall(1, .85f);','                FanStrategyTests.HeaderStrike(); HighlightBall(1,.85f);')
    $power=[IO.File]::ReadAllText((Join-Path $source 'PowerShot.cs')).Replace('        if (fan) target = ball.velocity * SpeedMultiplier;','        if (fan) { FanStrategyTests.PowerStrike(); target = ball.velocity * SpeedMultiplier; }')
    [IO.File]::WriteAllText((Join-Path $testBuild 'PlayerSkills.cs'),$skills,[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText((Join-Path $testBuild 'PowerShot.cs'),$power,[Text.UTF8Encoding]::new($false))
    $testSources=$testSources | Where-Object { (Split-Path $_ -Leaf) -notin @('PlayerSkills.cs','PowerShot.cs') }
    $testSources+=Join-Path $testBuild 'PlayerSkills.cs';$testSources+=Join-Path $testBuild 'PowerShot.cs'
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
if ($PrepareSelfPlay) {
    if ($Suite -ne 'SelfPlay') { throw 'Preparation only supports SelfPlay' }
    @{policyHash=$policyHash;testHash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'FanSelfPlayTests.cs')).Hash;assemblyHash=(Get-FileHash -LiteralPath (Join-Path $testPlayer 'FanZhiYi_Data\Managed\GameAIMod.dll')).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testPlayer 'selfplay-prepared.json') -Encoding utf8
    Write-Output ('SelfPlay prepared: '+$Edition); return
}
$logSuite = $Suite.ToLower() + $(if ($Baseline) { '-baseline' } else { '' })
$taskLog = Join-Path $PSScriptRoot ('ability-' + $Edition.ToLower() + '-' + $logSuite + '.log')
$selfplayArgs = if ($Suite -in @('SelfPlay','AbilitySelfPlay','SkillSelfPlay','FormlessSelfPlay')) { @('-batchmode','-nographics') } else { @() }
$launchArgs = $selfplayArgs + @('-selfplaySpeed',$SelfPlaySpeed.ToString(),'-policyHash',$policyHash,'-selfplayGoals',$GoalTarget.ToString(),'-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"' + $taskLog + '"'))
$taskProcess = Start-Process -FilePath (Join-Path $testPlayer 'FanZhiYi.exe') -WorkingDirectory $testPlayer -ArgumentList $launchArgs -WindowStyle Hidden -PassThru
$runStarted=Get-Date
while (-not $taskProcess.WaitForExit(30000)) { if($Suite -notin @('SelfPlay','AbilitySelfPlay','SkillSelfPlay','FormlessSelfPlay') -and ((Get-Date)-$runStarted).TotalSeconds -gt 150){$taskProcess.Kill();throw "Isolated runtime timed out"}; Write-Output ($Edition + ' ' + $Suite + ' verification running') }
Write-Output ('Exit: ' + $taskProcess.ExitCode)
Select-String -LiteralPath $taskLog -Pattern '(VISUAL|ABILITY|ROLLING|SELFPLAY|FANSTRATEGY|STRATEGY|BURSTRANGE|AIMATCH|CONTEST|MAGNETIC|POWER|BOUNDARY|CONTROLS|DEFENSE|GOALHEIGHT|DROP|BALANCE) (FAIL|COMPLETE)|Exception:|TIMEOUT|GotoState' | Select-Object -First 25
if ($taskProcess.ExitCode -ne 0) { throw 'Runtime checks failed' }

@{edition=$Edition.ToLower();suite=$Suite.ToLower();policyHash=$policyHash;logHash=(Get-FileHash -LiteralPath $taskLog).Hash;completedAt=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot ('ability-'+$Edition.ToLower()+'-'+$logSuite+'-validation.json')) -Encoding utf8