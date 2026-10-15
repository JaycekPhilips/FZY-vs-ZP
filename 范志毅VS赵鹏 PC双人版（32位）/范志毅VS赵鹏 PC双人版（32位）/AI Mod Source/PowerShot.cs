using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Ordinary powered shooting exists in classic mode too. It uses the native
// Kick motion and can launch only from a real, eligible foot-ball collision.
public sealed class PowerShot : MonoBehaviour
{
    public static float ShootingSkillRange(Component player){
        if(player==null)return float.PositiveInfinity;Type type=player.GetType().Assembly.GetType("GoalTrigger");if(type==null)return float.PositiveInfinity;float left=float.PositiveInfinity,right=float.NegativeInfinity;
        foreach(UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(type)){var goal=(item as Component).GetComponent<Collider2D>();if(goal==null)continue;float line=goal.transform.position.x<0?goal.bounds.max.x:goal.bounds.min.x;left=Mathf.Min(left,line);right=Mathf.Max(right,line);}return right>left?(right-left)*.125f:float.PositiveInfinity;
    }
    public static bool ShootingSkillSpace(Component player){
        if(player==null)return false;PowerShot ours=player.GetComponent<PowerShot>(),other=null;foreach(PowerShot p in players)if(p!=null&&p!=ours&&(p.name=="Fan"||p.name=="Zhao")){other=p;break;}
        if(ours==null||other==null||ours.body==null||other.body==null||Mathf.Abs(ours.body.position.x-other.body.position.x)+.0001f<ShootingSkillRange(player))return false;
        BuildPlayer build=player.GetComponent<BuildPlayer>();if(AbilityMode.Enabled&&build!=null&&build.InContest)return false;
        bool ourBall=false,theirBall=false;foreach(var a in ours.limbShapes){if(a==null||!a.enabled||a.isTrigger)continue;ourBall|=ours.ballShape!=null&&a.IsTouching(ours.ballShape);foreach(var b in other.limbShapes){if(b!=null&&b.enabled&&!b.isTrigger&&a.IsTouching(b))return false;}}
        foreach(var b in other.limbShapes)if(b!=null&&b.enabled&&!b.isTrigger)theirBall|=ours.ballShape!=null&&b.IsTouching(ours.ballShape);return !(ourBall&&theirBall);
    }
    public const float HeaderSpeedMultiplier = .9f;
    private float headerUntil = -1f;
    public const float SpeedMultiplier = 1.2f;
    public const float SkillBurstMultiplier = 1.1f;
    private Component controller, manager;
    private Rigidbody2D body, ball;
    private Collider2D headShape; private float headReferenceAngle;
    private Collider2D ballShape;
    private FieldInfo stopping;
    private readonly PhysicsHitBuffer rayHits = new PhysicsHitBuffer();
    private static readonly List<PowerShot> players = new List<PowerShot>(2);
    private Rigidbody2D[] limbs;private Collider2D[] limbShapes;
    private Rigidbody2D opponentBody;
    private void Awake() { players.Add(this); players.Sort(ComparePlayers); limbs = GetComponentsInChildren<Rigidbody2D>(true);limbShapes=GetComponentsInChildren<Collider2D>(true); }
    private static int ComparePlayers(PowerShot a, PowerShot b) { return a.GetInstanceID().CompareTo(b.GetInstanceID()); }
    private void OnDestroy() { players.Remove(this); }
    private void OnTransformChildrenChanged() { limbs = GetComponentsInChildren<Rigidbody2D>(true);limbShapes=GetComponentsInChildren<Collider2D>(true); }
    private int groundMask;
    private bool fan;
    private int requestedFrame = -1, commandFrame = -1;
    private float armedUntil = -1f, flatUntil = -1f, floorHeight;private bool bicycleFlat;private float flatClearX,flatDirection;
    public static void Attach(Component player)
    {
        if (player == null || (player.name != "Fan" && player.name != "Zhao") || player.GetComponent<PowerShot>() != null) return;
        PowerShot shot = player.gameObject.AddComponent<PowerShot>();
        shot.controller = player; shot.fan = player.name == "Fan";
        shot.body = player.GetType().GetField("rb").GetValue(player) as Rigidbody2D;
        Transform head=player.transform.Find("Head");shot.headShape=head.GetComponent<Collider2D>();shot.headReferenceAngle=head.eulerAngles.z;
        shot.groundMask = (int)(LayerMask)player.GetType().GetField("ground").GetValue(player);
        Component nativeBall = UnityEngine.Object.FindObjectOfType(player.GetType().Assembly.GetType("Ball")) as Component;
        if (nativeBall != null) { shot.ball = nativeBall.GetComponent<Rigidbody2D>(); shot.ballShape = nativeBall.GetComponent<Collider2D>(); if (nativeBall.GetComponent<HeaderContactSensor>() == null) nativeBall.gameObject.AddComponent<HeaderContactSensor>(); }
        Type managerType = player.GetType().Assembly.GetType("GameManager");
        shot.manager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
        shot.stopping = managerType.GetField("isStopping");
    }
    public static void Request(Component player) { Attach(player); PowerShot shot = player.GetComponent<PowerShot>(); if (shot != null && !shot.Stopped()) shot.requestedFrame = Time.frameCount; }
    public static bool IsPowerCommand(Component player) { PowerShot shot = player.GetComponent<PowerShot>(); return shot != null && (shot.requestedFrame == Time.frameCount || shot.commandFrame == Time.frameCount); }
    public static void OnAction(Component player, string action)
    {
        PowerShot shot = player.GetComponent<PowerShot>(); if (shot == null) return;
        if(action=="Kick"||action=="Head"){shot.flatUntil=-1f;shot.bicycleFlat=false;}
        shot.commandFrame = action == "Kick" && shot.requestedFrame == Time.frameCount ? Time.frameCount : -1;
        shot.armedUntil = shot.commandFrame >= 0 ? Time.time + .75f : -1f;
        shot.headerUntil = (action == "Head" || (action=="Kick"&&SkillRework.Has(player,1))) && !shot.Stopped() ? Time.time + .55f : -1f;
        shot.requestedFrame = -1;
    }
    public static void CompleteHeader(Component player) { PowerShot s = player != null ? player.GetComponent<PowerShot>() : null; if (s != null) s.headerUntil = s.armedUntil = -1f; }
    public static bool FrontalHead(Component player,Rigidbody2D ball){
        PowerShot s=player!=null?player.GetComponent<PowerShot>():null;if(s==null||s.headShape==null||ball==null||SkillRework.Mirrored(player))return false;
        Vector2 offset=ball.position-(Vector2)s.headShape.bounds.center;float facing=s.fan?1f:-1f;
        if(facing*offset.x < -s.headShape.bounds.extents.x*.12f)return false;
        Vector2 front=HeadFacing(player),up=new Vector2(-front.y,front.x)*facing;
        return Vector2.Dot(offset,front)>=-.015f||Vector2.Dot(offset,up)>s.headShape.bounds.extents.y*.35f;
    }
    public static float HeadRestRotation(Component player){PowerShot s=player!=null?player.GetComponent<PowerShot>():null;return s!=null?s.headReferenceAngle:0f;}
    public static Vector2 HeadFacing(Component player){PowerShot s=player!=null?player.GetComponent<PowerShot>():null;if(s==null||s.headShape==null)return Vector2.right*(player!=null&&player.name=="Zhao"?-1f:1f);float angle=Mathf.DeltaAngle(s.headReferenceAngle,s.headShape.transform.eulerAngles.z)*Mathf.Deg2Rad;return new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(s.fan?1f:-1f);}
    public static float ForeheadAlignment(Component player,Rigidbody2D ball){PowerShot s=player!=null?player.GetComponent<PowerShot>():null;if(s==null||ball==null)return 0f;return Vector2.Dot((ball.position-(Vector2)s.headShape.bounds.center).normalized,HeadFacing(player));}
    public static Vector2 OrdinaryHeaderVelocity(Component player,Vector2 incoming){return PlayerSkills.CannonVelocity(player.name=="Fan"?1f:-1f,incoming)*.85f;}
    private bool SlowHeader(Collider2D limb,bool exiting){
        if(exiting||Stopped()||Time.time>headerUntil||limb.name!="Head"||!headShape.IsTouching(ballShape))return false;
        if(!FrontalHead(controller,ball)){headerUntil=-1f;return false;}
        if(PlayerSkills.RescueHeader(controller))return true;
        if(AbilityMode.Enabled)return false;
        PlayerSkills skills=controller.GetComponent<PlayerSkills>();if(skills!=null&&skills.AwaitingFanHeader)return false;
        headerUntil=-1f;Vector2 target=OrdinaryHeaderVelocity(controller,ball.velocity);
        ball.collisionDetectionMode=CollisionDetectionMode2D.Continuous;ball.AddForce((target-ball.velocity)*ball.mass,ForceMode2D.Impulse);return true;
    }
    private bool Stopped() { return controller == null || ball == null || Time.timeScale <= 0f || GameAIMod.BallOutThisRally || (manager != null && (bool)stopping.GetValue(manager)); }
    private bool InFront() { return ball != null && body != null && (fan ? 1f : -1f) * (ball.position.x - body.position.x) >= -.1f; }
    private bool Contest()
    {
        if (opponentBody == null || !opponentBody.gameObject.activeInHierarchy)
        {
            GameObject opponent = GameObject.Find(fan ? "Zhao" : "Fan");
            if (opponent == null) return false;
            opponentBody = opponent.transform.Find("Body").GetComponent<Rigidbody2D>();
        }
        Rigidbody2D other = opponentBody;
        return Mathf.Abs(body.position.x - other.position.x) <= 2f && Mathf.Abs(ball.position.x - body.position.x) < 1.9f && Mathf.Abs(ball.position.x - other.position.x) < 1.9f && ball.position.y < Mathf.Max(body.position.y, other.position.y) + 2.5f;
    }
    private bool Strike(Collider2D limb, Vector2 contact, Vector2 normal)
    {
        if (Stopped() || Time.time > armedUntil || !InFront() || (!fan && Contest()) || !PlayerSkills.IsStrikeFoot(controller, limb, contact, normal, !fan && PlayerSkills.Enabled)) return false;
        ball.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        foreach (PowerShot player in players)
            if (player != null && player.gameObject.activeInHierarchy)
                foreach (Rigidbody2D limbBody in player.limbs)
                    if (limbBody != null && limbBody.gameObject.activeInHierarchy) limbBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        Vector2 target;
        if (fan) target = ball.velocity * SpeedMultiplier;
        else { float speed=Mathf.Clamp(Mathf.Max(10f,ball.velocity.magnitude*1.2f),10f,20f)*SpeedMultiplier;target=SkillRework.Aim(controller,ball.position,speed,SkillRework.ShootingLevel(controller),1f); }
        armedUntil = -1f;
        PlayerSkills.CancelShotFlights();
        ball.AddForce((target - ball.velocity) * ball.mass, ForceMode2D.Impulse);

        return true;
    }
    public static Vector2 GroundBicycle(Component player,float speed){var s=player.GetComponent<PowerShot>();float dir=player.name=="Fan"?1f:-1f;if(s==null)return Vector2.right*dir*speed;s.floorHeight=s.FindFloor();s.flatUntil=Time.time+.75f;s.bicycleFlat=true;s.flatDirection=dir;s.flatClearX=s.body.position.x;foreach(var c in s.limbShapes)if(!c.isTrigger)s.flatClearX=dir>0?Mathf.Max(s.flatClearX,c.bounds.max.x):Mathf.Min(s.flatClearX,c.bounds.min.x);s.flatClearX+=dir*(s.ballShape.bounds.extents.x+.12f);return new Vector2(dir*speed,speed*.18f);}

