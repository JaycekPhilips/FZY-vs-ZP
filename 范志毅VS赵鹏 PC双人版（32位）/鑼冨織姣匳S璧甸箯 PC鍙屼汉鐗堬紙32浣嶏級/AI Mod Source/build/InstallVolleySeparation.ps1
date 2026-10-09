$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$parent = Split-Path $taskRoot -Parent
$gameDirs = @(Get-ChildItem -LiteralPath $parent -Directory | Where-Object {
    $_.FullName -ne $taskRoot -and (Test-Path -LiteralPath (Join-Path $_.FullName 'FanZhiYi.exe'))
})
if ($gameDirs.Count -ne 1) { throw 'Expected exactly one installed game directory' }
$installRoot = $gameDirs[0].FullName
$fresh = Join-Path $taskRoot 'AI Mod Source\build\volley-separation-release\GameAIMod.dll'
foreach ($gameRoot in @($taskRoot, $installRoot)) {
    $backup = Join-Path $gameRoot ('AI Mod Source\backup-before-volley-separation-install-' + (Get-Date -Format yyyyMMdd-HHmmss))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $target = Join-Path $gameRoot 'FanZhiYi_Data\Managed\GameAIMod.dll'
    Copy-Item -LiteralPath $target -Destination $backup
    if ($gameRoot -eq $installRoot) {
        foreach ($name in @('PlayerSkills.cs','README.md')) {
            Copy-Item -LiteralPath (Join-Path $installRoot ('AI Mod Source\' + $name)) -Destination $backup
        }
    }
    try { Copy-Item -LiteralPath $fresh -Destination $target -Force }
    catch {
        $mapped = Join-Path $backup 'mapped-GameAIMod.dll'
        Move-Item -LiteralPath $target -Destination $mapped
        try { Copy-Item -LiteralPath $fresh -Destination $target }
        catch { Move-Item -LiteralPath $mapped -Destination $target; throw }
    }
    Write-Output ('Backup: ' + $backup)
}
foreach ($name in @('PlayerSkills.cs','README.md')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot ('AI Mod Source\' + $name)) -Destination (Join-Path $installRoot ('AI Mod Source\' + $name)) -Force
}
foreach ($relative in @('FanZhiYi_Data\Managed\GameAIMod.dll','AI Mod Source\PlayerSkills.cs','AI Mod Source\README.md')) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $installRoot $relative)).Hash) { throw ('Sync mismatch: ' + $relative) }
}
if ((Get-FileHash -LiteralPath $fresh).Hash -ne (Get-FileHash -LiteralPath (Join-Path $installRoot 'FanZhiYi_Data\Managed\GameAIMod.dll')).Hash) { throw 'Release mismatch' }
Write-Output 'Volley separation update installed and synchronized'
