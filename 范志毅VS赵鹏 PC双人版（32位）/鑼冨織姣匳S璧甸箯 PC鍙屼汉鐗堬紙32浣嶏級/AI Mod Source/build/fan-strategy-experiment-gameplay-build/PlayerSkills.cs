using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// All effects are overlays. The original meshes, sprites, animation clips and
// collider sizes remain untouched. Physics assistance runs at the physics rate.
public sealed class PlayerSkills : MonoBehaviour
{
    public static bool Enabled;
    // Preserve the earlier 5% height reduction, then reduce current height 20%.
    // Launch impulse scales by sqrt(0.8), not 0.8.
    public const float ZhaoJumpMultiplier = 1.35f * .974679434f * .894427191f;
    public const float FanHeaderSpeedMultiplier = .95f * PowerShot.HeaderSpeedMultiplier;
    public const float FanDropSpeed = 8f;
    public const float FanVolleyCommandWindow = .75f;
    public const float ShotDoubleTapWindow = .35f;
    public const float FanDribbleAnimationMultiplier = 1.5f;
    public const float FanAttackSprintMultiplier = 1.25f;
    public const float FanVolleyLiftSpeed = 15.75f;
    public const float FanVolleyFlightDuration = 3f;
    public const float FanVolleyArcHeight = 4.2f;
    public const float FanVolleySpeedMultiplier = .9f;
    public const float TackleGroundSpeed = 1.125f * 1.3f;
    public const float TackleAirSpeed = 2.0625f * 1.3f;
    public const float TackleGroundDuration = .22f;
    public const float TackleAirDuration = .32f;
    public const float GroundTackleSpeedMultiplier = 1.4f;
    public const float GroundTackleSpinMultiplier = 1.35f;
    public const float GroundTackleDuration = .28f;
    public const float GroundTackleCooldown = 1.05f;
    private static readonly List<PlayerSkills> players = new List<PlayerSkills>();
    private static Font skillFont;
    private static Texture2D haloTexture;
    private static PlayerSkills glowOwner;
    private static float glowUntil = -1f;
    private static Color glowColor;
    private static readonly List<Vector2> ballTrail = new List<Vector2>();
    private static float lastTrail;
    private static Rigidbody2D continuousBall;
    private static CollisionDetectionMode2D originalBallDetection;
    private static bool changedBallDetection;
    private Component controller;
    private Rigidbody2D body;
    private Rigidbody2D opponentBody;
    private Transform head;
    private Transform foot;
    private Collider2D headCollider;
    private FieldInfo groundedField;
    private int groundMask;
    private readonly ContactPoint2D[] groundContacts = new ContactPoint2D[16];
    private Component manager;
    private FieldInfo stoppingField;
    private Rigidbody2D ball;
    private Collider2D ballCollider;
    private Animator animator;
    private string horizontalAxis;
    private bool fasterDribble;
    private float normalAnimatorSpeed;
    private bool quickDrop;
    private float dropStarted = -10f;
    private float standingInterceptHeight;
    private Collider2D[] playerColliders;
    private Component muscles;
    private Rigidbody2D[] physicalLimbs;
    private float[] normalMasses;
    private object[] physicalMuscles;
    private float[] normalMuscleForces;
    private FieldInfo muscleForceField;
    private bool bracedMasses;
    private bool weakenedBalance;
    private float braceUntil = -1f;
    private float staggerUntil = -1f;
    // Two standing body widths, measured before the player tips or jumps.
    public const float TackleKnockbackBodyWidths = 2f;
    private float standingBodyWidth;
    private float knockbackUntil = -1f, knockbackOrigin, knockbackDistance, knockbackDirection;
    private float balanceGain = 18f;
    private float lastTackle = -10f;
    private float lastTackleCooldown = .95f;
    private readonly ContactPoint2D[] contestContacts = new ContactPoint2D[32];
    private float groundContestUntil = -1f, groundPressure;
    private bool fan;
    private float direction;
    private float headUntil = -1f;
    private float kickUntil = -1f;
    private float pendingShotTap = -10f;
    private int lastShotTapFrame = -1;
    private Collider2D curveGoal;
    private Vector2 curveOrigin;
    private Vector2 curveDestination;
    private float curveTravelTime;
    private float curveArcHeight;
    private float curveForwardSpeed;
    private float curveStarted = -10f;
    private float lastKick = -10f;
    private float lastHead = -10f;
    private float lastDribbleFx = -10f;
    private float shotUntil = -1f;
    private float burstUntil = -1f;
    private float burstFloorHeight;
    private float lastMovementFx = -10f;
    private int lastMovementSkill = -1;
    private readonly float[] effects = { -10f, -10f, -10f, -10f, -10f, -10f, -10f, -10f, -10f, -10f };
    private static readonly string[] names = { "旋风盘带", "炮弹式头球", "空中堡垒", "强力抢断", "掠地瞬击", "弧线落叶射门", "紧急回防", "进攻提速", "磁力脚", "伸脚抢截" };
    private static readonly Color[] colors = {
        new Color(1f, .76f, .12f), new Color(1f, .36f, .12f),
        new Color(.25f, .83f, 1f), new Color(.4f, 1f, .3f),
        new Color(1f, .93f, .3f), new Color(.87f, .45f, 1f),
        new Color(.2f, .95f, 1f), new Color(1f, .6f, .15f),
        new Color(1f, .45f, .65f), new Color(.3f, .8f, 1f)
    };

