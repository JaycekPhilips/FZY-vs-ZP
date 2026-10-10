param([string]$Ref='65b270ba388ac1c871abe6e0381dc050808258e6')
$ErrorActionPreference='Stop'
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$gameRoot=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'AI Mod Source\build\Mono.Cecil.dll')} | Select-Object -First 1).FullName
$capture=Join-Path $PSScriptRoot ('capture-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $capture -Force | Out-Null
$names=@('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ZhaoHeader.cs','ControlBindings.cs','PowerShot.cs','MagneticFoot.cs','LanMultiplayer.cs','PatchGame.cs','Assembly-CSharp.original.dll')
$relativeRoots=@{original=$gameRoot.Substring($workspace.Length+1).Replace('\','/');experiment=(Join-Path $gameRoot '实验版（模型75%）').Substring($workspace.Length+1).Replace('\','/')}
$paths=@()
foreach($edition in @('original','experiment')){foreach($name in $names){$paths+=($relativeRoots[$edition]+'/AI Mod Source/'+$name)}}
$paths+=($relativeRoots.experiment+'/AI Mod Source/ExperimentScale.cs')
$zip=Join-Path $capture 'baseline.zip'
& git -C $workspace archive --format=zip ('--output='+$zip) $Ref -- $paths
if($LASTEXITCODE -ne 0){throw 'Cannot capture Git baseline'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($zip,$capture)
foreach($edition in @('original','experiment')){
 $destination=Join-Path $PSScriptRoot ('baseline-'+$edition)
 if(Test-Path -LiteralPath $destination){throw ('Baseline already exists; preserve it or choose another checkout: '+$destination)}
 New-Item -ItemType Directory -Path $destination -Force | Out-Null
 $from=Join-Path $capture ($relativeRoots[$edition]+'/AI Mod Source')
 Get-ChildItem -LiteralPath $from -File | Copy-Item -Destination $destination
 [IO.File]::WriteAllText((Join-Path $destination 'source-ref.txt'),$Ref,[Text.UTF8Encoding]::new($false))
 Write-Output ('Captured immutable '+$edition+' source from '+$Ref)
}