    public static float SkillBurstSpeed(float incoming){return Mathf.Clamp(Mathf.Max(17f,incoming*1.65f),17f,23f)*SpeedMultiplier*SkillBurstMultiplier;}
    private float FindFloor()
    {
        foreach (RaycastHit2D hit in rayHits.Raycast(ball.position + Vector2.up * .1f, Vector2.down, 30f, groundMask))
        {
            if (hit.collider == null || hit.collider == ballShape || hit.collider.isTrigger || hit.normal.y < .5f || hit.collider.GetComponentInParent<PowerShot>() != null) continue;
            return hit.point.y + ballShape.bounds.extents.y + .035f;
        }
        return ball.position.y;
    }
    private float FlatVertical() { float clearance = ball.position.y - floorHeight; return clearance > .1f ? -Mathf.Clamp(clearance * 12f, 2f, 16f) : .2f; }
    private void FixedUpdate()
    {
        if (Stopped()) { headerUntil = armedUntil = flatUntil = -1f; return; }
        if (Time.time <= flatUntil && (!bicycleFlat||flatDirection*(ball.position.x-flatClearX)>0f)) ball.AddForce(Vector2.up * (FlatVertical() - ball.velocity.y) * ball.mass, ForceMode2D.Impulse);
    }
    public static bool OnBallCollision(Collision2D collision)
    {
        if (collision == null || collision.collider == null) return false;
        PowerShot striker = collision.collider.GetComponentInParent<PowerShot>();
        bool launched = false;
        if (striker != null && striker.SlowHeader(collision.collider, false)) return false;
        if (striker != null)
            for (int i = 0; i < collision.contactCount && !launched; i++)
            {
                ContactPoint2D point = collision.GetContact(i); Vector2 normal = point.normal;
                if (Vector2.Dot(normal, striker.ball.position - point.point) < 0) normal = -normal;
                launched = striker.Strike(collision.collider, point.point, normal);
            }
        foreach (PowerShot owner in players)
            if (owner != null && owner.gameObject.activeInHierarchy && owner != striker && striker != null) owner.flatUntil = -1f;
        return launched;
    }
    public static void HeaderContact(Collision2D collision, bool exiting)
    {
        if (collision == null || collision.collider == null || collision.collider.name != "Head") return;
        PowerShot owner = collision.collider.GetComponentInParent<PowerShot>();
        if (owner != null) owner.SlowHeader(collision.collider, exiting);
    }
    public static void ResetAll() { foreach (PowerShot shot in players) if (shot != null && shot.gameObject.activeInHierarchy) { shot.headerUntil = shot.armedUntil = shot.flatUntil = -1f; shot.requestedFrame = shot.commandFrame = -1; } }
}

