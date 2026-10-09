$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$installRoot = 'C:\Users\Alienware\Desktop\范志毅VS赵鹏\范志毅VS赵鹏 PC双人版（32位）\范志毅VS赵鹏 PC双人版（32位）'
if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'FanZhiYi.exe'))) { throw 'Installation missing' }
$backup = Join-Path $installRoot ('AI Mod Source\backup-before-double-shot-rules-' + (Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($name in @('GameAIMod.cs','PlayerSkills.cs','README.md')) {
    Copy-Item -LiteralPath (Join-Path $installRoot ('AI Mod Source\' + $name)) -Destination $backup
}
$dll = Join-Path $installRoot 'FanZhiYi_Data\Managed\GameAIMod.dll'
$newDll = Join-Path $taskRoot 'AI Mod Source\build\double-shot-release\GameAIMod.dll'
Copy-Item -LiteralPath $dll -Destination $backup
try { Copy-Item -LiteralPath $newDll -Destination $dll -Force }
catch {
    $mapped = Join-Path $backup 'mapped-GameAIMod.dll'
    Move-Item -LiteralPath $dll -Destination $mapped
    try { Copy-Item -LiteralPath $newDll -Destination $dll }
    catch { Move-Item -LiteralPath $mapped -Destination $dll; throw }
}
foreach ($name in @('GameAIMod.cs','PlayerSkills.cs','README.md')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot ('AI Mod Source\' + $name)) -Destination (Join-Path $installRoot ('AI Mod Source\' + $name)) -Force
}
foreach ($relative in @('FanZhiYi_Data\Managed\GameAIMod.dll','AI Mod Source\GameAIMod.cs','AI Mod Source\PlayerSkills.cs','AI Mod Source\README.md')) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $installRoot $relative)).Hash) { throw ('Sync mismatch: ' + $relative) }
}
'Installed and verified four files in both game folders'
Get-FileHash -LiteralPath $dll -Algorithm SHA256 | Select-Object Hash
'Backup: ' + $backup
