param([ValidateSet('Original','Experiment')][string]$Edition='Original',[switch]$Test,[switch]$UiTest)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$gameRoot=if($Edition -eq 'Experiment'){(Get-ChildItem -LiteralPath $taskRoot -Directory | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'AI Mod Source\ExperimentScale.cs')} | Select-Object -First 1).FullName}else{$taskRoot}
$source=Join-Path $gameRoot 'AI Mod Source'
$release=Join-Path $PSScriptRoot ('lan-'+$Edition.ToLower()+'-release')
New-Item -ItemType Directory -Path $release -Force | Out-Null
$managed=Join-Path $gameRoot 'FanZhiYi_Data\Managed'
$refs=@('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.ImageConversionModule.dll') | ForEach-Object {'/r:'+(Join-Path $managed $_)}
$sources=@('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ControlBindings.cs','PowerShot.cs','MagneticFoot.cs','LanMultiplayer.cs') | ForEach-Object {Join-Path $source $_}
if($Edition -eq 'Experiment'){$sources+=Join-Path $source 'ExperimentScale.cs'}
$csc='C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:library /optimize+ ('/out:'+(Join-Path $release 'GameAIMod.dll')) $refs $sources
if($LASTEXITCODE -ne 0){throw 'Production compilation failed'}
& $csc /nologo /target:exe /optimize+ ('/out:'+(Join-Path $release 'PatchGame.exe')) ('/r:'+(Join-Path $PSScriptRoot 'Mono.Cecil.dll')) (Join-Path $source 'PatchGame.cs')
if($LASTEXITCODE -ne 0){throw 'Patcher compilation failed'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mono.Cecil.dll') -Destination $release -Force
& (Join-Path $release 'PatchGame.exe') (Join-Path $source 'Assembly-CSharp.original.dll') (Join-Path $release 'GameAIMod.dll') (Join-Path $release 'Assembly-CSharp.dll')
if($LASTEXITCODE -ne 0){throw 'Game patching failed'}
Write-Output ('LAN production assemblies compiled for '+$Edition+': '+$release)
if($Test -or $UiTest){
    $testBuild=Join-Path $PSScriptRoot ('lan-'+$Edition.ToLower()+'-test-build')
    $testPlayer=Join-Path $PSScriptRoot ('lan-test-player-'+$Edition.ToLower())
    New-Item -ItemType Directory -Path $testBuild,$testPlayer -Force | Out-Null
    if(-not (Test-Path -LiteralPath (Join-Path $testPlayer 'FanZhiYi.exe'))){
        foreach($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe')){Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer}
        foreach($name in @('FanZhiYi_Data','MonoBleedingEdge')){Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer -Recurse}
        if(Test-Path -LiteralPath (Join-Path $gameRoot '按键设置.ini')){Copy-Item -LiteralPath (Join-Path $gameRoot '按键设置.ini') -Destination $testPlayer}
    }
    $testClass=if($UiTest){'LanMenuUiTests'}else{'LanMultiplayerTests'}
    $bridge=[IO.File]::ReadAllText((Join-Path $source 'GameAIMod.cs')).Replace('        menuPanel = panel;',('        '+$testClass+'.Boot();')+[Environment]::NewLine+'        menuPanel = panel;')
    [IO.File]::WriteAllText((Join-Path $testBuild 'GameAIMod.cs'),$bridge,[Text.UTF8Encoding]::new($false))
    $testSources=$sources | Where-Object {(Split-Path $_ -Leaf) -notin @('GameAIMod.cs','ControlBindings.cs')}
    $testSources+=(Join-Path $testBuild 'GameAIMod.cs')
    $testSources+=(Join-Path $source 'ControlBindings.cs')
    $testSources+=(Join-Path $PSScriptRoot ($testClass+'.cs'))
    & $csc /nologo /target:library /optimize+ ('/out:'+(Join-Path $testBuild 'GameAIMod.dll')) $refs $testSources
    if($LASTEXITCODE -ne 0){throw 'LAN test assembly compilation failed'}
    $testManaged=Join-Path $testPlayer 'FanZhiYi_Data\Managed'
    Copy-Item -LiteralPath (Join-Path $testBuild 'GameAIMod.dll') -Destination (Join-Path $testManaged 'GameAIMod.dll') -Force
    Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $testManaged 'Assembly-CSharp.dll') -Force
    $log=Join-Path $PSScriptRoot ('lan-'+$Edition.ToLower()+'-test.log')
    $process=Start-Process -FilePath (Join-Path $testPlayer 'FanZhiYi.exe') -WorkingDirectory $testPlayer -ArgumentList @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(30000)){$process.Kill();throw 'LAN runtime test timed out'}
    $logText=[IO.File]::ReadAllText($log)
    if($logText -notmatch 'LAN_TEST COMPLETE checks=(\d+) failures=0' -or $logText -match 'LAN_TEST FAIL|LAN_TEST INCOMPLETE'){
        Select-String -LiteralPath $log -Pattern 'LAN_TEST|Exception:|Error|timeout'
        throw ('LAN runtime test failed; inspect '+$log)
    }
    Write-Output ('LAN isolated runtime test passed: '+$Matches[1]+' checks; '+$log)
}