// Apply the ordinary 10% reduction once at separation, so a ball already
// resting against a commanded head is reduced after its actual launch too.
public sealed class HeaderContactSensor : MonoBehaviour
{
    private void OnCollisionEnter2D(Collision2D collision){AbilityMode.CannonWorldContact(collision);}
    private void OnCollisionStay2D(Collision2D collision) { AbilityMode.CannonWorldContact(collision);PowerShot.HeaderContact(collision, false); }
    private void OnCollisionExit2D(Collision2D collision) { PowerShot.HeaderContact(collision, true); }
}

public static class BallFlightSafety
{
    private static readonly PhysicsHitBuffer castHits = new PhysicsHitBuffer();
    public static bool WillHit(Rigidbody2D ball, Vector2 velocity, Component shooter, float age)
    {
        if (velocity.sqrMagnitude < .001f) return false;
        ContactFilter2D filter = new ContactFilter2D(); filter.SetLayerMask(Physics2D.GetLayerCollisionMask(ball.gameObject.layer)); filter.useTriggers = false;
        List<RaycastHit2D> hits = castHits.Cast(ball, velocity.normalized, filter, velocity.magnitude * Time.fixedDeltaTime + .015f);
        int count = hits.Count;
        if (count == 0) return false;
        PowerShot owner = shooter.GetComponent<PowerShot>();
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == ball) continue;
            if (hit.collider.GetComponentInParent<PowerShot>() == owner && age <= Time.fixedDeltaTime * 1.5f) continue;
            if (hit.distance <= .002f && velocity.y > 0 && hit.normal.y > .5f) continue;
            if (Vector2.Dot(velocity, hit.normal) < -.05f) return true;
        }
        return false;
    }
}
// Shared passive bonuses and an action-driven, contact-only unconventional shot.
public sealed class SkillRework : MonoBehaviour {
    Component player,manager,stick;Rigidbody2D body,ball;Collider2D ballShape,goal,head;Rigidbody2D[] rig;Collider2D[] shapes;Animator animator;
    FieldInfo score1,score2,stopping,grounded,bodyTarget;float jumpCommand=-10f,kickCommand=-10f;float facing,mass,baseHeight,baseWidth,armedUntil=-1f,bicycleUntil=-1f,lastBicycle=-10f,knockedUntil=-1f,started,recoverUntil=-1f;float standSince=-1f;float magneticShotUntil=-1f;bool magneticPower;bool jumped,turned,magneticHop,bicycleStruck;float landedAt=-1f,getupAt=-1f,standingBodyHeight;Vector3 mirrorScale;
    static readonly List<SkillRework> all=new List<SkillRework>(2);
    public static bool Has(Component p,int skill){if(p==null)return false;if(AbilityMode.Enabled)return AbilityMode.Has(p,skill);if(!PlayerSkills.Enabled)return false;return p.name=="Fan"?(skill==0||skill==1||skill==5||skill==7||skill==9||skill==10):(skill==2||skill==3||skill==4||skill==6||skill==8);}
    public static void Attach(Component p){if(p==null||p.GetComponent<SkillRework>()!=null)return;var s=p.gameObject.AddComponent<SkillRework>();s.player=p;s.facing=p.name=="Fan"?1:-1;var t=p.GetType();s.body=(Rigidbody2D)t.GetField("rb").GetValue(p);s.animator=(Animator)t.GetField("anim").GetValue(p);s.grounded=t.GetField("isOnGround");s.stick=p.GetComponent(t.Assembly.GetType("StickManController"));s.bodyTarget=s.stick.GetType().GetField("body");s.rig=p.GetComponentsInChildren<Rigidbody2D>();s.shapes=p.GetComponentsInChildren<Collider2D>();float bottom=s.body.position.y,top=bottom,min=s.body.position.x,max=min;foreach(var b in s.rig)s.mass+=b.mass;foreach(var c in s.shapes)if(!c.isTrigger){bottom=Mathf.Min(bottom,c.bounds.min.y);top=Mathf.Max(top,c.bounds.max.y);min=Mathf.Min(min,c.bounds.min.x);max=Mathf.Max(max,c.bounds.max.x);}s.standingBodyHeight=s.body.position.y-s.Floor();s.baseHeight=(top-bottom)/s.Scale;s.baseWidth=(max-min)/s.Scale;s.head=p.transform.Find("Head").GetComponent<Collider2D>();s.manager=UnityEngine.Object.FindObjectOfType(t.Assembly.GetType("GameManager")) as Component;s.score1=s.manager.GetType().GetField("p1Score");s.score2=s.manager.GetType().GetField("p2Score");s.stopping=s.manager.GetType().GetField("isStopping");var football=UnityEngine.Object.FindObjectOfType(t.Assembly.GetType("Ball")) as Component;s.ball=football.GetComponent<Rigidbody2D>();s.ballShape=football.GetComponent<Collider2D>();foreach(UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(t.Assembly.GetType("GoalTrigger"))){var c=(Component)o;if(c.transform.position.x*s.facing>0)s.goal=c.GetComponent<Collider2D>();}all.Add(s);MagneticFoot.Attach(p,true);}
    float Scale {get{return Mathf.Abs(transform.lossyScale.x)/.8f;}}float Height {get{return baseHeight*Scale;}}float Width {get{return baseWidth*Scale;}}
    bool Stopped {get{return player==null||ball==null||Time.timeScale<=0||(!turned&&(GameAIMod.BallOutThisRally||(manager!=null&&(bool)stopping.GetValue(manager))));}}
    bool Air {get{return !(bool)grounded.GetValue(player)&&body.position.y>Floor()+Height*.45f;}}
    float Floor(){var floor=GameObject.Find("Down");return floor!=null?floor.GetComponent<Collider2D>().bounds.max.y:body.position.y-Height*.5f;}
    SkillRework Other(){foreach(var s in all)if(s!=null&&s!=this&&s.player!=null&&s.player.gameObject.activeInHierarchy)return s;return null;}
    bool AerialDuel(){var o=Other();return o!=null&&!Stopped&&!o.Stopped&&(Air||o.Air)&&ball.position.y>Mathf.Min(body.position.y,o.body.position.y)+Height*.2f&&Mathf.Abs(body.position.x-o.body.position.x)<(Width+o.Width)*.55f+.25f*Scale;}
    public static float Effective(Component p,int dimension){float level=AbilityMode.Enabled?AbilityMode.For(p).Levels[dimension]:4;var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null||s.Stopped)return level;if(dimension==1||dimension==2){int a=(int)s.score1.GetValue(s.manager),b=(int)s.score2.GetValue(s.manager);bool ahead=p.name=="Fan"?a>=b:b>=a;if(Has(p,4)&&ahead)level+=1.5f;if(Has(p,2)&&s.AerialDuel())level+=1f;}return level;}
    public static float NativeJumpScale(Component p){return AbilityMode.JumpTempo(Effective(p,2))/AbilityMode.JumpTempo(4);}
    public static int ShootingLevel(Component p){return Mathf.Min(8,(AbilityMode.Enabled?AbilityMode.For(p).Levels[0]:4)+(MagneticFoot.AssistReady(p)?1:0));}
    public static void Tackled(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;if(s!=null)s.knockedUntil=Time.time+.85f;}
    bool AwkwardPose(){if(Time.time<=knockedUntil||Time.time<=bicycleUntil||Mathf.Abs(Mathf.DeltaAngle(body.rotation,0))>28f)return true;float a=(float)stick.GetType().GetField("L_Up_Leg").GetValue(stick),b=(float)stick.GetType().GetField("R_Up_Leg").GetValue(stick);return !Air&&Mathf.Abs(Mathf.DeltaAngle(a,b))>85f;}
    public static bool LowerBody(Collider2D c){return c!=null&&(c.name.IndexOf("Leg",StringComparison.OrdinalIgnoreCase)>=0||c.name.IndexOf("Foot",StringComparison.OrdinalIgnoreCase)>=0);}
    public static int Quality(float roll){return roll<.7f?0:roll<.9f?1:2;}
    public bool Bicycle {get{return Time.time<=bicycleUntil;}}
    bool BicycleRange(){float behind=-facing*(ball.position.x-body.position.x);return behind>0&&behind<=Width&&ball.position.y>=body.GetComponent<Collider2D>().bounds.center.y&&ball.position.y<=head.bounds.max.y+head.bounds.size.y;}
    public static float NativeContactForce(Component p){if(AbilityMode.Enabled||!PlayerSkills.Enabled)return 1f;var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null||s.Stopped)return 1f;var other=s.Other();if(other==null)return 1f;bool ours=false,theirs=false,contact=false;foreach(var c in s.shapes){ours|=c.IsTouching(s.ballShape);foreach(var q in other.shapes)contact|=c.IsTouching(q);}foreach(var q in other.shapes)theirs|=q.IsTouching(s.ballShape);return contact||(ours&&theirs)?(.6f+Effective(p,1)*.05f)/.8f:1f;}
    public static bool WantsBicycle(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;return s!=null&&!s.Stopped&&Has(p,10)&&!s.Bicycle&&!s.turned&&Time.time-s.lastBicycle>1.25f&&!s.turned&&s.BicycleRange();}
    public static bool Mirrored(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;return s!=null&&s.turned;}
    public static bool InBicycle(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;return s!=null&&!s.Stopped&&(s.Bicycle||s.turned);}
    public static bool WantsRecoveryShot(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null||s.Stopped||!Has(p,10)||!s.AwkwardPose())return false;foreach(var c in s.shapes)if(LowerBody(c)&&Vector2.Distance(c.ClosestPoint(s.ball.position),s.ball.position)<s.ballShape.bounds.extents.x+.3f*s.Scale)return true;return false;}
    void Update(){if(!Stopped&&Has(player,10)&&GameAIMod.GetButtonDown(player.name=="Fan"?KeyCode.W:KeyCode.UpArrow,player))OnAction(player,"Jump");}
    public static void OnAction(Component p,string action){var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null||s.Stopped)return;if(action=="Kick"&&Has(p,8)){s.magneticHop=true;s.magneticShotUntil=Time.time+.8f;s.magneticPower=PowerShot.IsPowerCommand(p);}if(action=="Jump")s.jumpCommand=Time.time;if(action=="Kick"&&Has(p,10)){s.kickCommand=Time.time;s.armedUntil=Time.time+.9f;}if((action=="Kick"||action=="Jump")&&Has(p,10)&&Time.time-s.jumpCommand<=.35f&&Time.time-s.kickCommand<=.35f&&!LanMultiplayer.IsRemoteSimulation&&Time.time-s.lastBicycle>1.25f&&!s.turned&&s.BicycleRange()){s.lastBicycle=Time.time;if(UnityEngine.Random.value<.5f){s.bicycleUntil=Time.time+.9f;s.standingBodyHeight=Mathf.Clamp(s.body.position.y-s.Floor(),s.Height*.45f,s.Height*.65f)-.04f*s.Scale;s.bicycleUntil=Time.time+2.4f;s.started=Time.time;s.jumped=false;s.bicycleStruck=false;s.landedAt=s.getupAt=-1f;s.TurnBack();PlayerSkills.ShowFormless(p);ShotCloseup.Play(p);}}if(action=="Head"&&!s.turned){s.armedUntil=-1f;s.bicycleUntil=-1f;}}
    static readonly string[] poseFields={"head","body","L_Up_Arm","L_Low_Arm","R_Up_Arm","R_Low_Arm","L_Up_Leg","L_Low_Leg","R_Up_Leg","R_Low_Leg"};
    void PoseBone(string field,float target,float rate=1000f){stick.GetType().GetField(field).SetValue(stick,target);}

    float FlipAngle(){float t=Time.time-started;float turn=Mathf.Lerp(0f,120f,Mathf.SmoothStep(0f,1f,(t-.035f)/.29f));if(t>.30f)turn=Mathf.Lerp(120f,90f,Mathf.Clamp01((t-.30f)/.16f));if(landedAt>=0)turn=90f;if(getupAt>=0)turn=Mathf.Lerp(90f,0f,Mathf.SmoothStep(0f,1f,(Time.time-getupAt)/.30f));return -facing*turn;}
    public static void BeforeMuscles(Component p){
        var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null||s.Stopped)return;
        if(!s.Bicycle){if(s.turned)foreach(string field in poseFields)s.PoseBone(field,(float)s.stick.GetType().GetField(field).GetValue(s.stick),720f);return;}
        float target=s.FlipAngle(),t=Time.time-s.started;
        s.PoseBone("body",target,820f);s.PoseBone("head",target,820f);
        s.PoseBone("L_Up_Arm",target-s.facing*35f);s.PoseBone("L_Low_Arm",target-s.facing*55f);
        s.PoseBone("R_Up_Arm",target+s.facing*30f);s.PoseBone("R_Low_Arm",target+s.facing*60f);
        if(s.landedAt<0&&!s.bicycleStruck&&t<.72f){
            MagneticFoot.AimAir(p,s.ball.position+s.ball.velocity*.045f-Vector2.right*s.facing*s.ballShape.bounds.extents.x*.55f);
            s.PoseBone("R_Up_Leg",target+s.facing*65f);s.PoseBone("R_Low_Leg",target-s.facing*40f);
        }else{
            s.PoseBone("L_Up_Arm",target);s.PoseBone("L_Low_Arm",target-s.facing*20f);s.PoseBone("R_Up_Arm",target);s.PoseBone("R_Low_Arm",target-s.facing*20f);
            float stand=s.getupAt<0?0f:Mathf.Clamp01((Time.time-s.getupAt)/.25f);
            s.PoseBone("L_Up_Leg",Mathf.LerpAngle(target-s.facing*65f,0f,stand));s.PoseBone("L_Low_Leg",Mathf.LerpAngle(target-s.facing*90f,0f,stand));
            s.PoseBone("R_Up_Leg",Mathf.LerpAngle(target-s.facing*35f,0f,stand));s.PoseBone("R_Low_Leg",Mathf.LerpAngle(target-s.facing*75f,0f,stand));
        }
    }
    void FixedUpdate(){if(Stopped){Clear();return;}if(LanMultiplayer.IsRemoteSimulation)return;
        if(magneticHop){magneticHop=false;float lift=ball.position.y-body.position.y;if(!Bicycle&&(bool)grounded.GetValue(player)&&facing*(ball.position.x-body.position.x)>0&&Mathf.Abs(ball.position.x-body.position.x)<1.3f*Scale&&lift>.1f*Scale&&lift<Height){float vy=Mathf.Min(5.5f*Scale,Mathf.Sqrt(2f*Mathf.Abs(Physics2D.gravity.y*body.gravityScale)*Mathf.Max(.05f*Scale,lift-.1f*Scale)));foreach(var limb in rig)limb.AddForce(Vector2.up*Mathf.Max(0,vy-body.velocity.y)*limb.mass,ForceMode2D.Impulse);}}
        if(Bicycle){
            float elapsed=Time.time-started;
            if(!jumped&&elapsed>.035f){jumped=true;float needed=Mathf.Clamp(ball.position.y-body.position.y+Height*.25f,Height*.24f,Height*.50f);float rise=Mathf.Min(16f*Scale,Mathf.Sqrt(2f*Mathf.Abs(Physics2D.gravity.y*body.gravityScale)*needed));float vy=body.velocity.y;foreach(var limb in rig)limb.AddForce(Vector2.up*Mathf.Max(0,rise-vy)*limb.mass,ForceMode2D.Impulse);}
            if(elapsed>.12f&&!bicycleStruck&&Mathf.Abs(Mathf.DeltaAngle(body.rotation,0))>50f)foreach(var c in shapes)if(LowerBody(c)&&Vector2.Distance(c.ClosestPoint(ball.position),ball.position)<ballShape.bounds.extents.x+.15f*Scale){ShotCloseup.End(player);break;}
            float desiredSpin=Mathf.Clamp(Mathf.DeltaAngle(body.rotation,FlipAngle())*24f,-680f,680f);float dw=Mathf.Clamp(desiredSpin-body.angularVelocity,-6000f*Time.fixedDeltaTime,6000f*Time.fixedDeltaTime)*Mathf.Deg2Rad;Vector2 center=Vector2.zero;foreach(var limb in rig)center+=limb.position*limb.mass;center/=mass;foreach(var limb in rig){Vector2 off=limb.position-center;limb.AddForce(new Vector2(-off.y,off.x)*dw*limb.mass,ForceMode2D.Impulse);limb.AddTorque(dw*limb.inertia,ForceMode2D.Impulse);}
            Collider2D torso=body.GetComponent<Collider2D>();var floorObject=GameObject.Find("Down");Collider2D floor=floorObject!=null?floorObject.GetComponent<Collider2D>():null;
            bool backDown=elapsed>.35f&&body.velocity.y<1f*Scale&&Mathf.Abs(Mathf.DeltaAngle(body.rotation,-facing*90f))<28f&&floor!=null&&torso.IsTouching(floor);
            if(landedAt<0&&backDown){landedAt=Time.time;armedUntil=-1f;ShotCloseup.End(player);}
            if(landedAt>=0&&getupAt<0&&Time.time-landedAt>.065f)getupAt=Time.time;
            if(getupAt>=0){float goalY=Floor()+standingBodyHeight;float vy=Mathf.Clamp((goalY-body.position.y)*9f,-2f*Scale,3.2f*Scale);float dv=Mathf.Clamp(vy-body.velocity.y,-36f*Scale*Time.fixedDeltaTime,36f*Scale*Time.fixedDeltaTime);foreach(var limb in rig)limb.AddForce(Vector2.up*dv*limb.mass,ForceMode2D.Impulse);if(Time.time-getupAt>.32f&&Mathf.Abs(Mathf.DeltaAngle(body.rotation,0f))<18f)bicycleUntil=-1f;}
            if(elapsed>2.35f)bicycleUntil=-1f;recoverUntil=Time.time+.5f;
        }else if(Time.time<recoverUntil||turned){float spin=Mathf.Clamp(Mathf.DeltaAngle(body.rotation,0)*7-body.angularVelocity,-800*Time.fixedDeltaTime,800*Time.fixedDeltaTime);body.AddTorque(spin*Mathf.Deg2Rad*body.inertia,ForceMode2D.Impulse);}
        if(!Bicycle)RestoreFacing();
        if(!AbilityMode.Enabled&&PlayerSkills.Enabled&&AerialDuel()){var o=Other();bool contact=false;foreach(var a in shapes)foreach(var b in o.shapes)if(a.IsTouching(b))contact=true;float advantage=Effective(player,1)-Effective(o.player,1);if(contact&&advantage>0){float turn=-facing*Mathf.Min(90,advantage*35)*Time.fixedDeltaTime; o.body.AddTorque(turn*o.body.inertia,ForceMode2D.Impulse);body.AddTorque(-turn*.2f*body.inertia,ForceMode2D.Impulse);}}
    }
    public static Vector2 Aim(Component p,Vector2 origin,float speed,int level,float accuracy){var s=p.GetComponent<SkillRework>();if(s==null||s.goal==null)return Vector2.right*(p.name=="Fan"?speed:-speed);float radius=s.ballShape.bounds.extents.y;Vector2 target=new Vector2(s.facing>0?s.goal.bounds.min.x+radius*.3f:s.goal.bounds.max.x-radius*.3f,BallBoundaryGuard.FindGoalCeiling(s.goal)-radius-.14f*s.Scale);target.y+=UnityEngine.Random.Range(-1f,1f)*AbilityMode.ShotSpread(level)*s.Scale/Mathf.Max(.1f,accuracy);float dx=Mathf.Abs(target.x-origin.x),dy=target.y-origin.y,g=Mathf.Max(.1f,-Physics2D.gravity.y*s.ball.gravityScale),v2=speed*speed,disc=v2*v2-g*(g*dx*dx+2*dy*v2);if(dx>.01f&&disc>=0){float tangent=(g*dx*dx+2*dy*v2)/(dx*(v2+Mathf.Sqrt(disc))),vx=speed/Mathf.Sqrt(1+tangent*tangent);return new Vector2(s.facing*vx,vx*tangent);}return (target-origin).normalized*speed;}
    static bool NativeMagneticContact(Collision2D c){if(AbilityMode.Enabled||c==null||c.collider==null||LanMultiplayer.IsRemoteSimulation)return false;var s=c.collider.GetComponentInParent<SkillRework>();if(s==null||s.Stopped||Time.time>s.magneticShotUntil||!Has(s.player,8)||!MagneticFoot.AssistReady(s.player))return false;bool sweet=false;for(int i=0;i<c.contactCount;i++){var pt=c.GetContact(i);Vector2 n=pt.normal;if(Vector2.Dot(n,s.ball.position-pt.point)<0)n=-n;sweet|=PlayerSkills.IsStrikeFoot(s.player,c.collider,pt.point,n,true);}if(!sweet)return false;int level=ShootingLevel(s.player);float speed=Mathf.Clamp(Mathf.Max(8,s.ball.velocity.magnitude),8,19)*(s.magneticPower?PowerShot.SpeedMultiplier:1f)*AbilityMode.Strength(level)/AbilityMode.Strength(4);Vector2 shot=Aim(s.player,s.ball.position,speed,level,1);PlayerSkills.CancelShotFlights();BuildPlayer.CancelFlights();s.ball.AddForce((shot-s.ball.velocity)*s.ball.mass,ForceMode2D.Impulse);s.magneticShotUntil=-1f;PlayerSkills.ShowMagneticFoot(s.player);return true;}
    public static bool OnBallContact(Collision2D c){if(NativeMagneticContact(c))return true;if(c==null||c.collider==null)return false;var s=c.collider.GetComponentInParent<SkillRework>();if(s==null||s.Stopped||LanMultiplayer.IsRemoteSimulation||Time.time>s.armedUntil||!Has(s.player,10)||!LowerBody(c.collider))return false;bool sweet=false;for(int i=0;i<c.contactCount;i++){var pt=c.GetContact(i);Vector2 n=pt.normal;if(Vector2.Dot(n,s.ball.position-pt.point)<0)n=-n;sweet|=PlayerSkills.IsStrikeFoot(s.player,c.collider,pt.point,n,true);}if(sweet&&!s.AwkwardPose())return false;
        bool bicycle=s.Bicycle;if(bicycle&&(s.bicycleStruck||s.landedAt>=0||c.collider.name.IndexOf("Up_Leg",StringComparison.OrdinalIgnoreCase)>=0||Time.time-s.started<.08f||Mathf.Abs(Mathf.DeltaAngle(s.body.rotation,0))<12f))return false;int level=ShootingLevel(s.player);float normalSpeed=Mathf.Clamp(Mathf.Max(10f,s.ball.velocity.magnitude),10f,19f)*(AbilityMode.Enabled?AbilityMode.Strength(level):1f);Vector2 shot;int quality=-1;
        if(bicycle){quality=Quality(UnityEngine.Random.value);float speed=normalSpeed*(quality==0?.6f:quality==1?1.2f:1.35f);shot=quality==0?new Vector2(s.facing*speed*.5f,speed*.86f):quality==1?PowerShot.GroundBicycle(s.player,speed):Aim(s.player,s.ball.position,speed,8,1f);}
        else shot=Aim(s.player,s.ball.position,normalSpeed*.9f,level,.9f);
        PlayerSkills.CancelShotFlights();BuildPlayer.CancelFlights();PowerShot.CompleteHeader(s.player);s.ball.collisionDetectionMode=CollisionDetectionMode2D.Continuous;s.ball.AddForce((shot-s.ball.velocity)*s.ball.mass,ForceMode2D.Impulse);s.armedUntil=-1f;if(bicycle)s.bicycleStruck=true;ShotCloseup.End(s.player);PlayerSkills.ShowFormless(s.player);return true;
    }
    void MirrorRig(bool mirror){
        Vector2 center=body.position;Vector2[] velocities=new Vector2[rig.Length];float[] spins=new float[rig.Length];Vector2 average=Vector2.zero;
        for(int i=0;i<rig.Length;i++){velocities[i]=rig[i].velocity;spins[i]=rig[i].angularVelocity;average+=velocities[i]*rig[i].mass;}average/=mass;
        if(mirror)mirrorScale=transform.localScale;
        Vector3 scale=mirror?new Vector3(-mirrorScale.x,mirrorScale.y,mirrorScale.z):mirrorScale;transform.localScale=scale;Physics2D.SyncTransforms();transform.position+=(Vector3)(center-body.position);Physics2D.SyncTransforms();
        for(int i=0;i<rig.Length;i++){rig[i].velocity=new Vector2(2*average.x-velocities[i].x,velocities[i].y);rig[i].angularVelocity=-spins[i];}
    }
    void TurnBack(){if(turned)return;MirrorRig(true);turned=true;LanMultiplayer.Mirror(player.name,true);}
    void RestoreFacing(bool force=false){if(!turned)return;if(!force){if(Bicycle||Air||!(bool)grounded.GetValue(player)||Mathf.Abs(Mathf.DeltaAngle(body.rotation,0))>18f||Mathf.Abs(body.velocity.y)>.7f||Mathf.Abs(body.angularVelocity)>65f){standSince=-1f;return;}if(standSince<0)standSince=Time.time;if(Time.time-standSince<.1f)return;}standSince=-1f;MirrorRig(false);turned=false;LanMultiplayer.Mirror(player.name,false);}
    public static void ApplyMirror(Component p,bool on){var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null)return;if(on&&!s.turned){s.MirrorRig(true);s.turned=true;}else if(!on&&s.turned)s.RestoreFacing(true);}
    void LateUpdate(){if(!LanMultiplayer.IsRemoteSimulation&&!Bicycle)RestoreFacing();}
    public static void ResetAll(){ShotCloseup.ClearAll();foreach(var s in all)if(s!=null){if(GameAIMod.BallOutThisRally&&s.turned&&s.isActiveAndEnabled){s.armedUntil=s.magneticShotUntil=-1f;continue;}s.Clear();}}
    void Clear(){landedAt=getupAt=-1f;bicycleStruck=false;magneticShotUntil=-1f;magneticHop=false;RestoreFacing(true);armedUntil=bicycleUntil=recoverUntil=knockedUntil=-1f;jumpCommand=kickCommand=-10f;ShotCloseup.End(player);}
    void OnDisable(){Clear();}void OnDestroy(){Clear();all.Remove(this);}
}

