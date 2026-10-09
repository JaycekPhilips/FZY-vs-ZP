$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$parent = Split-Path $taskRoot -Parent
$matches = @(Get-ChildItem -LiteralPath $parent -Directory | Where-Object {
    $_.FullName -ne $taskRoot -and (Test-Path -LiteralPath (Join-Path $_.FullName 'FanZhiYi.exe'))
})
if ($matches.Count -ne 1) { throw 'Expected exactly one installed game directory' }
$installRoot = $matches[0].FullName
$release = Join-Path $taskRoot 'AI Mod Source\build\natural-goal-release'
$backup = Join-Path $installRoot ('AI Mod Source\backup-before-natural-goal-' + (Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path $backup -Force | Out-Null

$sources = @('GameAIMod.cs', 'PlayerSkills.cs', 'PlayerMovement.cs', 'PatchGame.cs', 'README.md', 'Assembly-CSharp.patched.dll')
foreach ($name in $sources) {
    Copy-Item -LiteralPath (Join-Path $installRoot ('AI Mod Source\' + $name)) -Destination $backup
}
foreach ($name in @('GameAIMod.dll', 'Assembly-CSharp.dll')) {
    $relative = 'FanZhiYi_Data\Managed\' + $name
    $target = Join-Path $installRoot $relative
    $fresh = Join-Path $release $name
    Copy-Item -LiteralPath $target -Destination $backup
    try { Copy-Item -LiteralPath $fresh -Destination $target -Force }
    catch {
        $mapped = Join-Path $backup ('mapped-' + $name)
        Move-Item -LiteralPath $target -Destination $mapped
        try { Copy-Item -LiteralPath $fresh -Destination $target }
        catch { Move-Item -LiteralPath $mapped -Destination $target; throw }
    }
}
foreach ($name in $sources) {
    Copy-Item -LiteralPath (Join-Path $taskRoot ('AI Mod Source\' + $name)) -Destination (Join-Path $installRoot ('AI Mod Source\' + $name)) -Force
}
foreach ($relative in @('FanZhiYi_Data\Managed\GameAIMod.dll', 'FanZhiYi_Data\Managed\Assembly-CSharp.dll',
    'AI Mod Source\GameAIMod.cs', 'AI Mod Source\PlayerSkills.cs', 'AI Mod Source\PlayerMovement.cs',
    'AI Mod Source\PatchGame.cs', 'AI Mod Source\README.md', 'AI Mod Source\Assembly-CSharp.patched.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $relative)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $installRoot $relative)).Hash) { throw ('Sync mismatch: ' + $relative) }
}
Write-Output ('Installed game: ' + $installRoot)
Write-Output ('Backup: ' + $backup)
Write-Output 'Updated DLLs and source match the workspace'
