using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// All effects are overlays. The original meshes, sprites, animation clips and
// collider sizes remain untouched. Physics assistance runs at the physics rate.
public sealed class PlayerSkills : MonoBehaviour
{
    public static bool Enabled;
    public const float ZhaoJumpMultiplier = 1.35f;
    public const float ZhaoInterceptReach = 2.6f;
    public const float FanVolleyCommandWindow = .75f;
    public const float FanDribbleAnimationMultiplier = 1.5f;
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
    private float halfCourtDistance = float.PositiveInfinity;
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
    private Component muscles;
    private readonly FieldInfo[] legFields = new FieldInfo[4];
    private readonly float[] originalLegAngles = new float[4];
    private readonly float[] modifiedLegAngles = new float[4];
    private readonly object[] legMuscles = new object[4];
    private readonly float[] normalLegForce = new float[4];
    private FieldInfo legForceField;
    private bool strongerLegControl;
    private FieldInfo bodyAngleField;
    private float originalBodyAngle;
    private float modifiedBodyAngle;
    private bool adjustedBodyPose;
    private bool adjustedLegPose;
    private float extendedKickUntil = -1f;
    private bool fan;
    private float direction;
    private float headUntil = -1f;
    private float kickUntil = -1f;
    private bool jumpVolleyActive;
    private bool jumpLeftGround;
    private float jumpVolleyStarted = -10f;
    private float curveStarted = -10f;
    private float lastKick = -10f;
    private float lastHead = -10f;
    private float lastSteal = -10f;
    private float lastDribbleFx = -10f;
    private float shotUntil = -1f;
    private readonly float[] effects = { -10f, -10f, -10f, -10f, -10f, -10f };
    private static readonly string[] names = { "旋风盘带", "炮弹式头球", "空中堡垒", "伸脚抢截", "掠地瞬击", "弧线落叶射门" };
    private static readonly Color[] colors = {
        new Color(1f, .76f, .12f), new Color(1f, .36f, .12f),
        new Color(.25f, .83f, 1f), new Color(.4f, 1f, .3f),
        new Color(1f, .93f, .3f), new Color(.87f, .45f, 1f)
    };

    public static void Attach(Component player)
    {
        if (player == null || (player.name != "Fan" && player.name != "Zhao")) return;
        if (player.name == "Fan" && player.GetComponent<GameStyleIndicator>() == null)
            player.gameObject.AddComponent<GameStyleIndicator>();
        if (!Enabled) return;
        PlayerSkills skills = player.GetComponent<PlayerSkills>();
        if (skills != null) return;
        skills = player.gameObject.AddComponent<PlayerSkills>();
        skills.Initialize(player);
    }

    // Hook the actual animation command, after both human and AI input paths.
    public static void SetAction(Animator animator, string action, Component player)
    {
        animator.SetTrigger(action);
        Attach(player);
        PlayerSkills skills = player.GetComponent<PlayerSkills>();
        if (skills != null) skills.BeginAction(action);
    }

    public static float GetJumpForce(float original, Component player)
    {
        return Enabled && player != null && player.name == "Zhao" ? original * ZhaoJumpMultiplier : original;
    }