[DefaultExecutionOrder(20000)]
public sealed class ShotCloseup:MonoBehaviour {
    static ShotCloseup instance;Component actor;Collider2D[] focusShapes;Camera camera;Vector3 home;float size,savedSpeed,started,last,until;bool active,remote;public static bool Active {get{return instance!=null&&instance.active;}}
    public static void Play(Component p,bool fromNetwork=false){if(p==null)return;if(instance==null){var o=new GameObject("Shot Closeup");instance=o.AddComponent<ShotCloseup>();}var s=instance;if(s.active)s.Restore();s.camera=Camera.main;if(s.camera==null)return;s.actor=p;s.focusShapes=p.GetComponentsInChildren<Collider2D>();s.home=s.camera.transform.position;s.size=s.camera.orthographicSize;s.savedSpeed=Time.timeScale;s.started=Time.realtimeSinceStartup;s.until=s.started+1.6f;s.last=s.started;s.active=true;s.remote=fromNetwork;Time.timeScale=s.savedSpeed*.5f;if(!fromNetwork)LanMultiplayer.Cinematic(p.name,true);}
    public static void End(Component p){if(instance!=null&&instance.active&&(p==null||instance.actor==p)){bool remote=instance.remote;string name=instance.actor!=null?instance.actor.name:"Fan";instance.Restore();if(!remote)LanMultiplayer.Cinematic(name,false);}}
    public static void ClearAll(){End(null);}
    void Restore(){if(!active)return;if(camera!=null){camera.transform.position=home;camera.orthographicSize=size;}if(Time.timeScale>0&&Mathf.Abs(Time.timeScale-savedSpeed*.5f)<.01f)Time.timeScale=savedSpeed;active=false;}
    void LateUpdate(){if(!active)return;if(actor==null||Time.timeScale<=0||Time.realtimeSinceStartup>until||GameAIMod.BallOutThisRally){End(actor);return;}if(!ControlBindings.ShowSkillAnimations)return;float now=Time.realtimeSinceStartup,dt=Mathf.Min(.05f,now-last);last=now;Bounds bounds=actor.transform.Find("Body").GetComponent<Collider2D>().bounds;foreach(var shape in focusShapes)if(shape!=null&&!shape.isTrigger)bounds.Encapsulate(shape.bounds);Vector3 center=bounds.center;center.z=home.z;float blend=1-Mathf.Exp(-dt*15f);camera.transform.position=Vector3.Lerp(camera.transform.position,center,blend);camera.orthographicSize=Mathf.Lerp(camera.orthographicSize,Mathf.Max(.9f,bounds.extents.y*1.2f+.2f*Mathf.Abs(actor.transform.lossyScale.y)/.8f),blend);}
    void OnDisable(){Restore();}void OnDestroy(){Restore();if(instance==this)instance=null;}
}