    // A rearward movement command starts one bounded rescue attempt. Steering
    // opposite to it or shooting immediately returns control to the player.
    private float retreatUntil = -1f, standingHeight, standingHeadHeight;
    private bool retreatHeld, retreatJumped, retreatHeaded, rescueWaiting, retreatJumpQueued;
    private float rescueJumpRequestedAt = -10f, rescueJumpStarted = -10f, rescueStarted; private int rescueJumpAttempts;
    private float rescueRecoveryUntil = -1f, rescueInput, rescueSpeed, nativeRunSpeed, rescueFloorTime = -1f, rescueFloorHeight;
    private readonly RaycastHit2D[] rescueGroundHits = new RaycastHit2D[24];
    private Collider2D ownGoal;
    public static float RescueAxis(Component player, float input, bool allowStart = true)
    {
        if (!Enabled || player == null || player.name != "Zhao") return input;
        Attach(player);
        PlayerSkills s = player.GetComponent<PlayerSkills>();
        if (s == null) return input;
        s.rescueInput = input; s.rescueWaiting = false;
        bool pressed = input > .1f;
        if (s.Stopped() || GameAIMod.BallOutThisRally || s.ball == null)
        { s.retreatUntil = -1f; s.retreatHeld = pressed; return input; }
        if (allowStart && pressed && !s.retreatHeld && s.ball.position.x > s.body.position.x + .05f)
        { s.retreatUntil = Time.time + 2.6f; s.retreatJumped = s.retreatHeaded = s.retreatJumpQueued = false; s.rescueJumpAttempts = 0; s.rescueStarted = Time.time; s.Show(6); }
        s.retreatHeld = allowStart && pressed;
        if (input < -.1f) s.retreatUntil = s.rescueRecoveryUntil = -1f;
        if (Time.time > s.retreatUntil) return input;
        s.rescueRecoveryUntil = Time.time + 1f;
        float margin = s.standingBodyWidth * .6f + s.ballCollider.bounds.extents.x;
        float goalLine = s.ownGoal != null ? s.ownGoal.bounds.min.x : 100f;
        float rearExtent = s.standingBodyWidth * .5f;
        foreach (Collider2D limb in s.playerColliders)
            if (limb != null && !limb.isTrigger) rearExtent = Mathf.Max(rearExtent, limb.bounds.max.x - s.body.position.x);
        float target = Mathf.Min(s.ball.position.x + margin, goalLine - rearExtent - .12f);
        if (s.body.position.x >= target)
                {
            if (s.ball.position.x > s.body.position.x && target < s.ball.position.x + margin - .05f)
            { s.rescueWaiting = true; s.rescueSpeed = 0f; return 0f; }
            s.retreatUntil = -1f; return input;
        }
        float bottom = s.body.position.y;
        foreach (Collider2D limb in s.playerColliders)
            if (limb != null && !limb.isTrigger) bottom = Mathf.Min(bottom, limb.bounds.min.y);
        float normalSpeed = s.nativeRunSpeed;
        float remaining = Mathf.Max(0f, target - s.body.position.x);
        float crossingTime = s.retreatJumped ? .5f : .8f;
        if (s.retreatJumped && bottom > s.ballCollider.bounds.max.y + .12f)
        {
            float gravity = Mathf.Max(.1f, -Physics2D.gravity.y * s.body.gravityScale);
            float clearance = bottom - s.ballCollider.bounds.max.y - .12f;
            float airTime = (s.body.velocity.y + Mathf.Sqrt(s.body.velocity.y * s.body.velocity.y + 2f * gravity * clearance)) / gravity;
            crossingTime = Mathf.Clamp(airTime - .06f, .12f, .5f);
        }
        float needed = Mathf.Max(0f, s.ball.velocity.x) + remaining / crossingTime;
        s.rescueSpeed = Mathf.Min(Mathf.Clamp(needed, normalSpeed * .75f, normalSpeed * 2f), Mathf.Max(0f, s.ball.velocity.x) + Mathf.Sqrt(24f * remaining));
        // Low balls are crossed only after the native jump has lifted every
        // limb above them. This avoids running into a ball toward our own goal.

        float gap = s.ball.position.x - s.body.position.x;
        float height = s.ball.position.y - s.RescueFloor();
        if (s.ball.velocity.x < -.5f && gap < margin + Mathf.Abs(s.ball.velocity.x) * .25f && height < s.standingHeight * .8f)
        { s.retreatUntil = -1f; s.rescueWaiting = true; return 0f; }
        float closing = Mathf.Max(0f, s.body.velocity.x - s.ball.velocity.x);
        float brakeDistance = closing * .16f + closing * closing / 70f;
        float landingBottom = bottom + s.body.velocity.y * .16f + .5f * Physics2D.gravity.y * s.body.gravityScale * .16f * .16f;
        if (height < s.standingHeight * .6f && gap < 2.3f &&
            (bottom < s.ballCollider.bounds.max.y + .12f ||
            (landingBottom < s.ballCollider.bounds.max.y + .16f && gap < margin + .15f + brakeDistance)))
        { s.rescueWaiting = true; return 0f; }
        // A falling high ball may enter the torso band before we can pass it.
        // Hold position rather than add a goalward body collision.
        if (height >= s.standingHeight * .6f && height < s.standingHeight * .8f && gap < margin + .25f) { s.rescueWaiting = true; return 0f; }
        float encounter = Mathf.Clamp(gap / Mathf.Max(1f, s.rescueSpeed), .05f, .5f);
        float forecast = height + s.ball.velocity.y * encounter + .5f * Physics2D.gravity.y * s.ball.gravityScale * encounter * encounter;
        if (!s.retreatJumped && height > s.standingHeight && forecast < s.standingHeight && gap < margin + .6f) { s.rescueWaiting = true; return 0f; }
        if (s.retreatHeaded && gap < margin + .05f) { s.rescueWaiting = true; return 0f; }
        return 1f;
    }
    private void UpdateRescueBalance()
    {
        if (fan || Stopped() || GameAIMod.BallOutThisRally || Time.time > rescueRecoveryUntil || physicalLimbs == null) return;
        float angle = (float)muscles.GetType().GetField("body").GetValue(muscles);
        float desiredSpin = Mathf.Clamp(Mathf.DeltaAngle(body.rotation, angle) * 7f, -120f, 120f);
        float spinChange = Mathf.Clamp(desiredSpin - body.angularVelocity, -900f * Time.fixedDeltaTime, 900f * Time.fixedDeltaTime);
        ApplyAngularImpulse(spinChange * Mathf.Deg2Rad * RigInertia());
        if (rescueWaiting || Time.time <= retreatUntil || (Time.time > retreatUntil && Mathf.Abs(rescueInput) < .1f))
        {
            float velocity = 0f;
            foreach (Rigidbody2D limb in physicalLimbs) if (limb != null) velocity += limb.velocity.x * limb.mass;
            velocity /= TotalMass();
            float target = !rescueWaiting && Time.time <= retreatUntil ? rescueSpeed : 0f;
            ApplyLinearImpulse(Vector2.right * Mathf.Clamp(target - velocity, -35f * Time.fixedDeltaTime, 18f * Time.fixedDeltaTime) * TotalMass());
        }
    }
    private float RescueFloor()
    {
        if (rescueFloorTime == Time.fixedTime) return rescueFloorHeight;
        rescueFloorTime = Time.fixedTime; rescueFloorHeight = body.position.y - standingHeight * .5f;
        int count = Physics2D.RaycastNonAlloc(body.position, Vector2.down, rescueGroundHits, 30f, groundMask);
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = rescueGroundHits[i];
            if (hit.collider != null && !hit.collider.isTrigger && hit.normal.y > .5f && hit.collider != ballCollider &&
                hit.collider.transform.root.name != "Fan" && hit.collider.transform.root.name != "Zhao")
            { rescueFloorHeight = hit.point.y; break; }
        }
        return rescueFloorHeight;
    }
    public static bool RescueButton(Component player, bool jump)
    {
        PlayerSkills s = player != null ? player.GetComponent<PlayerSkills>() : null;
        if (s == null || s.fan || s.Stopped() || GameAIMod.BallOutThisRally || Time.time > s.retreatUntil || s.ball == null) return false;
        float gap = s.ball.position.x - s.body.position.x, height = s.ball.position.y - s.RescueFloor();
        if (jump)
        {
            if (s.retreatJumpQueued && Time.time - s.rescueJumpRequestedAt > .25f) s.retreatJumpQueued = false;
            if (s.retreatJumpQueued || s.rescueJumpAttempts >= 2 || Mathf.Abs(s.ball.velocity.y) > 1.5f ||
                height >= s.standingHeight * .6f || gap < -.1f || gap > 2.3f || !s.RescueHasSupport()) return false;
            if (s.retreatJumped && Time.time - s.rescueJumpStarted < .75f) return false;
            s.retreatJumpQueued = true; s.rescueJumpRequestedAt = Time.time; return true;
        }
        if (s.retreatHeaded || s.ball.velocity.y > 1.5f || height < s.standingHeadHeight - s.standingHeight * .15f || height > s.standingHeight || gap < -.1f ||
            Vector2.Distance(s.ball.position, s.head.position) > s.standingBodyWidth * .6f + s.ballCollider.bounds.extents.x + .45f) return false;
        s.retreatHeaded = true; s.retreatUntil = Mathf.Max(s.retreatUntil, Time.time + .65f); return true;
    }
    // The native foot-circle can be above the pitch while a real grounded
    // football supports the feet. Permit the same native jump from that
    // contact chain only during a low-ball rescue, never from free air.
    private bool RescueHasSupport()
    {
        if (TouchesGround()) return true;
        if (ballCollider == null || Mathf.Abs(body.velocity.y) > 1.2f) return false;
        bool groundedBall = false;
        int count = ballCollider.GetContacts(groundContacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D point = groundContacts[i];
            Collider2D other = point.collider == ballCollider ? point.otherCollider : point.collider;
            if (other == null || other.isTrigger || other.transform.root.name == "Fan" || other.transform.root.name == "Zhao") continue;
            Vector2 normal = point.normal; if (Vector2.Dot(normal, ball.position - point.point) < 0f) normal = -normal;
            if (normal.y > .5f) { groundedBall = true; break; }
        }
        if (!groundedBall) return false;
        foreach (Collider2D limb in playerColliders)
            if (limb != null && !limb.isTrigger && (limb.name.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0 || limb.name.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0) && limb.IsTouching(ballCollider)) return true;
        return false;
    }
    public static bool GetGrounded(bool native, Component player)
    {
        if (native) return true;
        PlayerSkills s = player != null ? player.GetComponent<PlayerSkills>() : null;
        return s != null && !s.fan && !s.Stopped() && !GameAIMod.BallOutThisRally && Time.time <= s.retreatUntil &&
            !s.retreatJumpQueued && s.rescueJumpAttempts < 2 &&
            s.ball.position.y - s.RescueFloor() < s.standingHeight * .6f && s.RescueHasSupport();
    }
    public static bool RescueActive(Component player)
    {
        PlayerSkills s = player != null ? player.GetComponent<PlayerSkills>() : null;
        return s != null && !s.fan && !s.Stopped() && !GameAIMod.BallOutThisRally && Time.time <= s.retreatUntil;
    }
    public static bool RescueRecovering(Component player)
    {
        PlayerSkills s = player != null ? player.GetComponent<PlayerSkills>() : null;
        return s != null && !s.fan && !s.Stopped() && !GameAIMod.BallOutThisRally && Time.time <= s.rescueRecoveryUntil;
    }
    public static bool BackwardHeaderPose(Component player) { PlayerSkills s = player != null ? player.GetComponent<PlayerSkills>() : null; return s != null && !s.fan && !s.Stopped() && !GameAIMod.BallOutThisRally && s.retreatHeaded && Time.time <= s.retreatUntil; }
    public bool AwaitingFanHeader { get { return fan && Enabled && BallInFront() && headUntil >= Time.time; } }
    public static bool RescueHeader(Component player)
    {
        PlayerSkills s = player != null ? player.GetComponent<PlayerSkills>() : null;
        if (s == null || s.fan || s.Stopped() || !s.retreatHeaded || Time.time > s.retreatUntil || GameAIMod.BallOutThisRally) return false;
        // Strike only on a real head contact. Clear above the own crossbar;
        // near the line, turn the ball back onto the pitch instead of risking
        // a goalward impulse with too little vertical clearance.
        float vx = 10f, vy = 5f;
        if (s.ownGoal != null)
        {
            float t = (s.ownGoal.bounds.min.x - s.ballCollider.bounds.max.x) / (vx * PowerShot.HeaderSpeedMultiplier);
            float bar = BallBoundaryGuard.FindGoalCeiling(s.ownGoal) + s.ballCollider.bounds.extents.y + .3f;
            if (t < .18f) { vx = -8f; vy = 8f; }
            else vy = Mathf.Max(3f, (bar - s.ball.position.y) / t - .5f * Physics2D.gravity.y * s.ball.gravityScale * t + 1f) / PowerShot.HeaderSpeedMultiplier;
        }
        Vector2 target = new Vector2(vx, vy) * PowerShot.HeaderSpeedMultiplier;
        s.EnableContinuousBall();
        s.ball.AddForce((target - s.ball.velocity) * s.ball.mass, ForceMode2D.Impulse);
        s.retreatUntil = -1f; PowerShot.CompleteHeader(player); return true;
    }

    public static void RescaleStanding(Component player, float scale)
    {
        PlayerSkills s = player != null ? player.GetComponent<PlayerSkills>() : null;
        if (s == null) return;
        s.standingHeight *= scale; s.standingHeadHeight *= scale;
        s.standingBodyWidth *= scale; s.standingInterceptHeight *= scale;
        s.rescueFloorTime = -1f;
    }
    public static void Attach(Component player)
    {
        if (player == null || (player.name != "Fan" && player.name != "Zhao")) return;
        PlayerMovement.Attach(player);
        PowerShot.Attach(player);
        ZhaoHeader.Attach(player);
        if (player.name == "Fan" && player.GetComponent<GameStyleIndicator>() == null)
            player.gameObject.AddComponent<GameStyleIndicator>();
        if (!Enabled) return;
        MagneticFoot.Attach(player);
        PlayerSkills skills = player.GetComponent<PlayerSkills>();
        if (skills != null) return;
        skills = player.gameObject.AddComponent<PlayerSkills>();
        skills.Initialize(player);
    }

    // Hook the actual animation command, after both human and AI input paths.
    public static void SetAction(Animator animator, string action, Component player)
    {
        MagneticFoot.OnAction(player, action);
        PlayerMovement.NotifyAction(player);
        animator.SetTrigger(action);
        Attach(player);
        PowerShot.OnAction(player, action);
        PlayerSkills skills = player.GetComponent<PlayerSkills>();
        if (skills != null) skills.BeginAction(action);
        ZhaoHeader.OnAction(player, action);
        ExperimentScale.OnAction(player, action);
    }

    public static float GetJumpForce(float original, Component player)
    {
        return Enabled && player != null && player.name == "Zhao" ? original * ZhaoJumpMultiplier : original;
    }

    public static void ResetAll()
    {
        PowerShot.ResetAll();
        MagneticFoot.ResetAll();
        foreach (PlayerSkills skills in players)
            if (skills != null) skills.ClearRound();
    }

    private void Initialize(Component player)
    {
        controller = player;
        fan = player.name == "Fan";
        direction = fan ? 1f : -1f;
        Type type = player.GetType();
        body = type.GetField("rb").GetValue(player) as Rigidbody2D;
        foot = type.GetField("footTrans").GetValue(player) as Transform;
        groundedField = type.GetField("isOnGround");
        groundMask = (int)(LayerMask)type.GetField("ground").GetValue(player);
        animator = type.GetField("anim").GetValue(player) as Animator;
        playerColliders = player.GetComponentsInChildren<Collider2D>();
        float standingBottom = body.position.y, thighTop = float.NegativeInfinity;
        foreach (Collider2D limb in playerColliders)
        {
            if (limb == null || limb.isTrigger) continue;
            standingBottom = Mathf.Min(standingBottom, limb.bounds.min.y);
            if (limb.name == "L_Up_Leg" || limb.name == "R_Up_Leg") thighTop = Mathf.Max(thighTop, limb.bounds.max.y);
        }
        standingInterceptHeight = float.IsInfinity(thighTop) ? 1.35f * Mathf.Abs(transform.lossyScale.y) / .8f : thighTop - standingBottom;
        float standingLeft = body.position.x, standingRight = body.position.x;
        foreach (Collider2D limb in playerColliders)
            if (limb != null && !limb.isTrigger)
            {
                standingLeft = Mathf.Min(standingLeft, limb.bounds.min.x);
                standingRight = Mathf.Max(standingRight, limb.bounds.max.x);
            }
        standingBodyWidth = Mathf.Max(.1f, standingRight - standingLeft);
        nativeRunSpeed = (float)type.GetField("maxVelocity").GetValue(player);
        float standingTop = body.position.y;
        foreach (Collider2D limb in playerColliders) if (limb != null && !limb.isTrigger) standingTop = Mathf.Max(standingTop, limb.bounds.max.y);
        standingHeight = standingTop - standingBottom;
        Transform standingHead = player.transform.Find("Head");
        standingHeadHeight = standingHead != null ? standingHead.position.y - standingBottom : standingHeight * .9f;
        foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(type.Assembly.GetType("GoalTrigger")))
        { Component goal = item as Component; if (goal != null && goal.transform.position.x > 0f) ownGoal = goal.GetComponent<Collider2D>(); }
        normalAnimatorSpeed = animator != null ? animator.speed : 1f;
        object input = type.GetField("input").GetValue(player);
        horizontalAxis = input != null ? input.GetType().GetField("horizontal").GetValue(input) as string : "Horizontal";
        Type muscleType = type.Assembly.GetType("StickManController");
        muscles = player.GetComponent(muscleType);
        InitializeTacklePhysics(muscleType);
        head = player.transform.Find("Head");
        if (head != null) headCollider = head.GetComponent<Collider2D>();
        GameObject opponent = GameObject.Find(fan ? "Zhao" : "Fan");
        if (opponent != null)
        {
            Transform opponentTransform = opponent.transform.Find("Body");
            if (opponentTransform != null) opponentBody = opponentTransform.GetComponent<Rigidbody2D>();
        }
        Type managerType = type.Assembly.GetType("GameManager");
        manager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
        stoppingField = managerType.GetField("isStopping");
        Type ballType = type.Assembly.GetType("Ball");
        Component ballComponent = UnityEngine.Object.FindObjectOfType(ballType) as Component;
        if (ballComponent != null)
        {
            ball = ballComponent.GetComponent<Rigidbody2D>();
            ballCollider = ballComponent.GetComponent<Collider2D>();
        }
        if (skillFont == null)
            skillFont = Font.CreateDynamicFontFromOSFont(new string[] { "Microsoft YaHei", "SimHei", "Arial" }, 22);
        players.Add(this);
    }

    public static float GetMovementSkillMultiplier(Component player, float axis)
    {
        if (!Enabled || player == null) return 1f;
        Attach(player);
        PlayerSkills skills = player.GetComponent<PlayerSkills>();
        if (skills == null || skills.Stopped()) return 1f;
        // Emergency retreat now matches the previous Fan retreat (1.5x total).
        // That speed is also the common Zhao retreat balance in classic mode.
        if (!skills.fan && axis * skills.direction < -.1f) return Time.time <= skills.retreatUntil ? skills.rescueSpeed / (skills.nativeRunSpeed * PlayerMovement.BackwardMultiplier) : 1f;
        return skills.CanAttackSprint(axis) ? FanAttackSprintMultiplier : 1f;
    }

    private bool CanAttackSprint(float axis)
    {
        return fan && !Stopped() && BallInFront() && axis * direction > .1f &&
            Time.time > shotUntil && foot != null &&
            Vector2.Distance(ball.position, body.position) > 1.6f &&
            Vector2.Distance(ball.position, foot.position) > 1.6f;
    }

    public static void NotifyMovement(Component player, float axis)
    {
        if (!Enabled || player == null) return;
        PlayerSkills skills = player.GetComponent<PlayerSkills>();
        if (skills == null || skills.Stopped()) return;
        int index = !skills.fan && axis * skills.direction < -.1f ? 6 : skills.CanAttackSprint(axis) ? 7 : -1;
        if (index < 0) { skills.lastMovementSkill = -1; return; }
        if (index != skills.lastMovementSkill || Time.time - skills.lastMovementFx >= .6f)
        {
            skills.lastMovementSkill = index;
            skills.lastMovementFx = Time.time;
            skills.Show(index);
        }
    }

    private bool Stopped()
    {
        return !Enabled || Time.timeScale <= 0f || controller == null || body == null ||
            (manager != null && stoppingField != null && (bool)stoppingField.GetValue(manager));
    }

    private bool BallInFront()
    {
        // Players face the opposing goal: Fan right, Zhao left. Moving
        // backward does not turn their models or change this facing direction.
        return ball != null && body != null && direction * (ball.position.x - body.position.x) >= 0f;
    }

    private void SuppressBehindSkills()
    {
        RestoreDribbleSpeed();
        headUntil = kickUntil = -1f;
        pendingShotTap = -10f;
    }

    private void BeginAction(string action)
    {
        if (Stopped()) return;
        if (action == "Jump" && retreatJumpQueued && Time.time <= retreatUntil)
        {
            retreatJumpQueued = false; retreatJumped = true; rescueJumpAttempts++; rescueJumpStarted = Time.time;
            retreatUntil = Mathf.Min(rescueStarted + 3.6f, Mathf.Max(retreatUntil, Time.time + 1.5f));
        }
        if (action == "Kick" || (action == "Head" && !retreatHeaded)) retreatUntil = -1f;
        // Aerial Fortress boosts any normal Zhao jump, even with a rear ball.
        if (!BallInFront())
        {
            SuppressBehindSkills();
            if (!fan && action == "Jump") Show(2);
            return;
        }
        float now = Time.time;
        if (action == "Kick" || action == "Head" || action == "Jump") RestoreDribbleSpeed();
        if (action == "Kick")
        {
            lastKick = now;
            // Normal Fan kicks prepare the leaf shot. Dedicated power kicks
            // are ordinary actions; Zhao no longer uses double-tap commands.
            if (lastShotTapFrame != Time.frameCount)
            {
                lastShotTapFrame = Time.frameCount;
                if (fan && !PowerShot.IsPowerCommand(controller))
                {
                    kickUntil = now + FanVolleyCommandWindow;
                    pendingShotTap = -10f;
                }
                else
                {
                    kickUntil = -1f;
                    pendingShotTap = -10f;
                }
            }
        }
        if (fan && action == "Head" && now - lastHead >= .28f)
        {
            lastHead = now;
            headUntil = now + .34f;
            Show(1);
            // Keep all player-body collisions and ragdoll constraints intact.
            // Header priority applies to the struck ball, not the player's limbs.
        }
        else if (action == "Jump" && !fan)
        {
            Show(2);
        }
        // An explicit strike must not be weakened by the ground-control assist.
        if (action == "Kick" || (fan && action == "Head"))
            shotUntil = now + (action == "Kick" ? FanVolleyCommandWindow : .5f);
    }

    public static void ShowMagneticFoot(Component player)
    {
        PlayerSkills skills = player != null ? player.GetComponent<PlayerSkills>() : null;
        if (skills != null) skills.Show(8);
    }

    private void Show(int index)
    {
        if (!Stopped() && (index == 2 || index == 3 || index == 6 || index == 9 || BallInFront())) effects[index] = Time.time;
    }

    private void Update()
    {
        if (controller != null && Stopped()) ClearRound();
        else if (!BallInFront()) SuppressBehindSkills();
        if (!Stopped())
        {
            UpdateTackleRecovery();
            if (fan && !BallInFront() && GameAIMod.GetDownCommand(controller)) BeginQuickDrop();
        }
    }

    private void FixedUpdate()
    {
        if (Stopped() || ball == null) return;
        UpdateTackleRecovery();
        if (fan && ShouldAutomaticQuickDrop()) BeginQuickDrop();
        UpdateQuickDrop();
        UpdateRescueBalance();
        UpdateTackleKnockback();
        if (!fan) UpdateGroundBallContest();
        if (!BallInFront()) SuppressBehindSkills();
        float now = Time.time;
        if (!fan && burstUntil >= now)
        {
            // Press an aerial strike down, then skim the ground without moving
            // the ball directly or disabling any physical collisions.
            float vertical = BurstVerticalSpeed();
            ball.AddForce(Vector2.up * (vertical - ball.velocity.y) * ball.mass, ForceMode2D.Impulse);
        }
        if (fan)
        {
            if (headUntil >= now && HeadReachable())
            {
                float speed = Mathf.Clamp(Mathf.Max(13f, ball.velocity.magnitude * 1.55f), 13f, 19f);
                Vector2 target = new Vector2(direction * speed, Mathf.Clamp(ball.velocity.y + 2.8f, 2.8f, 6f)) * FanHeaderSpeedMultiplier;
                EnableContinuousBall();
                ball.AddForce((target - ball.velocity) * ball.mass, ForceMode2D.Impulse);
                CancelCurves();
                headUntil = -1f; // One successful strike per heading command.
                PowerShot.CompleteHeader(controller);
                HighlightBall(1, .85f);
            }
            if (now > shotUntil) Dribble();
        }
        float curveAge = now - curveStarted;
        if (fan && curveGoal != null && curveAge >= 0f && !CurveAssistComplete(curveAge))
        {
            Vector2 target = GoalCurveVelocity(curveAge);
            if (BallFlightSafety.WillHit(ball, target, controller, curveAge)) { curveStarted = -10f; curveGoal = null; }
            else ball.AddForce((target - ball.velocity) * ball.mass, ForceMode2D.Impulse);
        }
        else if (curveGoal != null && curveAge >= 0f) { curveStarted = -10f; curveGoal = null; }
        if (glowOwner == this && now < glowUntil && now - lastTrail >= .035f)
        {
            lastTrail = now;
            ballTrail.Add(ball.position);
            if (ballTrail.Count > 9) ballTrail.RemoveAt(0);
        }
    }

    private void HighlightBall(int skill, float duration)
    {
        glowOwner = this;
        glowColor = colors[skill];
        glowUntil = Time.time + duration;
        ballTrail.Clear();
        lastTrail = -10f;
    }

    private bool IsFootContact(Collider2D limb, Vector2 contact, Vector2 outwardNormal)
    {
        return IsStrikeFoot(controller, limb, contact, outwardNormal, Enabled);
    }

    public static bool IsStrikeFoot(Component player, Collider2D limb, Vector2 contact, Vector2 outwardNormal)
    {
        return IsStrikeFoot(player, limb, contact, outwardNormal, false);
    }

    public static bool IsStrikeFoot(Component player, Collider2D limb, Vector2 contact, Vector2 outwardNormal, bool useVolleyRange)
    {
        if (player == null || limb == null || !limb.transform.IsChildOf(player.transform)) return false;
        bool fullShin = player.name == "Fan" || (player.name == "Zhao" && useVolleyRange);
        string name = limb.name;
        bool lowerLeg = name.IndexOf("Low_Leg", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("LowLeg", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("LowerLeg", StringComparison.OrdinalIgnoreCase) >= 0;
        bool separateFoot = fullShin && name.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!lowerLeg && !separateFoot) return false;
        // The lower-leg collider also covers the instep and ankle. Reject the
        // downward-facing sole using the actual local contact normal; the
        // upper-leg collider never qualifies. Do not alter collider geometry.
        Vector2 local = limb.transform.InverseTransformPoint(contact);
        Vector2 normal = limb.transform.InverseTransformDirection(outwardNormal);
        if (normal.sqrMagnitude < .0001f ||
            (normal.y < 0f && -normal.y >= Mathf.Abs(normal.x))) return false;
        Vector2 offset, size;
        BoxCollider2D box = limb as BoxCollider2D;
        CapsuleCollider2D capsule = limb as CapsuleCollider2D;
        if (box != null) { offset = box.offset; size = box.size; }
        else if (capsule != null) { offset = capsule.offset; size = capsule.size; }
        else return false;
        float upperY = offset.y + size.y * (fullShin ? .46f : .2f);
        return local.y > offset.y - size.y * .46f && local.y <= upperY;
    }

    private bool IsBallAirborne()
    {
        // Ground contact, not an absolute height, distinguishes even a low
        // aerial ball from a resting ball. The player need not be airborne.
        if (ballCollider == null || ball == null) return false;
        int count = ballCollider.GetContacts(groundContacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = groundContacts[i];
            Collider2D other = contact.collider == ballCollider ? contact.otherCollider : contact.collider;
            if (other == null) continue;
            string root = other.transform.root.name;
            if (root == "Fan" || root == "Zhao") continue;
            if ((groundMask & (1 << other.gameObject.layer)) != 0 && contact.point.y < ball.position.y - .05f)
                return false;
        }
        return true;
    }

    private void VolleyOnContact(Collider2D limb, Vector2 contact, Vector2 outwardNormal)
    {
        if (!fan || Stopped() || !BallInFront() || ballCollider == null || kickUntil < Time.time || IsCloseContest() ||
            !IsFootContact(limb, contact, outwardNormal)) return;
        Collider2D goal = FindCurveGoal();
        if (goal == null || !HasVolleySeparation(goal) ||
            goal.bounds.size.y <= ballCollider.bounds.extents.y * 2f + .24f) return;
        CancelCurves();
        PrepareGoalCurve(goal);
        Vector2 target = GoalCurveVelocity(0f);
        EnableContinuousBall();
        ball.AddForce((target - ball.velocity) * ball.mass, ForceMode2D.Impulse);
        kickUntil = headUntil = -1f;
        shotUntil = Time.time + curveTravelTime / FanVolleySpeedMultiplier + .4f;
        curveStarted = Time.time;
        Show(5);
        HighlightBall(5, Mathf.Min(FanVolleyFlightDuration / FanVolleySpeedMultiplier, curveTravelTime / FanVolleySpeedMultiplier + .8f));
    }

    private Collider2D FindCurveGoal()
    {
        Type type = controller.GetType().Assembly.GetType("GoalTrigger");
        if (type == null) return null;
        foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(type))
        {
            Component goal = item as Component;
            if (goal != null && goal.transform.position.x * direction > 0f)
                return goal.GetComponent<Collider2D>();
        }
        return null;
    }

    private bool HasVolleySeparation(Collider2D attackingGoal)
    {
        if (body == null || opponentBody == null || attackingGoal == null) return false;
        Type type = controller.GetType().Assembly.GetType("GoalTrigger");
        if (type == null) return false;
        foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(type))
        {
            Component goal = item as Component;
            if (goal == null || goal.transform.position.x * direction >= 0f) continue;
            Collider2D defendingGoal = goal.GetComponent<Collider2D>();
            if (defendingGoal == null) continue;
            // Measure the playable length between the two goal lines. Only
            // horizontal player separation counts, including during jumps.
            float attackLine = direction > 0f ? attackingGoal.bounds.min.x : attackingGoal.bounds.max.x;
            float defendLine = direction > 0f ? defendingGoal.bounds.max.x : defendingGoal.bounds.min.x;
            float quarterLength = Mathf.Abs(attackLine - defendLine) * .25f;
            return quarterLength > 0f && Mathf.Abs(body.position.x - opponentBody.position.x) >= quarterLength;
        }
        return false;
    }

    private void PrepareGoalCurve(Collider2D goal)
    {
        curveGoal = goal;
        curveOrigin = ball.position;
        Bounds mouth = goal.bounds;
        float radius = ballCollider.bounds.extents.y / (ExperimentScale.Enabled ? ExperimentScale.Size : 1f);
        float minY = mouth.min.y + radius + .12f;
        float maxY = mouth.max.y - radius - .12f;
        // Finish the arc before crossing the goal/out trigger line, accounting
        // for the whole ball. The final approach remains inside the mouth.
        curveDestination = new Vector2(direction > 0f ? mouth.min.x - ballCollider.bounds.extents.x / (ExperimentScale.Enabled ? ExperimentScale.Size : 1f) - .22f :
            mouth.max.x + ballCollider.bounds.extents.x / (ExperimentScale.Enabled ? ExperimentScale.Size : 1f) + .22f, Mathf.Lerp(minY, maxY, .52f));
        curveForwardSpeed = Mathf.Clamp(Mathf.Max(12f, Mathf.Abs(ball.velocity.x) * 1.3f + 10f), 12f, 18f);
        float distance = Mathf.Max(.1f, direction * (curveDestination.x - curveOrigin.x));
        curveTravelTime = Mathf.Clamp(distance / curveForwardSpeed, .08f, 1.5f);
        float ceiling = curveOrigin.y + FanVolleyArcHeight;
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(curveOrigin + Vector2.up * .05f, Vector2.up, 30f, groundMask))
        {
            if (hit.collider == null || hit.collider == ballCollider || hit.collider.isTrigger || hit.normal.y > -.5f) continue;
            string root = hit.collider.transform.root.name;
            if (root == "Fan" || root == "Zhao") continue;
            ceiling = hit.point.y - radius - .25f;
            break;
        }
        float available = Mathf.Max(0f, ceiling - Mathf.Max(curveOrigin.y, curveDestination.y));
        curveArcHeight = Mathf.Min(available, FanVolleyArcHeight, FanVolleyLiftSpeed * curveTravelTime * .25f);
        // Short-range shots need a short arc so they can descend before the bar.
        curveArcHeight *= Mathf.Min(1f, curveTravelTime / .45f) * (ExperimentScale.Enabled ? .9f : 1f);
    }

    private Vector2 GoalCurveVelocity(float age)
    {
        return GoalCurveVelocityAt(age, ball.position);
    }

    private Vector2 GoalCurveVelocityAt(float age, Vector2 position)
    {
        // Follow the same spatial curve at 90% of its former time rate.
        return GoalCurveProfileVelocityAt(age * FanVolleySpeedMultiplier, position) * FanVolleySpeedMultiplier;
    }

    private Vector2 GoalCurveProfileVelocityAt(float age, Vector2 position)
    {
        Bounds mouth = curveGoal.bounds;
        float radius = ballCollider.bounds.extents.y / (ExperimentScale.Enabled ? ExperimentScale.Size : 1f);
        float minY = mouth.min.y + radius + .12f;
        float maxY = mouth.max.y - radius - .12f;
        float aimY = Mathf.Clamp(curveDestination.y, minY, maxY);
        float distance = direction * (curveDestination.x - position.x);
        if (distance < 2f || age >= curveTravelTime)
        {
            float arrival = Mathf.Max(.08f, Mathf.Max(0f, distance) / (curveForwardSpeed * FanVolleySpeedMultiplier));
            float gravity = Physics2D.gravity.y * ball.gravityScale;
            float vertical = (aimY - position.y) / arrival - .5f * gravity * arrival;
            return new Vector2(direction * curveForwardSpeed, Mathf.Clamp(vertical / FanVolleySpeedMultiplier, -24f, 24f));
        }
        float u = Mathf.Clamp01(age / curveTravelTime);
        float wave = Mathf.Sin(u * Mathf.PI * 4f);
        float envelope = 4f * u * (1f - u);
        Vector2 desired = Vector2.Lerp(curveOrigin, curveDestination, u);
        desired.y += curveArcHeight * envelope + .18f * wave * envelope;
        Vector2 velocity = (curveDestination - curveOrigin) / curveTravelTime;
        velocity.y += (curveArcHeight * 4f * (1f - 2f * u) + .18f *
            (Mathf.PI * 4f * Mathf.Cos(u * Mathf.PI * 4f) * envelope + wave * 4f * (1f - 2f * u))) / curveTravelTime;
        velocity += (desired - position) * 8f;
        return new Vector2(direction * Mathf.Clamp(velocity.x * direction, 8f, 20f), Mathf.Clamp(velocity.y, -24f, 24f));
    }

    private bool CurveAssistComplete(float age)
    {
        return CurveAssistCompleteAt(age, ball.position);
    }

    private bool CurveAssistCompleteAt(float age, Vector2 position)
    {
        age *= FanVolleySpeedMultiplier;
        if (curveGoal == null || ballCollider == null || age >= FanVolleyFlightDuration ||
            age >= curveTravelTime + .35f) return true;
        Bounds mouth = curveGoal.bounds;
        float leadingEdge = position.x + direction * ballCollider.bounds.extents.x;
        float goalFront = direction > 0f ? mouth.min.x : mouth.max.x;
        // Once the ball reaches the mouth, gravity and the native goal/net
        // collisions take over. Never hold it against the frame with impulses.
        if (direction * (leadingEdge - goalFront) >= -.04f) return true;
        float radius = ballCollider.bounds.extents.y / (ExperimentScale.Enabled ? ExperimentScale.Size : 1f);
        bool insideHeight = position.y >= mouth.min.y + radius + .08f &&
            position.y <= mouth.max.y - radius - .08f;
        return insideHeight && direction * (position.x - curveDestination.x) >= 0f;
    }

    // Forecast the visible skill flight with the same velocity law as the ball.
    // The simulation uses local copies and never moves or applies forces to it.
    public static bool TryPredictFanVolley(float ahead, out Vector2 position, out Vector2 velocity)
    {
        position = velocity = Vector2.zero;
        foreach (PlayerSkills shooter in players)
        {
            if (shooter == null || !shooter.fan || shooter.Stopped() || shooter.ball == null ||
                shooter.curveGoal == null || shooter.curveStarted < 0f) continue;
            float age = Time.time - shooter.curveStarted;
            if (age < 0f || shooter.CurveAssistComplete(age)) continue;
            position = shooter.ball.position;
            velocity = shooter.ball.velocity;
            float elapsed = 0f;
            bool assisted = true;
            while (elapsed < Mathf.Clamp(ahead, 0f, 1.6f))
            {
                float step = Mathf.Min(Time.fixedDeltaTime, ahead - elapsed);
                if (assisted && shooter.CurveAssistCompleteAt(age + elapsed, position)) assisted = false;
                if (assisted) velocity = shooter.GoalCurveVelocityAt(age + elapsed, position);
                velocity += Physics2D.gravity * shooter.ball.gravityScale * step;
                velocity /= 1f + shooter.ball.drag * step;
                position += velocity * step;
                elapsed += step;
            }
            return true;
        }
        return false;
    }

    private bool CanBurst()
    {
        return BallInFront() && ballCollider != null && opponentBody != null &&
            !IsCloseContest();
    }

    private bool IsCloseContest()
    {
        if (ball == null || body == null || opponentBody == null) return false;
        float headHeight = head != null ? head.position.y : body.position.y + 1f;
        return Mathf.Abs(body.position.x - opponentBody.position.x) <= 2f &&
            Mathf.Abs(ball.position.x - body.position.x) < 1.9f &&
            Mathf.Abs(ball.position.x - opponentBody.position.x) < 1.9f &&
            ball.position.y < Mathf.Max(headHeight, opponentBody.position.y + 1f) + 1.5f;
    }

    private void BurstOnContact(Collider2D limb, Vector2 contact, Vector2 outwardNormal)
    {
        if (fan || Stopped() || kickUntil < Time.time || !CanBurst() ||
            !IsFootContact(limb, contact, outwardNormal)) return;
        float speed = Mathf.Clamp(Mathf.Max(17f, ball.velocity.magnitude * 1.65f), 17f, 23f);
        burstFloorHeight = FindBurstFloorHeight();
        float vertical = BurstVerticalSpeed();
        EnableContinuousBall();
        ball.AddForce((new Vector2(direction * speed, vertical) - ball.velocity) * ball.mass, ForceMode2D.Impulse);
        kickUntil = -1f;
        burstUntil = shotUntil = Time.time + .6f;
        Show(4);
        HighlightBall(4, .85f);
        CancelCurves();
    }

    private float FindBurstFloorHeight()
    {
        float radius = ballCollider.bounds.extents.y;
        RaycastHit2D[] hits = Physics2D.RaycastAll(ball.position + Vector2.up * .1f, Vector2.down, 30f, groundMask);
        foreach (RaycastHit2D hit in hits)
        {
            Collider2D ground = hit.collider;
            if (ground == null || ground == ballCollider || ground.isTrigger || hit.normal.y < .5f) continue;
            string root = ground.transform.root.name;
            if (root == "Fan" || root == "Zhao") continue;
            return hit.point.y + radius + .035f;
        }
        // If there is no pitch underneath, keep a flat strike rather than lob.
        return ball.position.y;
    }

    private float BurstVerticalSpeed()
    {
        float clearance = ball.position.y - burstFloorHeight;
        return clearance > .1f ? -Mathf.Clamp(clearance * 12f, 2f, 16f) : .2f;
    }

    private static void CancelCurves()
    {
        foreach (PlayerSkills player in players)
            if (player != null) { player.curveStarted = -10f; player.curveGoal = null; }
    }

    public static void CancelShotFlights() { CancelCurves(); foreach (PlayerSkills player in players) if (player != null) player.burstUntil = -1f; }
    public static void ShowPowerBurst(Component controller)
    {
        PlayerSkills skills = controller.GetComponent<PlayerSkills>();
        if (skills != null) { skills.Show(4); skills.HighlightBall(4, .85f); }
    }

    // An opponent touch ends flight assistance so the next player can take control.
    public static void OnBallCollision(Collision2D collision)
    {
        foreach (PlayerSkills owner in players) if (owner != null && !owner.fan) owner.UpdateGroundBallContest();
        BurstRangeTests.BeforeContact(collision);
        bool power = PowerShot.OnBallCollision(collision);
        BurstRangeTests.AfterContact(collision, power);
        Collider2D touched = collision.collider;
        if (touched == null) return;
        PlayerSkills playerAtContact = touched.GetComponentInParent<PlayerSkills>();
        if (playerAtContact != null) playerAtContact.CushionRescueContact(touched);
        if (playerAtContact != null && !power)
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);
                Vector2 normal = contact.normal;
                // Unity callbacks can report either collider's normal. Orient
                // it outward from the limb toward the ball before filtering.
                if (playerAtContact.ball != null &&
                    Vector2.Dot(normal, playerAtContact.ball.position - contact.point) < 0f)
                    normal = -normal;
                if (playerAtContact.fan) playerAtContact.VolleyOnContact(touched, contact.point, normal);
            }
        foreach (PlayerSkills player in players)
        {
            if (player != null && player.curveStarted > -1f &&
                ((playerAtContact != null && playerAtContact != player) ||
                 (playerAtContact == null && !touched.isTrigger && Time.time - player.curveStarted > .001f)))
            { player.curveStarted = -10f; player.curveGoal = null; }
            if (player != null && player.burstUntil >= Time.time && playerAtContact != null &&
                playerAtContact != player) player.burstUntil = -1f;
        }
    }

    // Absorb a real player collision during rescue instead of allowing
    // the running rig to accelerate a returning ball into its own goal.
    // The defensive contact absorbs the horizontal impulse; no collider,
    // position, scoring rule or incoming shot is changed without contact.
    private void CushionRescueContact(Collider2D touched)
    {
        if (!Enabled || fan || Stopped() || GameAIMod.BallOutThisRally || ball == null ||
            (Time.time > retreatUntil && Time.time > rescueRecoveryUntil)) return;

        if (touched == headCollider && retreatHeaded && Time.time <= retreatUntil)
        { if (RescueHeader(controller)) return; }
        // Preserve the existing deliberate backward header above the crossbar.
        if (retreatHeaded && ownGoal != null && ball.velocity.x > 0f && ball.velocity.y > 0f)
        {
            float t = (ownGoal.bounds.min.x - ballCollider.bounds.max.x) / ball.velocity.x;
            float ceiling = BallBoundaryGuard.FindGoalCeiling(ownGoal) + ballCollider.bounds.extents.y + .25f;
            if (t > 0f && ball.position.y + ball.velocity.y * t + .5f * Physics2D.gravity.y * ball.gravityScale * t * t > ceiling) return;
        }
        // Clockwise spin becomes goalward rolling velocity at the next pitch
        // bounce. Absorb it at this real defensive contact as well.
        if (ball.angularVelocity < 0f)
            ball.AddTorque(-ball.angularVelocity * Mathf.Deg2Rad * ball.inertia, ForceMode2D.Impulse);
        if (ball.velocity.x <= 0f) return;
        float momentum = ball.velocity.x * ball.mass;
        ball.AddForce(Vector2.left * momentum, ForceMode2D.Impulse);

    }
    private bool HeadReachable()
    {
        if (!BallInFront() || head == null || ball.position.y < -.6f) return false;
        if (headCollider != null)
            return Vector2.Distance(headCollider.ClosestPoint(ball.position), ball.position) <= .48f;
        return Vector2.Distance(head.position, ball.position) <= .95f;
    }

    private bool GroundBallInReach(float reach)
    {
        if (!BallInFront() || foot == null || ball.position.y > -.85f) return false;
        float height = ball.position.y - foot.position.y;
        float ahead = direction * (ball.position.x - body.position.x);
        return height >= -.4f && height <= 1.05f && ahead >= 0f && ahead <= reach;
    }

    private void Dribble()
    {
        if (groundedField == null || !(bool)groundedField.GetValue(controller) ||
            !GroundBallInReach(1.3f) || IsBallAirborne() ||
            Mathf.Abs(GameAIMod.GetAxis(horizontalAxis, controller)) < .1f)
        {
            RestoreDribbleSpeed();
            return;
        }
        if (animator != null && !fasterDribble)
        {
            PlayerMovement.PrepareDribble(controller);
            normalAnimatorSpeed = animator.speed;
            animator.speed = normalAnimatorSpeed * FanDribbleAnimationMultiplier;
            fasterDribble = true;
        }
        if (Time.time - lastDribbleFx > .68f)
        {
            Show(0);
            lastDribbleFx = Time.time;
        }
    }

    private void RestoreDribbleSpeed()
    {
        if (fasterDribble && animator != null) animator.speed = normalAnimatorSpeed;
        fasterDribble = false;
    }

    private void EnableContinuousBall()
    {
        if (ball == null) return;
        if (!changedBallDetection || continuousBall != ball)
        {
            continuousBall = ball;
            originalBallDetection = ball.collisionDetectionMode;
            changedBallDetection = true;
        }
        ball.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        foreach (PlayerSkills player in players)
            if (player != null && player.physicalLimbs != null)
                foreach (Rigidbody2D limb in player.physicalLimbs) if (limb != null) limb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    private static void RestoreBallDetection()
    {
        if (changedBallDetection && continuousBall != null) continuousBall.collisionDetectionMode = originalBallDetection;
        continuousBall = null;
        changedBallDetection = false;
    }

    private void InitializeTacklePhysics(Type muscleType)
    {
        physicalLimbs = controller.GetComponentsInChildren<Rigidbody2D>();
        normalMasses = new float[physicalLimbs.Length];
        for (int i = 0; i < physicalLimbs.Length; i++) normalMasses[i] = physicalLimbs[i].mass;
        Array source = muscleType.GetField("muscles").GetValue(muscles) as Array;
        if (source != null)
        {
            physicalMuscles = new object[source.Length];
            normalMuscleForces = new float[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                physicalMuscles[i] = source.GetValue(i);
                muscleForceField = physicalMuscles[i].GetType().GetField("force");
                normalMuscleForces[i] = (float)muscleForceField.GetValue(physicalMuscles[i]);
            }
        }
        if (!fan)
            foreach (Collider2D collider in controller.GetComponentsInChildren<Collider2D>())
            {
                if (!IsUpperBody(collider) || collider.isTrigger || collider.attachedRigidbody == null) continue;
                PowerTackleContact relay = collider.gameObject.AddComponent<PowerTackleContact>();
                relay.owner = this;
            }
    }

    private static bool IsUpperBody(Collider2D collider)
    {
        return collider != null && (collider.name == "Body" || collider.name == "Head" ||
            collider.name.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private bool AirborneForTackle()
    {
        return !TouchesGround() && DropClearance() > .20f * Mathf.Abs(transform.lossyScale.y) / .8f;
    }

    public void OnBodyContact(Collision2D collision)
    {
        if (fan || Stopped() || Time.time - lastTackle < .95f ||
            collision == null || collision.contactCount == 0 || !IsUpperBody(collision.collider)) return;
        PlayerSkills opponent = collision.collider.GetComponentInParent<PlayerSkills>();
        if (opponent == null || !opponent.fan || opponent.Stopped() || opponent.body != opponentBody ||
            direction * (opponentBody.position.x - body.position.x) < -.25f) return;
        bool sideContact = false;
        for (int i = 0; i < collision.contactCount; i++)
            if (Mathf.Abs(collision.GetContact(i).normal.x) > .3f) { sideContact = true; break; }
        if (!sideContact) return;
        bool aerial = AirborneForTackle() || opponent.AirborneForTackle();
        UpdateGroundBallContest();
        ApplyTackle(opponent, aerial, false);
    }

    public static bool IsGroundBallContest(Component player)
    {
        PlayerSkills skills = player != null ? player.GetComponent<PlayerSkills>() : null;
        return skills != null && !skills.fan && !skills.Stopped() && skills.BallInFront() && Time.time <= skills.groundContestUntil;
    }

    private void UpdateGroundBallContest()
    {
        if (Stopped() || ballCollider == null || opponentBody == null || !BallInFront()) { groundPressure = 0f; groundContestUntil = -1f; return; }
        PlayerSkills opponent = opponentBody.GetComponentInParent<PlayerSkills>();
        if (opponent == null || opponent.Stopped() ||
            ball.position.x < opponentBody.position.x || ball.position.x > body.position.x) { groundPressure = 0f; groundContestUntil = -1f; return; }
        bool ours = false, theirs = false, pitch = false;
        int count = ballCollider.GetContacts(contestContacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = contestContacts[i];
            Collider2D other = contact.collider == ballCollider ? contact.otherCollider : contact.collider;
            if (other == null || other.isTrigger || other.Distance(ballCollider).distance > .035f * Mathf.Abs(transform.lossyScale.x) / .8f) continue;
            PlayerSkills touched = other.GetComponentInParent<PlayerSkills>();
            if (touched != null && other.name.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0 && Mathf.Abs(contact.normal.x) > .25f)
            { if (touched == this) ours = true; if (touched == opponent) theirs = true; }
            else if (touched == null && (groundMask & (1 << other.gameObject.layer)) != 0 &&
                contact.point.y < ball.position.y - ballCollider.bounds.extents.y * .5f && Mathf.Abs(contact.normal.y) > .5f) pitch = true;
        }
        // A physical chain of contacts is required: Zhao's foot -> ball ->
        // Fan's foot, with the ball and both players supported by the pitch.
        // Merely standing nearby can never create a remote knockdown.
        if (!ours || !theirs || !pitch) { groundPressure = 0f; groundContestUntil = -1f; return; }
        // A player's supporting foot may rest on the grounded ball itself.
        // Require near-floor limbs as well as the full physical contact chain.
        float supportHeight = ballCollider.bounds.size.y + .2f * Mathf.Abs(transform.lossyScale.y) / .8f;
        if ((!TouchesGround() && DropClearance() > supportHeight) || (!opponent.TouchesGround() && opponent.DropClearance() > supportHeight))
        { groundPressure = 0f; groundContestUntil = -1f; return; }
        groundContestUntil = Time.time + Time.fixedDeltaTime * 1.1f;
        if (GameAIMod.GetAxis(horizontalAxis, controller) * direction <= .1f) { groundPressure = 0f; return; }
        groundPressure += Time.fixedDeltaTime;
        // One solver step of real opposing foot contacts is enough. The
        // smaller experimental ball often changes contact points every step.
        if (groundPressure < .015f) return;
        ApplyTackle(opponent, false, true);
    }

    private void ApplyTackle(PlayerSkills opponent, bool aerial, bool throughBall)
    {
        float cooldown = aerial ? .95f : GroundTackleCooldown;
        if (Time.time - lastTackle < Mathf.Max(lastTackleCooldown, cooldown)) return;
        lastTackleCooldown = cooldown;
        lastTackle = Time.time;
        braceUntil = Time.time + (aerial ? .16f : .20f);
        for (int i = 0; i < physicalLimbs.Length; i++)
            physicalLimbs[i].mass = normalMasses[i] * (aerial ? 1.35f : 1.45f);
        bracedMasses = true;
        opponent.staggerUntil = Time.time + (aerial ? TackleAirDuration : throughBall ? .26f : GroundTackleDuration);
        opponent.balanceGain = aerial ? 15f : 18f;
        opponent.ApplyBalanceGain();
        opponent.knockbackOrigin = opponent.RigCenter().x;
        opponent.knockbackDistance = opponent.standingBodyWidth * TackleKnockbackBodyWidths;
        opponent.knockbackDirection = direction;
        opponent.knockbackUntil = Time.time + .60f;
        float speed = aerial ? TackleAirSpeed : TackleGroundSpeed * GroundTackleSpeedMultiplier * (throughBall ? .85f : 1f);
        // Add equal/opposite impulses to the connected bodies. A briefly heavier
        // Zhao recoils less; Fan's softer muscle response lets the impact tip him.
        Vector2 impulse = Vector2.right * direction * speed * opponent.TotalMass();
        opponent.ApplyLinearImpulse(impulse);
        ApplyLinearImpulse(-impulse);
        float spin = -direction * (aerial ? 118.75f : 56.25f) * 1.3f;
        if (!aerial) spin *= GroundTackleSpinMultiplier * (throughBall ? .85f : 1f);
        float angularImpulse = spin * Mathf.Deg2Rad * opponent.RigInertia();
        opponent.ApplyAngularImpulse(angularImpulse);
        ApplyAngularImpulse(-angularImpulse);
        Show(3);
    }

    private void UpdateTackleKnockback()
    {
        if (!fan || (knockbackUntil < 0f && Time.time >= staggerUntil + .5f)) return;
        if (Stopped()) { knockbackUntil = -1f; return; }
        // Let the initial impact tip Fan, then damp rotation while he slides.
        // This uses torque through the connected rig, never a pose override.
        float angle = (float)muscles.GetType().GetField("body").GetValue(muscles);
        if (Time.time >= staggerUntil - .08f || Mathf.Abs(Mathf.DeltaAngle(body.rotation, angle)) > 32f)
        {
            float spinTarget = Mathf.Clamp(Mathf.DeltaAngle(body.rotation, angle) * 7f, -140f, 140f);
            float spinChange = Mathf.Clamp(spinTarget - body.angularVelocity, -1200f * Time.fixedDeltaTime, 1200f * Time.fixedDeltaTime);
            ApplyAngularImpulse(spinChange * Mathf.Deg2Rad * RigInertia());
        }
        if (knockbackUntil < 0f) return;
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
        if (Time.time >= knockbackUntil && velocity <= 80f * Time.fixedDeltaTime + .05f) knockbackUntil = -1f;
    }

    private float TotalMass()
    {
        float total = 0f;
        foreach (Rigidbody2D limb in physicalLimbs) if (limb != null) total += limb.mass;
        return Mathf.Max(.01f, total);
    }

    private Vector2 RigCenter()
    {
        Vector2 sum = Vector2.zero;
        foreach (Rigidbody2D limb in physicalLimbs) if (limb != null) sum += limb.worldCenterOfMass * limb.mass;
        return sum / TotalMass();
    }

    private float RigInertia()
    {
        Vector2 center = RigCenter();
        float inertia = 0f;
        foreach (Rigidbody2D limb in physicalLimbs)
            if (limb != null) inertia += limb.inertia + limb.mass * (limb.worldCenterOfMass - center).sqrMagnitude;
        return Mathf.Max(.01f, inertia);
    }

    private void ApplyLinearImpulse(Vector2 impulse)
    {
        float mass = TotalMass();
        foreach (Rigidbody2D limb in physicalLimbs)
            if (limb != null && limb.bodyType == RigidbodyType2D.Dynamic)
                limb.AddForce(impulse * (limb.mass / mass), ForceMode2D.Impulse);
    }

    private void ApplyAngularImpulse(float impulse)
    {
        // Spread the torque over the intact rig so a shoulder hit rotates the
        // connected player instead of wrenching one limb away from its joint.
        Vector2 center = RigCenter();
        float spin = impulse / RigInertia();
        foreach (Rigidbody2D limb in physicalLimbs)
        {
            if (limb == null || limb.bodyType != RigidbodyType2D.Dynamic) continue;
            Vector2 offset = limb.worldCenterOfMass - center;
            limb.AddForce(new Vector2(-offset.y, offset.x) * (spin * limb.mass), ForceMode2D.Impulse);
            limb.AddTorque(spin * limb.inertia, ForceMode2D.Impulse);
        }
    }

    public static void BeforeMuscles(Component stick)
    {
        ZhaoHeader.BeforeMuscles(stick);
        PlayerMovement.BeforeMuscles(stick);
        MagneticFoot.BeforeMuscles(stick);
        PlayerSkills skills = stick.GetComponent<PlayerSkills>();
        if (skills != null)
        {
            skills.UpdateTackleRecovery();
        }
    }

    public bool CanSmoothWalkingMuscles()
    {
        return Time.time > headUntil && Time.time > kickUntil;
    }

    private bool TouchesGround()
    {
        if (playerColliders == null) return false;
        foreach (Collider2D limb in playerColliders)
        {
            if (limb == null || limb.isTrigger) continue;
            int count = limb.GetContacts(groundContacts);
            for (int i = 0; i < count; i++)
            {
                ContactPoint2D contact = groundContacts[i];
                Collider2D other = contact.collider == limb ? contact.otherCollider : contact.collider;
                if (other == null || other.isTrigger || other.transform.root == transform.root ||
                    other.transform.root.name == "Fan" || other.transform.root.name == "Zhao" || other == ballCollider) continue;
                if ((groundMask & (1 << other.gameObject.layer)) != 0 && contact.point.y < body.position.y - .2f)
                    return true;
            }
        }
        return false;
    }

    public static bool CanQuickDrop(Component player)
    {
        PlayerSkills skills = player != null ? player.GetComponent<PlayerSkills>() : null;
        return skills != null && skills.fan && !skills.Stopped() && !skills.quickDrop && !skills.TouchesGround() &&
            skills.DropClearance() > .18f;
    }

    private void BeginQuickDrop()
    {
        if (!CanQuickDrop(controller)) return;
        RestoreDribbleSpeed();
        quickDrop = true;
        dropStarted = Time.time;
        Show(9);
    }

    private bool ShouldAutomaticQuickDrop()
    {
        if (!CanQuickDrop(controller) || !BallInFront() || ballCollider == null || !IsBallAirborne()) return false;
        float incoming = -direction * ball.velocity.x;
        float scale = Mathf.Abs(transform.lossyScale.x) / .8f;
        float gap = direction * (ball.position.x - body.position.x);
        if (incoming < .5f || gap > Mathf.Min(3f * scale, incoming * .65f + ballCollider.bounds.extents.x) ||
            Mathf.Abs(ball.velocity.y) > Mathf.Max(1.5f, incoming * .45f)) return false;
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(body.position, Vector2.down, 30f, groundMask))
        {
            if (hit.collider == null || hit.collider.isTrigger || hit.normal.y < .5f || hit.collider == ballCollider) continue;
            string root = hit.collider.transform.root.name;
            if (root == "Fan" || root == "Zhao") continue;
            return ballCollider.bounds.min.y >= hit.point.y - .02f &&
                ballCollider.bounds.max.y < hit.point.y + standingInterceptHeight;
        }
        return false;
    }

    private float DropClearance()
    {
        float lowest = body.position.y;
        foreach (Collider2D limb in playerColliders)
            if (limb != null && !limb.isTrigger) lowest = Mathf.Min(lowest, limb.bounds.min.y);
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(body.position, Vector2.down, 30f, groundMask))
        {
            Collider2D ground = hit.collider;
            if (ground == null || ground.isTrigger || ground == ballCollider || hit.normal.y < .5f) continue;
            string root = ground.transform.root.name;
            if (root == "Fan" || root == "Zhao") continue;
            return Mathf.Max(0f, lowest - hit.point.y);
        }
        return 30f;
    }

    private void UpdateQuickDrop()
    {
        if (!quickDrop) return;
        if (Stopped() || TouchesGround() || Time.time - dropStarted > 1.2f) { quickDrop = false; return; }
        float clearance = DropClearance();
        float speed = Mathf.Min(FanDropSpeed, Mathf.Sqrt(2f * 70f * clearance));
        float vertical = 0f;
        foreach (Rigidbody2D limb in physicalLimbs) if (limb != null) vertical += limb.velocity.y * limb.mass;
        vertical /= TotalMass();
        // Accelerate the whole connected rig together; brake near the pitch.
        // No teleporting, bone stretching, gravity edits or collision bypass.
        ApplyLinearImpulse(Vector2.up * (-speed - vertical) * TotalMass());
        if (clearance < .025f) quickDrop = false;
    }

    private void ApplyBalanceGain()
    {
        if (!fan || muscleForceField == null || physicalMuscles == null) return;
        // Keep arms and legs under their original muscle control. Only soften
        // the head/torso balance briefly, so the connected pose cannot fold up.
        for (int i = 0; i < Mathf.Min(2, physicalMuscles.Length); i++)
            muscleForceField.SetValue(physicalMuscles[i], Mathf.Min(normalMuscleForces[i], balanceGain));
        weakenedBalance = true;
    }

    private void UpdateTackleRecovery()
    {
        if (Stopped()) { RestoreTacklePhysics(); return; }
        if (bracedMasses && Time.time >= braceUntil)
        {
            for (int i = 0; i < physicalLimbs.Length; i++)
                if (physicalLimbs[i] != null) physicalLimbs[i].mass = normalMasses[i];
            bracedMasses = false;
        }
        if (fan && Time.time < staggerUntil) ApplyBalanceGain();
        else if (weakenedBalance)
        {
            float recovery = Mathf.Clamp01((Time.time - staggerUntil) / .18f);
            FieldInfo torsoTarget = muscles.GetType().GetField("body");
            float targetAngle = (float)torsoTarget.GetValue(muscles);
            bool upright = Mathf.Abs(Mathf.DeltaAngle(body.rotation, targetAngle)) < 10f;
            if (recovery >= 1f && (upright || Time.time - staggerUntil > .4f)) RestoreBalanceGain();
            else
                for (int i = 0; i < Mathf.Min(2, physicalMuscles.Length); i++)
                    muscleForceField.SetValue(physicalMuscles[i], Mathf.Min(normalMuscleForces[i], Mathf.Lerp(balanceGain, 35f, recovery)));
        }
    }

    private void RestoreBalanceGain()
    {
        if (weakenedBalance && muscleForceField != null && physicalMuscles != null)
            for (int i = 0; i < physicalMuscles.Length; i++) muscleForceField.SetValue(physicalMuscles[i], normalMuscleForces[i]);
        weakenedBalance = false;
    }

    private void RestoreTacklePhysics()
    {
        if (bracedMasses && physicalLimbs != null)
            for (int i = 0; i < physicalLimbs.Length; i++)
                if (physicalLimbs[i] != null) physicalLimbs[i].mass = normalMasses[i];
        bracedMasses = false;
        RestoreBalanceGain();
        braceUntil = staggerUntil = knockbackUntil = -1f;
        lastTackle = -10f;
        lastTackleCooldown = .95f;
        groundContestUntil = -1f;
        groundPressure = 0f;
    }

    private void ClearRound()
    {
        RestoreDribbleSpeed();
        quickDrop = false;
        retreatUntil = rescueRecoveryUntil = -1f; retreatHeld = retreatJumped = retreatHeaded = rescueWaiting = retreatJumpQueued = false; rescueJumpAttempts = 0;
        dropStarted = -10f;
        RestoreBallDetection();
        RestoreTacklePhysics();
        headUntil = shotUntil = kickUntil = burstUntil = -1f;
        pendingShotTap = -10f;
        lastShotTapFrame = -1;
        curveGoal = null;
        curveStarted = -10f;
        lastHead = lastDribbleFx = lastKick = -10f;
        lastMovementFx = -10f;
        lastMovementSkill = -1;
        for (int i = 0; i < effects.Length; i++) effects[i] = -10f;
        if (glowOwner == this) { glowOwner = null; glowUntil = -1f; ballTrail.Clear(); }
    }

    private void OnDisable() { ClearRound(); }
    private void OnDestroy() { ClearRound(); players.Remove(this); }

    // Screen overlays follow the actual head/foot positions even on a ragdoll.
    // No particle assets, shaders, sounds or model replacements are required.
    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || Stopped()) return;
        Camera camera = Camera.main;
        if (camera == null || head == null) return;
        Color savedColor = GUI.color;
        Matrix4x4 savedMatrix = GUI.matrix;
        float scale = Mathf.Clamp(Screen.height / 720f, .7f, 1.5f);
        int labelRow = 0;
        if (glowOwner == this && ball != null && Time.time < glowUntil) DrawBallHighlight(camera, scale);
        for (int index = 0; index < effects.Length; index++)
        {
            float age = Time.time - effects[index];
            if (age < 0f || age > .8f) continue;
            float progress = age / .8f;
            Color color = colors[index];
            color.a = 1f - progress * progress;
            Vector3 anchor = index == 8 ? MagneticFoot.VisualAnchor(controller) : index == 1 ? head.position : index == 2 || index == 3 || index == 5 || index == 6 || index == 7 || index == 9 ? (Vector3)body.position :
                (foot != null ? foot.position + Vector3.up * .3f : (Vector3)body.position);
            Vector3 projected = camera.WorldToScreenPoint(anchor);
            if (projected.z <= 0f) continue;
            Vector2 center = new Vector2(projected.x, Screen.height - projected.y);
            GUI.color = color;
            float radius = (index == 8 ? 21f + Mathf.Sin(progress * Mathf.PI) * 5f : index == 0 ? 30f : index == 1 ? 23f + progress * 52f : 32f + progress * 20f) * scale;
            float rotation = age * (index == 0 ? 680f : 240f);
            for (int segment = 0; segment < 30; segment++)
            {
                if ((index == 0 || index == 3) && segment % 15 > 10) continue;
                if (index == 8 && segment % 10 > 7) continue;
                float angle = rotation + segment * 12f;
                if (index == 3) angle = (fan ? 0f : 180f) - 65f + segment * 4.3f;
                Vector2 from = center + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius * (index == 0 || index == 8 ? .42f : 1f));
                float next = angle + (index == 3 ? 4.3f : 12f);
                Vector2 to = center + new Vector2(Mathf.Cos(next * Mathf.Deg2Rad) * radius,
                    Mathf.Sin(next * Mathf.Deg2Rad) * radius * (index == 0 || index == 8 ? .42f : 1f));
                Line(from, to, 3f * scale);
            }
            if (index == 1)
                for (int ray = -1; ray <= 1; ray++)
                    Line(center + new Vector2(direction * 15f, ray * 9f) * scale,
                        center + new Vector2(direction * (45f + progress * 65f), ray * 17f) * scale, 3f * scale);
            if (index == 3)
                for (int ray = -2; ray <= 2; ray++)
                {
                    Vector2 tip = center + new Vector2(direction * (34f + progress * 45f), ray * 11f) * scale;
                    Line(tip - new Vector2(direction * 28f, ray * 4f) * scale, tip, 3f * scale);
                    Line(tip, tip - new Vector2(direction * 8f, 7f) * scale, 3f * scale);
                    Line(tip, tip - new Vector2(direction * 8f, -7f) * scale, 3f * scale);
                }
            if (index == 2)
                for (int ray = -1; ray <= 1; ray++)
                {
                    Vector2 top = center + new Vector2(ray * 28f, -40f - progress * 42f) * scale;
                    Line(top + new Vector2(0f, 30f * scale), top, 3f * scale);
                    Line(top, top + new Vector2(-7f, 10f) * scale, 3f * scale);
                    Line(top, top + new Vector2(7f, 10f) * scale, 3f * scale);
                }
            if (index == 4)
                for (int ray = -1; ray <= 1; ray++)
                    Line(center + new Vector2(direction * (10f + progress * 90f), ray * 7f) * scale,
                        center + new Vector2(direction * (60f + progress * 120f), ray * 7f) * scale, 3f * scale);
            if (index == 5)
                for (int part = 0; part < 24; part++)
                {
                    float t = part / 24f;
                    float nextT = (part + 1f) / 24f;
                    Vector2 from = center + new Vector2(direction * t * 95f, -Mathf.Sin(t * Mathf.PI) * 60f - progress * 12f) * scale;
                    Vector2 to = center + new Vector2(direction * nextT * 95f, -Mathf.Sin(nextT * Mathf.PI) * 60f - progress * 12f) * scale;
                    Line(from, to, 3f * scale);
                }
            if (index == 6 || index == 7)
            {
                float travel = index == 6 ? -direction : direction;
                for (int ray = -1; ray <= 1; ray++)
                {
                    Vector2 tip = center + new Vector2(travel * (24f + progress * 28f), ray * 13f) * scale;
                    Line(tip - new Vector2(travel * 42f, 0f) * scale, tip, 3f * scale);
                    Line(tip, tip - new Vector2(travel * 9f, 7f) * scale, 3f * scale);
                    Line(tip, tip - new Vector2(travel * 9f, -7f) * scale, 3f * scale);
                }
            }
            if (index == 9)
                for (int ray = -1; ray <= 1; ray++)
                {
                    Vector2 tip = center + new Vector2(ray * 22f, 25f + progress * 50f) * scale;
                    Line(tip - Vector2.up * 35f * scale, tip, 3f * scale);
                    Line(tip, tip + new Vector2(-7f, -9f) * scale, 3f * scale);
                    Line(tip, tip + new Vector2(7f, -9f) * scale, 3f * scale);
                }
            Vector3 labelAnchor = camera.WorldToScreenPoint(head.position + Vector3.up * .7f);
            float width = 166f * scale;
            Rect label = new Rect(Mathf.Clamp(labelAnchor.x - width / 2f, 4f, Screen.width - width - 4f),
                Mathf.Clamp(Screen.height - labelAnchor.y - 25f * scale - labelRow * 29f * scale, 4f, Screen.height - 32f * scale),
                width, 29f * scale);
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.font = skillFont;
            style.fontSize = Mathf.RoundToInt(20f * scale);
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            style.normal.textColor = Color.white;
            GUI.color = new Color(1f, 1f, 1f, color.a * .58f);
            foreach (Vector2 offset in new Vector2[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
                GUI.Label(new Rect(label.x + offset.x * scale, label.y + offset.y * scale, label.width, label.height), names[index], style);
            GUI.color = color;
            GUI.Label(label, names[index], style);
            labelRow++;
        }
        GUI.color = savedColor;
        GUI.matrix = savedMatrix;
    }

    private static void Line(Vector2 from, Vector2 to, float thickness)
    {
        Matrix4x4 saved = GUI.matrix;
        Vector2 delta = to - from;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
        GUI.DrawTexture(new Rect(from.x, from.y - thickness / 2f, delta.magnitude, thickness), Texture2D.whiteTexture);
        GUI.matrix = saved;
    }

    private void DrawBallHighlight(Camera camera, float scale)
    {
        if (haloTexture == null)
        {
            haloTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f;
                    pixels[y * 64 + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - radius), 1.6f));
                }
            haloTexture.SetPixels(pixels);
            haloTexture.Apply();
        }
        // Use the interpolated render transform, so the glow stays on the sprite
        // instead of leading it by a physics tick at high shot speeds.
        Vector3 point = camera.WorldToScreenPoint(ball.transform.position);
        if (point.z <= 0f) return;
        Vector2 center = new Vector2(point.x, Screen.height - point.y);
        float fade = Mathf.Clamp01((glowUntil - Time.time) / .25f);
        GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, .8f * fade);
        float size = (58f + Mathf.Sin(Time.time * 24f) * 5f) * scale;
        GUI.DrawTexture(new Rect(center.x - size / 2f, center.y - size / 2f, size, size), haloTexture);
        for (int i = 1; i < ballTrail.Count; i++)
        {
            Vector3 a = camera.WorldToScreenPoint(ballTrail[i - 1]);
            Vector3 b = camera.WorldToScreenPoint(ballTrail[i]);
            GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, .38f * fade * i / ballTrail.Count);
            Line(new Vector2(a.x, Screen.height - a.y), new Vector2(b.x, Screen.height - b.y), 5f * scale);
        }
        GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, fade);
        for (int i = 0; i < 24; i++)
        {
            float angle = (i * 15f + Time.time * 160f) * Mathf.Deg2Rad;
            float next = angle + 15f * Mathf.Deg2Rad;
            float radius = 18f * scale;
            Line(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                center + new Vector2(Mathf.Cos(next), Mathf.Sin(next)) * radius, 2.5f * scale);
        }
        GUI.color = new Color(1f, 1f, 1f, fade);
        Vector2 glint = center + new Vector2(-8f, -9f) * scale;
        Line(glint - new Vector2(5f, 0f) * scale, glint + new Vector2(5f, 0f) * scale, 2f * scale);
        Line(glint - new Vector2(0f, 5f) * scale, glint + new Vector2(0f, 5f) * scale, 2f * scale);
    }
}

// Ragdoll collisions arrive on each native limb's Rigidbody2D, not its parent.
public sealed class PowerTackleContact : MonoBehaviour
{
    public PlayerSkills owner;
    private void OnCollisionEnter2D(Collision2D collision) { if (owner != null) owner.OnBodyContact(collision); }
    private void OnCollisionStay2D(Collision2D collision) { if (owner != null) owner.OnBodyContact(collision); }
}

public sealed class GameStyleIndicator : MonoBehaviour
{
    private Font font;
    private void OnGUI()
    {
        if (font == null) font = Font.CreateDynamicFontFromOSFont(new string[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.font = font;
        style.fontSize = 16;
        style.normal.textColor = PlayerSkills.Enabled ? new Color(1f, .86f, .38f) : Color.white;
        GUI.Label(new Rect(12f, Screen.height - 32f, 160f, 26f), PlayerSkills.Enabled ? "特技模式" : "经典模式", style);
    }
}

