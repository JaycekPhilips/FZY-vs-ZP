$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$parent = Split-Path $taskRoot -Parent
$gameDirs = @(Get-ChildItem -LiteralPath $parent -Directory | Where-Object {
    $_.FullName -ne $taskRoot -and (Test-Path -LiteralPath (Join-Path $_.FullName 'FanZhiYi.exe'))
})
if ($gameDirs.Count -ne 1) { throw 'Expected exactly one installed game directory' }
$installRoot = $gameDirs[0].FullName
$release = Join-Path $PSScriptRoot 'zhao-volley-release'
$pattern = 'DEFENSE COMPLETE baseline=(True|False) checks=(\d+) launches=(\d+) touches=(\d+) saves=(\d+) failures=(\d+)'
$baselineLog = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'zhao-volley-baseline.log'))
$testLog = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'zhao-volley-test.log'))
$controlsLog = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'controls-test.log'))
$before = [regex]::Match($baselineLog,$pattern)
$after = [regex]::Match($testLog,$pattern)
if (-not $before.Success -or -not $after.Success -or
    $before.Groups[1].Value -ne 'True' -or $after.Groups[1].Value -ne 'False' -or
    [int]$after.Groups[2].Value -lt 40 -or [int]$after.Groups[3].Value -ne 9 -or [int]$after.Groups[4].Value -ne 9 -or
    [int]$after.Groups[6].Value -ne 0 -or [int]$before.Groups[6].Value -ne 0 -or
    [int]$after.Groups[5].Value -le [int]$before.Groups[5].Value -or
    $testLog -match 'DEFENSE FAIL|Exception:|DEFENSE TIMEOUT' -or $baselineLog -match 'DEFENSE FAIL|Exception:|DEFENSE TIMEOUT' -or
    $controlsLog -notmatch 'CONTROLS COMPLETE checks=45 failures=0' -or $controlsLog -match 'CONTROLS FAIL|Exception:|CONTROLS TIMEOUT') {
    throw 'Required runtime tests have not passed'
}
Add-Type -Path (Join-Path $PSScriptRoot 'Mono.Cecil.dll')
function AssertProductionTypes($types) {
    foreach ($type in $types) {
        if ($type.Name -match 'Tests|Probe') { throw ('Test type in production: ' + $type.FullName) }
        AssertProductionTypes $type.NestedTypes
    }
}
foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $release $name))
    try { AssertProductionTypes $assembly.MainModule.Types } finally { $assembly.Dispose() }
}
$sources = @('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ControlBindings.cs','PatchGame.cs','README.md')
$stamp = Get-Date -Format yyyyMMdd-HHmmss
function InstallFile($fresh, $target, $backup, $backupName) {
    $old = Join-Path $backup $backupName
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination $old }
    try { Copy-Item -LiteralPath $fresh -Destination $target -Force }
    catch {
        if (-not (Test-Path -LiteralPath $target)) { throw }
        $mapped = Join-Path $backup ('mapped-' + $backupName)
        Move-Item -LiteralPath $target -Destination $mapped
        try { Copy-Item -LiteralPath $fresh -Destination $target }
        catch { Move-Item -LiteralPath $mapped -Destination $target; throw }
    }
    if ((Get-FileHash -LiteralPath $fresh).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw ('Release mismatch: ' + $target) }
}
foreach ($gameRoot in @($taskRoot,$installRoot)) {
    $backup = Join-Path $gameRoot ('AI Mod Source\backup-before-controls-volley-install-' + $stamp)
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) {
        InstallFile (Join-Path $release $name) (Join-Path $gameRoot ('FanZhiYi_Data\Managed\' + $name)) $backup $name
    }
    InstallFile (Join-Path $release 'Assembly-CSharp.dll') (Join-Path $gameRoot 'AI Mod Source\Assembly-CSharp.patched.dll') $backup 'Assembly-CSharp.patched.dll'
    if ($gameRoot -eq $installRoot) {
        foreach ($name in $sources) {
            InstallFile (Join-Path $taskRoot ('AI Mod Source\' + $name)) (Join-Path $installRoot ('AI Mod Source\' + $name)) $backup $name
        }
        $settings = Join-Path $installRoot '按键设置.ini'
        if (-not (Test-Path -LiteralPath $settings)) {
            Copy-Item -LiteralPath (Join-Path $taskRoot '按键设置.ini') -Destination $settings
        }
    }
    Write-Output ('Backup: ' + $backup)
}
foreach ($relative in (@('FanZhiYi_Data\Managed\GameAIMod.dll','FanZhiYi_Data\Managed\Assembly-CSharp.dll','AI Mod Source\Assembly-CSharp.patched.dll') + ($sources | ForEach-Object { 'AI Mod Source\' + $_ }))) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $installRoot $relative)).Hash) {
        throw ('Sync mismatch: ' + $relative)
    }
}
Write-Output ('Installed tested release. AI saves: ' + $before.Groups[5].Value + ' -> ' + $after.Groups[5].Value + ' / 9; controls: 45 checks passed.')
Write-Output ('Installed game: ' + $installRoot)