    public static void ResetAll()
    {
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
        normalAnimatorSpeed = animator != null ? animator.speed : 1f;
        object input = type.GetField("input").GetValue(player);
        horizontalAxis = input != null ? input.GetType().GetField("horizontal").GetValue(input) as string : "Horizontal";
        Type muscleType = type.Assembly.GetType("StickManController");
        muscles = player.GetComponent(muscleType);
        string[] fields = { "L_Up_Leg", "L_Low_Leg", "R_Up_Leg", "R_Low_Leg" };
        for (int i = 0; i < fields.Length; i++) legFields[i] = muscleType.GetField(fields[i]);
        bodyAngleField = muscleType.GetField("body");
        Array muscleArray = muscleType.GetField("muscles").GetValue(muscles) as Array;
        if (muscleArray != null && muscleArray.Length >= 10)
            for (int i = 0; i < legMuscles.Length; i++)
            {
                legMuscles[i] = muscleArray.GetValue(6 + i);
                legForceField = legMuscles[i].GetType().GetField("force");
                normalLegForce[i] = (float)legForceField.GetValue(legMuscles[i]);
            }
        head = player.transform.Find("Head");
        if (head != null) headCollider = head.GetComponent<Collider2D>();
        GameObject opponent = GameObject.Find(fan ? "Zhao" : "Fan");
        if (opponent != null)
        {
            Transform opponentTransform = opponent.transform.Find("Body");
            if (opponentTransform != null) opponentBody = opponentTransform.GetComponent<Rigidbody2D>();
        }
        halfCourtDistance = FindHalfCourtDistance(type.Assembly);
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

    private static float FindHalfCourtDistance(Assembly game)
    {
        Type goalType = game.GetType("GoalTrigger");
        if (goalType == null) return float.PositiveInfinity;
        UnityEngine.Object[] goals = UnityEngine.Object.FindObjectsOfType(goalType);
        Collider2D left = null, right = null;
        foreach (UnityEngine.Object goalObject in goals)
        {
            Component goal = goalObject as Component;
            Collider2D collider = goal != null ? goal.GetComponent<Collider2D>() : null;
            if (collider == null) continue;
            if (left == null || collider.bounds.center.x < left.bounds.center.x) left = collider;
            if (right == null || collider.bounds.center.x > right.bounds.center.x) right = collider;
        }
        // The pitch runs between the two goal mouths (inner trigger edges).
        if (left == null || right == null || left == right) return float.PositiveInfinity;
        float width = right.bounds.min.x - left.bounds.max.x;
        return width > 0f ? width * .5f : float.PositiveInfinity;
    }

    private bool Stopped()
    {
        return !Enabled || Time.timeScale <= 0f || controller == null || body == null ||
            (manager != null && stoppingField != null && (bool)stoppingField.GetValue(manager));
    }

    private void BeginAction(string action)
    {
        if (Stopped()) return;
        float now = Time.time;
        if (action == "Kick" || action == "Head" || action == "Jump") RestoreDribbleSpeed();
        if (action == "Kick" && (fan || now - lastKick >= .28f))
        {
            lastKick = now;
            // Every Fan kick command renews the full animation's contact window.
            // Movement direction never gates activation; Zhao keeps his timing.
            kickUntil = now + (fan ? FanVolleyCommandWindow : .32f);
            if (!fan) ExtendKickPose(now);
        }
        if (fan && action == "Head" && now - lastHead >= .28f)
        {
            lastHead = now;
            headUntil = now + .34f;
            Show(1);
            // Keep all player-body collisions and ragdoll constraints intact.
            // Header priority applies to the struck ball, not the player's limbs.
        }
        else if (action == "Jump")
        {
            if (fan)
            {
                jumpVolleyActive = true;
                jumpLeftGround = false;
                jumpVolleyStarted = now;
            }
            else Show(2);
        }
        // An explicit strike must not be weakened by the ground-control assist.
        if (fan && (action == "Kick" || action == "Head"))
            shotUntil = now + (action == "Kick" ? FanVolleyCommandWindow : .5f);
    }

    private void Show(int index) { effects[index] = Time.time; }

    private void Update()
    {
        if (controller != null && Stopped()) ClearRound();
        else UpdateJumpVolley();
    }

    private void FixedUpdate()
    {
        if (Stopped() || ball == null) return;
        UpdateJumpVolley();
        float now = Time.time;
        if (kickUntil >= now)
        {
            if (!fan && GroundBallInReach(1.25f, .3f) && CanGroundBurst())
            {
                float speed = Mathf.Clamp(Mathf.Max(17f, ball.velocity.magnitude * 1.65f), 17f, 23f);
                EnableContinuousBall();
                ball.AddForce((new Vector2(direction * speed, 1.2f) - ball.velocity) * ball.mass, ForceMode2D.Impulse);
                kickUntil = -1f;
                shotUntil = now + .6f;
                Show(4);
                HighlightBall(4, .85f);
                CancelCurves();
            }
        }
        if (fan)
        {
            if (headUntil >= now && HeadReachable())
            {
                float speed = Mathf.Clamp(Mathf.Max(13f, ball.velocity.magnitude * 1.55f), 13f, 19f);
                Vector2 target = new Vector2(direction * speed, Mathf.Clamp(ball.velocity.y + 2.8f, 2.8f, 6f));
                EnableContinuousBall();
                ball.AddForce((target - ball.velocity) * ball.mass, ForceMode2D.Impulse);
                CancelCurves();
                headUntil = -1f; // One successful strike per heading command.
                HighlightBall(1, .85f);
            }
            if (now > shotUntil) Dribble();
        }
        else if (now > shotUntil && now - lastSteal >= .6f && GroundBallInReach(ZhaoInterceptReach, .45f))
        {
            AutoIntercept(now);
        }
        float curveAge = now - curveStarted;
        if (fan && curveAge >= 0f && curveAge < .85f && IsBallAirborne())
        {
            float lateral = direction * Mathf.Sin(curveAge / .85f * Mathf.PI * 2f) * 5f;
            float vertical = curveAge < .32f ? 5.8f : -14.5f;
            ball.AddForce(new Vector2(lateral, vertical) * ball.mass);
        }
        else if (curveAge >= .85f || !IsBallAirborne()) curveStarted = -10f;
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
        if (limb == null || limb.GetComponentInParent<PlayerSkills>() != this) return false;
        string name = limb.name;
        if (name.IndexOf("Low_Leg", StringComparison.OrdinalIgnoreCase) < 0 &&
            name.IndexOf("LowLeg", StringComparison.OrdinalIgnoreCase) < 0 &&
            name.IndexOf("LowerLeg", StringComparison.OrdinalIgnoreCase) < 0) return false;
        // Feet and shins share the original lower-leg collider. Accept the
        // instep and lower shin, but exclude its sole/end face. Use limb-local
        // geometry and the actual contact normal so rotating a kick cannot
        // turn a sole contact into an instep contact. Never enlarge the collider.
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
        return local.y > offset.y - size.y * .46f &&
            local.y <= offset.y + size.y * .2f;
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
        UpdateJumpVolley();
        if (!fan || Stopped() || ball == null || (kickUntil < Time.time && !(jumpVolleyActive && jumpLeftGround)) ||
            !IsBallAirborne() || !IsFootContact(limb, contact, outwardNormal)) return;
        Vector2 target = new Vector2(direction * Mathf.Clamp(Mathf.Abs(ball.velocity.x) * 1.2f + 8f, 8f, 12f), 8.5f);
        EnableContinuousBall();
        ball.AddForce((target - ball.velocity) * ball.mass, ForceMode2D.Impulse);
        kickUntil = headUntil = -1f;
        jumpVolleyActive = false;
        shotUntil = Time.time + .85f;
        CancelCurves();
        curveStarted = Time.time;
        Show(5);
        HighlightBall(5, 1.1f);
    }

    private void UpdateJumpVolley()
    {
        if (!fan || !jumpVolleyActive || body == null || groundedField == null) return;
        bool grounded = (bool)groundedField.GetValue(controller);
        if (!grounded || body.velocity.y > .4f) jumpLeftGround = true;
        if (Time.time - jumpVolleyStarted > 2.5f ||
            (jumpLeftGround && grounded && body.velocity.y <= .4f && Time.time - jumpVolleyStarted > .08f))
            jumpVolleyActive = false;
    }

    private bool CanGroundBurst()
    {
        return ball != null && ballCollider != null && opponentBody != null &&
            !IsBallAirborne() && Mathf.Abs(ball.position.x - opponentBody.position.x) > halfCourtDistance;
    }

    private static void CancelCurves()
    {
        foreach (PlayerSkills player in players)
            if (player != null) player.curveStarted = -10f;
    }

    // An opponent touch ends flight assistance so the next player can take control.
    public static void OnBallCollision(Collision2D collision)
    {
        Collider2D touched = collision.collider;
        if (touched == null) return;
        PlayerSkills playerAtContact = touched.GetComponentInParent<PlayerSkills>();
        if (playerAtContact != null)
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);
                Vector2 normal = contact.normal;
                // Unity callbacks can report either collider's normal. Orient
                // it outward from the limb toward the ball before filtering.
                if (playerAtContact.ball != null &&
                    Vector2.Dot(normal, playerAtContact.ball.position - contact.point) < 0f)
                    normal = -normal;
                playerAtContact.VolleyOnContact(touched, contact.point, normal);
            }
        foreach (PlayerSkills player in players)
            if (player != null && player.curveStarted > -1f &&
                Time.time - player.curveStarted > .08f &&
                (touched.transform.root != player.transform.root)) player.curveStarted = -10f;
    }

    private bool HeadReachable()
    {
        if (head == null || ball.position.y < -.6f) return false;
        if (headCollider != null)
            return Vector2.Distance(headCollider.ClosestPoint(ball.position), ball.position) <= .48f;
        return Vector2.Distance(head.position, ball.position) <= .95f;
    }

    private bool GroundBallInReach(float reach, float behind)
    {
        if (foot == null || ball.position.y > -.85f) return false;
        float height = ball.position.y - foot.position.y;
        float ahead = direction * (ball.position.x - body.position.x);
        return height >= -.4f && height <= 1.05f && ahead >= -behind && ahead <= reach;
    }

    private void Dribble()
    {
        if (groundedField == null || !(bool)groundedField.GetValue(controller) ||
            !GroundBallInReach(1.3f, .25f) || IsBallAirborne() ||
            Mathf.Abs(GameAIMod.GetAxis(horizontalAxis, controller)) < .1f)
        {
            RestoreDribbleSpeed();
            return;
        }
        if (animator != null && !fasterDribble)
        {
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
    }

    private static void RestoreBallDetection()
    {
        if (changedBallDetection && continuousBall != null) continuousBall.collisionDetectionMode = originalBallDetection;
        continuousBall = null;
        changedBallDetection = false;
    }

    private void ExtendKickPose(float now)
    {
        extendedKickUntil = now + .45f;
        if (GroundBallInReach(ZhaoInterceptReach, .45f)) Show(3);
    }

    private void AutoIntercept(float now)
    {
        // Only animate the leg; taking possession requires its physical collision.
        // An automatic interception does not arm the explicit shooting skill.
        if (animator != null) animator.SetTrigger("Kick");
        ExtendKickPose(now);
        lastSteal = now;
    }

    // Run immediately before the original muscle controller moves its rigidbodies.
    // Increase forward leg swing and straighten the knee through its existing
    // target-angle parameters. Joint anchors, bone lengths and sprites stay intact.
    public static void BeforeMuscles(Component stick)
    {
        PlayerSkills skills = stick.GetComponent<PlayerSkills>();
        if (skills != null) skills.AdjustKickPose();
    }

    private void AdjustKickPose()
    {
        if (fan || Stopped() || Time.time > extendedKickUntil || muscles == null)
        {
            RestoreLegPose();
            return;
        }
        for (int i = 0; i < legFields.Length; i++)
        {
            float value = (float)legFields[i].GetValue(muscles);
            if (!adjustedLegPose || Mathf.Abs(Mathf.DeltaAngle(value, modifiedLegAngles[i])) > .001f)
                originalLegAngles[i] = value;
        }
        if (legForceField != null && !strongerLegControl)
        {
            for (int i = 0; i < legMuscles.Length; i++) legForceField.SetValue(legMuscles[i], normalLegForce[i] * 1.6f);
            strongerLegControl = true;
        }
        bool reaching = false;
        for (int leg = 0; leg < 4; leg += 2)
        {
            float upper = Mathf.DeltaAngle(0f, originalLegAngles[leg]);
            float lower = Mathf.DeltaAngle(0f, originalLegAngles[leg + 1]);
            if (direction * upper > 25f)
            {
                reaching = true;
                upper = direction * Mathf.Min(Mathf.Abs(upper) * 1.65f, 90f);
                lower = Mathf.LerpAngle(lower, upper, .95f);
            }
            modifiedLegAngles[leg] = upper;
            modifiedLegAngles[leg + 1] = lower;
            legFields[leg].SetValue(muscles, upper);
            legFields[leg + 1].SetValue(muscles, lower);
        }
        if (bodyAngleField != null)
        {
            float value = (float)bodyAngleField.GetValue(muscles);
            if (!adjustedBodyPose || Mathf.Abs(Mathf.DeltaAngle(value, modifiedBodyAngle)) > .001f)
                originalBodyAngle = value;
            // A modest native torso rotation brings the hip into the kick.
            // The whole connected skeleton responds through its original joints.
            modifiedBodyAngle = reaching ? Mathf.Clamp(Mathf.DeltaAngle(0f, originalBodyAngle) + direction * 20f, -35f, 35f) : originalBodyAngle;
            bodyAngleField.SetValue(muscles, modifiedBodyAngle);
            adjustedBodyPose = true;
        }
        adjustedLegPose = true;
    }

    private void RestoreLegPose()
    {
        if (adjustedBodyPose && muscles != null && bodyAngleField != null)
        {
            float value = (float)bodyAngleField.GetValue(muscles);
            if (Mathf.Abs(Mathf.DeltaAngle(value, modifiedBodyAngle)) < .001f)
                bodyAngleField.SetValue(muscles, originalBodyAngle);
            adjustedBodyPose = false;
        }
        if (strongerLegControl && legForceField != null)
        {
            for (int i = 0; i < legMuscles.Length; i++) legForceField.SetValue(legMuscles[i], normalLegForce[i]);
            strongerLegControl = false;
        }
        if (!adjustedLegPose || muscles == null) return;
        for (int i = 0; i < legFields.Length; i++)
        {
            float value = (float)legFields[i].GetValue(muscles);
            if (Mathf.Abs(Mathf.DeltaAngle(value, modifiedLegAngles[i])) < .001f)
                legFields[i].SetValue(muscles, originalLegAngles[i]);
        }
        adjustedLegPose = false;
    }

    private void ClearRound()
    {
        RestoreDribbleSpeed();
        RestoreLegPose();
        RestoreBallDetection();
        extendedKickUntil = -1f;
        headUntil = shotUntil = kickUntil = -1f;
        jumpVolleyActive = jumpLeftGround = false;
        jumpVolleyStarted = -10f;
        curveStarted = -10f;
        lastHead = lastSteal = lastDribbleFx = lastKick = -10f;
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
            Vector3 anchor = index == 1 ? head.position : index == 2 || index == 5 ? (Vector3)body.position :
                (foot != null ? foot.position + Vector3.up * .3f : (Vector3)body.position);
            Vector3 projected = camera.WorldToScreenPoint(anchor);
            if (projected.z <= 0f) continue;
            Vector2 center = new Vector2(projected.x, Screen.height - projected.y);
            GUI.color = color;
            float radius = (index == 0 ? 30f : index == 1 ? 23f + progress * 52f : 32f + progress * 20f) * scale;
            float rotation = age * (index == 0 ? 680f : 240f);
            for (int segment = 0; segment < 30; segment++)
            {
                if ((index == 0 || index == 3) && segment % 15 > 10) continue;
                float angle = rotation + segment * 12f;
                if (index == 3) angle = (fan ? 0f : 180f) - 65f + segment * 4.3f;
                Vector2 from = center + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius * (index == 0 ? .42f : 1f));
                float next = angle + (index == 3 ? 4.3f : 12f);
                Vector2 to = center + new Vector2(Mathf.Cos(next * Mathf.Deg2Rad) * radius,
                    Mathf.Sin(next * Mathf.Deg2Rad) * radius * (index == 0 ? .42f : 1f));
                Line(from, to, 3f * scale);
            }
            if (index == 1)
                for (int ray = -1; ray <= 1; ray++)
                    Line(center + new Vector2(direction * 15f, ray * 9f) * scale,
                        center + new Vector2(direction * (45f + progress * 65f), ray * 17f) * scale, 3f * scale);
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
