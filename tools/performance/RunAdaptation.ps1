param([ValidateSet('Both','Original','Experiment')][string]$Edition='Both',[string]$Suites='UI,Ability,NetworkPair')
$ErrorActionPreference='Stop'
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence=Join-Path $workspace '.work/adaptation'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$editions=if($Edition -eq 'Both'){@('Original','Experiment')}else{@($Edition)}
foreach($editionName in $editions){
 $label=$editionName.ToLower()
 $game=if($editionName -eq 'Original'){Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）/范志毅VS赵鹏 PC双人版（32位）'}else{Join-Path $workspace '范志毅VS赵鹏 实验版（模型75%）'}
 $destination=Join-Path $evidence ('regression-'+$label)
 New-Item -ItemType Directory -Path $destination -Force | Out-Null
 foreach($suite in $Suites.Split(',')){
  if($suite -notin @('UI','Ability','NetworkPair')){throw 'Adaptation checks support UI, Ability, NetworkPair'}
  try{
   & (Join-Path $PSScriptRoot 'RunRegressions.ps1') -Edition $editionName -Suite $suite
  }finally{
   $player=Join-Path $PSScriptRoot ('regression-'+$label)
   foreach($file in @(Get-ChildItem -LiteralPath $player -Filter '*.log' -File -ErrorAction SilentlyContinue)){
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
   }
   $screens=Join-Path $player 'UI-Screenshots'
   if(Test-Path -LiteralPath $screens){Copy-Item -LiteralPath $screens -Destination (Join-Path $destination 'screenshots') -Recurse -Force}
   foreach($path in @($player,($player+'-guest'))){
    $absolute=[IO.Path]::GetFullPath($path)
    if(!$absolute.StartsWith([IO.Path]::GetFullPath($PSScriptRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Test cleanup path escaped tools directory'}
    if(Test-Path -LiteralPath $absolute){Remove-Item -LiteralPath $absolute -Recurse -Force}
   }
  }
 }
 Get-ChildItem -LiteralPath (Join-Path $game 'AI Mod Source') -File | Where-Object {$_.Extension -eq '.cs'} | ForEach-Object {
  [pscustomobject]@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLower()}
 } | Export-Csv -LiteralPath (Join-Path $evidence ('source-hashes-'+$label+'.csv')) -NoTypeInformation -Encoding UTF8
}
