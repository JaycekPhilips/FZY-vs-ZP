param([ValidateSet('Original','Experiment')][string]$Edition='Original',[string]$Suite='Performance',[switch]$CompileOnly,[switch]$All,[switch]$Status,[switch]$Stop,[switch]$Baseline)
$ErrorActionPreference='Stop'
if($All){
 foreach($testSuite in @('Performance','Gameplay','Controls','Boundary','Defense','Goals','Magnetic','Contest','AI','Drop','Balance','Corner','Network','UI','NetworkPair')){& $PSCommandPath -Edition $Edition -Suite $testSuite -CompileOnly:$CompileOnly}
 return
}
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$gameRoot=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'AI Mod Source\build\Mono.Cecil.dll')} | Select-Object -First 1).FullName
$editionRoot=if($Edition -eq 'Experiment'){Join-Path $gameRoot '实验版（模型75%）'}else{$gameRoot}
$source=Join-Path $editionRoot 'AI Mod Source'
$testRoot=Join-Path $PSScriptRoot ('regression-'+$Edition.ToLower())
if($Baseline){$source=Join-Path $PSScriptRoot ('baseline-'+$Edition.ToLower());$testRoot=Join-Path $PSScriptRoot ('regression-baseline-'+$Edition.ToLower())}
if($Status -or $Stop){
 foreach($player in @(Get-Process -Name FanZhiYi -ErrorAction SilentlyContinue)){if($player.Path -eq (Join-Path $testRoot 'FanZhiYi.exe')){
  $player | Select-Object Id,CPU,Responding,WorkingSet,Path;if($Stop){Stop-Process -Id $player.Id -Force}
 }}
 Get-Content -LiteralPath (Join-Path $testRoot ($Suite+'.log')) -Tail 16 -ErrorAction SilentlyContinue
 return
}
$testBuild=Join-Path $testRoot $Suite
New-Item -ItemType Directory -Path $testRoot,$testBuild -Force | Out-Null
if(-not(Test-Path -LiteralPath (Join-Path $testRoot 'FanZhiYi.exe'))){
 foreach($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe')){Copy-Item -LiteralPath (Join-Path $editionRoot $name) -Destination $testRoot}
 foreach($name in @('FanZhiYi_Data','MonoBleedingEdge')){Copy-Item -LiteralPath (Join-Path $editionRoot $name) -Destination $testRoot -Recurse}
}
$managed=Join-Path $editionRoot 'FanZhiYi_Data\Managed'
$refs=@('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.ImageConversionModule.dll') | ForEach-Object {'/r:'+(Join-Path $managed $_)}
$names=@('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ControlBindings.cs','PowerShot.cs','MagneticFoot.cs','LanMultiplayer.cs')
if($Edition -eq 'Experiment'){$names+='ExperimentScale.cs'}
$testClass=switch($Suite){
 'NetworkPair'{'NetworkPairTests'}
 'Performance'{'PerformanceRegressionTests'} 'Gameplay'{'BurstRangeTests'} 'Controls'{'ControlsTests'} 'Boundary'{'BehindGoalTests'} 'Defense'{'ZhaoVolleyTests'} 'Goals'{'GoalHeightTests'} 'Magnetic'{'MagneticSpeedTests'} 'Contest'{'KnockbackTests'} 'AI'{'AIContestAirTests'} 'Drop'{'ThighInterceptTests'} 'Balance'{'RescueBalanceTests'} 'Corner'{'BehindGoalTests'} 'Network'{'LanMultiplayerTests'} 'UI'{'LanMenuUiTests'} default{throw 'Unknown suite'}
}
$sources=@()
foreach($name in $names){
 $code=[IO.File]::ReadAllText((Join-Path $source $name))
 if($name -eq 'GameAIMod.cs'){
  $code=$code.Replace('        menuPanel = panel;',('        Application.runInBackground = true; '+$testClass+'.Boot();')+[Environment]::NewLine+'        menuPanel = panel;')
  if($Suite -eq 'Corner'){$code=$code.Replace('BehindGoalTests.Boot();','BehindGoalTests.CornerOnly = true; BehindGoalTests.Boot();')}
  if($Suite -in @('Defense','AI')){$code=$code.Replace('        if (controller == null) return false;',('        if (!'+$testClass+'.UseAI) return false;')+[Environment]::NewLine+'        if (controller == null) return false;')}
 }
 if($name -eq 'ControlBindings.cs' -and $Suite -in @('Gameplay','Controls','Magnetic','Contest','AI','Drop','Balance')){$code=$code.Replace('return Input.GetKey(key);',('return '+$testClass+'.ReadKey(key);')).Replace('return Input.GetKeyDown(key);',('return '+$testClass+'.ReadDown(key);'))}
 if($Suite -eq 'NetworkPair' -and $name -in @('LanMultiplayer.cs','ControlBindings.cs')){$code=$code.Replace('Input.GetKey(key)','NetworkPairTests.ReadKey(key)').Replace('Input.GetKeyDown(key)','NetworkPairTests.ReadDown(key)')}
 if($name -eq 'PlayerSkills.cs' -and $Suite -eq 'Gameplay'){$code=$code.Replace('        bool power = PowerShot.OnBallCollision(collision);','        BurstRangeTests.BeforeContact(collision);'+[Environment]::NewLine+'        bool power = PowerShot.OnBallCollision(collision);'+[Environment]::NewLine+'        BurstRangeTests.AfterContact(collision, power);')}
 if($name -eq 'PlayerSkills.cs' -and $Suite -eq 'Contest'){$code=$code.Replace('        lastTackle = Time.time;','        KnockbackTests.OnTackle(controller, opponent.controller, aerial, throughBall);'+[Environment]::NewLine+'        lastTackle = Time.time;')}
 if($Suite -eq 'Balance'){
  if($name -eq 'PowerShot.cs'){$code=$code.Replace('        ball.AddForce(ball.velocity * ((HeaderSpeedMultiplier - 1f) * ball.mass), ForceMode2D.Impulse);','        Vector2 beforeHeader = ball.velocity; ball.AddForce(ball.velocity * ((HeaderSpeedMultiplier - 1f) * ball.mass), ForceMode2D.Impulse); RescueBalanceTests.HeaderScaled(controller, beforeHeader, ball.velocity);')}
  if($name -eq 'PlayerSkills.cs'){$code=$code.Replace('                PowerShot.CompleteHeader(controller);','                RescueBalanceTests.AssistedHeader(target, ball.velocity); PowerShot.CompleteHeader(controller);').Replace('        s.retreatUntil = -1f; PowerShot.CompleteHeader(player); return true;','        RescueBalanceTests.RescueStrike(s.ball.velocity); s.retreatUntil = -1f; PowerShot.CompleteHeader(player); return true;').Replace('        bool power = PowerShot.OnBallCollision(collision);','        RescueBalanceTests.Contact(collision); bool power = PowerShot.OnBallCollision(collision);')}
 }
 $file=Join-Path $testBuild $name;[IO.File]::WriteAllText($file,$code,[Text.UTF8Encoding]::new($false));$sources+=$file
}
if($Suite -eq 'Performance'){$sources+=Join-Path $PSScriptRoot 'PerformanceRegressionTests.cs';$sources+=Join-Path $PSScriptRoot 'PacketCodecTests.cs'}elseif($Suite -eq 'NetworkPair'){$sources+=Join-Path $PSScriptRoot 'NetworkPairTests.cs'}else{$sources+=Join-Path $gameRoot ('AI Mod Source\build\'+$testClass+'.cs')}
$csc='C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:library /optimize+ ('/out:'+(Join-Path $testBuild 'GameAIMod.dll')) $refs $sources
if($LASTEXITCODE -ne 0){throw 'Regression compile failed'}
& $csc /nologo /target:exe /optimize+ ('/out:'+(Join-Path $testBuild 'PatchGame.exe')) ('/r:'+(Join-Path $gameRoot 'AI Mod Source\build\Mono.Cecil.dll')) (Join-Path $source 'PatchGame.cs')
if($LASTEXITCODE -ne 0){throw 'Patcher compile failed'}
Copy-Item -LiteralPath (Join-Path $gameRoot 'AI Mod Source\build\Mono.Cecil.dll') -Destination $testBuild -Force
& (Join-Path $testBuild 'PatchGame.exe') (Join-Path $source 'Assembly-CSharp.original.dll') (Join-Path $testBuild 'GameAIMod.dll') (Join-Path $testBuild 'Assembly-CSharp.dll')
if($LASTEXITCODE -ne 0){throw 'Regression patch failed'}
if($CompileOnly){Write-Output ('COMPILED '+$Edition+' '+$Suite);return}
foreach($name in @('GameAIMod.dll','Assembly-CSharp.dll')){Copy-Item -LiteralPath (Join-Path $testBuild $name) -Destination (Join-Path $testRoot ('FanZhiYi_Data\Managed\'+$name)) -Force}
if($Suite -eq 'NetworkPair'){
 $guestRoot=$testRoot+'-guest'
 New-Item -ItemType Directory -Path $guestRoot -Force | Out-Null
 if(-not(Test-Path -LiteralPath (Join-Path $guestRoot 'FanZhiYi.exe'))){
  foreach($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe','FanZhiYi_Data','MonoBleedingEdge')){Copy-Item -LiteralPath (Join-Path $testRoot $name) -Destination $guestRoot -Recurse}
 }
 foreach($name in @('GameAIMod.dll','Assembly-CSharp.dll')){Copy-Item -LiteralPath (Join-Path $testBuild $name) -Destination (Join-Path $guestRoot ('FanZhiYi_Data\Managed\'+$name)) -Force}
 $pairProcesses=@()
 foreach($role in @('host','guest')){
  $playerRoot=if($role -eq 'host'){$testRoot}else{$guestRoot}
  $pairLog=Join-Path $testRoot ('NetworkPair-'+$role+'.log')
  $args=@('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$pairLog+'"'))
  if($role -eq 'host'){$args+='-network-host'}
  $pairProcesses+=Start-Process -FilePath (Join-Path $playerRoot 'FanZhiYi.exe') -WorkingDirectory $playerRoot -ArgumentList $args -WindowStyle Hidden -PassThru
 }
 try{
  foreach($player in $pairProcesses){if(-not $player.WaitForExit(60000)){throw 'Paired network regression timeout'}}
  foreach($role in @('host','guest')){
   $pairLog=Join-Path $testRoot ('NetworkPair-'+$role+'.log');$text=[IO.File]::ReadAllText($pairLog)
   Select-String -LiteralPath $pairLog -Pattern 'COMPLETE|FAIL|Exception:' | ForEach-Object Line
   if($text -notmatch 'PAIRTEST COMPLETE[^\r\n]*failures=0' -or $text -match '\bFAIL\b|Exception:'){throw ('Paired network regression failed: '+$role)}
  }
 }finally{foreach($player in $pairProcesses){if(-not $player.HasExited){$player.Kill()}}}
 Write-Output ('REGRESSION SUCCESS '+$Edition+' NetworkPair')
 return
}
$log=Join-Path $testRoot ($Suite+'.log')
$process=Start-Process -FilePath (Join-Path $testRoot 'FanZhiYi.exe') -WorkingDirectory $testRoot -ArgumentList @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
$start=Get-Date
while(-not $process.WaitForExit(30000)){if(((Get-Date)-$start).TotalSeconds -gt 180){$process.Kill();throw 'Regression timeout'};Write-Output ('Running '+$Edition+' '+$Suite)}
$logText=[IO.File]::ReadAllText($log)
$result=@(Select-String -LiteralPath $log -Pattern '\b(COMPLETE|FAIL|INCOMPLETE)|Exception:|PERFMICRO')
$result | ForEach-Object Line
if($logText -notmatch '\bCOMPLETE[^\r\n]*failures=0' -or $logText -match '\bFAIL\b|\bINCOMPLETE\b|Exception:'){
 throw ('Regression failed: '+$log)
}
Write-Output ('REGRESSION SUCCESS '+$Edition+' '+$Suite)
