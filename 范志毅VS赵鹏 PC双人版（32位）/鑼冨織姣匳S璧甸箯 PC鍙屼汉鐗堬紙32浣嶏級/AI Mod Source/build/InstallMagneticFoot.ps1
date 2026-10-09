$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$originalRoot = Join-Path (Split-Path $taskRoot -Parent) '范志毅VS赵鹏 PC双人版（32位）'
$experimentSource = Join-Path $taskRoot '实验版（模型75%）'
$experimentInstall = Join-Path (Split-Path (Split-Path $taskRoot -Parent) -Parent) '范志毅VS赵鹏 实验版（模型75%）'
$total = 0
foreach ($edition in @('original','experiment')) {
    foreach ($test in @(@('magnetic','MAGNETIC',61),@('gameplay','POWER',59),@('controls','CONTROLS',68),@('defense','DEFENSE',40))) {
        $path = Join-Path $PSScriptRoot ('magnetic-' + $edition + '-' + $test[0] + '.log')
        $log = [IO.File]::ReadAllText($path)
        $result = [regex]::Match($log,($test[1] + ' COMPLETE(?: baseline=False)? checks=(\d+).*failures=0'))
        if (-not $result.Success -or [int]$result.Groups[1].Value -lt $test[2] -or $log -match '(MAGNETIC|POWER|CONTROLS|BOUNDARY|DEFENSE) FAIL|Exception:|TIMEOUT|GotoState') { throw ('Required runtime checks failed: ' + $path) }
        $total += [int]$result.Groups[1].Value
    }
}
Add-Type -Path (Join-Path $PSScriptRoot 'Mono.Cecil.dll')
function AssertProduction($types) {
    foreach ($type in $types) {
        if ($type.Name -match 'Tests|Probe') { throw ('Test component in release: ' + $type.FullName) }
        AssertProduction $type.NestedTypes
    }
}
foreach ($edition in @('original','experiment')) {
    $release = Join-Path $PSScriptRoot ('magnetic-' + $edition + '-release')
    foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) {
        $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $release $name))
        try {
            AssertProduction $assembly.MainModule.Types
            if ($name -eq 'GameAIMod.dll' -and $assembly.MainModule.Types.Name -notcontains 'PowerShot') { throw 'Powered shooting missing' }
            if ($name -eq 'GameAIMod.dll') {
                $magneticType = $assembly.MainModule.Types | Where-Object Name -eq 'MagneticFoot'
                if ($null -eq $magneticType) { throw 'Magnetic technique missing' }
                foreach ($method in $magneticType.Methods) {
                    if (-not $method.HasBody) { continue }
                    foreach ($instruction in $method.Body.Instructions) {
                        if ($instruction.Operand -is [Mono.Cecil.MethodReference] -and $instruction.Operand.FullName -match '::(AddForce|set_velocity|set_position|set_localPosition|set_localScale|set_size|set_radius|set_connectedBody|set_bodyType)\(') { throw ('Forbidden magnetic ball/model operation: ' + $instruction) }
                    }
                }
                $skillType = $assembly.MainModule.Types | Where-Object Name -eq 'PlayerSkills'
                if ($skillType.Fields.Name -contains 'ZhaoQuickShotMultiplier') { throw 'Obsolete quick-shot boost still present' }
            }
            if ($edition -eq 'original' -and $assembly.MainModule.Types.Name -contains 'ExperimentScale') { throw 'Experimental scaling in original release' }
        } finally { $assembly.Dispose() }
    }
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
function ReplaceFile($fresh, $target, $backup, $label) {
    $saved = Join-Path $backup $label
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination $saved }
    try { Copy-Item -LiteralPath $fresh -Destination $target -Force }
    catch {
        if (-not (Test-Path -LiteralPath $target)) { throw }
        $mapped = $saved + '.mapped'; Move-Item -LiteralPath $target -Destination $mapped
        try { Copy-Item -LiteralPath $fresh -Destination $target }
        catch { Move-Item -LiteralPath $mapped -Destination $target; throw }
    }
    if ((Get-FileHash -LiteralPath $fresh).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw ('Installed file differs: ' + $target) }
}
foreach ($entry in @(@('original',$taskRoot,$originalRoot),@('experiment',$experimentSource,$experimentInstall))) {
    $release = Join-Path $PSScriptRoot ('magnetic-' + $entry[0] + '-release')
    $sourceRoot = $entry[1]; $installedRoot = $entry[2]
    $sources = @('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ControlBindings.cs','ZhaoHeader.cs','PowerShot.cs','MagneticFoot.cs','PatchGame.cs','README.md')
    if ($entry[0] -eq 'experiment') { $sources += 'ExperimentScale.cs' }
    foreach ($root in @($sourceRoot,$installedRoot)) {
        $backup = Join-Path $root ('AI Mod Source\backup-before-magnetic-install-' + $stamp)
        New-Item -ItemType Directory -Path $backup -Force | Out-Null
        foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) { ReplaceFile (Join-Path $release $name) (Join-Path $root ('FanZhiYi_Data\Managed\' + $name)) $backup $name }
        ReplaceFile (Join-Path $release 'Assembly-CSharp.dll') (Join-Path $root 'AI Mod Source\Assembly-CSharp.patched.dll') $backup 'Assembly-CSharp.patched.dll'
        if ($root -eq $installedRoot) {
            foreach ($name in $sources) { ReplaceFile (Join-Path $sourceRoot ('AI Mod Source\' + $name)) (Join-Path $root ('AI Mod Source\' + $name)) $backup $name }
            if ($entry[0] -eq 'experiment') { ReplaceFile (Join-Path $sourceRoot '实验版说明.txt') (Join-Path $root '实验版说明.txt') $backup '实验版说明.txt' }
        }
        Write-Output ('Installed ' + $entry[0] + ': ' + $root + '; backup: ' + $backup)
    }
    foreach ($relative in (@('FanZhiYi_Data\Managed\GameAIMod.dll','FanZhiYi_Data\Managed\Assembly-CSharp.dll','AI Mod Source\Assembly-CSharp.patched.dll') + ($sources | ForEach-Object { 'AI Mod Source\' + $_ }))) {
        if ((Get-FileHash -LiteralPath (Join-Path $sourceRoot $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $installedRoot $relative)).Hash) { throw ('Source/install mismatch: ' + $relative) }
    }
}
Write-Output ('Production updates installed and verified; checks passed: ' + $total)
