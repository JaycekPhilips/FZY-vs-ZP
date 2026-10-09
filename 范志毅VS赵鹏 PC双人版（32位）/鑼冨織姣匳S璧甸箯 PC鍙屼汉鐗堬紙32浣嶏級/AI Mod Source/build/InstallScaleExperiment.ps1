$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$experimentRoot = Join-Path $taskRoot '实验版（模型75%）'
$release = Join-Path $PSScriptRoot 'scale-release'
$publishRoot = Join-Path (Split-Path (Split-Path $taskRoot -Parent) -Parent) '范志毅VS赵鹏 实验版（模型75%）'
foreach ($entry in @(@('scale-test.log','SCALE COMPLETE checks=91 failures=0'),@('scale-boundary-test.log','BOUNDARY COMPLETE checks=150 failures=0'),@('scale-controls-test.log','CONTROLS COMPLETE checks=66 failures=0'))) {
    $log = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $entry[0]))
    if (-not $log.Contains($entry[1]) -or $log -match '(SCALE|BOUNDARY|CONTROLS) FAIL|Exception:|TIMEOUT|GotoState') { throw ('Runtime verification did not pass: ' + $entry[0]) }
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
        if ($name -eq 'GameAIMod.dll' -and ($assembly.MainModule.Types.Name -notcontains 'ExperimentScale' -or $assembly.MainModule.Types.Name -notcontains 'ExperimentActor')) { throw 'Experimental scaling component missing' }
    } finally { $assembly.Dispose() }
}
# Snapshot and verify the installed original without writing to it.
$originalRoot = Join-Path (Split-Path $taskRoot -Parent) '范志毅VS赵鹏 PC双人版（32位）'
foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) {
    $originalFile = Join-Path $originalRoot ('FanZhiYi_Data\Managed\' + $name)
    $originalRelease = Join-Path $PSScriptRoot ('zhao-volley-release\' + $name)
    if ((Get-FileHash -LiteralPath $originalFile).Hash -ne (Get-FileHash -LiteralPath $originalRelease).Hash) { throw ('Original release differs: ' + $name) }
    Copy-Item -LiteralPath (Join-Path $release $name) -Destination (Join-Path $experimentRoot ('FanZhiYi_Data\Managed\' + $name)) -Force
}
Copy-Item -LiteralPath (Join-Path $release 'Assembly-CSharp.dll') -Destination (Join-Path $experimentRoot 'AI Mod Source\Assembly-CSharp.patched.dll') -Force
if (Test-Path -LiteralPath $publishRoot) { throw ('Publish folder already exists; refusing to overwrite: ' + $publishRoot) }
Copy-Item -LiteralPath $experimentRoot -Destination $publishRoot -Recurse
$sourceFiles = Get-ChildItem -LiteralPath $experimentRoot -Recurse -File
foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($experimentRoot.Length + 1)
    $published = Join-Path $publishRoot $relative
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $published).Hash) { throw ('Published copy differs: ' + $relative) }
}
foreach ($name in @('GameAIMod.dll','Assembly-CSharp.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $originalRoot ('FanZhiYi_Data\Managed\' + $name))).Hash -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('zhao-volley-release\' + $name))).Hash) { throw 'Original changed during publication' }
}
Write-Output ('Published verified experimental copy: ' + $publishRoot)
Write-Output ('Matching files: ' + $sourceFiles.Count + '; production contains no tests; original release unchanged.')
