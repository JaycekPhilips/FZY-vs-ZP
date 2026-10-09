$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$experimentRoot = Join-Path $taskRoot '实验版（模型75%）'
$release = Join-Path $PSScriptRoot 'scale-release'
$publishRoot = Join-Path (Split-Path (Split-Path $taskRoot -Parent) -Parent) '范志毅VS赵鹏 实验版（模型75%）'
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'FanZhiYi.exe'))) { throw 'Published experimental game missing' }
$resultCount = 0
foreach ($entry in @(@('scale-goals-test.log','GOALHEIGHT',140),@('scale-boundary-test.log','BOUNDARY',150))) {
    $logPath = Join-Path $PSScriptRoot $entry[0]
    $log = [IO.File]::ReadAllText($logPath)
    $result = [regex]::Match($log,($entry[1] + ' COMPLETE checks=(\d+) failures=0'))
    if (-not $result.Success -or [int]$result.Groups[1].Value -lt $entry[2] -or $log -match '(GOALHEIGHT|BOUNDARY) FAIL|Exception:|TIMEOUT|GotoState') { throw ('Runtime verification failed: ' + $entry[0]) }
    if ((Get-Item -LiteralPath (Join-Path $release 'GameAIMod.dll')).LastWriteTimeUtc -gt (Get-Item -LiteralPath $logPath).LastWriteTimeUtc) { throw ('Test predates compiled release: ' + $entry[0]) }
    $resultCount += [int]$result.Groups[1].Value
}
Add-Type -Path (Join-Path $PSScriptRoot 'Mono.Cecil.dll')
function AssertProductionTypes($types) {
    foreach ($type in $types) {
        if ($type.Name -match 'Tests|Probe') { throw ('Test component in production: ' + $type.FullName) }
        AssertProductionTypes $type.NestedTypes
    }
}
foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $release $name))
    try {
        AssertProductionTypes $assembly.MainModule.Types
        if ($name -eq 'GameAIMod.dll' -and $assembly.MainModule.Types.Name -notcontains 'ExperimentGoal') { throw 'Goal resize component missing' }
    } finally { $assembly.Dispose() }
    Copy-Item -LiteralPath (Join-Path $release $name) -Destination (Join-Path $experimentRoot ('FanZhiYi_Data\Managed\' + $name)) -Force
}
Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $experimentRoot 'AI Mod Source\Assembly-CSharp.patched.dll') -Force
$files = @('FanZhiYi_Data\Managed\GameAIMod.dll','FanZhiYi_Data\Managed\Assembly-CSharp.dll','AI Mod Source\Assembly-CSharp.patched.dll','实验版说明.txt')
$files += @('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ControlBindings.cs','ZhaoHeader.cs','ExperimentScale.cs','PatchGame.cs','README.md') | ForEach-Object { 'AI Mod Source\' + $_ }
$backup = Join-Path $publishRoot ('AI Mod Source\backup-before-goal-height-' + (Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($relative in $files) {
    $fresh = Join-Path $experimentRoot $relative
    $target = Join-Path $publishRoot $relative
    $saved = Join-Path $backup ($relative.Replace('\','_'))
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination $saved }
    try { Copy-Item -LiteralPath $fresh -Destination $target -Force }
    catch {
        if (-not (Test-Path -LiteralPath $target)) { throw }
        $mapped = $saved + '.mapped'
        Move-Item -LiteralPath $target -Destination $mapped
        try { Copy-Item -LiteralPath $fresh -Destination $target }
        catch { Move-Item -LiteralPath $mapped -Destination $target; throw }
    }
    if ((Get-FileHash -LiteralPath $fresh).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw ('Installed copy differs: ' + $relative) }
}
$originalRoot = Join-Path (Split-Path $taskRoot -Parent) '范志毅VS赵鹏 PC双人版（32位）'
foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $originalRoot ('FanZhiYi_Data\Managed\' + $name))).Hash -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('zhao-volley-release\' + $name))).Hash) { throw 'Original differs from unchanged release' }
}
Write-Output ('Installed goal-height update. Checks passed: ' + $resultCount + '; files matched: ' + $files.Count)
Write-Output ('Experimental game: ' + $publishRoot)
Write-Output ('Backup: ' + $backup)
