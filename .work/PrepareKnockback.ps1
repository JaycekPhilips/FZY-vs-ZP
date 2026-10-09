$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$taskRoot = (Get-ChildItem -LiteralPath (Join-Path $workspace '范志毅VS赵鹏 PC双人版（32位）') -Directory -Force | Where-Object Name -like '鑼*').FullName
$source = Join-Path $taskRoot 'AI Mod Source'
$utf8 = [Text.UTF8Encoding]::new($false)
foreach ($src in @($source,(Join-Path $taskRoot '实验版（模型75%）\AI Mod Source'))) {
    $path = Join-Path $src 'PlayerSkills.cs'
    $code = [IO.File]::ReadAllText($path)
    $code = $code.Replace('    private float staggerUntil = -1f;', @'
    private float staggerUntil = -1f;
    // Two standing body widths, measured before the player tips or jumps.
    public const float TackleKnockbackBodyWidths = 2f;
    private float standingBodyWidth;
    private float knockbackUntil = -1f, knockbackOrigin, knockbackDistance, knockbackDirection;
'@)
    $code = $code.Replace('        normalAnimatorSpeed = animator != null ? animator.speed : 1f;', @'
        float standingLeft = body.position.x, standingRight = body.position.x;
        foreach (Collider2D limb in playerColliders)
            if (limb != null && !limb.isTrigger)
            {
                standingLeft = Mathf.Min(standingLeft, limb.bounds.min.x);
                standingRight = Mathf.Max(standingRight, limb.bounds.max.x);
            }
        standingBodyWidth = Mathf.Max(.1f, standingRight - standingLeft);
        normalAnimatorSpeed = animator != null ? animator.speed : 1f;
'@)
    $code = $code.Replace('        UpdateQuickDrop();', '        UpdateQuickDrop();' + "`r`n        UpdateTackleKnockback();")
    $code = $code.Replace('        opponent.ApplyBalanceGain();', @'
        opponent.ApplyBalanceGain();
        opponent.knockbackOrigin = opponent.RigCenter().x;
        opponent.knockbackDistance = opponent.standingBodyWidth * TackleKnockbackBodyWidths;
        opponent.knockbackDirection = direction;
        opponent.knockbackUntil = Time.time + .60f;
'@)
    $code = $code.Replace('    private float TotalMass()', @'
    private void UpdateTackleKnockback()
    {
        if (!fan || knockbackUntil < 0f) return;
        if (Stopped()) { knockbackUntil = -1f; return; }
        float travelled = knockbackDirection * (RigCenter().x - knockbackOrigin);
        float remaining = knockbackDistance - travelled;
        float velocity = 0f;
        foreach (Rigidbody2D limb in physicalLimbs)
            if (limb != null) velocity += limb.velocity.x * limb.mass;
        velocity = knockbackDirection * velocity / TotalMass();
        // Accelerate the entire connected skeleton horizontally, then brake at
        // the end of the shove. Real contacts still limit travel near the goal.
        bool complete = remaining <= .025f * standingBodyWidth || Time.time >= knockbackUntil;
        float target = complete ? Mathf.Min(velocity, 0f) :
            Mathf.Min(knockbackDistance / .30f, Mathf.Sqrt(2f * 28f * remaining));
        float change = Mathf.Clamp(target - velocity, -80f * Time.fixedDeltaTime, 80f * Time.fixedDeltaTime);
        ApplyLinearImpulse(Vector2.right * knockbackDirection * change * TotalMass());
        if (complete && velocity <= 80f * Time.fixedDeltaTime + .05f) knockbackUntil = -1f;
    }

    private float TotalMass()
'@)
    $code = $code.Replace('        braceUntil = staggerUntil = -1f;', '        braceUntil = staggerUntil = knockbackUntil = -1f;')
    [IO.File]::WriteAllText($path,$code,$utf8)
}
$buildPath = Join-Path $source 'build\BuildBurstRange.ps1'
$build = [IO.File]::ReadAllText($buildPath).Replace('burst-range','knockback').Replace('GroundContestTests','KnockbackTests')
[IO.File]::WriteAllText((Join-Path $source 'build\BuildKnockback.ps1'),$build,$utf8)
Write-Output ('Updated sources and build script: ' + $taskRoot)
