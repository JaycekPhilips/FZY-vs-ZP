$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$repoRoot = Join-Path $PSScriptRoot 'crazy-soccer-game'
$downloads = Join-Path $repoRoot 'downloads'
New-Item -ItemType Directory -Path $repoRoot,$downloads -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifest = @()
$packageBase = Join-Path $PSScriptRoot ('packages-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $packageBase -Force | Out-Null
foreach ($edition in @('original','experimental')) {
    $gameRoot = if ($edition -eq 'original') { $taskRoot } else { Join-Path $taskRoot '实验版（模型75%）' }
    $installedRoot = if ($edition -eq 'original') { Join-Path (Split-Path $taskRoot -Parent) '范志毅VS赵鹏 PC双人版（32位）' } else { Join-Path (Split-Path (Split-Path $taskRoot -Parent) -Parent) '范志毅VS赵鹏 实验版（模型75%）' }
    $packageRoot = Join-Path $packageBase ('package-' + $edition)
    if (Test-Path -LiteralPath $packageRoot) { throw ('Package already exists: ' + $packageRoot) }
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    foreach ($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe','按键设置.ini')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $packageRoot }
    foreach ($name in @('FanZhiYi_Data','MonoBleedingEdge')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $packageRoot -Recurse }
    $sourceRoot = Join-Path $gameRoot 'AI Mod Source'
    $packageSource = Join-Path $packageRoot 'AI Mod Source'
    $repoSource = Join-Path $repoRoot ('source\' + $edition)
    New-Item -ItemType Directory -Path $packageSource,$repoSource -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -File) {
        if ($file.Extension -notin @('.cs','.dll','.md')) { continue }
        Copy-Item -LiteralPath $file.FullName -Destination $packageSource
        Copy-Item -LiteralPath $file.FullName -Destination $repoSource
    }
    if ($edition -eq 'experimental') { Copy-Item -LiteralPath (Join-Path $gameRoot '实验版说明.txt') -Destination $packageRoot }
    $fileManifest = @()
    foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File -Recurse) {
        $relative = $file.FullName.Substring($packageRoot.Length+1)
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $gameRoot $relative) -Algorithm SHA256).Hash) { throw ('Packaging mismatch: ' + $relative) }
        if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $installedRoot $relative) -Algorithm SHA256).Hash) { throw ('Installed game differs: ' + $relative) }
        $fileManifest += [pscustomobject]@{ path=$relative.Replace('\','/'); bytes=$file.Length; sha256=$hash.ToLower() }
    }
    $archiveName = 'crazy-soccer-game-' + $edition + '-win32.zip'
    $archivePath = Join-Path $packageBase $archiveName
    [IO.Compression.ZipFile]::CreateFromDirectory($packageRoot,$archivePath,[IO.Compression.CompressionLevel]::Optimal,$true)
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $files = @($archive.Entries | Where-Object { $_.Name -ne '' })
        if ($files.Count -ne $fileManifest.Count) { throw 'Archive file count mismatch' }
        foreach ($entry in $files) {
            $relative = $entry.FullName.Substring($entry.FullName.IndexOf('/')+1)
            $expected = $fileManifest | Where-Object path -eq $relative
            if ($null -eq $expected -or $expected.bytes -ne $entry.Length) { throw ('Archive entry differs: ' + $entry.FullName) }
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $actual = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLower() }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ($actual -ne $expected.sha256) { throw ('Archive hash differs: ' + $entry.FullName) }
        }
    } finally { $archive.Dispose() }
    $manifest += [pscustomobject]@{ edition=$edition; archive=('downloads/' + $archiveName); archiveBytes=(Get-Item -LiteralPath $archivePath).Length; archiveSha256=(Get-FileHash -LiteralPath $archivePath).Hash.ToLower(); files=$fileManifest }
    Write-Output ('Packaged and verified ' + $edition + ': ' + $fileManifest.Count + ' files; ' + [Math]::Round((Get-Item -LiteralPath $archivePath).Length/1MB,2) + ' MB')
}
foreach ($item in $manifest) {
    Copy-Item -LiteralPath (Join-Path $packageBase (Split-Path $item.archive -Leaf)) -Destination (Join-Path $repoRoot $item.archive) -Force
    if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $item.archive)).Hash.ToLower() -ne $item.archiveSha256) { throw 'Final archive differs' }
}
[IO.File]::WriteAllText((Join-Path $repoRoot 'manifest.json'),($manifest | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$checksums = $manifest | ForEach-Object { $_.archiveSha256 + '  ' + $_.archive }
[IO.File]::WriteAllLines((Join-Path $repoRoot 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
Write-Output 'Both archives match every source and installed game file.'
