#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess=$true)]
param([ValidateSet('Both','Original','Experiment')][string]$Edition='Both',[string]$GameExe)
$ErrorActionPreference='Stop'
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$editions=if($GameExe){@('Custom')}elseif($Edition -eq 'Both'){@('Original','Experiment')}else{@($Edition)}
foreach($editionName in $editions){
 $relative=if($editionName -eq 'Original'){'范志毅VS赵鹏 PC双人版（32位）/范志毅VS赵鹏 PC双人版（32位）/FanZhiYi.exe'}else{'范志毅VS赵鹏 实验版（模型75%）/FanZhiYi.exe'}
 $exe=(Resolve-Path -LiteralPath $(if($GameExe){$GameExe}else{Join-Path $workspace $relative})).Path
 if((Split-Path $exe -Leaf) -ne 'FanZhiYi.exe'){throw 'GameExe must point to FanZhiYi.exe'}
 $name='FZY-LAN-'+$editionName+'-UDP'
 if($PSCmdlet.ShouldProcess($exe,'Allow inbound UDP 28950 from the local subnet on Private/Public networks')){
  $rule=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
  if($rule){
   Set-NetFirewallRule -Name $name -Enabled True -Direction Inbound -Action Allow -Profile Private,Public -Program $exe -Protocol UDP -LocalPort 28950 -RemoteAddress LocalSubnet | Out-Null
  }else{
   New-NetFirewallRule -Name $name -DisplayName ('FZY LAN '+$editionName+' UDP 28950') -Enabled True -Direction Inbound -Action Allow -Profile Private,Public -Program $exe -Protocol UDP -LocalPort 28950 -RemoteAddress LocalSubnet | Out-Null
  }
  Write-Output ('Allowed UDP 28950 for '+$exe)
 }
}
