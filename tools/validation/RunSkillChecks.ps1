param([ValidateSet('Original','Experiment')][string]$Edition='Original',[ValidateSet('ShotRescue','Ability','AIPressure','SkillSelfPlay','DownIntercept','CombatFlow','Startup','CannonTotal','Lottery')][string]$Suite='ShotRescue',[int]$GoalTarget=200)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$build=Join-Path $repo '范志毅VS赵鹏 PC双人版（32位）\范志毅VS赵鹏 PC双人版（32位）\AI Mod Source\build'
$e=$Edition.ToLower();$s=$Suite.ToLower();$record=Join-Path $repo ('.work\validation\'+$e+'-'+$s)
New-Item -ItemType Directory -Path $record -Force|Out-Null
try { & (Join-Path $build BuildAbility.ps1) -Edition $Edition -Suite $Suite -GoalTarget $GoalTarget }
finally {
 foreach($n in @(('ability-'+$e+'-'+$s+'.log'),('ability-'+$e+'-'+$s+'-validation.json'))){$f=Join-Path $build $n;if(Test-Path -LiteralPath $f){Copy-Item -LiteralPath $f -Destination $record -Force}}
 $player=Join-Path $build ('ability-test-player-'+$e)
 if($Suite -in @('CannonTotal','Lottery')){$f=Join-Path $player '老虎机抽签页.png';if(Test-Path -LiteralPath $f){Copy-Item -LiteralPath $f -Destination $record -Force}}
 if($Suite -eq 'Startup'){foreach($n in @('startup-metrics.json','startup-metrics.csv')){$f=Join-Path $player $n;if(Test-Path -LiteralPath $f){Copy-Item -LiteralPath $f -Destination $record -Force}}}
 if($Suite -eq 'SkillSelfPlay'){foreach($n in @('contest-selfplay-progress.json','contest-selfplay-goals.csv','contest-selfplay-matches.csv')){$f=Join-Path $player $n;if(Test-Path -LiteralPath $f){Copy-Item -LiteralPath $f -Destination $record -Force}}}
 foreach($dir in @($player,(Join-Path $build ('ability-'+$e+'-'+$s+'-build')))){
  if(Test-Path -LiteralPath $dir){$resolved=(Resolve-Path -LiteralPath $dir).Path;if(!$resolved.StartsWith($build+[IO.Path]::DirectorySeparatorChar)){throw 'Unsafe cleanup path'};Remove-Item -LiteralPath $resolved -Recurse -Force}
 }
 Write-Output ('Isolated test copies removed. Evidence: '+$record)
}
