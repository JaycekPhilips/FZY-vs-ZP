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
        BuildPlayer build=player.GetComponent<BuildPlayer>();if(AbilityMode.Runtime&&build!=null&&build.InContest)return false;
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
        if(AbilityMode.Runtime)return false;
        PlayerSkills skills=controller.GetComponent<PlayerSkills>();if(skills!=null&&skills.AwaitingFanHeader)return false;
        headerUntil=-1f;Vector2 target=OrdinaryHeaderVelocity(controller,ball.velocity);
        ball.collisionDetectionMode=CollisionDetectionMode2D.Continuous;ball.AddForce((target-ball.velocity)*ball.mass,ForceMode2D.Impulse);SkillRework.TrackShot(controller,target);return true;
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
        if(!AbilityMode.Runtime){target*=AbilityMode.Strength(SkillRework.ShootingLevel(controller))/AbilityMode.Strength(4);target=SkillRework.OrdinaryShot(controller,target);}
        armedUntil = -1f;
        PlayerSkills.CancelShotFlights();
        ball.AddForce((target - ball.velocity) * ball.mass, ForceMode2D.Impulse);
        if(!AbilityMode.Runtime)SkillRework.TrackShot(controller,target);
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
    Component player,manager,stick;Rigidbody2D body,ball;Collider2D ballShape,goal,head,floorShape;Rigidbody2D[] rig;Collider2D[] shapes;Animator animator;
    FieldInfo score1,score2,stopping,goaling,grounded,bodyTarget;float facing,mass,baseHeight,baseWidth,armedUntil=-1f,bicycleUntil=-1f,lastBicycle=-10f,knockedUntil=-1f,started,recoverUntil=-1f;float standSince=-1f;float magneticShotUntil=-1f;bool magneticPower;bool jumped,turned,magneticHop,bicycleStruck,bicycleOpportunity;float landedAt=-1f,getupAt=-1f,standingBodyHeight;Vector3 mirrorScale;
    FieldInfo[] poseTargets;Rigidbody2D[] poseBones;float[] poseAngles,poseSpeeds,poseDesired;bool poseOwned;float strikeAt=-1f;Vector2 strikeLeg;float observedAngle,observedAt=-1f,observedSpin;
    int resilienceBlocks,observedP1,observedP2;static SkillRework pendingShot;static float shotAt;static Vector2 shotOrigin;static bool shotSeparated;
    float nativeCannonStarted=-10f,nativeCannonGravity,nativeCannonSpeed;bool nativeCannonLob,nativeCannonFlight;Vector2 nativeCannonTarget;
    bool hookActive;bool[] hookIgnored;float hookReleaseUntil=-1f;Vector2 hookOrigin,hookIncoming;
    static readonly List<SkillRework> all=new List<SkillRework>(2);
    public static bool Has(Component p,int skill){if(p==null)return false;if(AbilityMode.Runtime)return AbilityMode.Has(p,skill);if(!PlayerSkills.Enabled)return false;return p.name=="Fan"?(skill==0||skill==1||skill==5||skill==7||skill==8||skill==10||skill==11):(skill==2||skill==3||skill==4||skill==6||skill==9);}
    public static void Attach(Component p){if(p==null||p.GetComponent<SkillRework>()!=null)return;var s=p.gameObject.AddComponent<SkillRework>();s.player=p;s.facing=p.name=="Fan"?1:-1;var t=p.GetType();s.body=(Rigidbody2D)t.GetField("rb").GetValue(p);s.animator=(Animator)t.GetField("anim").GetValue(p);s.grounded=t.GetField("isOnGround");s.stick=p.GetComponent(t.Assembly.GetType("StickManController"));s.bodyTarget=s.stick.GetType().GetField("body");s.rig=p.GetComponentsInChildren<Rigidbody2D>();s.shapes=p.GetComponentsInChildren<Collider2D>();float bottom=s.body.position.y,top=bottom,min=s.body.position.x,max=min;foreach(var b in s.rig)s.mass+=b.mass;foreach(var c in s.shapes)if(!c.isTrigger){bottom=Mathf.Min(bottom,c.bounds.min.y);top=Mathf.Max(top,c.bounds.max.y);min=Mathf.Min(min,c.bounds.min.x);max=Mathf.Max(max,c.bounds.max.x);}s.standingBodyHeight=s.body.position.y-s.Floor();s.baseHeight=(top-bottom)/s.Scale;s.baseWidth=(max-min)/s.Scale;s.head=p.transform.Find("Head").GetComponent<Collider2D>();var groundObject=GameObject.Find("Down");s.floorShape=groundObject!=null?groundObject.GetComponent<Collider2D>():null;s.manager=UnityEngine.Object.FindObjectOfType(t.Assembly.GetType("GameManager")) as Component;s.score1=s.manager.GetType().GetField("p1Score");s.score2=s.manager.GetType().GetField("p2Score");s.stopping=s.manager.GetType().GetField("isStopping");s.goaling=s.manager.GetType().GetField("isGoaling",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);var football=UnityEngine.Object.FindObjectOfType(t.Assembly.GetType("Ball")) as Component;s.ball=football.GetComponent<Rigidbody2D>();s.ballShape=football.GetComponent<Collider2D>();foreach(UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(t.Assembly.GetType("GoalTrigger"))){var c=(Component)o;if(c.transform.position.x*s.facing>0)s.goal=c.GetComponent<Collider2D>();}all.Add(s);MagneticFoot.Attach(p,true);s.observedP1=(int)s.score1.GetValue(s.manager);s.observedP2=(int)s.score2.GetValue(s.manager);}
    float Scale {get{return Mathf.Abs(transform.lossyScale.x)/.8f;}}float Height {get{return baseHeight*Scale;}}float Width {get{return baseWidth*Scale;}}
    bool RallyLive {get{return player!=null&&ball!=null&&!GameAIMod.BallOutThisRally&&(manager==null||(!(bool)stopping.GetValue(manager)&&!(bool)goaling.GetValue(manager)));}}
    // An action already in the air may finish landing after the whistle; it cannot start or shoot.
    bool Stopped {get{return player==null||ball==null||Time.timeScale<=0||(!turned&&!RallyLive);}}
    bool FeetOnFloor(){if((bool)grounded.GetValue(player))return true;if(floorShape==null)return false;foreach(var c in shapes)if(c.enabled&&!c.isTrigger&&LowerBody(c)&&c.name.IndexOf("Up_Leg",StringComparison.OrdinalIgnoreCase)<0&&(c.IsTouching(floorShape)||Physics2D.Distance(c,floorShape).distance<=.035f*Scale))return true;return false;}
    bool Air {get{return !FeetOnFloor()&&body.position.y>Floor()+Height*.45f;}}
    float Floor(){var floor=GameObject.Find("Down");return floor!=null?floor.GetComponent<Collider2D>().bounds.max.y:body.position.y-Height*.5f;}
    SkillRework Other(){foreach(var s in all)if(s!=null&&s!=this&&s.player!=null&&s.player.gameObject.activeInHierarchy)return s;return null;}
    bool AerialDuel(){var o=Other();return o!=null&&!Stopped&&!o.Stopped&&(Air||o.Air)&&ball.position.y>Mathf.Min(body.position.y,o.body.position.y)+Height*.2f&&Mathf.Abs(body.position.x-o.body.position.x)<(Width+o.Width)*.55f+.25f*Scale;}
    bool LeaderActive {get{if(!RallyLive||manager==null||!Has(player,4))return false;int a=(int)score1.GetValue(manager),b=(int)score2.GetValue(manager);return player.name=="Fan"?a>=b:b>=a;}}
    bool ContactLinked(SkillRework other){
        if(other==null)return false;bool ours=false,theirs=false;
        foreach(var c in shapes)if(c.enabled&&!c.isTrigger){ours|=c.IsTouching(ballShape);foreach(var q in other.shapes)if(q.enabled&&!q.isTrigger&&c.IsTouching(q))return true;}
        if(!ours)return false;foreach(var q in other.shapes)if(q.enabled&&!q.isTrigger)theirs|=q.IsTouching(ballShape);return theirs;
    }
    bool DuelGrounded(){
        if(FeetOnFloor()&&body.position.y-Floor()<Height*.85f)return true;
        if(floorShape==null||Mathf.Abs(body.velocity.y)>1.5f*Scale||Physics2D.Distance(ballShape,floorShape).distance>.035f*Scale)return false;
        foreach(var c in shapes)if(c.enabled&&!c.isTrigger&&LowerBody(c)&&c.name.IndexOf("Up_Leg",StringComparison.OrdinalIgnoreCase)<0&&c.IsTouching(ballShape)&&c.bounds.min.y<Floor()+ballShape.bounds.size.y+.15f*Scale)return true;
        return false;
    }
    bool GroundDuel(){var other=Other();return RallyLive&&other!=null&&other.RallyLive&&DuelGrounded()&&other.DuelGrounded()&&ContactLinked(other);}
    public static float Effective(Component p,int dimension){float level=AbilityMode.Enabled?AbilityMode.For(p).Levels[dimension]:4;var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null)return level;s.ObserveScore();if(s.Stopped)return level;if(dimension==0&&Has(p,8)&&MagneticFoot.AssistReady(p))level+=1f;if(dimension==0&&Has(p,11))level+=s.resilienceBlocks*1.5f;if(dimension==1||dimension==2){if(s.LeaderActive)level+=1.5f;if(Has(p,2)&&s.AerialDuel())level+=1f;if(dimension==1&&Has(p,8)&&s.GroundDuel())level+=2f;}return level;}
    void LeaderContactEffect(){if(!AbilityMode.Runtime&&PlayerSkills.Enabled&&LeaderActive&&ContactLinked(Other()))PlayerSkills.ShowRearLeader(player);}
    public static void OnBallTouch(Collision2D c){
        MagneticFoot.CushionGroundContact(c);ObserveBlockedShot(c);
        if(c!=null&&c.collider!=null&&!c.collider.isTrigger){var touched=c.collider.GetComponentInParent<SkillRework>();foreach(var owner in all)if(owner!=null&&owner.nativeCannonFlight&&(touched!=owner||Time.time-owner.nativeCannonStarted>.08f))owner.nativeCannonFlight=false;}
        if(c!=null&&c.collider!=null&&!c.collider.isTrigger){var touched=c.collider.GetComponentInParent<SkillRework>();foreach(var owner in all)if(owner!=null&&owner.hookActive&&touched!=owner)owner.CancelHook();}
        if(AbilityMode.Runtime||!PlayerSkills.Enabled||c==null||c.collider==null||c.contactCount==0)return;
        var s=c.collider.GetComponentInParent<SkillRework>();if(s==null||s.Stopped||!s.LeaderActive||c.otherCollider!=s.ballShape)return;
        if(c.collider==s.head||s.ball.position.y>=s.body.position.y+s.Height*.15f||s.ContactLinked(s.Other()))PlayerSkills.ShowRearLeader(s.player);
    }
    public static void OnPlayerTouch(Component player,Collision2D c){
        if(AbilityMode.Runtime||!PlayerSkills.Enabled||player==null||c==null||c.collider==null||c.contactCount==0)return;
        var s=player.GetComponent<SkillRework>();var other=c.collider.GetComponentInParent<SkillRework>();
        if(s!=null&&!s.Stopped&&s.LeaderActive&&other!=null&&other!=s)PlayerSkills.ShowRearLeader(player);
    }
    public static float NativeJumpScale(Component p){return AbilityMode.JumpTempo(Effective(p,2))/AbilityMode.JumpTempo(4);}
    public static float ShootingLevel(Component p){return Mathf.Max(0,Effective(p,0));}
    public static void Tackled(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;if(s!=null)s.knockedUntil=Time.time+.85f;}
    public static Vector2 OrdinaryShot(Component p,Vector2 shot){var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null||!Has(p,10)||!s.RallyLive)return shot;PlayerSkills.ShowFormless(p);return shot*1.2f;}
    public static Vector2 RecoveryShotDirection(Component p,Vector2 incoming){if(incoming.sqrMagnitude<.0001f)return Vector2.right*(p.name=="Fan"?1f:-1f);incoming.x=Mathf.Abs(incoming.x)*(p.name=="Fan"?1f:-1f);return incoming.normalized;}
    // Each actual launched shot can earn at most one block; commands and contact duels never earn credit.
    bool TouchingBall(){foreach(var c in shapes)if(c.enabled&&!c.isTrigger&&c.IsTouching(ballShape))return true;return false;}
    bool ShotDuel(SkillRework other){return other!=null&&ContactLinked(other);}
    void ObserveScore(){if(manager==null)return;int a=(int)score1.GetValue(manager),b=(int)score2.GetValue(manager);if(a!=observedP1||b!=observedP2||(bool)goaling.GetValue(manager)){resilienceBlocks=0;if(pendingShot==this)pendingShot=null;}observedP1=a;observedP2=b;}
    public static int ResilienceStacks(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null)return 0;s.ObserveScore();return Has(p,11)?s.resilienceBlocks:0;}
    public static int ResilienceFor(string side){foreach(var s in all)if(s!=null&&s.player!=null&&s.player.name==side)return ResilienceStacks(s.player);return 0;}
    public static void ApplyResilience(int fan,int zhao){if(!LanMultiplayer.IsRemoteSimulation)return;foreach(var s in all)if(s!=null&&s.player!=null){s.ObserveScore();int n=Has(s.player,11)?Mathf.Max(0,s.player.name=="Fan"?fan:zhao):0;if(n>s.resilienceBlocks)PlayerSkills.ShowPassive(s.player,11);s.resilienceBlocks=n;}}
    public static void TrackShot(Component p,Vector2 velocity){
        if(LanMultiplayer.IsRemoteSimulation)return;var s=p!=null?p.GetComponent<SkillRework>():null;pendingShot=null;
        if(s==null||s.Stopped||!s.RallyLive||!Has(p,11)||s.facing*velocity.x<=.1f||s.ShotDuel(s.Other()))return;
        s.ObserveScore();pendingShot=s;shotAt=Time.time;shotOrigin=s.ball.position;shotSeparated=!s.TouchingBall();
    }
    static void ObserveBlockedShot(Collision2D c){
        var s=pendingShot;if(s==null||c==null||c.collider==null||c.contactCount==0||c.collider.isTrigger||Time.timeScale<=0f||LanMultiplayer.IsRemoteSimulation)return;
        if(!s.RallyLive||!Has(s.player,11)||c.otherCollider!=s.ballShape){pendingShot=null;return;}
        var touched=c.collider.GetComponentInParent<SkillRework>();
        if(touched==s){if(shotSeparated)pendingShot=null;return;}
        if(touched==null){
            if(s.goal!=null&&s.goal.transform.parent!=null&&c.collider.transform.IsChildOf(s.goal.transform.parent)){
                pendingShot=null;bool postDeparted=shotSeparated||!s.TouchingBall();
                if(postDeparted&&!s.ShotDuel(s.Other())&&s.facing*(s.ball.position.x-shotOrigin.x)>s.ballShape.bounds.extents.x*.1f){s.ObserveScore();s.resilienceBlocks++;PlayerSkills.ShowPassive(s.player,11);}
            }else if(c.collider!=s.floorShape)pendingShot=null;
            return;
        }
        // A real opponent touch consumes this shot even if it is an excluded close contest.
        pendingShot=null;bool departed=shotSeparated||!s.TouchingBall();
        if(!departed||s.ShotDuel(touched)||s.facing*(s.ball.position.x-shotOrigin.x)<=s.ballShape.bounds.extents.x*.1f)return;
        s.ObserveScore();s.resilienceBlocks++;PlayerSkills.ShowPassive(s.player,11);
    }
    void MonitorShot(){ObserveScore();if(pendingShot!=this)return;if(!RallyLive||!Has(player,11)||ShotDuel(Other())||(Time.time-shotAt>.2f&&ball.velocity.sqrMagnitude<.04f)){pendingShot=null;return;}if(!TouchingBall())shotSeparated=true;}
    public static void ResetShotTracking(){pendingShot=null;}
    bool AwkwardPose(){if(Time.time<=knockedUntil||Time.time<=bicycleUntil||Mathf.Abs(Mathf.DeltaAngle(body.rotation,0))>28f)return true;float a=(float)stick.GetType().GetField("L_Up_Leg").GetValue(stick),b=(float)stick.GetType().GetField("R_Up_Leg").GetValue(stick);return !Air&&Mathf.Abs(Mathf.DeltaAngle(a,b))>85f;}
    public static bool LowerBody(Collider2D c){return c!=null&&(c.name.IndexOf("Leg",StringComparison.OrdinalIgnoreCase)>=0||c.name.IndexOf("Foot",StringComparison.OrdinalIgnoreCase)>=0);}
    public static int Quality(float roll,int shooting=4){return roll<.2f?0:2;}
    public bool Bicycle {get{return Time.time<=bicycleUntil;}}
    bool BicycleRange(float margin=0f){float behind=-facing*(ball.position.x-body.position.x);return behind>-margin&&behind<=Width+margin&&ball.position.y>=body.GetComponent<Collider2D>().bounds.center.y-margin&&ball.position.y<=head.bounds.max.y+head.bounds.size.y+margin;}
    public static float NativeContactForce(Component p){if(AbilityMode.Runtime||!PlayerSkills.Enabled)return 1f;var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null||s.Stopped)return 1f;var other=s.Other();if(other==null)return 1f;return s.ContactLinked(other)?(.6f+Effective(p,1)*.05f)/.8f:1f;}
    public static bool WantsBicycle(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;return s!=null&&!s.Stopped&&s.RallyLive&&Has(p,10)&&!s.Bicycle&&!s.turned&&Time.time-s.lastBicycle>1.25f&&!s.bicycleOpportunity&&s.BicycleRange();}
    public static bool Mirrored(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;return s!=null&&s.turned;}
    public static bool InBicycle(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;return s!=null&&!s.Stopped&&(s.Bicycle||s.turned);}
    public static bool WantsRecoveryShot(Component p){var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null||s.Stopped||!s.RallyLive||!Has(p,10)||!s.AwkwardPose())return false;foreach(var c in s.shapes)if(s.facing*(s.ball.position.x-s.body.position.x)>=0f&&LowerBody(c)&&Vector2.Distance(c.ClosestPoint(s.ball.position),s.ball.position)<s.ballShape.bounds.extents.x+.3f*s.Scale)return true;return false;}
    bool MirrorClearOfBall(){
        // Reflect a query point, never the football. Decline a start that would materialise a limb inside it.
        Vector2 reflected=new Vector2(2f*body.position.x-ball.position.x,ball.position.y);
        float clearance=ballShape.bounds.extents.x+.012f*Scale;
        foreach(var shape in shapes)if(shape.enabled&&!shape.isTrigger&&Vector2.Distance(shape.ClosestPoint(reflected),reflected)<clearance)return false;
        return true;
    }
    bool StrikeContact(Collider2D limb,Vector2 point,Vector2 normal,bool bicycle){
        if(limb==null||!limb.enabled||limb.isTrigger||!LowerBody(limb))return false;
        if(!bicycle&&facing*(ball.position.x-body.position.x)<0f)return false;
        // A thigh/sole strike is valid; a simultaneous torso, back or head contact is never a shot.
        foreach(var shape in shapes)if(shape.enabled&&!shape.isTrigger&&(shape.attachedRigidbody==body||shape==head)){
            ColliderDistance2D gap=Physics2D.Distance(shape,ballShape);if(gap.isOverlapped||gap.distance<.008f*Scale)return false;
        }
        if(bicycle){var box=limb as BoxCollider2D;if(box==null||limb.name.IndexOf("Up_Leg",StringComparison.OrdinalIgnoreCase)>=0)return false;Vector2 local=limb.transform.InverseTransformPoint(point);if(local.y-box.offset.y>-.08f*box.size.y)return false;}
        return true;
    }
    // One probability roll per incoming opportunity; a small exit margin avoids re-rolling on idle sway.
    void AutoBicycle(){var s=this;
        if(!s.RallyLive||!Has(s.player,10)){s.bicycleOpportunity=false;return;}
        if(!s.BicycleRange(.12f*s.Scale)){s.bicycleOpportunity=false;return;}
        if(s.bicycleOpportunity||s.turned||s.Bicycle||Time.time-s.lastBicycle<=1.25f||!s.BicycleRange()||!s.MirrorClearOfBall())return;
        s.bicycleOpportunity=true;s.lastBicycle=Time.time;if(UnityEngine.Random.value<.5f){s.bicycleUntil=Time.time+.9f;s.armedUntil=Time.time+.9f;s.standingBodyHeight=Mathf.Clamp(s.body.position.y-s.Floor(),s.Height*.45f,s.Height*.65f)-.04f*s.Scale;s.bicycleUntil=Time.time+2.4f;s.started=Time.time;s.jumped=false;s.bicycleStruck=false;s.landedAt=s.getupAt=-1f;s.TurnBack();s.BeginHook();PlayerSkills.ShowFormless(s.player);ShotCloseup.Play(s.player);}
    }
    public static void OnAction(Component p,string action){var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null||s.Stopped||!s.RallyLive)return;if(action=="Kick"&&Has(p,8)){s.magneticHop=true;s.magneticShotUntil=Time.time+.8f;s.magneticPower=PowerShot.IsPowerCommand(p);}if(action=="Kick"&&Has(p,10))s.armedUntil=Time.time+.9f;if(action=="Head"&&!s.turned){s.armedUntil=-1f;s.bicycleUntil=-1f;}}
    static readonly string[] poseFields={"head","body","L_Up_Arm","L_Low_Arm","R_Up_Arm","R_Low_Arm","L_Up_Leg","L_Low_Leg","R_Up_Leg","R_Low_Leg"};
    void BeginPose(){
        int n=poseFields.Length;poseTargets=new FieldInfo[n];poseBones=new Rigidbody2D[n];poseAngles=new float[n];poseSpeeds=new float[n];poseDesired=new float[n];
        Array muscles=(Array)stick.GetType().GetField("muscles").GetValue(stick);
        for(int i=0;i<n;i++){poseTargets[i]=stick.GetType().GetField(poseFields[i]);object muscle=muscles.GetValue(i);poseBones[i]=(Rigidbody2D)muscle.GetType().GetField("muscle").GetValue(muscle);poseAngles[i]=poseDesired[i]=poseBones[i].rotation;}
        poseOwned=true;strikeAt=-1f;observedAngle=body.rotation;observedAt=Time.fixedTime;observedSpin=0f;
    }
    void ReleasePose(){poseOwned=false;}

    void PoseBone(string field,float target){int i=Array.IndexOf(poseFields,field);poseDesired[i]=target;}
    void ApplyPose(){
        float dt=Mathf.Max(.0001f,Time.deltaTime);
        for(int i=0;i<poseFields.Length;i++){
            float rate=i<2?450f:i<6?950f:1800f;float smooth=i<2?.035f:.022f;
            float before=poseAngles[i];float after=Mathf.SmoothDampAngle(before,poseDesired[i],ref poseSpeeds[i],smooth,rate,dt);poseAngles[i]=Mathf.MoveTowardsAngle(before,after,rate*dt);
            poseTargets[i].SetValue(stick,poseAngles[i]);
        }
    }
    static float Ease(float a,float b,float t){t=Mathf.Clamp01(t);return Mathf.Lerp(a,b,t*t*t*(10f+t*(-15f+6f*t)));}
    static float MotionCurve(float t,float[] times,float[] values){
        if(t<=times[0])return values[0];int n=times.Length;if(t>=times[n-1])return values[n-1];
        for(int i=0;i<n-1;i++)if(t<=times[i+1]){float h=times[i+1]-times[i],u=(t-times[i])/h,u2=u*u,u3=u2*u;float left=i==0?0:(values[i+1]-values[i-1])/(times[i+1]-times[i-1]);float right=i+2==n?0:(values[i+2]-values[i])/(times[i+2]-times[i]);return (2*u3-3*u2+1)*values[i]+(u3-2*u2+u)*h*left+(-2*u3+3*u2)*values[i+1]+(u3-u2)*h*right;}return values[n-1];
    }
    static readonly float[] poseTimes={0f,.08f,.22f,.42f,.65f};
    static readonly float[] torsoCurve={0f,35f,86f,99f,90f};
    // Shoulder leads the throw; the bent forearm follows instead of moving as a rigid rod.
    // Separate beats keep the launch, aerial counterbalance and back-landing preparation visible.
    static readonly float[] armTimes={0f,.075f,.16f,.27f,.40f,.56f,.72f};
    static readonly float[] leftArmCurve={5f,28f,8f,-22f,-22f,-10f,-8f};
    static readonly float[] leftElbowCurve={-22f,-48f,-52f,-42f,-30f,-22f,-12f};
    static readonly float[] trailUpperCurve={0f,35f,75f,115f,140f};
    static readonly float[] trailLowerCurve={0f,85f,130f,160f,165f};
    float FlipAngle(){float turn=MotionCurve(Time.time-started,poseTimes,torsoCurve);if(landedAt>=0)turn=90f;if(getupAt>=0)turn=Ease(90f,0f,(Time.time-getupAt)/.42f);return -facing*turn;}
    public static void BeforeMuscles(Component p){
        var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null||s.Stopped||LanMultiplayer.IsRemoteSimulation)return;
        if(!s.turned){s.ReleasePose();return;}if(!s.poseOwned)s.BeginPose();
        if(!s.Bicycle){foreach(string field in poseFields)s.PoseBone(field,0f);s.ApplyPose();return;}
        float t=Time.time-s.started,target=s.FlipAngle(),landing=s.landedAt<0?0f:Ease(0f,1f,(Time.time-s.landedAt)/.14f),stand=s.getupAt<0?0f:Ease(0f,1f,(Time.time-s.getupAt)/.40f);
        s.PoseBone("body",target);s.PoseBone("head",target+s.facing*Ease(0f,8f,t/.25f)*(1f-stand));
        float left=MotionCurve(t,armTimes,leftArmCurve);
        float leftElbow=MotionCurve(Mathf.Max(0f,t-.025f),armTimes,leftElbowCurve);
        // Do not flatten the arms when the legs begin to tuck. Let the elbows follow through,
        // then soften into the recovery pose only after the back has actually touched down.
        float armSettle=s.landedAt<0?0f:Ease(0f,1f,(Time.time-s.landedAt)/.16f);
        s.PoseBone("L_Up_Arm",target+s.facing*Mathf.Lerp(left,-8f,armSettle)*(1f-stand));s.PoseBone("L_Low_Arm",target+s.facing*Mathf.Lerp(left+leftElbow,-20f,armSettle)*(1f-stand));
        // The inner (far) hand stays raised against gravity throughout the aerial action.
        // Keeping this arm in world space avoids dropping it as the torso turns horizontal.
        s.PoseBone("R_Up_Arm",Mathf.LerpAngle(Mathf.LerpAngle(s.facing*125f,target+s.facing*8f,armSettle),0f,stand));s.PoseBone("R_Low_Arm",Mathf.LerpAngle(Mathf.LerpAngle(s.facing*100f,target-s.facing*12f,armSettle),0f,stand));
        float upper=-s.facing*MotionCurve(t,poseTimes,trailUpperCurve),lower=-s.facing*MotionCurve(t,poseTimes,trailLowerCurve);
        // Keep the proven approach to the real ball contact, then open into the held aerial silhouette.
        float trailHold=s.strikeAt<0?Ease(0f,1f,(t-.23f)/.12f):Ease(0f,1f,(Time.time-s.strikeAt)/.12f);
        upper=Mathf.LerpAngle(upper,-s.facing*105f,trailHold);lower=Mathf.LerpAngle(lower,-s.facing*115f,trailHold);
        s.PoseBone("R_Up_Leg",Mathf.LerpAngle(Mathf.LerpAngle(upper,-s.facing*140f,landing),0f,stand));s.PoseBone("R_Low_Leg",Mathf.LerpAngle(Mathf.LerpAngle(lower,-s.facing*165f,landing),0f,stand));
        if(s.RallyLive&&!s.bicycleStruck&&s.landedAt<0&&t<.68f){MagneticFoot.AimAir(p,s.ball.position+s.ball.velocity*.04f-Vector2.right*s.facing*s.ballShape.bounds.extents.x*.5f);s.poseDesired[6]=(float)s.poseTargets[6].GetValue(s.stick);s.poseDesired[7]=(float)s.poseTargets[7].GetValue(s.stick);}
        else{
            float follow=s.strikeAt<0?1f:Mathf.Clamp01((Time.time-s.strikeAt)/.16f);
            float up=s.strikeAt<0?-s.facing*155f:Mathf.LerpAngle(s.strikeLeg.x,-s.facing*155f,Ease(0f,1f,follow));float low=s.strikeAt<0?-s.facing*165f:Mathf.LerpAngle(s.strikeLeg.y,-s.facing*165f,Ease(0f,1f,follow));
            s.PoseBone("L_Up_Leg",Mathf.LerpAngle(Mathf.LerpAngle(up,-s.facing*150f,landing),0f,stand));s.PoseBone("L_Low_Leg",Mathf.LerpAngle(Mathf.LerpAngle(low,-s.facing*170f,landing),0f,stand));
        }
        float split=Mathf.DeltaAngle(s.poseDesired[6],s.poseDesired[8]);if(Mathf.Abs(split)>100f){s.poseDesired[8]=s.poseDesired[6]+Mathf.Sign(split)*100f;s.poseDesired[9]=s.poseDesired[8]+(s.strikeAt<0?-s.facing*55f:s.facing*20f);}
        s.ApplyPose();
    }
    void BeginHook(){
        hookOrigin=ball.position;hookIncoming=ball.velocity;hookActive=true;hookReleaseUntil=-1f;
        hookIgnored=new bool[shapes.Length];for(int i=0;i<shapes.Length;i++){hookIgnored[i]=Physics2D.GetIgnoreCollision(shapes[i],ballShape);if(!shapes[i].isTrigger)Physics2D.IgnoreCollision(shapes[i],ballShape,true);}
    }
    void RestoreHookCollisions(){if(hookIgnored!=null){for(int i=0;i<shapes.Length;i++)if(shapes[i]!=null&&ballShape!=null&&!shapes[i].isTrigger)Physics2D.IgnoreCollision(shapes[i],ballShape,hookIgnored[i]);hookIgnored=null;}hookReleaseUntil=-1f;}
    void EndHook(bool released=false){hookActive=false;if(released)hookReleaseUntil=Time.time+.16f;else RestoreHookCollisions();}
    void CancelHook(){if(!hookActive)return;EndHook();armedUntil=-1f;bicycleStruck=true;}
    Vector2 HookInstep(out Vector2 normal){
        var foot=poseBones[7].GetComponent<BoxCollider2D>();float side=Vector2.Dot(foot.transform.TransformVector(Vector2.right),Vector2.right*facing)>=0?1f:-1f;
        normal=((Vector2)foot.transform.TransformVector(Vector2.right*side)).normalized;
        return foot.transform.TransformPoint(foot.offset+new Vector2(side*foot.size.x*.5f,-foot.size.y*.35f));
    }
    void AssistHook(){
        if(!hookActive)return;if(!RallyLive||!Bicycle||landedAt>=0||bicycleStruck||!Has(player,10)){CancelHook();return;}
        float t=Time.time-started;Vector2 normal;Vector2 instep=HookInstep(out normal),target=instep+normal*(ballShape.bounds.extents.x+.006f*Scale);
        float u=Ease(0f,1f,(t-.035f)/.30f);Vector2 drift=hookOrigin+Vector2.ClampMagnitude(hookIncoming,8f*Scale)*Mathf.Min(t,.08f);
        // A short, continuous skill path joins the raised instep. No torso or ordinary recovery shot owns it.
        ball.position=Vector2.Lerp(drift,target,u);ball.velocity=Vector2.zero;ball.angularVelocity=0f;
        bool raised=instep.y>body.position.y+.12f*Scale;
        if(t>=.335f&&raised){ReleaseShot(true);}
    }
    void Update(){if(Time.timeScale<=0f&&hookActive)CancelHook();}
    void FixedUpdate(){ObserveScore();if(Stopped){Clear();return;}if(LanMultiplayer.IsRemoteSimulation)return;
        if(hookReleaseUntil>=0f&&Time.time>=hookReleaseUntil)RestoreHookCollisions();
        MonitorShot();NativeCannonFlight();
        AutoBicycle();
        LeaderContactEffect();
        if(turned&&Time.fixedTime>observedAt){observedSpin=Mathf.DeltaAngle(observedAngle,body.rotation)/(Time.fixedTime-observedAt);observedAngle=body.rotation;observedAt=Time.fixedTime;}
        if(magneticHop){magneticHop=false;float lift=ball.position.y-body.position.y;if(!Bicycle&&(bool)grounded.GetValue(player)&&facing*(ball.position.x-body.position.x)>0&&Mathf.Abs(ball.position.x-body.position.x)<1.3f*Scale&&lift>.1f*Scale&&lift<Height){float vy=Mathf.Min(5.5f*Scale,Mathf.Sqrt(2f*Mathf.Abs(Physics2D.gravity.y*body.gravityScale)*Mathf.Max(.05f*Scale,lift-.1f*Scale)));foreach(var limb in rig)limb.AddForce(Vector2.up*Mathf.Max(0,vy-body.velocity.y)*limb.mass,ForceMode2D.Impulse);}}
        if(!RallyLive){EndHook();armedUntil=magneticShotUntil=-1f;magneticHop=false;bicycleOpportunity=false;bicycleStruck=true;}
        if(Bicycle){
            float elapsed=Time.time-started;
            if(!jumped&&elapsed>.035f){jumped=true;float needed=Mathf.Clamp(ball.position.y-body.position.y+Height*.10f,Height*.18f,Height*.32f);float rise=Mathf.Min(16f*Scale,Mathf.Sqrt(2f*Mathf.Abs(Physics2D.gravity.y*body.gravityScale)*needed));float vy=body.velocity.y;foreach(var limb in rig)limb.AddForce(Vector2.up*Mathf.Max(0,rise-vy)*limb.mass,ForceMode2D.Impulse);}
            if(elapsed>.12f&&!bicycleStruck&&Mathf.Abs(Mathf.DeltaAngle(body.rotation,0))>50f)foreach(var c in shapes)if(LowerBody(c)&&Vector2.Distance(c.ClosestPoint(ball.position),ball.position)<ballShape.bounds.extents.x+.15f*Scale){ShotCloseup.End(player);break;}
            if(poseOwned){
                Vector2 center=Vector2.zero,velocity=Vector2.zero;foreach(var limb in rig){center+=limb.position*limb.mass;velocity+=limb.velocity*limb.mass;}center/=mass;velocity/=mass;
                float inertia=0f,momentum=0f;foreach(var limb in rig){Vector2 off=limb.position-center,dv=limb.velocity-velocity;inertia+=limb.inertia+limb.mass*off.sqrMagnitude;momentum+=limb.inertia*limb.angularVelocity*Mathf.Deg2Rad+limb.mass*(off.x*dv.y-off.y*dv.x);}
                float actual=momentum/Mathf.Max(.001f,inertia)*Mathf.Rad2Deg;float wanted=Mathf.Clamp(Mathf.DeltaAngle(body.rotation,poseAngles[1])*8f+poseSpeeds[1],-320f,320f);float dw=Mathf.Clamp(wanted-actual,-1400f*Time.fixedDeltaTime,1400f*Time.fixedDeltaTime)*Mathf.Deg2Rad;
                foreach(var limb in rig){Vector2 off=limb.position-center;limb.AddForce(new Vector2(-off.y,off.x)*dw*limb.mass,ForceMode2D.Impulse);limb.AddTorque(dw*limb.inertia,ForceMode2D.Impulse);}
            }
            AssistHook();
            Collider2D torso=body.GetComponent<Collider2D>();var floorObject=GameObject.Find("Down");Collider2D floor=floorObject!=null?floorObject.GetComponent<Collider2D>():null;
            bool backDown=elapsed>.35f&&body.velocity.y<1f*Scale&&Mathf.Abs(Mathf.DeltaAngle(body.rotation,-facing*90f))<28f&&floor!=null&&torso.IsTouching(floor);
            if(landedAt<0&&backDown){landedAt=Time.time;armedUntil=-1f;ShotCloseup.End(player);}
            if(landedAt>=0&&getupAt<0&&Time.time-landedAt>.065f)getupAt=Time.time;
            if(getupAt>=0){float goalY=Floor()+standingBodyHeight;float vy=Mathf.Clamp((goalY-body.position.y)*9f,-2f*Scale,3.2f*Scale);float dv=Mathf.Clamp(vy-body.velocity.y,-36f*Scale*Time.fixedDeltaTime,36f*Scale*Time.fixedDeltaTime);foreach(var limb in rig)limb.AddForce(Vector2.up*dv*limb.mass,ForceMode2D.Impulse);if(Time.time-getupAt>.46f&&Mathf.Abs(Mathf.DeltaAngle(body.rotation,0f))<18f)bicycleUntil=-1f;}
            if(elapsed>2.35f)bicycleUntil=-1f;recoverUntil=Time.time+.5f;
        }
        if(turned&&getupAt>=0)RestoreFacing();
        if(!Bicycle){if(hookActive)CancelHook();RestoreFacing();}
        if(!AbilityMode.Runtime&&PlayerSkills.Enabled&&AerialDuel()){var o=Other();bool contact=false;foreach(var a in shapes)foreach(var b in o.shapes)if(a.IsTouching(b))contact=true;float advantage=Effective(player,1)-Effective(o.player,1);if(contact&&advantage>0){float turn=-facing*Mathf.Min(90,advantage*35)*Time.fixedDeltaTime; o.body.AddTorque(turn*o.body.inertia,ForceMode2D.Impulse);body.AddTorque(-turn*.2f*body.inertia,ForceMode2D.Impulse);}}
    }
    public static Vector2 NativeCannon(Component p,Vector2 incoming){var s=p.GetComponent<SkillRework>();Vector2 baseline=PlayerSkills.CannonVelocity(p.name=="Fan"?1f:-1f,incoming);if(s==null)return baseline;float total=AbilityMode.CannonTotal(p);Vector2 shot=baseline*AbilityMode.CannonSpeed(total);if(total<=12)return shot;
        float radius=s.ballShape.bounds.extents.y;s.nativeCannonTarget=new Vector2(s.facing>0?s.goal.bounds.min.x+radius*.3f:s.goal.bounds.max.x-radius*.3f,BallBoundaryGuard.FindGoalCeiling(s.goal)-radius-(total>=14?.1f:.16f)*s.Scale);s.nativeCannonSpeed=shot.magnitude;s.nativeCannonLob=UnityEngine.Random.value<.5f;
        if(s.nativeCannonLob){Vector2 origin=s.ball.position;float distance=Mathf.Max(.1f*s.Scale,s.facing*(s.nativeCannonTarget.x-origin.x)),height=Mathf.Clamp(distance*.4f,.6f*s.Scale,3f*s.Scale);var opponent=s.Other();if(opponent!=null){float u=s.facing*(opponent.body.position.x-origin.x)/distance;if(u>.08f&&u<.9f){float top=opponent.head.bounds.max.y;float required=(top+radius+.25f*s.Scale-Mathf.Lerp(origin.y,s.nativeCannonTarget.y,u))/Mathf.Max(.1f,4f*u*(1f-u));height=Mathf.Max(height,required);}}height=Mathf.Clamp(height,.12f*s.Scale,Mathf.Min(3.2f*s.Scale,Mathf.Max(.12f*s.Scale,distance*.65f)));
            float slope=(s.nativeCannonTarget.y-origin.y+4f*height)/distance,low=0,high=s.nativeCannonSpeed,vx=0,vy=0;for(int i=0;i<22;i++){vx=(low+high)*.5f;vy=slope*vx+4f*height*vx*vx*Time.fixedDeltaTime/(distance*distance);if(vx*vx+vy*vy>s.nativeCannonSpeed*s.nativeCannonSpeed)high=vx;else low=vx;}s.nativeCannonGravity=-8f*height*vx*vx/(distance*distance);shot=new Vector2(s.facing*vx,vy);
        }else shot=Aim(p,s.ball.position,s.nativeCannonSpeed,8,100f);s.nativeCannonFlight=true;s.nativeCannonStarted=Time.time;return shot;}
    void NativeCannonFlight(){if(!nativeCannonFlight)return;float age=Time.time-nativeCannonStarted;if(!RallyLive||age>2.2f||facing*(nativeCannonTarget.x-ball.position.x)<=ballShape.bounds.extents.x*.25f){nativeCannonFlight=false;return;}Vector2 velocity=nativeCannonLob?(ball.velocity+Vector2.up*nativeCannonGravity*Time.fixedDeltaTime)*(1f+ball.drag*Time.fixedDeltaTime)-Physics2D.gravity*ball.gravityScale*Time.fixedDeltaTime:Aim(player,ball.position,nativeCannonSpeed,8,100f);if(!nativeCannonLob&&BallFlightSafety.WillHit(ball,velocity,player,age)){nativeCannonFlight=false;return;}ball.AddForce((velocity-ball.velocity)*ball.mass,ForceMode2D.Impulse);}
    public static Vector2 Aim(Component p,Vector2 origin,float speed,float level,float accuracy){var s=p.GetComponent<SkillRework>();if(s==null||s.goal==null)return Vector2.right*(p.name=="Fan"?speed:-speed);float radius=s.ballShape.bounds.extents.y;Vector2 target=new Vector2(s.facing>0?s.goal.bounds.min.x+radius*.3f:s.goal.bounds.max.x-radius*.3f,BallBoundaryGuard.FindGoalCeiling(s.goal)-radius-.14f*s.Scale);target.y+=UnityEngine.Random.Range(-1f,1f)*AbilityMode.ShotSpread(level)*s.Scale/Mathf.Max(.1f,accuracy);float dx=Mathf.Abs(target.x-origin.x),dy=target.y-origin.y,g=Mathf.Max(.1f,-Physics2D.gravity.y*s.ball.gravityScale),v2=speed*speed,disc=v2*v2-g*(g*dx*dx+2*dy*v2);if(dx>.01f&&disc>=0){float tangent=(g*dx*dx+2*dy*v2)/(dx*(v2+Mathf.Sqrt(disc))),vx=speed/Mathf.Sqrt(1+tangent*tangent);return new Vector2(s.facing*vx,vx*tangent);}return (target-origin).normalized*speed;}
    static bool NativeMagneticContact(Collision2D c){if(AbilityMode.Runtime||c==null||c.collider==null||LanMultiplayer.IsRemoteSimulation)return false;var s=c.collider.GetComponentInParent<SkillRework>();if(s==null||s.Stopped||!s.RallyLive||Time.time>s.magneticShotUntil||!Has(s.player,8)||!MagneticFoot.AssistReady(s.player))return false;bool sweet=false;for(int i=0;i<c.contactCount;i++){var pt=c.GetContact(i);Vector2 n=pt.normal;if(Vector2.Dot(n,s.ball.position-pt.point)<0)n=-n;sweet|=PlayerSkills.IsStrikeFoot(s.player,c.collider,pt.point,n,true);}if(!sweet||(Has(s.player,5)&&PowerShot.ShootingSkillSpace(s.player)&&!s.magneticPower))return false;float level=ShootingLevel(s.player);float speed=Mathf.Clamp(Mathf.Max(8,s.ball.velocity.magnitude),8,19)*(s.magneticPower?PowerShot.SpeedMultiplier:1f)*AbilityMode.Strength(level)/AbilityMode.Strength(4);Vector2 shot=OrdinaryShot(s.player,Aim(s.player,s.ball.position,speed,level,1));PlayerSkills.CancelShotFlights();BuildPlayer.CancelFlights();s.ball.AddForce((shot-s.ball.velocity)*s.ball.mass,ForceMode2D.Impulse);TrackShot(s.player,shot);s.magneticShotUntil=-1f;PlayerSkills.ShowMagneticFoot(s.player);return true;}
    public static bool OnBallContact(Collision2D c){if(NativeMagneticContact(c))return true;if(c==null||c.collider==null)return false;var s=c.collider.GetComponentInParent<SkillRework>();if(s==null||s.Stopped||!s.RallyLive||LanMultiplayer.IsRemoteSimulation||Time.time>s.armedUntil||!Has(s.player,10)||!LowerBody(c.collider)||c.otherCollider!=s.ballShape)return false;bool sweet=false,valid=false;for(int i=0;i<c.contactCount;i++){var pt=c.GetContact(i);Vector2 n=pt.normal;if(Vector2.Dot(n,s.ball.position-pt.point)<0)n=-n;if(!s.StrikeContact(c.collider,pt.point,n,s.Bicycle))continue;valid=true;sweet|=PlayerSkills.IsStrikeFoot(s.player,c.collider,pt.point,n,true);}if(!valid)return false;if(sweet&&!s.AwkwardPose()){if(AbilityMode.Runtime||PowerShot.IsPowerCommand(s.player)||(Has(s.player,5)&&PowerShot.ShootingSkillSpace(s.player)))return false;s.armedUntil=-1f;PowerShot.CompleteHeader(s.player);Vector2 clean=OrdinaryShot(s.player,s.ball.velocity);PlayerSkills.CancelShotFlights();BuildPlayer.CancelFlights();s.ball.AddForce((clean-s.ball.velocity)*s.ball.mass,ForceMode2D.Impulse);TrackShot(s.player,clean);return true;}
        bool bicycle=s.Bicycle;if(bicycle&&(s.bicycleStruck||s.landedAt>=0||c.collider.name.IndexOf("Up_Leg",StringComparison.OrdinalIgnoreCase)>=0||Time.time-s.started<.08f))return false;return s.ReleaseShot(bicycle);
    }
    bool ReleaseShot(bool bicycle){var s=this;if(!s.RallyLive||s.Stopped||LanMultiplayer.IsRemoteSimulation||(bicycle&&(s.bicycleStruck||s.landedAt>=0)))return false;float level=ShootingLevel(s.player);float normalSpeed=Mathf.Clamp(Mathf.Max(10f,bicycle&&s.hookActive?s.hookIncoming.magnitude:s.ball.velocity.magnitude),10f,19f)*(AbilityMode.Enabled?AbilityMode.Strength(level):AbilityMode.Strength(level)/AbilityMode.Strength(4));Vector2 shot;int quality=-1;
        if(bicycle){quality=Quality(UnityEngine.Random.value,AbilityMode.Enabled?AbilityMode.For(s.player).Levels[0]:4);float speed=normalSpeed*1.2f*(quality==0?.8f:1.2f);shot=quality==0?new Vector2(s.facing*.5f,.86f).normalized*speed:Aim(s.player,s.ball.position,speed,8,1f);}
        else shot=RecoveryShotDirection(s.player,s.ball.velocity)*normalSpeed*1.2f;
        PlayerSkills.CancelShotFlights();BuildPlayer.CancelFlights();PowerShot.CompleteHeader(s.player);s.ball.collisionDetectionMode=CollisionDetectionMode2D.Continuous;s.ball.AddForce((shot-s.ball.velocity)*s.ball.mass,ForceMode2D.Impulse);TrackShot(s.player,shot);if(bicycle){s.strikeAt=Time.time;s.strikeLeg=new Vector2(s.poseBones[6].rotation,s.poseBones[7].rotation);}s.armedUntil=-1f;if(bicycle){s.bicycleStruck=true;s.EndHook(true);}ShotCloseup.End(s.player);PlayerSkills.ShowFormless(s.player);return true;
    }
    void MirrorRig(bool mirror){
        Vector2 center=body.position;Vector2[] velocities=new Vector2[rig.Length];float[] spins=new float[rig.Length];Vector2 average=Vector2.zero;
        for(int i=0;i<rig.Length;i++){velocities[i]=rig[i].velocity;spins[i]=rig[i].angularVelocity;average+=velocities[i]*rig[i].mass;}average/=mass;
        if(mirror)mirrorScale=transform.localScale;
        Vector3 scale=mirror?new Vector3(-mirrorScale.x,mirrorScale.y,mirrorScale.z):mirrorScale;transform.localScale=scale;Physics2D.SyncTransforms();transform.position+=(Vector3)(center-body.position);Physics2D.SyncTransforms();
        for(int i=0;i<rig.Length;i++){rig[i].velocity=new Vector2(2*average.x-velocities[i].x,velocities[i].y);rig[i].angularVelocity=-spins[i];}
    }
    void TurnBack(){if(turned)return;MirrorRig(true);turned=true;BeginPose();LanMultiplayer.Mirror(player.name,true);}
    bool UprightSupported(){return body!=null&&FeetOnFloor()&&body.position.y-Floor()>=Height*.37f&&Mathf.Abs(Mathf.DeltaAngle(body.rotation,0f))<18f;}
    void RestoreFacing(bool force=false){
        if(!turned)return;
        // Actual upright support ends recovery immediately. Residual joint spin must never latch AI input off.
        if(!force&&(!UprightSupported()||(Bicycle&&getupAt<0)))return;
        bicycleUntil=armedUntil=recoverUntil=-1f;standSince=-1f;observedSpin=0f;
        EndHook();ReleasePose();MirrorRig(false);turned=false;LanMultiplayer.Mirror(player.name,false);
    }
    public static void ApplyMirror(Component p,bool on){var s=p!=null?p.GetComponent<SkillRework>():null;if(s==null)return;if(on&&!s.turned){s.MirrorRig(true);s.turned=true;}else if(!on&&s.turned)s.RestoreFacing(true);}
    void LateUpdate(){if(!LanMultiplayer.IsRemoteSimulation&&!Bicycle)RestoreFacing();}
    public static void ResetAll(){ShotCloseup.ClearAll();foreach(var s in all)if(s!=null){if(GameAIMod.BallOutThisRally&&s.turned&&s.isActiveAndEnabled){s.EndHook();s.armedUntil=s.magneticShotUntil=-1f;continue;}s.Clear();}}
    void Clear(){if(pendingShot==this)pendingShot=null;nativeCannonFlight=false;EndHook();ReleasePose();landedAt=getupAt=-1f;bicycleStruck=false;magneticShotUntil=-1f;magneticHop=false;RestoreFacing(true);armedUntil=bicycleUntil=recoverUntil=knockedUntil=-1f;bicycleOpportunity=false;ShotCloseup.End(player);}
    void OnDisable(){Clear();}void OnDestroy(){Clear();all.Remove(this);}
}

// Compatibility entry points for older LAN packets. Cinematic camera and slow motion are removed.
public sealed class ShotCloseup:MonoBehaviour {
    public static bool Active {get{return false;}}
    public static void Play(Component p,bool fromNetwork=false){}
    public static void End(Component p){}
    public static void ClearAll(){}
}
