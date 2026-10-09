$taskRoot=(Get-ChildItem -LiteralPath '范志毅VS赵鹏 PC双人版（32位）' -Directory -Force | Where-Object Name -like '鑼*').FullName
foreach($base in @($taskRoot,(Join-Path $taskRoot '实验版（模型75%）'))){
 $p=Join-Path $base 'AI Mod Source\PlayerSkills.cs';$s=[IO.File]::ReadAllText($p)
 $s=$s.Replace('    private bool retreatHeld, retreatJumped, retreatHeaded;', '    private bool retreatHeld, retreatJumped, retreatHeaded, rescueWaiting;'+[Environment]::NewLine+'    private float rescueRecoveryUntil = -1f, rescueInput;')
 $s=$s.Replace('        bool pressed = input > .1f;', '        s.rescueInput = input; s.rescueWaiting = false;'+[Environment]::NewLine+'        bool pressed = input > .1f;')
 $s=$s.Replace('        if (input < -.1f) s.retreatUntil = -1f;', '        if (input < -.1f) s.retreatUntil = s.rescueRecoveryUntil = -1f;')
 $s=$s.Replace('        float margin = s.standingBodyWidth', '        s.rescueRecoveryUntil = Time.time + 1f;'+[Environment]::NewLine+'        float margin = s.standingBodyWidth')
 $s=$s.Replace('bottom < s.ballCollider.bounds.max.y + .12f) return 0f;', 'bottom < s.ballCollider.bounds.max.y + .12f) { s.rescueWaiting = true; return 0f; }')
 $s=$s.Replace('gap < margin + .25f) return 0f;', 'gap < margin + .25f) { s.rescueWaiting = true; return 0f; }')
 $s=$s.Replace('retreatUntil = -1f;'+[Environment]::NewLine+'        // Aerial Fortress','retreatUntil = rescueRecoveryUntil = -1f;'+[Environment]::NewLine+'        // Aerial Fortress')
 $s=$s.Replace('retreatUntil = -1f; retreatHeld = retreatJumped = retreatHeaded = false;', 'retreatUntil = rescueRecoveryUntil = -1f; retreatHeld = retreatJumped = retreatHeaded = rescueWaiting = false;')
 $s=$s.Replace('        UpdateQuickDrop();','        UpdateQuickDrop();'+[Environment]::NewLine+'        UpdateRescueBalance();')
 $s=$s.Replace('    private float RescueFloor()',@'
    private void UpdateRescueBalance()
    {
        if (fan || Stopped() || GameAIMod.BallOutThisRally || Time.time > rescueRecoveryUntil || physicalLimbs == null) return;
        float angle = (float)muscles.GetType().GetField("body").GetValue(muscles);
        float desiredSpin = Mathf.Clamp(Mathf.DeltaAngle(body.rotation, angle) * 7f, -120f, 120f);
        float spinChange = Mathf.Clamp(desiredSpin - body.angularVelocity, -900f * Time.fixedDeltaTime, 900f * Time.fixedDeltaTime);
        ApplyAngularImpulse(spinChange * Mathf.Deg2Rad * RigInertia());
        if (rescueWaiting || (Time.time > retreatUntil && Mathf.Abs(rescueInput) < .1f))
        {
            float velocity = 0f;
            foreach (Rigidbody2D limb in physicalLimbs) if (limb != null) velocity += limb.velocity.x * limb.mass;
            velocity /= TotalMass();
            ApplyLinearImpulse(Vector2.right * Mathf.Clamp(-velocity, -35f * Time.fixedDeltaTime, 35f * Time.fixedDeltaTime) * TotalMass());
        }
    }
    private float RescueFloor()
'@)
 [IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
}
