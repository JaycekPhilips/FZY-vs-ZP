$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$originalRoot = Join-Path (Split-Path $taskRoot -Parent) '范志毅VS赵鹏 PC双人版（32位）'
$experimentSource = Join-Path $taskRoot '实验版（模型75%）'
$experimentInstall = Join-Path (Split-Path (Split-Path $taskRoot -Parent) -Parent) '范志毅VS赵鹏 实验版（模型75%）'
$total = 0
foreach ($edition in @('original','experiment')) {
    foreach ($test in @(@('gameplay','POWER',59),@('controls','CONTROLS',68),@('boundary','BOUNDARY',150),@('defense','DEFENSE',40))) {
        $path = Join-Path $PSScriptRoot ('power-' + $edition + '-' + $test[0] + '.log')
        $log = [IO.File]::ReadAllText($path)
        $result = [regex]::Match($log,($test[1] + ' COMPLETE(?: baseline=False)? checks=(\d+).*failures=0'))
        if (-not $result.Success -or [int]$result.Groups[1].Value -lt $test[2] -or $log -match '(POWER|CONTROLS|BOUNDARY|DEFENSE) FAIL|Exception:|TIMEOUT|GotoState') { throw ('Required runtime checks failed: ' + $path) }
        $total += [int]$result.Groups[1].Value
    }
}
$goalLog = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'power-experiment-goals.log'))
if ($goalLog -notmatch 'GOALHEIGHT COMPLETE checks=140 failures=0' -or $goalLog -match 'GOALHEIGHT FAIL|Exception:|TIMEOUT') { throw 'Reduced-goal regression checks failed' }
$total += 140
Add-Type -Path (Join-Path $PSScriptRoot 'Mono.Cecil.dll')
function AssertProduction($types) {
    foreach ($type in $types) {
        if ($type.Name -match 'Tests|Probe') { throw ('Test component in release: ' + $type.FullName) }
        AssertProduction $type.NestedTypes
    }
}
foreach ($edition in @('original','experiment')) {
    $release = Join-Path $PSScriptRoot ('power-' + $edition + '-release')
    foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) {
        $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $release $name))
        try {
            AssertProduction $assembly.MainModule.Types
            if ($name -eq 'GameAIMod.dll' -and $assembly.MainModule.Types.Name -notcontains 'PowerShot') { throw 'Powered shooting missing' }
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
    $release = Join-Path $PSScriptRoot ('power-' + $entry[0] + '-release')
    $sourceRoot = $entry[1]; $installedRoot = $entry[2]
    $sources = @('GameAIMod.cs','PlayerSkills.cs','PlayerMovement.cs','ControlBindings.cs','ZhaoHeader.cs','PowerShot.cs','PatchGame.cs','README.md')
    if ($entry[0] -eq 'experiment') { $sources += 'ExperimentScale.cs' }
    foreach ($root in @($sourceRoot,$installedRoot)) {
        $backup = Join-Path $root ('AI Mod Source\backup-before-power-install-' + $stamp)
        New-Item -ItemType Directory -Path $backup -Force | Out-Null
        foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) { ReplaceFile (Join-Path $release $name) (Join-Path $root ('FanZhiYi_Data\Managed\' + $name)) $backup $name }
        ReplaceFile (Join-Path $release 'Assembly-CSharp.dll') (Join-Path $root 'AI Mod Source\Assembly-CSharp.patched.dll') $backup 'Assembly-CSharp.patched.dll'
        if ($root -eq $installedRoot) {
            foreach ($name in $sources) { ReplaceFile (Join-Path $sourceRoot ('AI Mod Source\' + $name)) (Join-Path $root ('AI Mod Source\' + $name)) $backup $name }
            if ($entry[0] -eq 'experiment') { ReplaceFile (Join-Path $sourceRoot '实验版说明.txt') (Join-Path $root '实验版说明.txt') $backup '实验版说明.txt' }
        }
        $settings = Join-Path $root '按键设置.ini'
        if (Test-Path -LiteralPath $settings) { Copy-Item -LiteralPath $settings -Destination (Join-Path $backup '按键设置.ini') }
        # Preserve existing custom movement/shooting keys, migrate the header
        # and add the two explicitly requested powered-shooting defaults.
        $lines = if (Test-Path -LiteralPath $settings) { [IO.File]::ReadAllLines($settings) } else { @('[Fan]','Head=J','','[Zhao]','Head=RightShift') }
        $output = New-Object 'System.Collections.Generic.List[string]'; $section = ''; $hasPower = $false
        foreach ($line in $lines) {
            if ($line -match '^\[(Fan|Zhao)\]$') {
                if ($section -and -not $hasPower) { $output.Add($(if ($section -eq 'Fan') { 'PowerKick=L' } else { 'PowerKick=Keypad0' })) }
                $section = $Matches[1]; $hasPower = $false
            }
            if ($section -eq 'Zhao' -and $line -match '^Head=') { $output.Add('Head=RightShift') }
            elseif ($line -match '^PowerKick=') { $output.Add($(if ($section -eq 'Fan') { 'PowerKick=L' } else { 'PowerKick=Keypad0' })); $hasPower = $true }
            else { $output.Add($line) }
        }
        if ($section -and -not $hasPower) { $output.Add($(if ($section -eq 'Fan') { 'PowerKick=L' } else { 'PowerKick=Keypad0' })) }
        [IO.File]::WriteAllLines($settings,$output,[Text.UTF8Encoding]::new($false))
        Write-Output ('Installed ' + $entry[0] + ': ' + $root + '; backup: ' + $backup)
    }
    foreach ($relative in (@('FanZhiYi_Data\Managed\GameAIMod.dll','FanZhiYi_Data\Managed\Assembly-CSharp.dll','AI Mod Source\Assembly-CSharp.patched.dll') + ($sources | ForEach-Object { 'AI Mod Source\' + $_ }))) {
        if ((Get-FileHash -LiteralPath (Join-Path $sourceRoot $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $installedRoot $relative)).Hash) { throw ('Source/install mismatch: ' + $relative) }
    }
}
Write-Output ('Production updates installed and verified; checks passed: ' + $total)
