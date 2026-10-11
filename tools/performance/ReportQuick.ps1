param([string]$MeasurementRoot)
$ErrorActionPreference='Stop'
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(-not $MeasurementRoot){$MeasurementRoot=Join-Path $workspace '.work\performance'}
$evidence=Join-Path $workspace 'performance'
$data=Import-Csv -LiteralPath (Join-Path $evidence 'summary.csv')
if($data.Count -ne 24){throw 'Quick report requires 24 scenario summaries'}
function F([double]$value){return $value.ToString('F3',[Globalization.CultureInfo]::InvariantCulture)}
$rows=@();$micro=@();$validation=@()
foreach($edition in @('original','experiment')){foreach($style in @(0,1)){foreach($mode in @(0,1,2)){
 $scenario='r0-style'+$style+'-mode'+$mode
 $before=$data | Where-Object {$_.phase -eq 'baseline' -and $_.edition -eq $edition -and $_.scenario -eq $scenario}
 $after=$data | Where-Object {$_.phase -eq 'candidate' -and $_.edition -eq $edition -and $_.scenario -eq $scenario}
 if($null -eq $before -or $null -eq $after){throw ('Missing scenario '+$scenario)}
 $label=if($edition -eq 'original'){'原版'}else{'实验版'}
 $styleLabel=if($style -eq 0){'经典'}else{'特技'}
 $modeLabel=@('本地双人','范对赵 AI','赵对范 AI')[$mode]
 $gcBefore=10000*[double]$before.gc0/[double]$before.frames;$gcAfter=10000*[double]$after.gc0/[double]$after.frames
 $rows+='| '+$label+' / '+$styleLabel+' / '+$modeLabel+' | '+(F ([double]$before.mean_ms))+' → '+(F ([double]$after.mean_ms))+' | '+(F ([double]$before.p95_ms))+' → '+(F ([double]$after.p95_ms))+' | '+(F ([double]$before.p99_ms))+' → '+(F ([double]$after.p99_ms))+' | '+$before.over33+' → '+$after.over33+' | '+$before.over50+' → '+$after.over50+' | '+(F $gcBefore)+' → '+(F $gcAfter)+' |'
}}}
foreach($edition in @('original','experiment')){
 foreach($suite in @('Performance','Gameplay','UI','NetworkPair-host','NetworkPair-guest')){
  $log=Join-Path $MeasurementRoot ('regression-'+$edition+'\'+$suite+'.log')
  $result=(Select-String -LiteralPath $log -Pattern 'COMPLETE.*failures=' | Select-Object -Last 1).Line
  if($result -notmatch 'checks=(\d+) failures=(\d+)'){throw ('Missing complete validation '+$log)}
  $validation+='| '+$edition+' | '+$suite+' | '+$Matches[1]+' | '+$Matches[2]+' |'
 }
 $log=Join-Path $MeasurementRoot ('regression-'+$edition+'\Performance.log')
 $parsed=@()
 foreach($line in (Select-String -LiteralPath $log -Pattern '^PERFMICRO ' | ForEach-Object Line)){
  if($line -match '^PERFMICRO (\S+) repeat=(\d+) calls=(\d+) old_ms=(\S+) new_ms=(\S+) old_gc=(\d+) new_gc=(\d+)'){
   $parsed+=[pscustomobject]@{edition=$edition;name=$Matches[1];repeat=[int]$Matches[2];calls=[int]$Matches[3];old_ms=[double]$Matches[4];new_ms=[double]$Matches[5];old_gc=[int]$Matches[6];new_gc=[int]$Matches[7]}
  }
 }
 $micro+=$parsed
}
$micro | Export-Csv -LiteralPath (Join-Path $evidence 'micro.csv') -NoTypeInformation -Encoding UTF8
$microRows=@()
foreach($group in ($micro | Group-Object edition,name)){
 $values=@($group.Group);if($values.Count -ne 3){throw 'Expected three microbenchmark repetitions'}
 $old=@($values.old_ms | Sort-Object)[1];$new=@($values.new_ms | Sort-Object)[1]
 $microRows+='| '+$values[0].edition+' | '+$values[0].name+' | '+(F $old)+' | '+(F $new)+' | '+(F (100*(1-$new/$old)))+'% | '+(($values.old_gc -join '/'))+' → '+(($values.new_gc -join '/'))+' |'
}
$hardware=@()
try{$cpu=Get-CimInstance Win32_Processor | Select-Object -First 1;$hardware+='CPU: '+$cpu.Name}catch{$hardware+='CPU: unavailable'}
try{$os=Get-CimInstance Win32_OperatingSystem;$hardware+='OS: '+$os.Caption+' '+$os.Version}catch{$hardware+='OS: unavailable'}
try{$gpu=Get-CimInstance Win32_VideoController;$hardware+='GPU: '+(($gpu | ForEach-Object {$_.Name+' ('+$_.DriverVersion+')'}) -join '; ')}catch{$hardware+='GPU: unavailable'}
$hardware=@($hardware | ForEach-Object {$_.TrimEnd()})
[IO.File]::WriteAllLines((Join-Path $evidence 'environment.txt'),$hardware,[Text.UTF8Encoding]::new($false))
$report=@(
'# 两版游戏的性能优化与简短检查',
'',
'## 检查范围',
'',
'2026-10-10：用户要求缩短检查，停止原定完整矩阵，改做两版的短测和关键回归。此前未完成的长测不作为交付结论。一次长测曾遇到数分钟环境中断，记录保留在隔离工作目录中。',
'',
'本次短测：每版覆盖经典/特技 × 本地双人/范对赵 AI/赵对范 AI；每场预热 2 秒、采样 4 秒、1 次。基线来自提交 `65b270ba388ac1c871abe6e0381dc050808258e6`，优化版使用当前源码。所有游戏按顺序运行，1280×720，VSync=1，targetFrameRate=-1。每场相同随机种子和实时输入安排。测试副本延长比赛时钟、替换输入并允许后台运行；正式版没有这些入口。',
'',
'**这些是隐藏窗口的代码负载对照，不能换算成实际游玩 FPS。短测没有三次整场重复，不能排除自然波动，不能证明持续卡顿已经解决。** 引擎 CPU/GPU/Physics Recorder 虽被尝试，但没有有效样本，明确标为不可用。启动和菜单准备开销不计入比赛采样。',
'',
'环境：',
'',($hardware | ForEach-Object {'- '+$_}),
'',
'## 实施的优化',
'',
'- 球门高度仅在完整越线判断时查询；保留实时几何检查，避免实验版缩放或球门变动后使用旧数据。物理射线和 Cast 复用容器，满时扩容并重新查询，保留命中顺序、遮罩、触发器过滤和判定公式。',
'- 行走、磁吸脚、实验版碰撞修正复用动画片段列表；动态动画权重和状态仍在原时点读取。稳定反射字段、方法和组件初始化时缓存。',
'- 大力射门维护角色生命周期集合，保留原来的活动对象范围和顺序。肢体缓存支持隐藏/重新启用；实验版接触点缓冲复用，接触冲量公式和顺序不变。',
'- 技能文字、模式提示复用 GUIStyle 和描边偏移；足球高亮纹理和字体在开赛前准备。纹理像素公式、字号、颜色、位置、轨迹和动画参数不变。',
'- 联机输入/快照复用显式小端序读写缓冲，发送有效字节数，保留协议、频率、确认与快照应用时点。菜单仅在状态或尺寸变化时更新。截断快照在消耗序号前拒绝。',
'',
'物理固定步长 0.02 秒、位置迭代 64、速度迭代 24；AI 决策频率、技能参数、画质、特效数量和正式版帧率设置保持原值。两版模型、球门和球路差异保留。',
'',
'接口参考：[Unity 2019.4 RaycastNonAlloc](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Physics2D.RaycastNonAlloc.html)、[Animator.GetCurrentAnimatorClipInfo 列表重载](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Animator.GetCurrentAnimatorClipInfo.html)。',
'',
'## 短测实际结果',
'',
'单位：毫秒；箭头左侧基线，右侧优化版。慢帧列为各 4 秒采样中的帧数。GC 使用每 10,000 帧的 Gen0 次数归一化，避免吞吐量变化误导。完整帧数、物理步、内存、最大帧耗时和 GC0/1/2 见 CSV。',
'',
'| 版本 / 模式 / 对战 | 平均 ms | P95 ms | P99 ms | >33.3 ms | >50 ms | GC / 万帧 |',
'|---|---:|---:|---:|---:|---:|---:|',($rows),
'',
'## 各组微型对照',
'',
'每组旧/新各执行 10,000 次，预热后重复 3 次；表中耗时为中位数。它验证这些具体调用的开销变化，**并不是整场游戏或实际 FPS 的提升百分比**。射线负载包含 48 个有效命中；快照负载含 27 个身体状态；GUI 样式比较原生复制构造与复用，不执行实际绘制。GC 列逐次列出三轮结果。',
'',
'| 版本 | 调用组 | 旧 ms | 新 ms | 调用耗时减少 | 旧/新 GC 三轮 |',
'|---|---|---:|---:|---:|---|',($microRows),
'',
'## 关键回归',
'',
'| 版本 | 检查组 | 检查数 | 失败数 |',
'|---|---|---:|---:|',($validation),
'',
'Performance 组还执行 30,003 次协议编解码比较：旧 BinaryWriter/BinaryReader 字节格式、浮点位模式、边界与扩容，以及旧快照中的身体状态和比分。检查动画片段和混合权重、射线排序/过滤/扩容、球门移动后的实时高度、角色与肢体重新启用、四次场景重建的集合清理和物理参数。',
'',
'Gameplay 沿用现有断言和容差；UI 检查透明全页、联机/返回按钮分离、必要控件、1280×720、1280×800 和 800×600、状态变化及重新打开。NetworkPair 使用相同 DLL 的两个独立进程，检查建房/加入、移动/动作确认、快照、比分、断线清除输入、重新建房加入，并从经典切到特技。它使用本机回环网络；没有验证第二台实体电脑的网络/驱动/防火墙。',
'',
'### 已存在的压力测试问题',
'',
'原版 Gameplay 149 项中，关节强冲撞压力用例在优化前代码也失败：关节误差约 0.620，既有阈值 0.4。优化版首次约 0.618、原样复测约 0.432，其他 148 项通过。没有修改断言、阈值或物理参数。原版第三次原样复测通过全部 149 项；不能把前两次记录称为通过。正式安装仍要求关键检查最新结果没有失败或异常。最后一次结果见上表，原始情况见[检查说明](performance/validation-notes.txt)。',
'',
'按用户最新要求，没有继续执行完整的 20秒预热/120秒×3 整场矩阵、所有专题玩法测试、长期内存压力或完整视觉逐帧比较。保留了可复现工具，后续需要时可运行。',
'',
'## 证据与交付校验',
'',
'- [24 场短测原始摘要](performance/summary.csv)、[方法计时](performance/methods.csv)、[引擎指标可用性](performance/engine.csv)、[微型对照原始数据](performance/micro.csv)、[关键测试日志](performance/validation.txt)。',
'- [测量和回归工具](tools/performance/README.md)；候选源码 SHA-256 随隔离测试保存，正式构建安装前核对。',
'- 安装脚本要求短测和关键测试成功，重新编译不带测试的正式 DLL；检查测试类型与调用、备份原运行 DLL 和源码、安装两版，再逐文件核对游戏包。',
'- 所有安装目录的按键配置独立保留并比较安装前后哈希；分发包使用仓库默认配置。[配置保留校验](performance/preserved-controls.csv)、[游戏包校验](downloads/SHA256SUMS.txt)。',
'',
'## 结论的范围',
'',
'本次消除了多个已确认的重复查询和临时分配，并用微型对照与关键回归核实。短场景数据只提供初步负载参考。当前证据不足以确认用户实际卡顿的主要原因或排除引擎物理、渲染、驱动和系统调度的影响，也不足以作出长期稳定性保证。'
)
$flatReport=foreach($part in $report){foreach($line in @($part)){([string]$line).TrimEnd()}}
[IO.File]::WriteAllLines((Join-Path $workspace 'PERFORMANCE.md'),[string[]]$flatReport,[Text.UTF8Encoding]::new($false))
Write-Output ($rows -join [Environment]::NewLine)
Write-Output ($microRows -join [Environment]::NewLine)
Write-Output ($validation -join [Environment]::NewLine)
Write-Output 'QUICK REPORT WRITTEN'
