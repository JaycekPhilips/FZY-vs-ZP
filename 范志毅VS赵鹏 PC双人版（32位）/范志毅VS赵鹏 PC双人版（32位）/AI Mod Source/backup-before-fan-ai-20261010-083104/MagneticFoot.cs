using System;
using System.Reflection;
using UnityEngine;

// A two-link leg controller, not a ball controller. All ball motion comes
// from the existing colliders and physics solver. No forces, teleports,
// velocity assignments, extra colliders or ball constraints are used here.
public sealed class MagneticFoot : MonoBehaviour
{
    public const float GroundPressureForceMultiplier = 1.45f;
    private Component controller, stick, manager;
    private Animator animator;
    private Rigidbody2D body, ball;
    private Collider2D ballCollider;
    private FieldInfo stopping;
    private Leg[] legs;
    private float protectedUntil = -1f, lastEffect = -10f;
    public bool Active { get; private set; }
    private sealed class Leg
    {
        public Rigidbody2D upper, lower;
        public HingeJoint2D hip, knee;
        public BoxCollider2D collider;
        public FieldInfo upperTarget, lowerTarget, force;
        public object upperMuscle, lowerMuscle;
        public float upperGain, lowerGain;
        public Vector2 instep;
        public float desiredUpper, desiredLower;
    }

    public static void Attach(Component player)
    {
        if (!PlayerSkills.Enabled || player == null || player.name != "Zhao" || player.GetComponent<MagneticFoot>() != null) return;
        MagneticFoot skill = player.gameObject.AddComponent<MagneticFoot>();
        skill.controller = player;
        Type pt = player.GetType(), st = pt.Assembly.GetType("StickManController");
        skill.stick = player.GetComponent(st);
        skill.body = pt.GetField("rb").GetValue(player) as Rigidbody2D;
        skill.animator = pt.GetField("anim").GetValue(player) as Animator;
        Type mt = pt.Assembly.GetType("GameManager");
        skill.manager = UnityEngine.Object.FindObjectOfType(mt) as Component;
        skill.stopping = mt.GetField("isStopping");
        Component football = UnityEngine.Object.FindObjectOfType(pt.Assembly.GetType("Ball")) as Component;
        if (football != null) { skill.ball = football.GetComponent<Rigidbody2D>(); skill.ballCollider = football.GetComponent<Collider2D>(); }
        Array muscles = st.GetField("muscles").GetValue(skill.stick) as Array;
        skill.legs = new Leg[2];
        for (int i = 0; i < 2; i++)
        {
            string side = i == 0 ? "L" : "R";
            Leg leg = new Leg();
            leg.upper = player.transform.Find(side + "_Up_Leg").GetComponent<Rigidbody2D>();
            leg.lower = player.transform.Find(side + "_LowLeg").GetComponent<Rigidbody2D>();
            leg.hip = leg.upper.GetComponent<HingeJoint2D>(); leg.knee = leg.lower.GetComponent<HingeJoint2D>();
            leg.collider = leg.lower.GetComponent<BoxCollider2D>();
            leg.upperTarget = st.GetField(side + "_Up_Leg"); leg.lowerTarget = st.GetField(side + "_Low_Leg");
            leg.upperMuscle = muscles.GetValue(6 + i * 2); leg.lowerMuscle = muscles.GetValue(7 + i * 2);
            leg.force = leg.upperMuscle.GetType().GetField("force");
            leg.upperGain = (float)leg.force.GetValue(leg.upperMuscle); leg.lowerGain = (float)leg.force.GetValue(leg.lowerMuscle);
            // Front-facing lower-shin/instep face. TransformVector handles
            // both the native orientation and the 75% experimental scale.
            float sideX = Vector2.Dot(leg.lower.transform.TransformVector(Vector2.right),Vector2.left) > 0f ? 1f : -1f;
            leg.instep = leg.collider.offset + new Vector2(sideX * leg.collider.size.x * .5f, -leg.collider.size.y * .30f);
            skill.legs[i] = leg;
        }
    }

