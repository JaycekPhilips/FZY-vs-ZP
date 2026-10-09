param([ValidateSet('Boundary','Controls','Goals')][string]$Kind)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Join-Path $taskRoot '实验版（模型75%）\AI Mod Source'
$release = Join-Path $PSScriptRoot 'scale-release'
$testBuild = Join-Path $PSScriptRoot ('scale-' + $Kind.ToLower() + '-build')
$testPlayer = Join-Path $PSScriptRoot 'scale-test-player'
New-Item -ItemType Directory -Path $testBuild -Force | Out-Null
$managed = Join-Path $taskRoot 'FanZhiYi_Data\Managed'
$refs = @('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.ImageConversionModule.dll') | ForEach-Object { '/r:' + (Join-Path $managed $_) }
$testClass = if ($Kind -eq 'Boundary') { 'BehindGoalTests' } elseif ($Kind -eq 'Goals') { 'GoalHeightTests' } else { 'ControlsTests' }
$bridge = [IO.File]::ReadAllText((Join-Path $source 'GameAIMod.cs'))
$anchor = '        menuPanel = panel;'
if (-not $bridge.Contains($anchor)) { throw 'Bootstrap anchor missing' }
$bridge = $bridge.Replace($anchor,('        ' + $testClass + '.Boot();' + [Environment]::NewLine + $anchor))
$bindings = [IO.File]::ReadAllText((Join-Path $source 'ControlBindings.cs'))
if ($Kind -eq 'Controls') { $bindings = $bindings.Replace('return Input.GetKey(key);','return ControlsTests.ReadKey(key);').Replace('return Input.GetKeyDown(key);','return ControlsTests.ReadDown(key);') }
[IO.File]::WriteAllText((Join-Path $testBuild 'GameAIMod.cs'),$bridge,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $testBuild 'ControlBindings.cs'),$bindings,[Text.UTF8Encoding]::new($false))
$sources = @('PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ExperimentScale.cs') | ForEach-Object { Join-Path $source $_ }
& 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' /nologo /target:library /optimize+ ('/out:' + (Join-Path $testBuild 'GameAIMod.dll')) $refs $sources (Join-Path $testBuild 'GameAIMod.cs') (Join-Path $testBuild 'ControlBindings.cs') (Join-Path $PSScriptRoot ($testClass + '.cs'))
if ($LASTEXITCODE -ne 0) { throw 'Experimental checks compilation failed' }
Copy-Item -LiteralPath (Join-Path $testBuild 'GameAIMod.dll') -Destination (Join-Path $testPlayer 'FanZhiYi_Data\Managed\GameAIMod.dll') -Force
Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $testPlayer 'FanZhiYi_Data\Managed\Assembly-CSharp.dll') -Force
$taskLog = Join-Path $PSScriptRoot ('scale-' + $Kind.ToLower() + '-test.log')
$taskProcess = Start-Process -FilePath (Join-Path $testPlayer 'FanZhiYi.exe') -WorkingDirectory $testPlayer -ArgumentList @('-screen-width','1100','-screen-height','650','-screen-fullscreen','0','-logFile',('"' + $taskLog + '"')) -WindowStyle Hidden -PassThru
while (-not $taskProcess.WaitForExit(30000)) { Write-Output ($Kind + ' verification running') }
Write-Output ('Exit: ' + $taskProcess.ExitCode)
Select-String -LiteralPath $taskLog -Pattern '(BOUNDARY|CONTROLS|GOALHEIGHT) (FAIL|COMPLETE|DUMP)|Exception|TIMEOUT|GotoState'
if ($taskProcess.ExitCode -ne 0) { throw ($Kind + ' runtime checks failed') }
