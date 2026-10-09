$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$taskRoot=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory -Force | Where-Object Name -like '鑼*').FullName
$build=Join-Path $taskRoot 'AI Mod Source\build'
$installed32=Join-Path (Split-Path $taskRoot -Parent) '范志毅VS赵鹏 PC双人版（32位）'
$experimentSource=Join-Path $taskRoot '实验版（模型75%）'
$experimentInstall=Join-Path $workspace '范志毅VS赵鹏 实验版（模型75%）'
$win64=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（64位）') -Directory -Force | Select-Object -First 1).FullName
$total=0
foreach($edition in @('original','experiment','win64')) {
    $tests=@(@('contest','CONTEST',52),@('ai','AIMATCH',80))
    if($edition -eq 'win64'){$tests+=,@('gameplay','BURSTRANGE',149)}
    foreach($test in $tests){
        $log=[IO.File]::ReadAllText((Join-Path $build ('knockback-'+$edition+'-'+$test[0]+'.log')))
        $result=[regex]::Match($log,($test[1]+' COMPLETE(?: baseline=False)? checks=(\d+).*failures=0'))
        if(!$result.Success -or [int]$result.Groups[1].Value -lt $test[2] -or $log -match '(CONTEST|AIMATCH|BURSTRANGE) FAIL|Exception:|TIMEOUT|GotoState'){throw ('Runtime validation failed: '+$edition+' '+$test[0])}
        $total+=[int]$result.Groups[1].Value
    }
}
Add-Type -Path (Join-Path $build 'Mono.Cecil.dll')
foreach($edition in @('original','experiment','win64')){
    foreach($name in @('GameAIMod.dll','Assembly-CSharp.dll')){
        $asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $build ('knockback-'+$edition+'-release\'+$name)))
        try{
            foreach($type in $asm.MainModule.Types){if($type.Name -match 'Tests|Probe'){throw ('Test code in production: '+$type.Name)}}
            if($name -eq 'GameAIMod.dll'){
                $skill=$asm.MainModule.Types | Where-Object Name -eq 'PlayerSkills'
                if(!($skill.Fields | Where-Object Name -eq 'TackleKnockbackBodyWidths')){throw 'Knockback missing'}
                foreach($method in $skill.Methods | Where-Object Name -eq 'UpdateTackleKnockback'){
                    foreach($instruction in $method.Body.Instructions){
                        if($instruction.Operand -is [Mono.Cecil.MethodReference] -and $instruction.Operand.FullName -match '::set_(position|rotation|velocity|bodyType|size|radius|connectedBody)\('){throw ('Non-physical shove: '+$instruction)}
                    }
                }
            }
        }finally{$asm.Dispose()}
    }
}
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
foreach($entry in @(@('original',$taskRoot,$taskRoot),@('original',$taskRoot,$installed32),@('experiment',$experimentSource,$experimentSource),@('experiment',$experimentSource,$experimentInstall),@('win64',$taskRoot,$win64))){
    $edition=$entry[0];$src=Join-Path $entry[1] 'AI Mod Source';$target=$entry[2]
    $targetSource=Join-Path $target 'AI Mod Source'
    $backup=Join-Path $targetSource ('backup-before-knockback-'+$stamp)
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    if($edition -eq 'win64' -and !(Test-Path -LiteralPath (Join-Path $targetSource 'Assembly-CSharp.original.dll'))){Copy-Item -LiteralPath (Join-Path $target 'FanZhiYi_Data\Managed\Assembly-CSharp.dll') -Destination (Join-Path $targetSource 'Assembly-CSharp.original.dll')}
    foreach($name in @('GameAIMod.dll','Assembly-CSharp.dll')){
        $dest=Join-Path $target ('FanZhiYi_Data\Managed\'+$name)
        if(Test-Path -LiteralPath $dest){Copy-Item -LiteralPath $dest -Destination (Join-Path $backup $name)}
        $fresh=Join-Path $build ('knockback-'+$edition+'-release\'+$name)
        Copy-Item -LiteralPath $fresh -Destination $dest -Force
        if((Get-FileHash -LiteralPath $fresh).Hash -ne (Get-FileHash -LiteralPath $dest).Hash){throw ('Installation mismatch: '+$dest)}
    }
    Copy-Item -LiteralPath (Join-Path $build ('knockback-'+$edition+'-release\Assembly-CSharp.dll')) -Destination (Join-Path $targetSource 'Assembly-CSharp.patched.dll') -Force
    if($entry[1] -ne $target){
        foreach($file in Get-ChildItem -LiteralPath $src -File | Where-Object Extension -in @('.cs','.md')){
            $dest=Join-Path $targetSource $file.Name
            if(Test-Path -LiteralPath $dest){Copy-Item -LiteralPath $dest -Destination (Join-Path $backup $file.Name)}
            Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
            if((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $dest).Hash){throw 'Installed source differs'}
        }
    }
    if($edition -eq 'win64'){Copy-Item -LiteralPath (Join-Path $taskRoot '按键设置.ini') -Destination (Join-Path $target '按键设置.ini') -Force;Copy-Item -LiteralPath (Join-Path $taskRoot 'AGENTS.md') -Destination (Join-Path $target 'AGENTS.md')}
    Write-Output ('Installed and verified '+$edition+': '+$target)
}
Write-Output ('Passed checks: '+$total)
