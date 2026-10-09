$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$taskRoot=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory -Force | Where-Object Name -like '鑼*').FullName
$buildRoot=Join-Path $taskRoot 'AI Mod Source\build'
$utf8=[Text.UTF8Encoding]::new($false)
$code=[IO.File]::ReadAllText((Join-Path $buildRoot 'GroundContestTests.cs')).Replace('GroundContestTests','KnockbackTests')
$code=$code.Replace('    float firstHit=-1f,lastHit=-10f,minInterval=100f;', @'
    float firstHit=-1f,lastHit=-10f,minInterval=100f;
    float hitOrigin, hitWidth, maxTravel;
    float Center(Component p) { float m=0,x=0; foreach(Rigidbody2D b in p.GetComponentsInChildren<Rigidbody2D>()){m+=b.mass;x+=b.mass*b.worldCenterOfMass.x;}return x/m; }
'@)
$code=$code.Replace('        if(host.firstHit<0)host.firstHit=Time.time;', @'
        if(host.firstHit<0) { host.firstHit=Time.time; host.hitOrigin=host.Center(opponent);host.hitWidth=(float)host.Field(opponent.GetComponent<PlayerSkills>(),"standingBodyWidth");host.maxTravel=0; }
'@)
$code=$code.Replace('            weak|=(bool)Field', '            if(firstHit>=0)maxTravel=Mathf.Max(maxTravel,hitOrigin-Center(fan));' + "`r`n            weak|=(bool)Field")
$code=$code.Replace('        Held.Clear();Debug.Log("CONTEST RUN', '        if(!sustained)Check(maxTravel/hitWidth>=1.75f&&maxTravel/hitWidth<=2.4f,"through-ball pushes Fan two body widths actual="+(maxTravel/hitWidth));' + "`r`n" + '        Held.Clear();Debug.Log("CONTEST RUN')
$code=$code.Replace('    IEnumerator Guards()', @'
    IEnumerator Direct(bool aerial)
    {
        yield return Scene(true);
        float scale=Mathf.Abs(zhao.transform.lossyScale.x)/.8f;
        Move(fan,new Vector2(Body(zhao).position.x-1.25f*scale,Body(fan).position.y));
        if(aerial){Held.Add(KeyCode.W);Held.Add(KeyCode.UpArrow);yield return new WaitForSeconds(.12f);Held.Clear();}
        Held.Add(KeyCode.D);Held.Add(KeyCode.LeftArrow);
        float tilt=0,error=0,recovered=-1;bool weak=false;
        for(int i=0;i<120;i++)
        {
            yield return new WaitForFixedUpdate();
            if(firstHit>=0){maxTravel=Mathf.Max(maxTravel,hitOrigin-Center(fan));if(Time.time-firstHit>.08f)Held.Clear();}
            tilt=Mathf.Max(tilt,Tilt(fan));error=Mathf.Max(error,Error(fan),Error(zhao));
            weak|=(bool)Field(fan.GetComponent<PlayerSkills>(),"weakenedBalance");
            if(firstHit>=0&&Time.time-firstHit>.3f&&!(bool)Field(fan.GetComponent<PlayerSkills>(),"weakenedBalance")&&recovered<0)recovered=Time.time-firstHit;
        }
        Held.Clear();Check(directHits>0&&bridgeHits==0,"real direct-body tackle air="+aerial);
        Check(maxTravel/hitWidth>=1.75f&&maxTravel/hitWidth<=2.4f,"direct pushes two body widths air="+aerial+" actual="+(maxTravel/hitWidth));
        Check(weak&&tilt>2f&&error<.35f,"direct tackle staggers intact rig air="+aerial+" tilt="+tilt+" error="+error);
        Check(recovered>=0&&recovered<.85f,"direct balance recovers air="+aerial+" time="+recovered);
        Check((float)Field(fan.GetComponent<PlayerSkills>(),"knockbackUntil")<0,"shove finishes air="+aerial);
        PlayerSkills.ResetAll();Check((float)Field(fan.GetComponent<PlayerSkills>(),"knockbackUntil")<0,"reset clears shove air="+aerial);
    }

    IEnumerator Guards()
'@)
$code=$code.Replace('yield return Bridge(true);','yield return Bridge(true);yield return Direct(false);yield return Direct(true);')
[IO.File]::WriteAllText((Join-Path $buildRoot 'KnockbackTests.cs'),$code,$utf8)
$path=Join-Path $buildRoot 'BuildKnockback.ps1'
$build=[IO.File]::ReadAllText($path)
$build=$build.Replace("[ValidateSet('Original','Experiment')]","[ValidateSet('Original','Experiment','Win64')]")
$build=$build.Replace("`$source = Join-Path `$gameRoot 'AI Mod Source'", @'
$source = Join-Path $gameRoot 'AI Mod Source'
$originalAssembly = Join-Path $source 'Assembly-CSharp.original.dll'
if ($Edition -eq 'Win64') {
    $source = Join-Path $taskRoot 'AI Mod Source'
    $workspace = Split-Path (Split-Path $taskRoot -Parent) -Parent
    $gameRoot = (Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（64位）') -Directory -Force | Select-Object -First 1).FullName
    $originalAssembly = Join-Path $gameRoot 'FanZhiYi_Data\Managed\Assembly-CSharp.dll'
}
'@)
$build=$build.Replace("foreach (`$name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe')) { Copy-Item -LiteralPath (Join-Path `$gameRoot `$name) -Destination `$testPlayer }", @'
    if ($Edition -eq 'Win64') {
        Copy-Item -LiteralPath (Join-Path $gameRoot 'D.exe') -Destination (Join-Path $testPlayer 'FanZhiYi.exe')
        foreach ($name in @('UnityPlayer.dll','UnityCrashHandler64.exe')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer }
    } else {
        foreach ($name in @('FanZhiYi.exe','UnityPlayer.dll','UnityCrashHandler32.exe')) { Copy-Item -LiteralPath (Join-Path $gameRoot $name) -Destination $testPlayer }
    }
'@)
$build=$build.Replace("`$managed = Join-Path `$taskRoot 'FanZhiYi_Data\Managed'","`$managed = Join-Path `$gameRoot 'FanZhiYi_Data\Managed'")
$build=$build.Replace("(Join-Path `$source 'Assembly-CSharp.original.dll')",'$originalAssembly')
[IO.File]::WriteAllText($path,$build,$utf8)
Write-Output 'Prepared real contact, distance, balance, cooldown and 64-bit checks.'
