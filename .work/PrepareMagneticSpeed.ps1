$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$taskRoot=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory -Force | Where-Object Name -like '鑼*').FullName
$source=Join-Path $taskRoot 'AI Mod Source'
$utf8=[Text.UTF8Encoding]::new($false)
foreach($src in @($source,(Join-Path $taskRoot '实验版（模型75%）\AI Mod Source'))){
    $p=Join-Path $src 'MagneticFoot.cs';$s=[IO.File]::ReadAllText($p)
    $s=[regex]::Replace($s,'(?s)        // Shorten the player.*?return Mathf.Clamp\(safeSpeed/3\.6f,\.06f,1f\);',@'
        // Match Fan's ordinary forward speed while magnetic foot owns the
        // feet. Its short control pose remains independent of the speed cap.
        return 1f / PlayerMovement.ZhaoForwardMultiplier;
'@)
    if($s -match 'safeSpeed'){throw 'Old magnetic speed cap still present'}
    $s=$s.Replace('        // Keep the short safe step, but stop weakening its pushing force.','        // Preserve the existing contest pushing force independently of speed.')
    [IO.File]::WriteAllText($p,$s,$utf8)
}
$build=[IO.File]::ReadAllText((Join-Path $source 'build\BuildKnockback.ps1')).Replace('knockback-','magnetic-speed-').Replace('MagneticFootTests','MagneticSpeedTests')
$build=$build.Replace("`$originalAssembly = Join-Path `$gameRoot 'FanZhiYi_Data\Managed\Assembly-CSharp.dll'","`$originalAssembly = Join-Path `$gameRoot 'AI Mod Source\Assembly-CSharp.original.dll'")
[IO.File]::WriteAllText((Join-Path $source 'build\BuildMagneticSpeed.ps1'),$build,$utf8)
$test=[IO.File]::ReadAllText((Join-Path $source 'build\MagneticFootTests.cs')).Replace('MagneticFootTests','MagneticSpeedTests').Replace('MagneticContactProbe','MagneticSpeedContactProbe')
$test=$test.Replace('float minAhead=100,maxGap=0,maxError=0,maxTilt=0,maxCrouch=0,maxKnee=0;int active=0;', 'float minAhead=100,maxGap=0,maxError=0,maxTilt=0,maxCrouch=0,maxKnee=0,maxActiveSpeed=0;int active=0;bool equalLimit=true,equalForce=true;')
$test=$test.Replace('if(skill.Active){active++;',@'
            if(skill.Active){active++;
                if(Held.Contains(KeyCode.LeftArrow))
                {
                    float fanLimit=(float)pt.GetField("maxVelocity").GetValue(fan);
                    float zhaoLimit=(float)pt.GetField("maxVelocity").GetValue(zhao);
                    equalLimit&=Mathf.Abs(PlayerMovement.GetMovementLimit(zhaoLimit,zhao)-fanLimit)<.001f;
                    if(!PlayerSkills.IsGroundBallContest(zhao))equalForce&=Mathf.Abs(PlayerMovement.GetMovementForce((float)pt.GetField("playerSpeed").GetValue(zhao),zhao)-(float)pt.GetField("playerSpeed").GetValue(fan))<.01f;
                    maxActiveSpeed=Mathf.Max(maxActiveSpeed,-Body(zhao).velocity.x);
                }
'@)
$test=$test.Replace('        Held.Clear();Debug.Log("MAGNETIC RUN',@'
        if(Held.Contains(KeyCode.LeftArrow)){
            Check(equalLimit&&active>0,"active magnetic forward cap equals Fan without sprint "+scenario);
            Check(equalForce,"active ordinary movement force equals Fan; contest pressure retained "+scenario);
            Debug.Log("MAGNETIC SPEED scenario="+scenario+" maxActiveSpeed="+maxActiveSpeed+" fanLimit="+pt.GetField("maxVelocity").GetValue(fan));
        }
        Held.Clear();Debug.Log("MAGNETIC RUN
'@)
$test=$test.Replace('        PlayerSkills.SetAction(zhao.GetComponent<Animator>(),"Kick",zhao);Check', '        Held.Add(KeyCode.LeftArrow);Check(Mathf.Abs(PlayerMovement.GetMultiplier(zhao)-.9f)<.001f,"classic forward speed remains unchanged");Held.Clear();' + "`r`n"+'        PlayerSkills.SetAction(zhao.GetComponent<Animator>(),"Kick",zhao);Check')
$test=$test.Replace('        Check(!skill.Active,"distant ball never triggers skill");', '        Check(!skill.Active,"distant ball never triggers skill");Held.Add(KeyCode.LeftArrow);Check(Mathf.Abs(PlayerMovement.GetMultiplier(zhao)-.9f)<.001f,"inactive forward speed stays at baseline");Held.Clear();Held.Add(KeyCode.RightArrow);Check(Mathf.Abs(PlayerMovement.GetMultiplier(zhao)-1.5f)<.001f,"backward speed stays unchanged");Held.Clear();')
[IO.File]::WriteAllText((Join-Path $source 'build\MagneticSpeedTests.cs'),$test,$utf8)
Write-Output 'Prepared magnetic forward speed update and runtime checks.'
