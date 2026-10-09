$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$parent = Split-Path $taskRoot -Parent
$gameDirs = @(Get-ChildItem -LiteralPath $parent -Directory | Where-Object {
    $_.FullName -ne $taskRoot -and (Test-Path -LiteralPath (Join-Path $_.FullName 'FanZhiYi.exe'))
})
if ($gameDirs.Count -ne 1) { throw 'Expected exactly one installed game directory' }
$installRoot = $gameDirs[0].FullName
$release = Join-Path $PSScriptRoot 'behind-goal-release'
$testLog = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'behind-goal-test.log'))
if ($testLog -notmatch 'BOUNDARY COMPLETE checks=\d+ failures=0' -or
    $testLog -match 'BOUNDARY FAIL|BOUNDARY TIMEOUT|Exception:|Invalid IL code') { throw 'Boundary runtime tests have not passed' }
$sources = @('GameAIMod.cs','PatchGame.cs','README.md')
foreach ($gameRoot in @($taskRoot,$installRoot)) {
    $backup = Join-Path $gameRoot ('AI Mod Source\backup-before-behind-goal-install-' + (Get-Date -Format yyyyMMdd-HHmmss))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) {
        $target = Join-Path $gameRoot ('FanZhiYi_Data\Managed\' + $name)
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
    Copy-Item -LiteralPath (Join-Path $gameRoot 'AI Mod Source\Assembly-CSharp.patched.dll') -Destination $backup
    Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $gameRoot 'AI Mod Source\Assembly-CSharp.patched.dll') -Force
    if ($gameRoot -eq $installRoot) {
        $instructions = Join-Path $installRoot 'AGENTS.md'
        if (Test-Path -LiteralPath $instructions) { Copy-Item -LiteralPath $instructions -Destination $backup }
        Copy-Item -LiteralPath (Join-Path $taskRoot 'AGENTS.md') -Destination $instructions -Force
        foreach ($name in $sources) {
            $target = Join-Path $installRoot ('AI Mod Source\' + $name)
            Copy-Item -LiteralPath $target -Destination $backup
            Copy-Item -LiteralPath (Join-Path $taskRoot ('AI Mod Source\' + $name)) -Destination $target -Force
        }
    }
    Write-Output ('Backup: ' + $backup)
}
foreach ($relative in @('FanZhiYi_Data\Managed\GameAIMod.dll','FanZhiYi_Data\Managed\Assembly-CSharp.dll',
    'AI Mod Source\GameAIMod.cs','AI Mod Source\PatchGame.cs','AI Mod Source\README.md','AI Mod Source\Assembly-CSharp.patched.dll','AGENTS.md')) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $relative)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $installRoot $relative)).Hash) { throw ('Sync mismatch: ' + $relative) }
}
foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $release $name)).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $installRoot ('FanZhiYi_Data\Managed\' + $name))).Hash) { throw 'Release mismatch' }
}
Write-Output 'Tested behind-goal update installed and synchronized'
