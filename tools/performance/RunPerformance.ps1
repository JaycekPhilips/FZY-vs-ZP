param([ValidateSet('baseline','candidate')][string]$Phase='baseline',[ValidateSet('Original','Experiment')][string]$Edition='Original',[switch]$Smoke,[switch]$Status,[switch]$Stop,[switch]$ContinueMatrix,[switch]$ExportResults,[switch]$Repair,[string]$Scenarios,[switch]$StopMatrix,[switch]$Quick,[switch]$InstallQuick,[switch]$ReportQuick,[switch]$QuickRegressions,[switch]$QuickRemainder,[switch]$QuickExperiment)
$ErrorActionPreference='Stop'
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if($StopMatrix){
 foreach($process in (Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'")){
  if($process.CommandLine -match 'RunPerformance\.ps1.+-ContinueMatrix' -and $process.CommandLine.Contains($PSScriptRoot)){Stop-Process -Id $process.ProcessId -Force;Write-Output ('Stopped benchmark coordinator '+$process.ProcessId)}
 }
 foreach($player in @(Get-Process -Name FanZhiYi -ErrorAction SilentlyContinue)){
  if($player.Path.StartsWith($PSScriptRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){Stop-Process -Id $player.Id -Force;Write-Output ('Stopped isolated test player '+$player.Id)}
 }
 return
}
if($Quick -or $QuickRegressions -or $QuickRemainder -or $QuickExperiment){
 if($Quick){foreach($quickPhase in @('baseline','candidate')){foreach($quickEdition in @('Original','Experiment')){& $PSCommandPath -Phase $quickPhase -Edition $quickEdition -Smoke}}}
 $quickVersions=if($QuickExperiment){@('Experiment')}else{@('Original','Experiment')}
 foreach($quickEdition in $quickVersions){foreach($quickSuite in @('Performance','Gameplay','UI','NetworkPair')){
  if($QuickRemainder -and $quickEdition -eq 'Original' -and $quickSuite -eq 'Performance'){continue}
  & (Join-Path $PSScriptRoot 'RunRegressions.ps1') -Edition $quickEdition -Suite $quickSuite
 }}
 & $PSCommandPath -ExportResults -Smoke
 Write-Output 'QUICK CHECKS ALL COMPLETE'
 return
}
if($InstallQuick){& (Join-Path $workspace 'tools\performance\InstallRelease.ps1') -MeasurementRoot $PSScriptRoot -Quick;return}
if($ReportQuick){& (Join-Path $workspace 'tools\performance\ReportQuick.ps1') -MeasurementRoot $PSScriptRoot;return}
if($Repair){
 $folder=Join-Path $PSScriptRoot ($Phase+'-'+$Edition.ToLower())
 $invalid=@()
 foreach($file in (Get-ChildItem -LiteralPath $folder -Filter '*-summary.csv' -File)){
  $row=Import-Csv -LiteralPath $file.FullName
  if([double]$row.seconds -gt 122 -or [double]$row.max_ms -gt 5000){$invalid+=$file.Name.Replace('-summary.csv','')}
 }
 if($invalid.Count){
  $archive=Join-Path $folder ('interrupted-'+(Get-Date -Format 'yyyyMMdd-HHmmss'));New-Item -ItemType Directory -Path $archive | Out-Null
  Copy-Item -LiteralPath (Join-Path $folder 'run.log') -Destination $archive
  foreach($scenario in $invalid){Get-ChildItem -LiteralPath $folder -Filter ($scenario+'-*.csv') -File | Copy-Item -Destination $archive}
  Write-Output ('ENVIRONMENT INTERRUPTION RETRY '+$Phase+' '+$Edition+' '+($invalid -join ','))
  & $PSCommandPath -Phase $Phase -Edition $Edition -Scenarios ($invalid -join ',')
  foreach($scenario in $invalid){
   $row=Import-Csv -LiteralPath (Join-Path $folder ($scenario+'-summary.csv'))
   if([double]$row.seconds -gt 122 -or [double]$row.max_ms -gt 5000){throw ('Environment still interrupted after retry: '+$scenario)}
  }
 }
 return
}
if($ContinueMatrix){
 $baselineFolder=Join-Path $PSScriptRoot 'baseline-original'
 $baselineExe=Join-Path $baselineFolder 'FanZhiYi.exe'
 foreach($running in @(Get-Process -Name FanZhiYi -ErrorAction SilentlyContinue)){
  if($running.Path -eq $baselineExe){
   while(-not $running.WaitForExit(30000)){Write-Output ('MATRIX waiting for original baseline PID='+$running.Id)}
  }else{throw ('Unrelated game is running: '+$running.Path)}
 }
 if([IO.File]::ReadAllText((Join-Path $baselineFolder 'run.log')) -notmatch 'PERF ALL COMPLETE'){throw 'Original baseline must complete successfully before continuation'}
 foreach($job in @(@('baseline','Experiment'),@('candidate','Original'),@('candidate','Experiment'))){& $PSCommandPath -Phase $job[0] -Edition $job[1]}
 foreach($repairPhase in @('baseline','candidate')){foreach($repairEdition in @('Original','Experiment')){& $PSCommandPath -Phase $repairPhase -Edition $repairEdition -Repair}}
 foreach($version in @('Original','Experiment')){& (Join-Path $PSScriptRoot 'RunRegressions.ps1') -Edition $version -All}
 & $PSCommandPath -ExportResults
 Write-Output 'MATRIX ALL COMPLETE'
 return
}
if($ExportResults){
 $evidence=Join-Path $workspace 'performance'
 New-Item -ItemType Directory -Path $evidence -Force | Out-Null
 $summaries=@();$methods=@();$engines=@();$validation=@()
 foreach($phaseName in @('baseline','candidate')){foreach($editionName in @('original','experiment')){
  $folder=Join-Path $PSScriptRoot ($phaseName+'-'+$editionName+$(if($Smoke){'-smoke'}else{''}))
  $map=@{};foreach($line in ([IO.File]::ReadAllLines((Join-Path $folder 'method-map.csv')) | Select-Object -Skip 1)){
   $columns=$line -split ',',2;$map[$columns[0]]=$columns[1].Trim('"')
  }
  foreach($file in (Get-ChildItem -LiteralPath $folder -Filter '*-summary.csv' -File)){
   $scenario=$file.Name.Replace('-summary.csv','')
   $row=Import-Csv -LiteralPath $file.FullName
   if([double]$row.seconds -gt 122 -or [double]$row.max_ms -gt 5000){throw ('Interrupted scenario must be repeated before export: '+$file.FullName)}
   $row | Add-Member phase $phaseName;$row | Add-Member edition $editionName;$row | Add-Member scenario $scenario
   $summaries+=$row
   foreach($method in (Import-Csv -LiteralPath (Join-Path $folder ($scenario+'-methods.csv')))){
    $methods+=[pscustomobject]@{phase=$phaseName;edition=$editionName;scenario=$scenario;method=$map[$method.id];calls=$method.calls;total_ms=$method.total_ms;ms_per_1000_frames=1000*[double]$method.total_ms/[double]$row.frames;us_per_call=1000*[double]$method.total_ms/[double]$method.calls}
   }
   foreach($engine in (Import-Csv -LiteralPath (Join-Path $folder ($scenario+'-engine.csv')))){
    $engine | Add-Member phase $phaseName;$engine | Add-Member edition $editionName;$engine | Add-Member scenario $scenario;$engines+=$engine
   }
  }
  $expected=if($Smoke){6}else{18}
  if(@($summaries | Where-Object { $_.phase -eq $phaseName -and $_.edition -eq $editionName }).Count -ne $expected){throw ('Missing complete scenarios: '+$folder)}
 }}
 $summaries | Export-Csv -LiteralPath (Join-Path $evidence 'summary.csv') -NoTypeInformation -Encoding UTF8
 $methods | Export-Csv -LiteralPath (Join-Path $evidence 'methods.csv') -NoTypeInformation -Encoding UTF8
 $engines | Export-Csv -LiteralPath (Join-Path $evidence 'engine.csv') -NoTypeInformation -Encoding UTF8
 foreach($editionName in @('original','experiment')){
  foreach($logFile in (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot ('regression-'+$editionName)) -Filter '*.log' -File)){
   if($Smoke -and $logFile.BaseName -notin @('Performance','Gameplay','UI','NetworkPair-host','NetworkPair-guest')){continue}
   $lines=@(Select-String -LiteralPath $logFile.FullName -Pattern '\b(COMPLETE|FAIL|INCOMPLETE)|Exception:|PERFMICRO' | ForEach-Object Line)
   if($lines.Count -gt 0){$validation+='=== '+$editionName+' '+$logFile.Name+' ===';$validation+=$lines}
  }
 }
 [IO.File]::WriteAllLines((Join-Path $evidence 'validation.txt'),[string[]]@($validation | ForEach-Object {$_.TrimEnd()}),[Text.UTF8Encoding]::new($false))
 $summaries | ConvertTo-Json -Compress
 Write-Output ('EXPORTED '+$evidence)
 return
}
if($Status -or $Stop){
 $targetFolder=Join-Path $PSScriptRoot ($Phase+'-'+$Edition.ToLower()+$(if($Smoke){'-smoke'}else{''}))
 $targetExe=Join-Path $targetFolder 'FanZhiYi.exe'
 Write-Output ('Status UTC '+[DateTime]::UtcNow.ToString('o'))
 foreach($gameProcess in @(Get-Process -Name FanZhiYi -ErrorAction SilentlyContinue)){
  if($gameProcess.Path -eq $targetExe){
   $gameProcess | Select-Object Id,StartTime,CPU,Responding,WorkingSet,VirtualMemorySize64,Path
   if($Stop){Stop-Process -Id $gameProcess.Id -Force;Write-Output 'Stopped only the matching isolated benchmark player'}
  }
 }
 Get-Content -LiteralPath (Join-Path $targetFolder 'run.log') -Tail 6 -ErrorAction SilentlyContinue
 Get-ChildItem -LiteralPath $targetFolder -File -Filter '*summary.csv' | Select-Object Name,LastWriteTime
 return
}
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$gameRoot=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'AI Mod Source\build\Mono.Cecil.dll')} | Select-Object -First 1).FullName
$editionRoot=if($Edition -eq 'Experiment'){Join-Path $gameRoot '实验版（模型75%）'}else{$gameRoot}
$source=if($Phase -eq 'baseline'){Join-Path $PSScriptRoot ('baseline-'+$Edition.ToLower())}else{Join-Path $editionRoot 'AI Mod Source'}
$output=Join-Path $PSScriptRoot ($Phase+'-'+$Edition.ToLower()+$(if($Smoke){'-smoke'}else{''}))
$build=Join-Path $output 'instrumented'
New-Item -ItemType Directory -Path $output,$build -Force | Out-Null
if(-not(Test-Path -LiteralPath (Join-Path $output 'FanZhiYi.exe'))){
 foreach($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe')){Copy-Item -LiteralPath (Join-Path $editionRoot $name) -Destination $output}
 foreach($name in @('FanZhiYi_Data','MonoBleedingEdge')){Copy-Item -LiteralPath (Join-Path $editionRoot $name) -Destination $output -Recurse}
}
$managed=Join-Path $editionRoot 'FanZhiYi_Data\Managed'
$refs=@('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.ImageConversionModule.dll') | ForEach-Object {'/r:'+(Join-Path $managed $_)}
$names=@('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ControlBindings.cs','PowerShot.cs','MagneticFoot.cs','LanMultiplayer.cs')
if($Edition -eq 'Experiment'){$names+='ExperimentScale.cs'}
$names | ForEach-Object {[pscustomobject]@{name=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $source $_) -Algorithm SHA256).Hash.ToLower()}} | Export-Csv -LiteralPath (Join-Path $output 'source-hashes.csv') -NoTypeInformation -Encoding UTF8
foreach($name in $names){
 $code=[IO.File]::ReadAllText((Join-Path $source $name))
 if($name -eq 'GameAIMod.cs'){$code=$code.Replace('        menuPanel = panel;','        PerformanceBench.Boot();'+[Environment]::NewLine+'        menuPanel = panel;')}
 if($name -eq 'ControlBindings.cs'){$code=$code.Replace('return Input.GetKey(key);','return PerformanceBench.ReadKey(key);').Replace('return Input.GetKeyDown(key);','return PerformanceBench.ReadDown(key);')}
 [IO.File]::WriteAllText((Join-Path $build $name),$code,[Text.UTF8Encoding]::new($false))
}
$sources=@($names | ForEach-Object {Join-Path $build $_})+(Join-Path $PSScriptRoot 'PerformanceBench.cs')
$csc='C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:library /optimize+ ('/out:'+(Join-Path $build 'GameAIMod.raw.dll')) $refs $sources
if($LASTEXITCODE -ne 0){throw 'Benchmark compilation failed'}
[void][Reflection.Assembly]::LoadFrom((Join-Path $gameRoot 'AI Mod Source\build\Mono.Cecil.dll'))
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $build 'GameAIMod.raw.dll'))
$bench=$assembly.MainModule.Types | Where-Object Name -eq 'PerformanceBench'
$enter=$bench.Methods | Where-Object Name -eq 'Enter'
$leave=$bench.Methods | Where-Object Name -eq 'Leave'
$methods=@()
foreach($type in $assembly.MainModule.Types){if($type.Name -match 'PerformanceBench|^<'){continue};foreach($method in $type.Methods){if($method.HasBody -and ($method.Name -match '^(Update|FixedUpdate|LateUpdate|OnGUI|UpdateDecision|BeforeMuscles|OnBallCollision|FindGoalCeiling|DropClearance|Eligible|CanCorrect)$')){$methods+=$method}}}
$map=@('id,method');$id=0
foreach($method in $methods){
 $map+=($id.ToString()+',"'+$method.FullName+'"')
 $il=$method.Body.GetILProcessor(); $entry=$method.Body.Instructions[0]
 $il.InsertBefore($entry,$il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4,$id));$il.InsertBefore($entry,$il.Create([Mono.Cecil.Cil.OpCodes]::Call,$enter))
 foreach($ret in @($method.Body.Instructions | Where-Object OpCode -eq ([Mono.Cecil.Cil.OpCodes]::Ret))){
  $ret.OpCode=[Mono.Cecil.Cil.OpCodes]::Ldc_I4;$ret.Operand=$id
  $call=$il.Create([Mono.Cecil.Cil.OpCodes]::Call,$leave);$il.InsertAfter($ret,$call);$il.InsertAfter($call,$il.Create([Mono.Cecil.Cil.OpCodes]::Ret))
 }
 # Inserting timers can push an original short branch beyond its signed-byte
 # range. Expand every short branch before Cecil writes the instrumented IL.
 foreach($instruction in $method.Body.Instructions){
  if($instruction.OpCode.OperandType -eq [Mono.Cecil.Cil.OperandType]::ShortInlineBrTarget){
   $fieldName=$instruction.OpCode.Name.Replace('.s','').Replace('.','_')
   $field=[Mono.Cecil.Cil.OpCodes].GetField($fieldName,([Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::IgnoreCase))
   if($null -eq $field){throw ('Unknown branch '+$fieldName)}
   $instruction.OpCode=$field.GetValue($null)
  }
 }
 $method.Body.MaxStackSize+=2
 $id++
}
if($id -ge 512){throw 'Too many benchmark methods'}
$assembly.Name.Name='GameAIMod';$assembly.MainModule.Name='GameAIMod.dll';$assembly.Write((Join-Path $build 'GameAIMod.dll'));$assembly.Dispose()
[IO.File]::WriteAllLines((Join-Path $output 'method-map.csv'),$map,[Text.UTF8Encoding]::new($false))
& $csc /nologo /target:exe /optimize+ ('/out:'+(Join-Path $build 'PatchGame.exe')) ('/r:'+(Join-Path $gameRoot 'AI Mod Source\build\Mono.Cecil.dll')) (Join-Path $source 'PatchGame.cs')
if($LASTEXITCODE -ne 0){throw 'Patcher compilation failed'}
Copy-Item -LiteralPath (Join-Path $gameRoot 'AI Mod Source\build\Mono.Cecil.dll') -Destination $build -Force
& (Join-Path $build 'PatchGame.exe') (Join-Path $source 'Assembly-CSharp.original.dll') (Join-Path $build 'GameAIMod.dll') (Join-Path $build 'Assembly-CSharp.dll')
if($LASTEXITCODE -ne 0){throw 'Benchmark patch failed'}
foreach($name in @('GameAIMod.dll','Assembly-CSharp.dll')){Copy-Item -LiteralPath (Join-Path $build $name) -Destination (Join-Path $output ('FanZhiYi_Data\Managed\'+$name)) -Force}
$log=Join-Path $output 'run.log'
$arguments=@('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-logFile',('"'+$log+'"'))
if($Smoke){$arguments+='-bench-smoke'}
if($Scenarios){$arguments+=@('-bench-scenarios',$Scenarios)}
$process=Start-Process -FilePath (Join-Path $output 'FanZhiYi.exe') -WorkingDirectory $output -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output ('PERF PROCESS '+$Phase+' '+$Edition+' PID='+$process.Id)
while(-not $process.WaitForExit(30000)){Write-Output ('PERF progress '+$Phase+' '+$Edition+': '+((Select-String -LiteralPath $log -Pattern 'PERF COMPLETE' | Select-Object -Last 1).Line))}
Write-Output ('PERF process exit code='+$process.ExitCode)
$logText=[IO.File]::ReadAllText($log)
if($logText -notmatch 'PERF ALL COMPLETE' -or $logText -match 'Exception:|InvalidProgramException|StackOverflow'){throw ('Benchmark failed: '+$log)}
Write-Output ('PERF SUCCESS '+$Phase+' '+$Edition)
if(-not $Smoke -and -not $Scenarios){& $PSCommandPath -Phase $Phase -Edition $Edition -Repair}