    public static void OnAction(Component player, string action)
    {
        MagneticFoot skill = player != null ? player.GetComponent<MagneticFoot>() : null;
        if (skill == null) return;
        skill.Release();
        skill.protectedUntil = Time.time + (action == "Kick" ? .75f : action == "Head" ? .55f : .35f);
    }
    public static void ResetAll()
    {
        foreach (MagneticFoot skill in UnityEngine.Object.FindObjectsOfType<MagneticFoot>())
        { skill.Release(); skill.protectedUntil = -1f; skill.lastEffect = -10f; }
    }
    public static float MovementMultiplier(Component player, float input)
    {
        MagneticFoot skill = player != null ? player.GetComponent<MagneticFoot>() : null;
        if (skill == null || !skill.Active || input >= -.1f || !skill.Eligible()) return 1f;
        // Match Fan's ordinary forward speed while magnetic foot owns the
        // feet. Its short control pose remains independent of the speed cap.
        return 1f / PlayerMovement.ZhaoForwardMultiplier;
    }
    public static Vector3 VisualAnchor(Component player)
    {
        MagneticFoot skill=player!=null?player.GetComponent<MagneticFoot>():null;
        return skill!=null&&skill.legs!=null ? skill.legs[0].lower.transform.TransformPoint(skill.legs[0].instep) : player.transform.position;
    }
    public static float MovementForceMultiplier(Component player, float input)
    {
        MagneticFoot skill = player != null ? player.GetComponent<MagneticFoot>() : null;
        if (skill == null || !skill.Active || input >= -.1f || !skill.Eligible() || !PlayerSkills.IsGroundBallContest(player)) return 1f;
        // Preserve the existing contest pushing force independently of speed.
        // This changes only the player's native movement force, never the ball.
        return GroundPressureForceMultiplier / MovementMultiplier(player, input);
    }
    private bool Eligible()
    {
        if (!PlayerSkills.Enabled || controller == null || ball == null || ballCollider == null || legs == null || Time.timeScale <= 0f || Time.time < protectedUntil ||
            (manager != null && (bool)stopping.GetValue(manager))) return false;
        float ahead = body.position.x - ball.position.x;
        if (ahead < 0f || ahead > 1.25f * Mathf.Abs(transform.lossyScale.x) / .8f) return false;
        // Only balls at foot height and manageable relative speed. A distant
        // incoming shot cannot be caught or slowed without a real collision.
        Vector2 relative = ball.velocity - body.velocity;
        if (relative.magnitude > 7f || ball.position.y > body.position.y - .65f * Mathf.Abs(transform.lossyScale.y) / .8f) return false;
        foreach (AnimatorClipInfo info in animator.GetCurrentAnimatorClipInfo(0))
            if (info.clip != null && info.weight > .25f && (info.clip.name == "Kick" || info.clip.name == "Head" || info.clip.name == "Jump")) return false;
        foreach (Leg leg in legs)
        {
            Vector2 hip = leg.hip.connectedBody.transform.TransformPoint(leg.hip.connectedAnchor);
            float length = Link(leg.upper,leg.knee.connectedAnchor-leg.hip.anchor).magnitude + Link(leg.lower,leg.instep-leg.knee.anchor).magnitude;
            if (Vector2.Distance(hip,ball.position+Vector2.right*ballCollider.bounds.extents.x) < length * 1.045f &&
                Vector2.Distance(leg.collider.ClosestPoint(ball.position),ball.position) <= ballCollider.bounds.extents.x + .20f * Mathf.Abs(transform.lossyScale.x) / .8f) return true;
        }
        return false;
    }
    private static Vector2 Link(Rigidbody2D limb, Vector2 local)
    {
        return Vector2.Scale(local,new Vector2(limb.transform.lossyScale.x,limb.transform.lossyScale.y));
    }
    private static float Angle(Vector2 vector) { return Mathf.Atan2(vector.y,vector.x)*Mathf.Rad2Deg; }
    private void Pose(Leg leg, Vector2 target)
    {
        Vector2 hip = leg.hip.connectedBody.transform.TransformPoint(leg.hip.connectedAnchor);
        Vector2 upperLink = Link(leg.upper,leg.knee.connectedAnchor-leg.hip.anchor);
        Vector2 lowerLink = Link(leg.lower,leg.instep-leg.knee.anchor);
        float a = upperLink.magnitude,b = lowerLink.magnitude;
        Vector2 delta=target-hip;
        float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.02f,a+b-.006f);
        float line=Angle(delta);
        float bend=Mathf.Acos(Mathf.Clamp((a*a+distance*distance-b*b)/(2f*a*distance),-1f,1f))*Mathf.Rad2Deg;
        // Keep the knee ahead and the shin close to upright. The boot stays
        // on the rear side of the ball instead of swinging over its top.
        float upper=line-bend-Angle(upperLink);
        Vector2 knee=hip+new Vector2(Mathf.Cos((upper+Angle(upperLink))*Mathf.Deg2Rad),Mathf.Sin((upper+Angle(upperLink))*Mathf.Deg2Rad))*a;
        float lower=Angle(hip+delta.normalized*distance-knee)-Angle(lowerLink);
        upper=Mathf.Clamp(Mathf.DeltaAngle(0f,upper),-48f,48f);
        lower=Mathf.Clamp(Mathf.DeltaAngle(0f,lower),-18f,22f);
        SetAngles(leg,upper,lower);
    }
    private void SetAngles(Leg leg,float upper,float lower)
    {
        leg.desiredUpper=upper;leg.desiredLower=lower;
        // Bounded targets and finite muscle gains preserve the connected rig.
        bool support=leg==legs[1];
        leg.upperTarget.SetValue(stick,support?upper:Mathf.MoveTowardsAngle(leg.upper.rotation,upper,1000f*Time.fixedDeltaTime));
        leg.lowerTarget.SetValue(stick,support?lower:Mathf.MoveTowardsAngle(leg.lower.rotation,lower,1000f*Time.fixedDeltaTime));
        float gain=PlayerSkills.IsGroundBallContest(controller)?180f:120f;
        leg.force.SetValue(leg.upperMuscle,support?leg.upperGain:Mathf.Min(leg.upperGain,gain));
        leg.force.SetValue(leg.lowerMuscle,support?leg.lowerGain:Mathf.Min(leg.lowerGain,gain));
    }
    public static void BeforeMuscles(Component stick)
    {
        MagneticFoot skill=stick != null ? stick.GetComponent<MagneticFoot>() : null;
        if(skill==null)return;
        if(!skill.Eligible()){skill.Release();return;}
        skill.Active=true;
        float radius=skill.ballCollider.bounds.extents.x;
        // Follow a short prediction with the physical instep on the rear side
        // of the ball. Closing balls are cushioned by moving the foot back;
        // outgoing balls are pushed through the same real contact.
        Vector2 target=skill.ball.position+skill.ball.velocity*.025f+new Vector2(radius*.94f+.012f,radius*.30f);
        if (PlayerSkills.IsGroundBallContest(skill.controller)) target.x -= .025f * Mathf.Abs(skill.transform.lossyScale.x) / .8f;
        skill.Pose(skill.legs[0],target);
        // The support foot also stays behind the ball instead of executing a
        // full native swing across its top. It remains lower and farther back.
        // An upright support leg carries the torso. Do not solve both legs
        // toward the football: that would pull the hips down into a crouch.
        skill.SetAngles(skill.legs[1],Mathf.Sin(Time.time*10f)*5f,0f);
        if(Time.time-skill.lastEffect>.55f){PlayerSkills.ShowMagneticFoot(skill.controller);skill.lastEffect=Time.time;}
    }
    private void Release()
    {
        if(Active && legs != null)foreach(Leg leg in legs)
        {
            float upperForce=(float)leg.force.GetValue(leg.upperMuscle),lowerForce=(float)leg.force.GetValue(leg.lowerMuscle);
            if(Mathf.Abs(upperForce-Mathf.Min(leg.upperGain,120f))<.001f || Mathf.Abs(upperForce-Mathf.Min(leg.upperGain,180f))<.001f)leg.force.SetValue(leg.upperMuscle,leg.upperGain);
            if(Mathf.Abs(lowerForce-Mathf.Min(leg.lowerGain,120f))<.001f || Mathf.Abs(lowerForce-Mathf.Min(leg.lowerGain,180f))<.001f)leg.force.SetValue(leg.lowerMuscle,leg.lowerGain);
        }
        Active=false;
    }
    void Update(){if(!Eligible())Release();}
    void FixedUpdate()
    {
        if(!Eligible()){Release();return;}
        BeforeMuscles(stick);
        foreach(Leg leg in legs)
        {
            bool support=leg==legs[1];
            leg.upper.MoveRotation(support?leg.desiredUpper:Mathf.MoveTowardsAngle(leg.upper.rotation,leg.desiredUpper,1000f*Time.fixedDeltaTime));
            leg.lower.MoveRotation(support?leg.desiredLower:Mathf.MoveTowardsAngle(leg.lower.rotation,leg.desiredLower,1000f*Time.fixedDeltaTime));
        }
        float target=(float)stick.GetType().GetField("body").GetValue(stick);
        float error=Mathf.DeltaAngle(body.rotation,target)*Mathf.Deg2Rad;
        // Movement force is unchanged in the small-model edition, while the
        // torso inertia scales with size squared. Keep the same balance effort.
        float rigScale=Mathf.Abs(transform.lossyScale.y)/.8f;
        float balanceInertia=body.inertia/Mathf.Max(.25f,rigScale*rigScale);
        if (PlayerSkills.IsGroundBallContest(controller)) balanceInertia *= 1.3f;
        float torque=(error*240f-body.angularVelocity*Mathf.Deg2Rad*32f)*balanceInertia;
        body.AddTorque(Mathf.Clamp(torque,-balanceInertia*500f,balanceInertia*500f),ForceMode2D.Force);
    }
    void OnDisable(){Release();}
    void OnDestroy(){Release();}
}
