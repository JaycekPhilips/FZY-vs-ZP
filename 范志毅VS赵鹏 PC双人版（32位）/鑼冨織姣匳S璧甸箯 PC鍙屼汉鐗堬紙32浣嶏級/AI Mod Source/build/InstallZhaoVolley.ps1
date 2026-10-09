$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$parent = Split-Path $taskRoot -Parent
$gameDirs = @(Get-ChildItem -LiteralPath $parent -Directory | Where-Object {
    $_.FullName -ne $taskRoot -and (Test-Path -LiteralPath (Join-Path $_.FullName 'FanZhiYi.exe'))
})
if ($gameDirs.Count -ne 1) { throw 'Expected exactly one installed game directory' }
$installRoot = $gameDirs[0].FullName
$fresh = Join-Path $PSScriptRoot 'zhao-volley-release\GameAIMod.dll'
$pattern = 'DEFENSE COMPLETE baseline=(True|False) checks=(\d+) launches=(\d+) touches=(\d+) saves=(\d+) failures=(\d+)'
$baselineLog = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'zhao-volley-baseline.log'))
$testLog = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'zhao-volley-test.log'))
$before = [regex]::Match($baselineLog,$pattern)
$after = [regex]::Match($testLog,$pattern)
if (-not $before.Success -or -not $after.Success -or [int]$after.Groups[6].Value -ne 0 -or
    [int]$before.Groups[6].Value -ne 0 -or [int]$after.Groups[3].Value -ne 9 -or
    [int]$after.Groups[5].Value -le [int]$before.Groups[5].Value -or $testLog -match 'DEFENSE FAIL|Exception:|DEFENSE TIMEOUT') {
    throw 'AI runtime comparison has not passed'
}
$sources = @('GameAIMod.cs','PlayerSkills.cs','README.md')
foreach ($gameRoot in @($taskRoot,$installRoot)) {
    $backup = Join-Path $gameRoot ('AI Mod Source\backup-before-zhao-volley-install-' + (Get-Date -Format yyyyMMdd-HHmmss))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $target = Join-Path $gameRoot 'FanZhiYi_Data\Managed\GameAIMod.dll'
    Copy-Item -LiteralPath $target -Destination $backup
    try { Copy-Item -LiteralPath $fresh -Destination $target -Force }
    catch {
        $mapped = Join-Path $backup 'mapped-GameAIMod.dll'
        Move-Item -LiteralPath $target -Destination $mapped
        try { Copy-Item -LiteralPath $fresh -Destination $target }
        catch { Move-Item -LiteralPath $mapped -Destination $target; throw }
    }
    if ($gameRoot -eq $installRoot) {
        foreach ($name in $sources) {
            $target = Join-Path $installRoot ('AI Mod Source\' + $name)
            Copy-Item -LiteralPath $target -Destination $backup
            Copy-Item -LiteralPath (Join-Path $taskRoot ('AI Mod Source\' + $name)) -Destination $target -Force
        }
    }
    Write-Output ('Backup: ' + $backup)
}
foreach ($relative in @('FanZhiYi_Data\Managed\GameAIMod.dll','AI Mod Source\GameAIMod.cs','AI Mod Source\PlayerSkills.cs','AI Mod Source\README.md')) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $relative)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $installRoot $relative)).Hash) { throw ('Sync mismatch: ' + $relative) }
}
if ((Get-FileHash -LiteralPath $fresh).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $installRoot 'FanZhiYi_Data\Managed\GameAIMod.dll')).Hash) { throw 'Release mismatch' }
Write-Output ('Tested Zhao AI installed: saves in nine shots ' + $before.Groups[5].Value + ' -> ' + $after.Groups[5].Value)
