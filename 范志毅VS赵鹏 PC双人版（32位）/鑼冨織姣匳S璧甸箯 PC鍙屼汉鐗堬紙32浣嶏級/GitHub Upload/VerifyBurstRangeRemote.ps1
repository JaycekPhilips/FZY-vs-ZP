$ErrorActionPreference = 'Stop'
$taskRepo = Join-Path $PSScriptRoot 'crazy-soccer-game'
$taskHead = (& git -C $taskRepo rev-parse HEAD).Trim()
$taskRefFile = Join-Path $PSScriptRoot 'remote-main.json'
$taskTreeFile = Join-Path $PSScriptRoot 'remote-tree.json'
& curl.exe --fail --silent --show-error --max-time 45 -H 'Accept: application/vnd.github+json' -H 'User-Agent: Crazy-Soccer-Verification' 'https://api.github.com/repos/JaycekPhilips/crazy-soccer-game/git/refs/heads/main' -o $taskRefFile
if ($LASTEXITCODE -ne 0) { throw 'Remote branch read failed' }
$taskRef = [IO.File]::ReadAllText($taskRefFile) | ConvertFrom-Json
if ($taskRef.object.sha -ne $taskHead) { throw ('Remote main not updated yet: ' + $taskRef.object.sha) }
& curl.exe --fail --silent --show-error --max-time 45 -H 'Accept: application/vnd.github+json' -H 'User-Agent: Crazy-Soccer-Verification' ('https://api.github.com/repos/JaycekPhilips/crazy-soccer-game/git/trees/' + $taskHead + '?recursive=1') -o $taskTreeFile
if ($LASTEXITCODE -ne 0) { throw 'Remote file tree read failed' }
$taskTree = [IO.File]::ReadAllText($taskTreeFile) | ConvertFrom-Json
if ($taskTree.truncated) { throw 'Remote tree truncated' }
$taskBlobs = @($taskTree.tree | Where-Object type -eq 'blob')
$taskLocal = @{}
foreach ($taskLine in (& git -C $taskRepo -c core.quotepath=false ls-tree -r HEAD)) {
 if ($taskLine -notmatch '^\d+ blob ([a-f0-9]+)\t(.+)$') { throw ('Unexpected local tree entry: ' + $taskLine) }
 $taskLocal[$Matches[2]] = $Matches[1]
}
if ($taskLocal.Count -ne $taskBlobs.Count) { throw 'Remote file count differs' }
foreach ($taskBlob in $taskBlobs) {
 if ($taskLocal[$taskBlob.path] -ne $taskBlob.sha) { throw ('Remote file differs: ' + $taskBlob.path) }
}
if ((& git -C $taskRepo status --porcelain)) { throw 'Repository has uncommitted files' }
Write-Output ('Verified remote main ' + $taskHead + '; all ' + $taskBlobs.Count + ' files match, including both complete archives.')
