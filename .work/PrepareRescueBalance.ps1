$ErrorActionPreference='Stop'
$taskRoot=(Get-ChildItem -LiteralPath '范志毅VS赵鹏 PC双人版（32位）' -Directory -Force | Where-Object Name -like '鑼*').FullName
$enc=[Text.UTF8Encoding]::new($false)
$retreat=@'
    // A rearward movement command starts one bounded rescue attempt. Steering
    // opposite to it or shooting immediately returns control to the player.
    private float retreatUntil = -1f, standingHeight, standingHeadHeight;
    private bool retreatHeld, retreatJumped, retreatHeaded;
    private Collider2D ownGoal;
    public static float RescueAxis(Component player, float input)
    {
        if (!Enabled || player == null || player.name != "Zhao") return input;
        Attach(player);
        PlayerSkills s = player.GetComponent<PlayerSkills>();
        if (s == null) return input;
        bool pressed = input > .1f;
        if (s.Stopped() || GameAIMod.BallOutThisRally || s.ball == null)
        { s.retreatUntil = -1f; s.retreatHeld = pressed; return input; }
        if (pressed && !s.retreatHeld && s.ball.position.x > s.body.position.x + .05f)
        { s.retreatUntil = Time.time + 2f; s.retreatJumped = s.retreatHeaded = false; s.Show(6); }
        s.retreatHeld = pressed;
        if (input < -.1f) s.retreatUntil = -1f;
        if (Time.time > s.retreatUntil) return input;
        float margin = s.standingBodyWidth * .6f + s.ballCollider.bounds.extents.x;
        float goalLine = s.ownGoal != null ? s.ownGoal.bounds.min.x : 100f;
        float target = Mathf.Min(s.ball.position.x + margin, goalLine - s.standingBodyWidth * .5f);
        if (s.body.position.x >= target)
        { s.retreatUntil = -1f; return input; }
        // Low balls are crossed only after the native jump has lifted every
        // limb above them. This avoids running into a ball toward our own goal.
        float bottom = s.body.position.y;
        foreach (Collider2D limb in s.playerColliders)
            if (limb != null && !limb.isTrigger) bottom = Mathf.Min(bottom, limb.bounds.min.y);
        float gap = s.ball.position.x - s.body.position.x;
        float height = s.ball.position.y - s.RescueFloor();
        if (height < s.standingHeight * .6f && gap < 2.3f && bottom < s.ballCollider.bounds.max.y + .12f) return 0f;
        // A falling high ball may enter the torso band before we can pass it.
        // Hold position rather than add a goalward body collision.
        if (height >= s.standingHeight * .6f && height < s.standingHeight * .8f && gap < margin + .25f) return 0f;
        if (s.retreatHeaded && gap < margin + .25f) return 0f;
        return 1f;
    }
    private float RescueFloor()
    {
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(body.position, Vector2.down, 30f, groundMask))
            if (hit.collider != null && !hit.collider.isTrigger && hit.normal.y > .5f &&
                hit.collider != ballCollider && hit.collider.transform.root.name != "Fan" && hit.collider.transform.root.name != "Zhao") return hit.point.y;
        return body.position.y - standingHeight * .5f;
    }
    public static bool RescueButton(Component player, bool jump)
    {
        PlayerSkills s = player != null ? player.GetComponent<PlayerSkills>() : null;
        if (s == null || s.fan || s.Stopped() || GameAIMod.BallOutThisRally || Time.time > s.retreatUntil || s.ball == null) return false;
        float gap = s.ball.position.x - s.body.position.x, height = s.ball.position.y - s.RescueFloor();
        if (jump)
        {
            if (s.retreatJumped || height >= s.standingHeight * .6f || gap < -.1f || gap > 2.3f || !s.TouchesGround()) return false;
            s.retreatJumped = true; return true;
        }
        if (s.retreatHeaded || height < s.standingHeight * .8f || height > s.standingHeight * 1.12f || gap < -.1f ||
            Vector2.Distance(s.ball.position, s.head.position) > s.standingBodyWidth * .6f + s.ballCollider.bounds.extents.x + .45f) return false;
        s.retreatHeaded = true; return true;
    }
    public static bool RescueHeader(Component player)
    {
        PlayerSkills s = player != null ? player.GetComponent<PlayerSkills>() : null;
        if (s == null || s.fan || s.Stopped() || !s.retreatHeaded || Time.time > s.retreatUntil || GameAIMod.BallOutThisRally) return false;
        // Strike only on a real head contact. Clear above the own crossbar;
        // near the line, turn the ball back onto the pitch instead of risking
        // a goalward impulse with too little vertical clearance.
        float vx = 6f, vy = 14f;
        if (s.ownGoal != null)
        {
            float t = (s.ownGoal.bounds.min.x - s.ballCollider.bounds.max.x) / vx;
            float bar = BallBoundaryGuard.FindGoalCeiling(s.ownGoal) + s.ballCollider.bounds.extents.y + .3f;
            if (t < .18f) vx = -8f;
            else vy = Mathf.Max(vy, (bar - s.ball.position.y) / t - .5f * Physics2D.gravity.y * s.ball.gravityScale * t + 2f);
        }
        Vector2 target = new Vector2(vx, vy) * PowerShot.HeaderSpeedMultiplier;
        s.ball.AddForce((target - s.ball.velocity) * s.ball.mass, ForceMode2D.Impulse);
        s.retreatUntil = -1f; PowerShot.CompleteHeader(player); return true;
    }

