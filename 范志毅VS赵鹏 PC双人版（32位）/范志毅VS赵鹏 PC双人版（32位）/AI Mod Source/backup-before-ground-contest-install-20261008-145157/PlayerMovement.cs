using System;
using System.Reflection;
using UnityEngine;

// Base movement balance applies to both modes. Skill boosts use the same
// native force and speed-cap parameters, never repositioning the skeleton.
public sealed class PlayerMovement : MonoBehaviour
{
    public const float BackwardMultiplier = 1.5f;
    public const float FanRetreatReduction = .65f;
    public const float ZhaoForwardMultiplier = .9f;
    private Component controller;
    private Animator animator;
    private string axis;
    private bool fan;
    private float originalAnimatorSpeed;
    private bool adjustedCadence;
    private float adjustedAnimatorSpeed;
    private Component manager;
    private FieldInfo stoppingField;
    private float actionProtectionUntil = -1f;
    private float referenceWalkLength = 1f / 3f;
    private float referenceAnimatorSpeed = 1f;
    private readonly object[] walkingMuscles = new object[4];
    private readonly float[] originalMuscleForce = new float[4];
    private FieldInfo muscleForceField;
    private bool smoothedMuscles;
    private Rigidbody2D body;
    private Component stick;

    public static void Attach(Component player)
    {
        if (player == null || (player.name != "Fan" && player.name != "Zhao") || player.GetComponent<PlayerMovement>() != null) return;
        // Faster native movement needs more constraint solving, so limbs stay
        // joined under a retreat impulse without changing joints or geometry.
        Physics2D.positionIterations = Mathf.Max(Physics2D.positionIterations, 64);
        Physics2D.velocityIterations = Mathf.Max(Physics2D.velocityIterations, 24);
        Physics2D.baumgarteScale = Mathf.Max(Physics2D.baumgarteScale, .35f);
        Physics2D.maxLinearCorrection = Mathf.Max(Physics2D.maxLinearCorrection, .4f);
        PlayerMovement movement = player.gameObject.AddComponent<PlayerMovement>();
        movement.controller = player;
        movement.fan = player.name == "Fan";
        movement.animator = player.GetType().GetField("anim").GetValue(player) as Animator;
        movement.originalAnimatorSpeed = movement.animator != null ? movement.animator.speed : 1f;
        object input = player.GetType().GetField("input").GetValue(player);
        movement.axis = input != null ? input.GetType().GetField("horizontal").GetValue(input) as string : "Horizontal";
        Type managerType = player.GetType().Assembly.GetType("GameManager");
        movement.manager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
        movement.stoppingField = managerType.GetField("isStopping");
        Type muscleType = player.GetType().Assembly.GetType("StickManController");
        Component muscles = player.GetComponent(muscleType);
        movement.stick = muscles;
        movement.body = player.GetType().GetField("rb").GetValue(player) as Rigidbody2D;
        Array muscleArray = muscleType.GetField("muscles").GetValue(muscles) as Array;
        if (muscleArray != null && muscleArray.Length >= 10)
            for (int i = 0; i < 4; i++)
            {
                movement.walkingMuscles[i] = muscleArray.GetValue(6 + i);
                movement.muscleForceField = movement.walkingMuscles[i].GetType().GetField("force");
                movement.originalMuscleForce[i] = (float)movement.muscleForceField.GetValue(movement.walkingMuscles[i]);
            }
        GameObject reference = GameObject.Find("Fan");
        Animator referenceAnimator = reference != null ? reference.GetComponent<Animator>() : null;
        PlayerMovement referenceMovement = reference != null ? reference.GetComponent<PlayerMovement>() : null;
        if (referenceMovement != null) movement.referenceAnimatorSpeed = referenceMovement.originalAnimatorSpeed;
        if (referenceAnimator != null && referenceAnimator.runtimeAnimatorController != null)
            foreach (AnimationClip clip in referenceAnimator.runtimeAnimatorController.animationClips)
                if (clip.name == "Walk" && clip.length > 0f) { movement.referenceWalkLength = clip.length; break; }
    }

    public static float GetMultiplier(Component player)
    {
        Attach(player);
        PlayerMovement movement = player != null ? player.GetComponent<PlayerMovement>() : null;
        if (movement == null || movement.Stopped()) return 1f;
        float input = GameAIMod.GetAxis(movement.axis, player);
        float facing = movement.fan ? 1f : -1f;
        float baseMultiplier = input * facing < -.1f ? BackwardMultiplier : 1f;
        if (movement.fan && input * facing < -.1f) baseMultiplier *= FanRetreatReduction;
        if (!movement.fan && input * facing > .1f) baseMultiplier *= ZhaoForwardMultiplier;
        return baseMultiplier * PlayerSkills.GetMovementSkillMultiplier(player, input) * MagneticFoot.MovementMultiplier(player, input);
    }

    public static float GetMovementForce(float original, Component player) { return original * GetMultiplier(player); }
    public static float GetMovementLimit(float original, Component player) { return original * GetMultiplier(player); }

    public static void NotifyAction(Component player)
    {
        PlayerMovement movement = player != null ? player.GetComponent<PlayerMovement>() : null;
        if (movement == null) return;
        movement.RestoreCadence();
        movement.RestoreMuscles();
        movement.actionProtectionUntil = Time.time + .12f;
    }

