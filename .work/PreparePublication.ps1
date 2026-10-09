$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$taskRoot=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory -Force | Where-Object Name -like '鑼*').FullName
$repoRoot=Join-Path $taskRoot 'GitHub Upload\crazy-soccer-game'
$utf8=[Text.UTF8Encoding]::new($false)
foreach($src in @((Join-Path $taskRoot 'AI Mod Source'),(Join-Path $taskRoot '实验版（模型75%）\AI Mod Source'))){
    $p=Join-Path $src 'README.md'
    $doc=[IO.File]::ReadAllText($p)
    $doc=$doc.Replace('地面／隔球／空中失衡参数为','新增约两个身位的水平击退：以范志毅站立时的实际身体宽度计算目标距离，模型 75% 版相应缩短；整套骨架通过水平冲量推开并减速，击退后段通过真实力矩辅助恢复平衡。直接碰撞、隔球顶牛与空中碰撞共用这一效果；球门或障碍物会限制实际距离。地面／隔球／空中失衡参数为')
    $doc+="`r`n## 2026-10-09 强力抢断击退更新`r`n`r`n赵鹏强力抢断保留短暂失衡，新增约两个身位的击退，隔着足球顶牛也能触发。按各版实际站立模型宽度计算距离，仅对真实身体碰撞或脚—球—脚接触链施力；暂停、进球、出界和重置清理击退，真实障碍物限制位移。不搬移球员、不绕过碰撞、不更改骨架。32 位原版、模型 75% 实验版和 64 位原版均有正式构建。`r`n"
    [IO.File]::WriteAllText($p,$doc,$utf8)
}
$package=[IO.File]::ReadAllText((Join-Path $taskRoot 'GitHub Upload\PreparePublication.ps1'))
$package=$package.Replace('$taskRoot = Split-Path $PSScriptRoot -Parent',@'
$workspace=Split-Path $PSScriptRoot -Parent
$taskRoot=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory -Force | Where-Object Name -like '鑼*').FullName
$win64=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（64位）') -Directory -Force | Select-Object -First 1).FullName
'@)
$package=$package.Replace("`$repoRoot = Join-Path `$PSScriptRoot 'crazy-soccer-game'","`$repoRoot = Join-Path `$taskRoot 'GitHub Upload\crazy-soccer-game'")
$package=$package.Replace("@('original','experimental')","@('original','experimental','original-win64')")
$package=$package.Replace("`$gameRoot = if (`$edition -eq 'original') { `$taskRoot } else { Join-Path `$taskRoot '实验版（模型75%）' }","`$gameRoot = if (`$edition -eq 'original-win64') { `$win64 } elseif (`$edition -eq 'original') { `$taskRoot } else { Join-Path `$taskRoot '实验版（模型75%）' }")
$package=$package.Replace("`$installedRoot = if (`$edition -eq 'original')", "`$installedRoot = if (`$edition -eq 'original-win64') { `$win64 } elseif (`$edition -eq 'original')")
$package=$package.Replace("    foreach (`$name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe','按键设置.ini'))", "    `$packageFiles = if (`$edition -eq 'original-win64') { @('D.exe','UnityPlayer.dll','UnityCrashHandler64.exe','按键设置.ini') } else { @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe','按键设置.ini') }; foreach (`$name in `$packageFiles)")
$package=$package.Replace("`$archiveName = 'crazy-soccer-game-' + `$edition + '-win32.zip'", "`$archiveName = if (`$edition -eq 'original-win64') { 'crazy-soccer-game-original-win64.zip' } else { 'crazy-soccer-game-' + `$edition + '-win32.zip' }")
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'PackageKnockback.ps1'),$package,$utf8)
$p=Join-Path $repoRoot 'README.md';$doc=[IO.File]::ReadAllText($p)
$doc=$doc.Replace('更新时间：2026-10-08。','更新时间：2026-10-09。')
$doc=$doc.Replace('**848 项运行检查**','**545 项运行检查**')
$doc=$doc.Replace('本次正式构建在隔离游戏副本中通过两版合计','本次正式构建在隔离游戏副本中通过三版合计')
$doc=$doc.Replace('覆盖上角与地滚球进球、门后出界与反弹、半自动抢截、AI 顶牛与高空球、正常射门','覆盖强力抢断的地面/空中直接碰撞、隔球顶牛、约两个身位击退、持续对抗冷却、障碍物阻挡、暂停重置、AI 顶牛与高空球，以及 64 位版正常射门')
$doc=$doc.Replace('两个游戏包均为 Windows 32 位程序，也可在支持 32 位应用的 64 位 Windows 系统上运行。','原版 32 位与实验版是 Windows 32 位程序；64 位原版启动文件为 `D.exe`。')
$doc=$doc.Replace('两版都支持','三版都支持')
$doc=$doc.Replace('Windows 足球对战游戏，包含原版和模型 75% 实验版。','Windows 足球对战游戏，包含 32 位原版、64 位原版和模型 75% 实验版。')
$doc=$doc.Replace('| 实验版 |','| 64 位原版 | [下载 64 位原版](https://github.com/JaycekPhilips/crazy-soccer-game/raw/refs/heads/main/downloads/crazy-soccer-game-original-win64.zip) | 原版模型与球场，64 位运行程序 |'+"`r`n"+'| 实验版 |')
$doc+="`r`n### 强力抢断击退更新`r`n`r`n三版特技模式中，赵鹏强力抢断使范志毅短暂失衡，并向后推开约两个身位。身位按实际站立模型宽度计算；隔着贴地足球真实顶牛也能触发，无需新按键。击退由整套骨架的真实冲量完成，后段减速并恢复平衡，球门和障碍物会限制实际距离。暂停、进球、出界及重置清除击退，持续接触保留冷却；经典模式保持普通身体对抗。`r`n"
[IO.File]::WriteAllText($p,$doc,$utf8)
$p=Join-Path $repoRoot 'tools\Build.ps1';$code=[IO.File]::ReadAllText($p).Replace("[ValidateSet('original','experimental')]","[ValidateSet('original','experimental','original-win64')]")
$code=$code.Replace("if (-not (Test-Path -LiteralPath (Join-Path `$gameRoot 'FanZhiYi.exe')))","`$exe = if (`$Edition -eq 'original-win64') { 'D.exe' } else { 'FanZhiYi.exe' }; if (-not (Test-Path -LiteralPath (Join-Path `$gameRoot `$exe)))")
$code=$code.Replace("    Expand-Archive -LiteralPath (Join-Path `$repoRoot ('downloads\crazy-soccer-game-'+`$Edition+'-win32.zip')) -DestinationPath `$runtime -Force", "    `$archive = if (`$Edition -eq 'original-win64') { 'downloads\crazy-soccer-game-original-win64.zip' } else { 'downloads\crazy-soccer-game-'+`$Edition+'-win32.zip' }; Expand-Archive -LiteralPath (Join-Path `$repoRoot `$archive) -DestinationPath `$runtime -Force")
[IO.File]::WriteAllText($p,$code,$utf8)
Copy-Item -LiteralPath (Join-Path $taskRoot 'AI Mod Source\build\KnockbackTests.cs') -Destination (Join-Path $repoRoot 'tests\KnockbackTests.cs') -Force
Write-Output 'Prepared publication documentation and three-edition packaging.'
