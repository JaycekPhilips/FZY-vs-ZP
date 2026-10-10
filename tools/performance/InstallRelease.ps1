param([string]$MeasurementRoot,[switch]$Quick)
$ErrorActionPreference='Stop'
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(-not $MeasurementRoot){$MeasurementRoot=Join-Path $workspace '.work\performance'}
$gameRoot=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'AI Mod Source\build\Mono.Cecil.dll')} | Select-Object -First 1).FullName
$installedOriginal=Join-Path (Split-Path $gameRoot -Parent) '范志毅VS赵鹏 PC双人版（32位）'
$experiment=Join-Path $gameRoot '实验版（模型75%）'
$installedExperiment=Join-Path $workspace '范志毅VS赵鹏 实验版（模型75%）'
$suiteNames=if($Quick){@('Performance','Gameplay','UI','NetworkPair-host','NetworkPair-guest')}else{@('Performance','Gameplay','Controls','Boundary','Defense','Goals','Magnetic','Contest','AI','Drop','Balance','Corner','Network','UI','NetworkPair-host','NetworkPair-guest')}
foreach($edition in @('original','experiment')){
 foreach($phase in @('baseline','candidate')){
  $folder=Join-Path $MeasurementRoot ($phase+'-'+$edition+$(if($Quick){'-smoke'}else{''}))
  $log=[IO.File]::ReadAllText((Join-Path $folder 'run.log'))
  if($log -notmatch 'PERF ALL COMPLETE' -or $log -match 'Exception:|InvalidProgramException'){throw ('Incomplete measurements: '+$folder)}
  $expected=if($Quick){6}else{18}
  if(@(Get-ChildItem -LiteralPath $folder -Filter '*-summary.csv' -File).Count -ne $expected){throw ('Missing measurement scenarios: '+$folder)}
  foreach($file in (Get-ChildItem -LiteralPath $folder -Filter '*-summary.csv' -File)){
   $row=Import-Csv -LiteralPath $file.FullName
   if([double]$row.seconds -gt 122 -or [double]$row.max_ms -gt 5000){throw ('Interrupted measurement must be repeated: '+$file.FullName)}
  }
 }
 foreach($suite in $suiteNames){
  $log=[IO.File]::ReadAllText((Join-Path $MeasurementRoot ('regression-'+$edition+'\'+$suite+'.log')))
  if($log -notmatch '\bCOMPLETE[^\r\n]*failures=0' -or $log -match '\bFAIL\b|\bINCOMPLETE\b|Exception:'){
   throw ('Regression gate failed: '+$edition+' '+$suite)
  }
 }
}
if(@(Get-Process -Name FanZhiYi -ErrorAction SilentlyContinue).Count -gt 0){throw 'Close game players before installing validated assemblies'}
[void][Reflection.Assembly]::LoadFrom((Join-Path $gameRoot 'AI Mod Source\build\Mono.Cecil.dll'))
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$fileNames=@('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ControlBindings.cs','PowerShot.cs','MagneticFoot.cs','LanMultiplayer.cs','PatchGame.cs')
$csc='C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
$releases=@{}
foreach($edition in @('original','experiment')){
 $canonical=if($edition -eq 'original'){$gameRoot}else{$experiment}
 $source=Join-Path $canonical 'AI Mod Source'
 $names=$fileNames;if($edition -eq 'experiment'){$names+='ExperimentScale.cs'}
 foreach($hash in (Import-Csv -LiteralPath (Join-Path $MeasurementRoot ('candidate-'+$edition+$(if($Quick){'-smoke'}else{''})+'\source-hashes.csv')))){
  if((Get-FileHash -LiteralPath (Join-Path $source $hash.name)).Hash.ToLower() -ne $hash.sha256){throw ('Source changed after candidate measurement: '+$hash.name)}
 }
 $build=Join-Path $MeasurementRoot ('release-'+$edition)
 New-Item -ItemType Directory -Path $build -Force | Out-Null
 $managed=Join-Path $canonical 'FanZhiYi_Data\Managed'
 $refs=@('netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.Physics2DModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.AudioModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.ImageConversionModule.dll') | ForEach-Object {'/r:'+(Join-Path $managed $_)}
 $sources=$names | Where-Object {$_ -ne 'PatchGame.cs'} | ForEach-Object {Join-Path $source $_}
 & $csc /nologo /target:library /optimize+ ('/out:'+(Join-Path $build 'GameAIMod.dll')) $refs $sources
 if($LASTEXITCODE -ne 0){throw 'Release compilation failed'}
 & $csc /nologo /target:exe /optimize+ ('/out:'+(Join-Path $build 'PatchGame.exe')) ('/r:'+(Join-Path $gameRoot 'AI Mod Source\build\Mono.Cecil.dll')) (Join-Path $source 'PatchGame.cs')
 if($LASTEXITCODE -ne 0){throw 'Patcher compilation failed'}
 Copy-Item -LiteralPath (Join-Path $gameRoot 'AI Mod Source\build\Mono.Cecil.dll') -Destination $build -Force
 & (Join-Path $build 'PatchGame.exe') (Join-Path $source 'Assembly-CSharp.original.dll') (Join-Path $build 'GameAIMod.dll') (Join-Path $build 'Assembly-CSharp.dll')
 if($LASTEXITCODE -ne 0){throw 'Release patch failed'}
 foreach($dll in @('GameAIMod.dll','Assembly-CSharp.dll')){
  $assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $build $dll))
  try{
   foreach($type in $assembly.MainModule.Types){
    if($type.Name -match 'Tests$|^PerformanceBench$'){throw ('Test type in release: '+$type.Name)}
    foreach($method in $type.Methods){if($method.HasBody){foreach($instruction in $method.Body.Instructions){
     if($instruction.Operand -is [Mono.Cecil.MethodReference] -and $instruction.Operand.DeclaringType.Name -match 'Tests$|^PerformanceBench$'){throw ('Test hook in release: '+$method.FullName)}
    }}}
   }
  }finally{$assembly.Dispose()}
 }
 $releases[$edition]=$build
}
# Save every pre-install file and the user's exact configuration hashes.
$targets=@(@{edition='original';canonical=$gameRoot;install=$gameRoot},@{edition='original';canonical=$gameRoot;install=$installedOriginal},@{edition='experiment';canonical=$experiment;install=$experiment},@{edition='experiment';canonical=$experiment;install=$installedExperiment})
$configuration=@()
foreach($target in $targets){
 $config=Join-Path $target.install '按键设置.ini'
 $configuration+=[pscustomobject]@{path=$config;sha256=(Get-FileHash -LiteralPath $config).Hash.ToLower()}
 $source=Join-Path $target.canonical 'AI Mod Source';$destination=Join-Path $target.install 'AI Mod Source'
 $backup=Join-Path $destination ('backup-before-performance-'+$stamp)
 New-Item -ItemType Directory -Path $backup -Force | Out-Null
 $names=$fileNames;if($target.edition -eq 'experiment'){$names+='ExperimentScale.cs'}
 foreach($name in $names){
  $old=if($target.install -eq $target.canonical){Join-Path $MeasurementRoot ('baseline-'+$target.edition+'\'+$name)}else{Join-Path $destination $name}
  Copy-Item -LiteralPath $old -Destination (Join-Path $backup $name)
  if($source -ne $destination){Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $destination $name) -Force}
 }
 $managed=Join-Path $target.install 'FanZhiYi_Data\Managed'
 foreach($name in @('GameAIMod.dll','Assembly-CSharp.dll')){
  Copy-Item -LiteralPath (Join-Path $managed $name) -Destination (Join-Path $backup ('runtime-'+$name))
  Copy-Item -LiteralPath (Join-Path $releases[$target.edition] $name) -Destination (Join-Path $managed $name) -Force
 }
 Copy-Item -LiteralPath (Join-Path $destination 'Assembly-CSharp.patched.dll') -Destination $backup
 Copy-Item -LiteralPath (Join-Path $releases[$target.edition] 'Assembly-CSharp.dll') -Destination (Join-Path $destination 'Assembly-CSharp.patched.dll') -Force
 Write-Output ('INSTALLED '+$target.edition+' '+$target.install+' backup='+$backup)
}
$downloads=Join-Path $workspace 'downloads';New-Item -ItemType Directory -Path $downloads -Force | Out-Null
$manifest=@()
foreach($edition in @('original','experiment')){
 $canonical=if($edition -eq 'original'){$gameRoot}else{$experiment}
 $installed=if($edition -eq 'original'){$installedOriginal}else{$installedExperiment}
 $label=if($edition -eq 'original'){'original'}else{'experimental'}
 $package=Join-Path $MeasurementRoot ('package-'+$label+'-'+$stamp)
 New-Item -ItemType Directory -Path $package | Out-Null
 foreach($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe','按键设置.ini')){Copy-Item -LiteralPath (Join-Path $canonical $name) -Destination $package}
 foreach($name in @('FanZhiYi_Data','MonoBleedingEdge')){Copy-Item -LiteralPath (Join-Path $canonical $name) -Destination $package -Recurse}
 $packageSource=Join-Path $package 'AI Mod Source';New-Item -ItemType Directory -Path $packageSource | Out-Null
 Get-ChildItem -LiteralPath (Join-Path $canonical 'AI Mod Source') -File | Where-Object {$_.Extension -in @('.cs','.dll','.md')} | Copy-Item -Destination $packageSource
 if($edition -eq 'experiment'){Copy-Item -LiteralPath (Join-Path $canonical '实验版说明.txt') -Destination $package}
 $files=@()
 foreach($file in (Get-ChildItem -LiteralPath $package -Recurse -File)){
  $relative=$file.FullName.Substring($package.Length+1);$hash=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLower()
  if($hash -ne (Get-FileHash -LiteralPath (Join-Path $canonical $relative)).Hash.ToLower()){throw ('Package/canonical mismatch: '+$relative)}
  # Local key choices are intentionally independent of the distributed defaults.
  if($relative -ne '按键设置.ini' -and $hash -ne (Get-FileHash -LiteralPath (Join-Path $installed $relative)).Hash.ToLower()){throw ('Installed mismatch: '+$relative)}
  $files+=[pscustomobject]@{path=$relative.Replace('\','/');bytes=$file.Length;sha256=$hash}
 }
 $archiveName='fzy-vs-zp-'+$label+'-win32.zip'
 $archivePath=Join-Path $MeasurementRoot ($stamp+'-'+$archiveName)
 # Use portable ZIP entry separators on .NET Framework as well as newer .NET.
 $zipStream=[IO.File]::Open($archivePath,[IO.FileMode]::CreateNew)
 $zipWriter=[IO.Compression.ZipArchive]::new($zipStream,[IO.Compression.ZipArchiveMode]::Create)
 try{
  foreach($info in $files){
   $entry=$zipWriter.CreateEntry((Split-Path $package -Leaf)+'/'+$info.path,[IO.Compression.CompressionLevel]::Optimal)
   $inputStream=[IO.File]::OpenRead((Join-Path $package $info.path));$outputStream=$entry.Open()
   try{$inputStream.CopyTo($outputStream)}finally{$inputStream.Dispose();$outputStream.Dispose()}
  }
 }finally{$zipWriter.Dispose();$zipStream.Dispose()}
 $archive=[IO.Compression.ZipFile]::OpenRead($archivePath)
 try{
  $entries=@($archive.Entries | Where-Object {$_.Name -ne ''})
  if($entries.Count -ne $files.Count){throw 'ZIP file count mismatch'}
  foreach($entry in $entries){
   $portable=$entry.FullName.Replace('\','/')
   $relative=$portable.Substring($portable.IndexOf('/')+1);$expected=$files | Where-Object {$_.path -eq $relative}
   if($null -eq $expected -or $expected.bytes -ne $entry.Length){throw ('ZIP entry mismatch: '+$entry.FullName)}
   $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
   try{$hash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLower()}finally{$stream.Dispose();$sha.Dispose()}
   if($hash -ne $expected.sha256){throw ('ZIP content hash mismatch: '+$entry.FullName)}
  }
 }finally{$archive.Dispose()}
 Copy-Item -LiteralPath $archivePath -Destination (Join-Path $downloads $archiveName) -Force
 $manifest+=[pscustomobject]@{edition=$label;archive=('downloads/'+$archiveName);archiveBytes=(Get-Item -LiteralPath $archivePath).Length;archiveSha256=(Get-FileHash -LiteralPath $archivePath).Hash.ToLower();files=$files}
 Write-Output ('PACKAGE VERIFIED '+$label+' files='+$files.Count+' bytes='+(Get-Item -LiteralPath $archivePath).Length)
}
foreach($config in $configuration){if((Get-FileHash -LiteralPath $config.path).Hash.ToLower() -ne $config.sha256){throw ('User configuration changed: '+$config.path)}}
[IO.File]::WriteAllText((Join-Path $downloads 'manifest.json'),($manifest | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines((Join-Path $downloads 'SHA256SUMS.txt'),@($manifest | ForEach-Object {$_.archiveSha256+'  '+$_.archive}),[Text.UTF8Encoding]::new($false))
$configuration | Export-Csv -LiteralPath (Join-Path $workspace 'performance\preserved-controls.csv') -NoTypeInformation -Encoding UTF8
Write-Output 'RELEASE COMPLETE: both editions installed, backed up, packages verified, all controls preserved'