    private bool Stopped()
    {
        return controller == null || Time.timeScale <= 0f ||
            (manager != null && stoppingField != null && (bool)stoppingField.GetValue(manager));
    }

    public static void PrepareDribble(Component player)
    {
        PlayerMovement movement = player != null ? player.GetComponent<PlayerMovement>() : null;
        if (movement != null) movement.RestoreCadence();
    }

    public static void BeforeMuscles(Component stick)
    {
        PlayerMovement movement = stick != null ? stick.GetComponent<PlayerMovement>() : null;
        if (movement == null) return;
        PlayerSkills skills = stick.GetComponent<PlayerSkills>();
        bool smooth = !movement.fan && !movement.Stopped() && movement.animator != null &&
            Time.time >= movement.actionProtectionUntil && GameAIMod.GetAxis(movement.axis, movement.controller) > .1f &&
            (skills == null || skills.CanSmoothWalkingMuscles());
        if (smooth)
        {
            smooth = false;
            foreach (AnimatorClipInfo clip in movement.animator.GetCurrentAnimatorClipInfo(0))
                if (clip.clip != null && clip.clip.name == "BackWalk") { smooth = true; break; }
        }
        if (!smooth || movement.muscleForceField == null) { movement.RestoreMuscles(); return; }
        // The original 1500 gain snaps each leg straight to its new angle.
        // Smooth the doubled backward gait through the existing muscle gain,
        // keeping its cycle frequency and all native joint/geometry data.
        for (int i = 0; i < 4; i++)
            movement.muscleForceField.SetValue(movement.walkingMuscles[i], Mathf.Min(movement.originalMuscleForce[i], PlayerSkills.Enabled ? 35f : 25f));
        movement.smoothedMuscles = true;
    }

    private void RestoreMuscles()
    {
        if (!smoothedMuscles || muscleForceField == null) return;
        for (int i = 0; i < 4; i++)
            if (Mathf.Abs((float)muscleForceField.GetValue(walkingMuscles[i]) - Mathf.Min(originalMuscleForce[i], 25f)) < .001f ||
                Mathf.Abs((float)muscleForceField.GetValue(walkingMuscles[i]) - Mathf.Min(originalMuscleForce[i], 35f)) < .001f)
                muscleForceField.SetValue(walkingMuscles[i], originalMuscleForce[i]);
        smoothedMuscles = false;
    }

    private void Update()
    {
        if (Stopped()) { RestoreCadence(); RestoreMuscles(); return; }
        float input = GameAIMod.GetAxis(axis, controller);
        PlayerSkills.NotifyMovement(controller, input);
        if (animator == null || Mathf.Abs(input) <= .1f || Time.time < actionProtectionUntil) { RestoreCadence(); return; }
        float sprint = fan ? PlayerSkills.GetMovementSkillMultiplier(controller, input) : 1f;
        if (fan && sprint <= 1f) { RestoreCadence(); return; }
        AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
        foreach (AnimatorClipInfo info in clips)
        {
            AnimationClip clip = info.clip;
            if (clip == null || (clip.name != "Walk" && clip.name != "BackWalk")) continue;
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            float stateSpeed = Mathf.Abs(state.speed * state.speedMultiplier);
            if (stateSpeed < .01f || clip.length <= 0f) break;
            adjustedAnimatorSpeed = fan ? originalAnimatorSpeed * sprint :
                Mathf.Clamp(clip.length * referenceAnimatorSpeed / (referenceWalkLength * stateSpeed), .25f, 4f);
            animator.speed = adjustedAnimatorSpeed;
            adjustedCadence = true;
            return;
        }
        RestoreCadence();
    }

    private void FixedUpdate()
    {
        if (fan || !PlayerSkills.Enabled || Stopped() || body == null || stick == null || Time.time < actionProtectionUntil ||
            GameAIMod.GetAxis(axis, controller) <= .1f || !(bool)controller.GetType().GetField("isOnGround").GetValue(controller)) return;
        bool walking = false;
        foreach (AnimatorClipInfo clip in animator.GetCurrentAnimatorClipInfo(0)) if (clip.clip != null && clip.clip.name == "BackWalk") walking = true;
        if (!walking) return;
        // Dampen body roll through real torque, preserving all native joints,
        // animation targets, movement speed and unrestricted jump/shot motions.
        float target = (float)stick.GetType().GetField("body").GetValue(stick);
        float error = Mathf.DeltaAngle(body.rotation, target) * Mathf.Deg2Rad;
        float torque = (error * 90f - body.angularVelocity * Mathf.Deg2Rad * 18f) * body.inertia;
        body.AddTorque(Mathf.Clamp(torque, -body.inertia * 140f, body.inertia * 140f), ForceMode2D.Force);
    }

    private void RestoreCadence()
    {
        if (adjustedCadence && animator != null && Mathf.Abs(animator.speed - adjustedAnimatorSpeed) < .001f)
            animator.speed = originalAnimatorSpeed;
        adjustedCadence = false;
    }
    private void OnDisable() { RestoreCadence(); RestoreMuscles(); }
    private void OnDestroy() { RestoreCadence(); RestoreMuscles(); }
}
