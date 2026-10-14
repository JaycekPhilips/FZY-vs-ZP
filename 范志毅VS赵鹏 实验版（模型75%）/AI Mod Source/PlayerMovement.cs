using System;
using System.Collections.Generic;
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
    private Rigidbody2D[] runningLimbs;private Collider2D[] runningShapes;private readonly ContactPoint2D[] runningContacts=new ContactPoint2D[20];
    private float runningMass,nativeLimit,previousInput,assistAt=-10f;
    private readonly FieldInfo[] gaitFields=new FieldInfo[4];private readonly float[] gaitAngles=new float[4],gaitVelocity=new float[4];private bool gaitPrimed;
    private FieldInfo groundField, bodyTargetField;
    private readonly List<AnimatorClipInfo> clips = new List<AnimatorClipInfo>(4);
    private List<AnimatorClipInfo> CurrentClips() { animator.GetCurrentAnimatorClipInfo(0, clips); return clips; }

    public static void Attach(Component player)
    {
        if (player == null || (player.name != "Fan" && player.name != "Zhao") || player.GetComponent<PlayerMovement>() != null) return;
        SkillAnimationToggle.Attach(player);
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
        movement.groundField = player.GetType().GetField("isOnGround");
        movement.bodyTargetField = muscleType.GetField("body");
        string[] gaitNames={"L_Up_Leg","L_Low_Leg","R_Up_Leg","R_Low_Leg"};for(int i=0;i<4;i++)movement.gaitFields[i]=muscleType.GetField(gaitNames[i]);
        movement.body = player.GetType().GetField("rb").GetValue(player) as Rigidbody2D;
        movement.runningLimbs=player.GetComponentsInChildren<Rigidbody2D>();movement.runningShapes=player.GetComponentsInChildren<Collider2D>();foreach(var limb in movement.runningLimbs)movement.runningMass+=limb.mass;
        movement.nativeLimit=(float)player.GetType().GetField("maxVelocity").GetValue(player);
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
        return baseMultiplier * PlayerSkills.GetMovementSkillMultiplier(player, input) * MagneticFoot.MovementMultiplier(player, input) * AbilityMode.Movement(player, input);
    }

    public static float GetMovementForce(float original, Component player)
    {
        PlayerMovement movement=player!=null?player.GetComponent<PlayerMovement>():null;
        float input=movement!=null?GameAIMod.GetAxis(movement.axis,player):0f;
        return original * GetMultiplier(player) * MagneticFoot.MovementForceMultiplier(player,input) * AbilityMode.ContactForce(player,input);
    }
    public static float GetMovementLimit(float original,Component player){
        // Native inertia controls acceleration. Never hold back the input or
        // wait for a walk clip before allowing the requested running speed.
        return original * GetMultiplier(player);
    }

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

    public static void BeforeMuscles(Component stick){
        PlayerMovement m=stick!=null?stick.GetComponent<PlayerMovement>():null;if(m==null||m.animator==null)return;
        bool walking=false;foreach(var info in m.CurrentClips())if(info.clip!=null&&(info.clip.name=="Walk"||info.clip.name=="BackWalk"))walking=true;
        bool smooth=walking&&!m.Stopped()&&Time.time>=m.actionProtectionUntil&&(bool)m.groundField.GetValue(m.controller);
        // Preserve native leg strength. Ease animation targets rather than
        // turning the legs into weak springs which trail behind the torso.
        float cadence=Mathf.Max(1f,m.animator.speed/Mathf.Max(.01f,m.originalAnimatorSpeed));
        for(int i=0;i<4;i++){if(m.gaitFields[i]==null)continue;float target=(float)m.gaitFields[i].GetValue(stick);if(!m.gaitPrimed||!smooth){m.gaitAngles[i]=target;m.gaitVelocity[i]=0;}else{m.gaitAngles[i]=Mathf.SmoothDampAngle(m.gaitAngles[i],target,ref m.gaitVelocity[i],.028f/cadence,1800f*cadence,Time.deltaTime);m.gaitFields[i].SetValue(stick,m.gaitAngles[i]);}}
        m.gaitPrimed=true;
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
        float sprint = input * (fan ? 1f : -1f) > .1f ? PlayerSkills.GetMovementSkillMultiplier(controller, input) * AbilityMode.Movement(controller, input) : 1f;
        if (fan && sprint <= 1f) { RestoreCadence(); return; }
        List<AnimatorClipInfo> clips = CurrentClips();
        foreach (AnimatorClipInfo info in clips)
        {
            AnimationClip clip = info.clip;
            if (clip == null || (clip.name != "Walk" && clip.name != "BackWalk")) continue;
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            float stateSpeed = Mathf.Abs(state.speed * state.speedMultiplier);
            if (stateSpeed < .01f || clip.length <= 0f) break;
            float targetAnimatorSpeed = fan ? originalAnimatorSpeed * sprint :
                Mathf.Clamp(clip.length * referenceAnimatorSpeed / (referenceWalkLength * stateSpeed), .25f, 4f) * Mathf.Max(1f, sprint);
            adjustedAnimatorSpeed=Mathf.MoveTowards(adjustedCadence?adjustedAnimatorSpeed:animator.speed,targetAnimatorSpeed,16f*Time.deltaTime);
            animator.speed = adjustedAnimatorSpeed;
            adjustedCadence = true;
            return;
        }
        RestoreCadence();
    }

    private void AssistStartup(){
        if(Stopped()||body==null){previousInput=0;return;}
        float input=GameAIMod.GetAxis(axis,controller);if(Mathf.Abs(input)<=.1f){previousInput=0;return;}
        if(Mathf.Abs(previousInput)<.1f||Mathf.Sign(previousInput)!=Mathf.Sign(input))assistAt=Time.time;previousInput=input;
        if(Time.time-assistAt>.16f||Time.time<actionProtectionUntil||!(bool)groundField.GetValue(controller)||PlayerSkills.RescueActive(controller)||BuildPlayer.Rescuing(controller))return;
        // Contact contests keep native thrust. Starting support only acts on
        // an unobstructed grounded rig, with the same acceleration on every limb.
        foreach(var shape in runningShapes){int count=shape.GetContacts(runningContacts);for(int i=0;i<count;i++){var other=runningContacts[i].collider==shape?runningContacts[i].otherCollider:runningContacts[i].collider;if(other==null||other.transform.IsChildOf(transform))continue;string owner=other.transform.root.name;if(owner=="Fan"||owner=="Zhao"||other.GetComponentInParent<BallBoundaryGuard>()!=null)return;}}
        float speed=0;foreach(var limb in runningLimbs)speed+=limb.velocity.x*limb.mass;speed/=Mathf.Max(.001f,runningMass);
        float scale=Mathf.Max(.1f,Mathf.Abs(transform.lossyScale.x)/.8f),direction=Mathf.Sign(input);
        float target=Mathf.Min(nativeLimit*GetMultiplier(controller),(.9f+16f*(Time.time-assistAt))*scale);
        float change=direction*Mathf.Clamp(target-direction*speed,0f,40f*scale*Time.fixedDeltaTime);
        foreach(var limb in runningLimbs)limb.AddForce(Vector2.right*change*limb.mass,ForceMode2D.Impulse);
    }

    private void FixedUpdate()
    {
        AssistStartup();
        if (fan || !PlayerSkills.Enabled || Stopped() || body == null || stick == null || Time.time < actionProtectionUntil ||
            GameAIMod.GetAxis(axis, controller) <= .1f || !(bool)groundField.GetValue(controller)) return;
        bool walking = false;
        foreach (AnimatorClipInfo clip in CurrentClips()) if (clip.clip != null && clip.clip.name == "BackWalk") walking = true;
        if (!walking) return;
        // Dampen body roll through real torque, preserving all native joints,
        // animation targets, movement speed and unrestricted jump/shot motions.
        float target = (float)bodyTargetField.GetValue(stick);
        float error = Mathf.DeltaAngle(body.rotation, target) * Mathf.Deg2Rad;
        float torque = (error * 90f - body.angularVelocity * Mathf.Deg2Rad * 18f) * body.inertia;
        body.AddTorque(Mathf.Clamp(torque, -body.inertia * 140f, body.inertia * 140f), ForceMode2D.Force);
    }

    // Ability physics runs after animation updates and must preserve the current running cadence.
    public static float AnimationSpeed(Component player,float native){PlayerMovement m=player!=null?player.GetComponent<PlayerMovement>():null;return m!=null&&m.adjustedCadence?Mathf.Max(native,m.adjustedAnimatorSpeed):native;}

    private void RestoreCadence()
    {
        if (adjustedCadence && animator != null && Mathf.Abs(animator.speed - adjustedAnimatorSpeed) < .001f)
            animator.speed = originalAnimatorSpeed;
        adjustedCadence = false;
    }
    private void OnDisable() { RestoreCadence(); RestoreMuscles(); }
    private void OnDestroy() { RestoreCadence(); RestoreMuscles(); }
}