'@
foreach($base in @($taskRoot,(Join-Path $taskRoot '实验版（模型75%）'))){
 $src=Join-Path $base 'AI Mod Source'
 $p=Join-Path $src PlayerSkills.cs; $s=[IO.File]::ReadAllText($p)
 $s=$s.Replace('public const float FanHeaderSpeedMultiplier = .95f;','public const float FanHeaderSpeedMultiplier = .95f * PowerShot.HeaderSpeedMultiplier;')
 $s=$s.Replace('standingShinHeight','standingInterceptHeight').Replace('shinTop','thighTop').Replace('limb.name == "L_LowLeg" || limb.name == "R_LowLeg"','limb.name == "L_Up_Leg" || limb.name == "R_Up_Leg"').Replace('? .75f * Mathf.Abs(transform.lossyScale.y)', '? 1.35f * Mathf.Abs(transform.lossyScale.y)')
 $s=$s.Replace('    public static void Attach(Component player)',($retreat+[Environment]::NewLine+'    public static void Attach(Component player)'))
 $s=$s.Replace('        normalAnimatorSpeed = animator != null ? animator.speed : 1f;',@'
        float standingTop = body.position.y;
        foreach (Collider2D limb in playerColliders) if (limb != null && !limb.isTrigger) standingTop = Mathf.Max(standingTop, limb.bounds.max.y);
        standingHeight = standingTop - standingBottom;
        Transform standingHead = player.transform.Find("Head");
        standingHeadHeight = standingHead != null ? standingHead.position.y - standingBottom : standingHeight * .9f;
        foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(type.Assembly.GetType("GoalTrigger")))
        { Component goal = item as Component; if (goal != null && goal.transform.position.x > 0f) ownGoal = goal.GetComponent<Collider2D>(); }
        normalAnimatorSpeed = animator != null ? animator.speed : 1f;
'@)
 $s=$s.Replace('        if (Stopped()) return;'+[Environment]::NewLine+'        // Aerial Fortress', '        if (Stopped()) return;'+[Environment]::NewLine+'        if (action == "Kick" || (action == "Head" && !retreatHeaded)) retreatUntil = -1f;'+[Environment]::NewLine+'        // Aerial Fortress')
 $s=$s.Replace('                headUntil = -1f; // One successful strike per heading command.','                headUntil = -1f; // One successful strike per heading command.'+[Environment]::NewLine+'                PowerShot.CompleteHeader(controller);')
 $s=$s.Replace('        quickDrop = false;'+[Environment]::NewLine+'        dropStarted = -10f;', '        quickDrop = false;'+[Environment]::NewLine+'        retreatUntil = -1f; retreatHeld = retreatJumped = retreatHeaded = false;'+[Environment]::NewLine+'        dropStarted = -10f;')
 # The source may use LF, so handle the two line-sensitive insertions too.
 $s=$s.Replace("        if (Stopped()) return;`n        // Aerial Fortress", "        if (Stopped()) return;`n        if (action == `"Kick`" || (action == `"Head`" && !retreatHeaded)) retreatUntil = -1f;`n        // Aerial Fortress")
 $s=$s.Replace("        quickDrop = false;`n        dropStarted = -10f;", "        quickDrop = false;`n        retreatUntil = -1f; retreatHeld = retreatJumped = retreatHeaded = false;`n        dropStarted = -10f;")
 [IO.File]::WriteAllText($p,$s,$enc)
 $p=Join-Path $src PowerShot.cs; $s=[IO.File]::ReadAllText($p)
 $s=$s.Replace('    public const float SpeedMultiplier = 1.2f;', '    public const float HeaderSpeedMultiplier = .9f;'+[Environment]::NewLine+'    private float headerUntil = -1f;'+[Environment]::NewLine+'    public const float SpeedMultiplier = 1.2f;')
 $s=$s.Replace('        shot.requestedFrame = -1;', '        shot.headerUntil = action == "Head" && !shot.Stopped() ? Time.time + .55f : -1f;'+[Environment]::NewLine+'        shot.requestedFrame = -1;')
 $s=$s.Replace('    private bool Stopped()',@'
    public static void CompleteHeader(Component player) { PowerShot s = player != null ? player.GetComponent<PowerShot>() : null; if (s != null) s.headerUntil = -1f; }
    private bool SlowHeader(Collider2D limb)
    {
        if (Stopped() || Time.time > headerUntil || limb.name != "Head") return false;
        if (PlayerSkills.RescueHeader(controller)) return true;
        // Fan's assisted cannon header supplies its own reduced target once.
        PlayerSkills skills = controller.GetComponent<PlayerSkills>();
        if (fan && PlayerSkills.Enabled && InFront() && skills != null &&
            (float)typeof(PlayerSkills).GetField("headUntil", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(skills) >= Time.time) return false;
        headerUntil = -1f;
        ball.AddForce(ball.velocity * ((HeaderSpeedMultiplier - 1f) * ball.mass), ForceMode2D.Impulse);
        return true;
    }
    private bool Stopped()
'@)
 $s=$s.Replace('if (Stopped()) { armedUntil = flatUntil = -1f; return; }','if (Stopped()) { headerUntil = armedUntil = flatUntil = -1f; return; }')
 $s=$s.Replace('        bool launched = false;', '        bool launched = false;'+[Environment]::NewLine+'        if (striker != null && striker.SlowHeader(collision.collider)) return false;')
 $s=$s.Replace('shot.armedUntil = shot.flatUntil = -1f;', 'shot.headerUntil = shot.armedUntil = shot.flatUntil = -1f;')
 [IO.File]::WriteAllText($p,$s,$enc)
 $p=Join-Path $src GameAIMod.cs; $s=[IO.File]::ReadAllText($p)
 $s=$s.Replace('if (!IsAI(controller)) return ControlBindings.GetHorizontal(controller);','if (!IsAI(controller)) return PlayerSkills.RescueAxis(controller, ControlBindings.GetHorizontal(controller));')
 $s=$s.Replace('        return state.axis;', '        return PlayerSkills.RescueAxis(controller, state.axis);')
 $s=$s.Replace('        if (!IsAI(controller)) return ControlBindings.GetNativeButtonDown(key, controller);',@'
        if ((key == KeyCode.W || key == KeyCode.UpArrow) && PlayerSkills.RescueButton(controller, true)) return true;
        if ((key == KeyCode.None || key == KeyCode.Keypad0) && controller != null && controller.name == "Zhao" && PlayerSkills.RescueButton(controller, false)) return true;
        if (!IsAI(controller)) return ControlBindings.GetNativeButtonDown(key, controller);
'@)
 [IO.File]::WriteAllText($p,$s,$enc)
}
