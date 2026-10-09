$taskRoot=(Get-ChildItem -LiteralPath '范志毅VS赵鹏 PC双人版（32位）' -Directory -Force | Where-Object Name -like '鑼*').FullName
foreach($base in @($taskRoot,(Join-Path $taskRoot '实验版（模型75%）'))){
 $p=Join-Path $base 'AI Mod Source\PlayerSkills.cs';$s=[IO.File]::ReadAllText($p)
 $s=$s.Replace('remaining / .8f','remaining / (s.retreatJumped ? .5f : .8f)')
 $s=$s.Replace('        if (s.retreatHeaded && gap < margin + .25f)', '        float encounter = Mathf.Clamp(gap / Mathf.Max(1f, s.rescueSpeed), .05f, .5f);'+[Environment]::NewLine+'        float forecast = height + s.ball.velocity.y * encounter + .5f * Physics2D.gravity.y * s.ball.gravityScale * encounter * encounter;'+[Environment]::NewLine+'        if (!s.retreatJumped && height > s.standingHeight && forecast < s.standingHeight && gap < margin + .6f) { s.rescueWaiting = true; return 0f; }'+[Environment]::NewLine+'        if (s.retreatHeaded && gap < margin + .05f)')
 $s=$s.Replace('        if (rescueWaiting || (Time.time > retreatUntil && Mathf.Abs(rescueInput) < .1f))', '        if (rescueWaiting || Time.time <= retreatUntil || (Time.time > retreatUntil && Mathf.Abs(rescueInput) < .1f))')
 $s=$s.Replace('ApplyLinearImpulse(Vector2.right * Mathf.Clamp(-velocity, -35f * Time.fixedDeltaTime, 35f * Time.fixedDeltaTime) * TotalMass());','float target = !rescueWaiting && Time.time <= retreatUntil ? rescueSpeed : 0f;'+[Environment]::NewLine+'            ApplyLinearImpulse(Vector2.right * Mathf.Clamp(target - velocity, -35f * Time.fixedDeltaTime, 18f * Time.fixedDeltaTime) * TotalMass());')
 $s=$s.Replace('    public bool AwaitingFanHeader', '    public static bool BackwardHeaderPose(Component player) { PlayerSkills s = player != null ? player.GetComponent<PlayerSkills>() : null; return s != null && !s.fan && !s.Stopped() && !GameAIMod.BallOutThisRally && s.retreatHeaded && Time.time <= s.retreatUntil; }'+[Environment]::NewLine+'    public bool AwaitingFanHeader')
 [IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
 $p=Join-Path $base 'AI Mod Source\ZhaoHeader.cs';$s=[IO.File]::ReadAllText($p)
 $s=$s.Replace('    private bool heading;', '    private bool heading, backwardHeading;')
 $s=$s.Replace('        if (!header.PrepareSampler()) return;', '        header.backwardHeading = PlayerSkills.BackwardHeaderPose(player);'+[Environment]::NewLine+'        if (!header.PrepareSampler()) return;')
 $s=$s.Replace('        header.headAngle.SetValue(header.stick, -(float)header.headAngle.GetValue(header.sampleStick));',@'
        if (header.backwardHeading)
        {
            // Clockwise torso/neck targets make Zhao arch toward the rear
            // ball. The intact joints and muscles supply the actual motion.
            float wave = Mathf.Sin(Mathf.Clamp01(age / header.duration) * Mathf.PI);
            header.bodyAngle.SetValue(header.stick, -48f * wave);
            header.headAngle.SetValue(header.stick, -65f * wave);
        }
        else header.headAngle.SetValue(header.stick, -(float)header.headAngle.GetValue(header.sampleStick));
'@)
 $s=$s.Replace('        header.bodyAngle.SetValue(header.stick, -(float)header.bodyAngle.GetValue(header.sampleStick));', '        if (!header.backwardHeading) header.bodyAngle.SetValue(header.stick, -(float)header.bodyAngle.GetValue(header.sampleStick));')
 [IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
}
