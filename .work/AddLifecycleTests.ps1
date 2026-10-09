$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$taskRoot=(Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory -Force | Where-Object Name -like '鑼*').FullName
$p=Join-Path $taskRoot 'AI Mod Source\build\KnockbackTests.cs'
$s=[IO.File]::ReadAllText($p)
$s=$s.Replace('    IEnumerator Guards()', @'
    IEnumerator Lifecycle(bool pause, bool blocked)
    {
        yield return Scene(true);SetupGap();Held.Add(KeyCode.D);Held.Add(KeyCode.LeftArrow);
        for(int i=0;i<80&&firstHit<0;i++)yield return new WaitForFixedUpdate();
        Check(firstHit>=0,"lifecycle starts from real tackle pause="+pause+" blocked="+blocked);
        PlayerSkills skill=fan.GetComponent<PlayerSkills>();
        Check((float)Field(skill,"knockbackUntil")>Time.time,"shove is active before cancellation or obstruction");
        Held.Clear();Move(zhao,new Vector2(Body(zhao).position.x+3,Body(zhao).position.y));
        if(blocked)
        {
            GameObject wall=new GameObject("ShoveTestWall");BoxCollider2D shape=wall.AddComponent<BoxCollider2D>();
            wall.transform.position=new Vector3(hitOrigin-hitWidth*1.25f,Body(fan).position.y,0);shape.size=new Vector2(.15f,8f);Physics2D.SyncTransforms();
            float travel=0,error=0;for(int i=0;i<60;i++){yield return new WaitForFixedUpdate();travel=Mathf.Max(travel,hitOrigin-Center(fan));error=Mathf.Max(error,Error(fan));}
            Check(travel<hitWidth*1.75f,"real wall blocks full shove distance travel="+travel/hitWidth);
            Check(Body(fan).position.x>shape.bounds.max.x&&error<.35f,"shove cannot pass through obstruction or disconnect rig");
            Check((float)Field(skill,"knockbackUntil")<0,"blocked shove times out");Destroy(wall);
        }
        else
        {
            if(pause){Time.timeScale=0;yield return new WaitForSecondsRealtime(.15f);}
            else PlayerSkills.ResetAll();
            Check((float)Field(skill,"knockbackUntil")<0,"pause/reset clears active shove pause="+pause);
            Check(!(bool)Field(skill,"weakenedBalance")&&!(bool)Field(zhao.GetComponent<PlayerSkills>(),"bracedMasses"),"pause/reset restores muscle and mass parameters");
            Time.timeScale=1;yield return new WaitForFixedUpdate();Check((float)Field(skill,"knockbackUntil")<0,"shove does not restart after resume/reset");
        }
    }

    IEnumerator Guards()
'@)
$s=$s.Replace('yield return Direct(true);','yield return Direct(true);yield return Lifecycle(true,false);yield return Lifecycle(false,false);yield return Lifecycle(false,true);')
[IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
