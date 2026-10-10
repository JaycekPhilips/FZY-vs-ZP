using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
public sealed class BurstSolidProbe : MonoBehaviour
{
    public bool hit;
    public Collider2D target;
    public Transform player;
    void OnCollisionEnter2D(Collision2D c) { if (c.collider == target || (player != null && c.collider.transform.IsChildOf(player))) { hit = true; Debug.Log("BURSTRANGE CONTACT solid=" + c.collider.name); } }
}
public sealed class BurstRangeTests : MonoBehaviour
{
    public static HashSet<KeyCode> Held = new HashSet<KeyCode>();
    public static KeyCode DownKey; public static int DownFrame = -1;
    public static bool ReadKey(KeyCode key) { return Held.Contains(key); }
    public static bool ReadDown(KeyCode key) { return Time.frameCount == DownFrame && key == DownKey; }
    static bool booted;
    static Vector2 beforeContact;
    static public Vector2 BeforePower, AfterPower;
    static public int Launches;
    static public bool NewBandContact;
    public static void BeforeContact(Collision2D c)
    {
        Rigidbody2D rb = c.otherCollider.attachedRigidbody; if (rb != null) beforeContact = rb.velocity;
        BoxCollider2D limb = c.collider as BoxCollider2D;
        if (limb == null || limb.GetComponentInParent<PowerShot>() == null) return;
        Component player = limb.GetComponentInParent<PowerShot>();
        for (int i = 0; i < c.contactCount; i++)
        {
            ContactPoint2D point = c.GetContact(i); Vector2 normal = point.normal;
            if (Vector2.Dot(normal, rb.position - point.point) < 0f) normal = -normal;
            float height = (limb.transform.InverseTransformPoint(point.point).y - limb.offset.y) / limb.size.y;
            bool accepted = PlayerSkills.IsStrikeFoot(player, limb, point.point, normal, true);
            NewBandContact |= player.name == "Zhao" && height > .2f && accepted;
            Debug.Log("BURSTRANGE CONTACT " + limb.name + " height=" + height + " accepted=" + accepted);
        }
    }
    public static void AfterContact(Collision2D c, bool launched) { if (launched) { Launches++; BeforePower = beforeContact; AfterPower = c.otherCollider.attachedRigidbody.velocity; } }
    public static void Boot() { if (booted) return; booted = true; ControlBindings.settingsPath = System.IO.Path.Combine(Application.dataPath,"..","power-test-bindings.ini"); var host = new GameObject("BurstRangeTests"); DontDestroyOnLoad(host); host.AddComponent<BurstRangeTests>(); }
    const BindingFlags IP = BindingFlags.Instance | BindingFlags.NonPublic, SP = BindingFlags.Static | BindingFlags.NonPublic;
    Component fan, zhao, manager;
    Rigidbody2D ball;
    Type playerType;
    int checks, failures;
    void Check(bool ok, string label) { checks++; if (!ok) failures++; Debug.Log((ok ? "BURSTRANGE PASS " : "BURSTRANGE FAIL ") + label); }
    object Field(object target, string name) { return target.GetType().GetField(name, IP).GetValue(target); }
    Rigidbody2D Body(Component p) { return (Rigidbody2D)playerType.GetField("rb").GetValue(p); }
    float JointError(Component p) { float error = 0; foreach (HingeJoint2D j in p.GetComponentsInChildren<HingeJoint2D>()) if (j.connectedBody != null) error = Mathf.Max(error, Vector2.Distance(j.transform.TransformPoint(j.anchor),j.connectedBody.transform.TransformPoint(j.connectedAnchor))); return error; }
    void Move(Component p, Vector2 location) { p.transform.position += (Vector3)(location - Body(p).position); foreach (Rigidbody2D b in p.GetComponentsInChildren<Rigidbody2D>()) { b.velocity = Vector2.zero; b.angularVelocity = 0; } Physics2D.SyncTransforms(); }
    IEnumerator Scene(bool skills)
    {
        Held.Clear(); DownFrame = -1; Launches = 0; NewBandContact = false; ControlBindings.ResetDefaults(); PlayerSkills.Enabled = skills; Time.timeScale = 1;
        typeof(GameAIMod).GetMethod("StartGame",SP).Invoke(null,new object[]{0}); yield return new WaitForSeconds(.25f);
        playerType = Type.GetType("PlayerController, Assembly-CSharp"); fan = GameObject.Find("Fan").GetComponent(playerType); zhao = GameObject.Find("Zhao").GetComponent(playerType);
        manager = FindObjectOfType(Type.GetType("GameManager, Assembly-CSharp")) as Component;
        ball = (FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>(); ball.position = new Vector2(0,3); ball.velocity = Vector2.zero; ball.gravityScale = 0; Physics2D.SyncTransforms();
    }
    IEnumerator Shot(bool skills, string name, bool powered)
    {
        yield return Scene(skills);
        Component p = name == "Fan" ? fan : zhao; float direction = name == "Fan" ? 1f : -1f;
        Move(p,new Vector2(direction * -2f,Body(p).position.y)); Move(name == "Fan" ? zhao : fan,new Vector2(direction * 4f,Body(name == "Fan" ? zhao : fan).position.y));
        Animator anim = p.GetComponent<Animator>(); anim.updateMode = AnimatorUpdateMode.AnimatePhysics; yield return new WaitForSeconds(.5f);
        DownKey = ControlBindings.Get(name == "Fan",powered ? GameControlAction.PowerKick : GameControlAction.Kick); DownFrame = Time.frameCount + 1;
        for(int i=0;i<25;i++){yield return new WaitForFixedUpdate();if(anim.GetCurrentAnimatorStateInfo(0).IsName("Kick")&&anim.GetCurrentAnimatorStateInfo(0).normalizedTime>=.2f)break;}
        float fastest = float.NegativeInfinity; Vector2 location = Vector2.zero;
        foreach(string n in new[]{"L_LowLeg","R_LowLeg"})
        {
            BoxCollider2D c=p.transform.Find(n).GetComponent<BoxCollider2D>();
            foreach(float side in new[]{-1f,1f})
            {
                Vector2 normal=c.transform.TransformVector(Vector2.right*side).normalized;if(normal.x*direction<.1f)continue;
                Vector2 point=c.transform.TransformPoint(c.offset+new Vector2(c.size.x*side*.5f,-c.size.y*.3f));
                float speed=c.attachedRigidbody.GetPointVelocity(point).x*direction;
                if(speed>fastest){fastest=speed;location=point+normal*ball.GetComponent<Collider2D>().bounds.extents.x*.86f;}
            }
        }
        ball.position=location;ball.velocity=Vector2.zero;Physics2D.SyncTransforms();
        float error=0;
        for(int i=0;i<16;i++){yield return new WaitForFixedUpdate();error=Mathf.Max(error,JointError(p));}
        Check(anim.GetCurrentAnimatorStateInfo(0).IsName("Kick") || fastest>.2f,"native kick action actually ran "+name+" powered="+powered+" skills="+skills);
        Check(Launches==(powered?1:0),"one real contact launch, ordinary shot has none "+name+" powered="+powered+" launches="+Launches+" skills="+skills);
        if(powered&&Launches>0)
        {
            if(name=="Fan")Check(Mathf.Abs(AfterPower.magnitude/BeforePower.magnitude-1.2f)<.002f,"Fan real collision ball speed +20% ratio="+AfterPower.magnitude/BeforePower.magnitude);
            else
            {
                float expected=Mathf.Clamp(Mathf.Max(17f,BeforePower.magnitude*1.65f),17f,23f)*1.2f*(skills?1.1f:1f);
                Check(Mathf.Abs(AfterPower.x+expected)<.01f,"Zhao real contact power speed skills="+skills+" vx="+AfterPower.x+" expected="+expected);
                Check(AfterPower.y<=.3f,"Zhao powered shot is driven low even in air vy="+AfterPower.y);
                if(skills)Check(((float[])Field(zhao.GetComponent<PlayerSkills>(),"effects"))[4]>=0,"keypad 0 displays burst skill/highlight");
            }
        }
        Check(error<.36f,"shooting keeps the connected skeleton "+name+" error="+error);
        if(!skills)Check(p.GetComponent<PlayerSkills>()==null,"classic power shot requires no special skill component");
    }
    IEnumerator InputGuards()
    {
        yield return Scene(true);
        DownFrame=Time.frameCount;DownKey=KeyCode.Keypad0;
        Check(GameAIMod.GetButtonDown(KeyCode.KeypadEnter,zhao)&&!GameAIMod.GetButtonDown(KeyCode.Keypad0,zhao),"keypad 0 selects kick, never header");
        DownKey=KeyCode.RightShift;Check(GameAIMod.GetButtonDown(KeyCode.Keypad0,zhao)&&!GameAIMod.GetButtonDown(KeyCode.KeypadEnter,zhao),"right Shift selects header, never kick");
        DownKey=KeyCode.L;Check(GameAIMod.GetButtonDown(KeyCode.K,fan)&&!GameAIMod.GetButtonDown(KeyCode.J,fan),"L selects Fan powered native kick");
        DownFrame=-1;
        PlayerSkills.SetAction(zhao.GetComponent<Animator>(),"Kick",zhao);yield return null;PlayerSkills.SetAction(zhao.GetComponent<Animator>(),"Kick",zhao);
        Check((float)Field(zhao.GetComponent<PlayerSkills>(),"kickUntil")<Time.time,"double ordinary shooting no longer arms burst");
        PowerShot.Request(fan);PlayerSkills.SetAction(fan.GetComponent<Animator>(),"Kick",fan);PlayerSkills.ResetAll();
        Check((float)Field(fan.GetComponent<PowerShot>(),"armedUntil")<Time.time,"round reset clears powered shooting request");
    }
    IEnumerator RangeRules()
    {
        yield return Scene(true);
        foreach (string name in new[]{"L_LowLeg","R_LowLeg"})
        {
            BoxCollider2D f = fan.transform.Find(name).GetComponent<BoxCollider2D>();
            BoxCollider2D z = zhao.transform.Find(name).GetComponent<BoxCollider2D>();
            foreach (float height in new[]{-.49f,-.45f,-.3f,0f,.19f,.3f,.45f,.49f})
            foreach (Vector2 localNormal in new[]{Vector2.left,Vector2.right,Vector2.down,new Vector2(.4f,-.9f).normalized})
            {
                Vector2 fp=f.transform.TransformPoint(f.offset+new Vector2(f.size.x*.5f,f.size.y*height));
                Vector2 zp=z.transform.TransformPoint(z.offset+new Vector2(z.size.x*.5f,z.size.y*height));
                bool expected=PlayerSkills.IsStrikeFoot(fan,f,fp,f.transform.TransformDirection(localNormal));
                bool actual=PlayerSkills.IsStrikeFoot(zhao,z,zp,z.transform.TransformDirection(localNormal),true);
                Check(actual==expected,"same Fan/Zhao contact region "+name+" height="+height+" normal="+localNormal);
            }
            Vector2 upper=z.transform.TransformPoint(z.offset+new Vector2(z.size.x*.5f,z.size.y*.34f));
            Vector2 side=z.transform.TransformDirection(Vector2.right);
            Check(PlayerSkills.IsStrikeFoot(zhao,z,upper,side,true),"expanded shin accepted "+name);
            Check(!PlayerSkills.IsStrikeFoot(zhao,z,upper,side),"classic shin range unchanged "+name);
        }
        foreach(string name in new[]{"Body","Head","L_Up_Leg","R_Up_Leg"})
        {
            Transform t=zhao.transform.Find(name); Collider2D shape=t==null?null:t.GetComponent<Collider2D>();
            Check(shape!=null&&!PlayerSkills.IsStrikeFoot(zhao,shape,shape.bounds.center,Vector2.left,true),"reject body/head/thigh "+name);
        }
        // Independent foot colliders use the same rule if present in a model.
        GameObject foot=new GameObject("Foot");foot.transform.SetParent(zhao.transform,false);BoxCollider2D box=foot.AddComponent<BoxCollider2D>();
        Check(PlayerSkills.IsStrikeFoot(zhao,box,foot.transform.TransformPoint(new Vector2(.5f,0)),foot.transform.TransformDirection(Vector2.right),true),"independent instep accepted");
        Check(!PlayerSkills.IsStrikeFoot(zhao,box,foot.transform.TransformPoint(new Vector2(0,-.5f)),foot.transform.TransformDirection(Vector2.down),true),"independent sole rejected");
        Check(!PlayerSkills.IsStrikeFoot(zhao,box,foot.transform.position,Vector2.right),"classic independent foot unchanged");
        Check(!PlayerSkills.IsStrikeFoot(fan,box,foot.transform.position,Vector2.right,true),"opponent limb cannot trigger skill");
        Destroy(foot);
    }
    IEnumerator UpperContact(bool skills)
    {
        yield return Scene(skills);
        Move(zhao,new Vector2(2,Body(zhao).position.y));Move(fan,new Vector2(-4,Body(fan).position.y));
        Animator anim=zhao.GetComponent<Animator>();anim.updateMode=AnimatorUpdateMode.AnimatePhysics;yield return new WaitForSeconds(.5f);
        DownKey=KeyCode.Keypad0;DownFrame=Time.frameCount+1;
        for(int i=0;i<25;i++){yield return new WaitForFixedUpdate();if(anim.GetCurrentAnimatorStateInfo(0).IsName("Kick")&&anim.GetCurrentAnimatorStateInfo(0).normalizedTime>=.2f)break;}
        Check(anim.GetCurrentAnimatorStateInfo(0).IsName("Kick"),"expanded collision uses keypad 0 native kick skills="+skills);
        BoxCollider2D limb=null;Vector2 point=Vector2.zero,normal=Vector2.zero;float best=float.NegativeInfinity;
        foreach(string n in new[]{"L_LowLeg","R_LowLeg"})foreach(float side in new[]{-1f,1f})
        {
            BoxCollider2D c=zhao.transform.Find(n).GetComponent<BoxCollider2D>();Vector2 candidate=c.transform.TransformDirection(Vector2.right*side).normalized;
            Vector2 p=c.transform.TransformPoint(c.offset+new Vector2(c.size.x*side*.5f,c.size.y*.34f));
            if(candidate.x<-.1f && -p.x>best){best=-p.x;limb=c;point=p;normal=candidate;}
        }
        // Hold the native kick pose in this isolated test so a rejected upper
        // shin collision cannot later slide onto the already eligible instep.
        anim.enabled=false;((Behaviour)zhao).enabled=false;
        ((Behaviour)zhao.GetComponent(Type.GetType("StickManController, Assembly-CSharp"))).enabled=false;
        foreach(Rigidbody2D b in zhao.GetComponentsInChildren<Rigidbody2D>())b.bodyType=RigidbodyType2D.Static;
        if(skills)zhao.GetComponent<PlayerSkills>().enabled=false;
        float radius=ball.GetComponent<Collider2D>().bounds.extents.x;
        ball.position=point+normal*(radius+.005f);ball.velocity=-normal*2f;Physics2D.SyncTransforms();
        for(int i=0;i<5;i++)yield return new WaitForFixedUpdate();
        Check(NewBandContact,"real collision in newly eligible shin band skills="+skills);
        Check(Launches==(skills?1:0),"upper shin activates only burst, once skills="+skills+" launches="+Launches);
        if(skills&&Launches>0)
        {
            float expected=Mathf.Clamp(Mathf.Max(17f,BeforePower.magnitude*1.65f),17f,23f)*1.2f*1.1f;
            Check(Mathf.Abs(AfterPower.x+expected)<.01f,"expanded contact keeps burst speed");
            Check(AfterPower.y<=.3f,"expanded airborne contact keeps low flight");
            Check(((float[])Field(zhao.GetComponent<PlayerSkills>(),"effects"))[4]>=0,"expanded contact shows burst highlight");
        }
    }
    IEnumerator RangeGuards()
    {
        yield return Scene(true);Move(zhao,new Vector2(2,Body(zhao).position.y));Move(fan,new Vector2(-4,Body(fan).position.y));
        BoxCollider2D limb=zhao.transform.Find("L_LowLeg").GetComponent<BoxCollider2D>();
        Vector2 point=limb.transform.TransformPoint(limb.offset+new Vector2(limb.size.x*.5f,limb.size.y*.34f)),normal=limb.transform.TransformDirection(Vector2.right);
        PowerShot power=zhao.GetComponent<PowerShot>();MethodInfo strike=typeof(PowerShot).GetMethod("Strike",IP);
        ball.position=new Vector2(1,1);Physics2D.SyncTransforms();
        Check(!(bool)strike.Invoke(power,new object[]{limb,point,normal}),"expanded range still requires powered instruction");
        PowerShot.Request(zhao);PowerShot.OnAction(zhao,"Kick");
        ball.position=new Vector2(3,1);Check(!(bool)strike.Invoke(power,new object[]{limb,point,normal}),"behind player cannot launch");
        ball.position=new Vector2(1,Body(zhao).position.y);Move(fan,new Vector2(.4f,Body(fan).position.y));
        Check(!(bool)strike.Invoke(power,new object[]{limb,point,normal}),"contest cannot launch expanded skill");
        Move(fan,new Vector2(-4,Body(fan).position.y));
        Vector2 sole=limb.transform.TransformPoint(limb.offset+new Vector2(0,-limb.size.y*.5f));
        Check(!(bool)strike.Invoke(power,new object[]{limb,sole,(Vector2)limb.transform.TransformDirection(Vector2.down)}),"powered sole contact still rejected");
        typeof(PowerShot).GetField("armedUntil",IP).SetValue(power,Time.time-1);
        Check(!(bool)strike.Invoke(power,new object[]{limb,point,normal}),"expired contact window rejected");
    }
    void StartCurve(PlayerSkills skill, Collider2D goal)
    {
        typeof(PlayerSkills).GetMethod("PrepareGoalCurve",IP).Invoke(skill,new object[]{goal});
        typeof(PlayerSkills).GetField("curveStarted",IP).SetValue(skill,Time.time);typeof(PlayerSkills).GetField("shotUntil",IP).SetValue(skill,Time.time+3f);
        typeof(PlayerSkills).GetMethod("EnableContinuousBall",IP).Invoke(skill,null);
    }
    Collider2D RightGoal(){foreach(UnityEngine.Object o in FindObjectsOfType(Type.GetType("GoalTrigger, Assembly-CSharp")))if(((Component)o).transform.position.x>0)return ((Component)o).GetComponent<Collider2D>();return null;}
    IEnumerator CurveAndCollision()
    {
        yield return Scene(true);PlayerSkills skill=fan.GetComponent<PlayerSkills>();
        ball.position=new Vector2(-3,-.6f);ball.velocity=new Vector2(8,0);ball.gravityScale=1;Physics2D.SyncTransforms();StartCurve(skill,RightGoal());
        float duration=(float)Field(skill,"curveTravelTime"),height=(float)Field(skill,"curveArcHeight");Vector2 origin=(Vector2)Field(skill,"curveOrigin"),destination=(Vector2)Field(skill,"curveDestination");
        for(int i=1;i<=3;i++)
        {
            float u=i*.15f;Vector2 point=Vector2.Lerp(origin,destination,u);point.y+=height*4f*u*(1-u)+.18f*Mathf.Sin(u*Mathf.PI*4)*4f*u*(1-u);
            Vector2 actual=(Vector2)typeof(PlayerSkills).GetMethod("GoalCurveVelocityAt",IP).Invoke(skill,new object[]{duration*u/.9f,point});
            Vector2 former=(Vector2)typeof(PlayerSkills).GetMethod("GoalCurveProfileVelocityAt",IP).Invoke(skill,new object[]{duration*u,point});
            Check(Vector2.Distance(actual,former*.9f)<.01f,"same curve at 90% speed, spatial sample="+u);
        }
        // A real solid body stands on the planned route. Keep its collider;
        // disabling simulation would make a tunneling test meaningless.
        BoxCollider2D obstacle=zhao.transform.Find("Body").GetComponent<BoxCollider2D>();
        Vector2 launch=new Vector2(1,obstacle.bounds.center.y);ball.position=launch;ball.velocity=new Vector2(18,0);ball.gravityScale=0;Physics2D.SyncTransforms();StartCurve(skill,RightGoal());
        Rigidbody2D target=obstacle.attachedRigidbody;Move(zhao,new Vector2(2,Body(zhao).position.y));
        foreach(Rigidbody2D limb in zhao.GetComponentsInChildren<Rigidbody2D>())limb.bodyType=RigidbodyType2D.Static;
        zhao.GetComponent<Animator>().enabled=false;((Behaviour)zhao.GetComponent(Type.GetType("StickManController, Assembly-CSharp"))).enabled=false;
        ball.position=new Vector2(obstacle.bounds.min.x-1.0f,obstacle.bounds.center.y);ball.velocity=new Vector2(18,0);Physics2D.SyncTransforms();StartCurve(skill,RightGoal());
        BurstSolidProbe probe=ball.gameObject.AddComponent<BurstSolidProbe>();probe.target=obstacle;probe.player=zhao.transform;
        // Put the reference curve along this real opponent contact, so the
        // collision itself (not merely an unrelated missed arc) is exercised.
        typeof(PlayerSkills).GetField("curveDestination",IP).SetValue(skill,new Vector2(5,ball.position.y));typeof(PlayerSkills).GetField("curveArcHeight",IP).SetValue(skill,0f);
        float initialSide=obstacle.bounds.min.x;bool crossed=false;
        for(int i=0;i<14;i++){yield return new WaitForFixedUpdate();crossed|=ball.position.x>obstacle.bounds.max.x;}
        Check(probe.hit,"leaf ball physically hits opponent body");Check(!crossed,"leaf ball never tunnels through body");
        Check((float)Field(skill,"curveStarted")<0,"solid collision ends curve drive instead of pushing through");
        Check(ball.collisionDetectionMode==CollisionDetectionMode2D.Continuous,"skill ball uses continuous detection");
    }
    IEnumerator Retreat()
    {
        yield return Scene(true);Move(zhao,new Vector2(-3,Body(zhao).position.y));Move(fan,new Vector2(4,Body(fan).position.y));
        ball.position=new Vector2(0,3);yield return new WaitForSeconds(.5f);Held.Add(KeyCode.RightArrow);
        float maxTilt=0,maxError=0,maxSpeed=0;
        Component stick=zhao.GetComponent(Type.GetType("StickManController, Assembly-CSharp"));
        for(int i=0;i<100;i++)
        {
            yield return new WaitForFixedUpdate(); maxError=Mathf.Max(maxError,JointError(zhao));
            float targetAngle=(float)stick.GetType().GetField("body").GetValue(stick);
            maxTilt=Mathf.Max(maxTilt,Mathf.Abs(Mathf.DeltaAngle(Body(zhao).rotation,targetAngle)));
            maxSpeed=Mathf.Max(maxSpeed,Body(zhao).velocity.x);
        }
        Check(maxSpeed>1,"emergency retreat keeps moving at real speed="+maxSpeed);Check(maxTilt<35f,"emergency retreat remains balanced deviation="+maxTilt);Check(maxError<.35f,"retreat limbs remain joined error="+maxError);Held.Clear();
    }
    IEnumerator Tackle()
    {
        foreach(bool air in new[]{false,true})
        {
            yield return Scene(true);float y=air?1.3f:Body(fan).position.y;
            Move(fan,new Vector2(-1.2f,y));Move(zhao,new Vector2(1.2f,y));ball.position=new Vector2(0,3);
            foreach(Rigidbody2D b in fan.GetComponentsInChildren<Rigidbody2D>())b.velocity=new Vector2(3,air?2:0);
            foreach(Rigidbody2D b in zhao.GetComponentsInChildren<Rigidbody2D>())b.velocity=new Vector2(-3,air?2:0);
            Held.Add(KeyCode.D);Held.Add(KeyCode.LeftArrow);float maxError=0,maxDeviation=0;bool fired=false;float firstHit=-1f;
            for(int i=0;i<130;i++)
            {
                yield return new WaitForFixedUpdate();maxError=Mathf.Max(maxError,JointError(fan),JointError(zhao));
                if(!fired&&((float[])Field(zhao.GetComponent<PlayerSkills>(),"effects"))[3]>=0)
                {
                    fired=true;
                    firstHit=Time.time;
                    // Measure one impact's recovery, not a later legitimate
                    // skill retrigger during sustained body contact.
                    foreach(PowerTackleContact relay in zhao.GetComponentsInChildren<PowerTackleContact>())Destroy(relay);
                }
                maxDeviation=Mathf.Max(maxDeviation,Mathf.Abs(Mathf.DeltaAngle(Body(fan).rotation,0)));
                if(i>=80&&firstHit>=0&&Time.time-firstHit>=.8f)break;
            }
            Check(fired,"stronger tackle uses real body contact air="+air);Check(maxError<.4f,"30% stronger impact keeps skeleton joined error="+maxError);Check(maxDeviation<60,"impact does not fold Fan body angle="+maxDeviation);Check(!(bool)Field(fan.GetComponent<PlayerSkills>(),"weakenedBalance"),"balance recovers promptly after impact");Held.Clear();
        }
        Check(Mathf.Abs(PlayerSkills.TackleGroundSpeed-1.4625f)<.0001f&&Mathf.Abs(PlayerSkills.TackleAirSpeed-2.68125f)<.0001f,"tackle strength +30%, duration unchanged");
    }
    IEnumerator Start()
    {
        Application.runInBackground=true;Application.targetFrameRate=120;QualitySettings.vSyncCount=0;AudioListener.volume=0;
        yield return new WaitForSeconds(.2f);yield return InputGuards();
        foreach(bool skill in new[]{false,true})foreach(string name in new[]{"Fan","Zhao"}){yield return Shot(skill,name,false);yield return Shot(skill,name,true);}
        yield return CurveAndCollision();yield return Retreat();yield return Tackle();
        yield return RangeRules();yield return UpperContact(false);yield return UpperContact(true);yield return RangeGuards();
        Debug.Log("BURSTRANGE COMPLETE checks="+checks+" failures="+failures);Application.Quit(failures==0?0:1);
    }
    void Update(){if(Time.realtimeSinceStartup>120){Debug.LogError("BURSTRANGE TIMEOUT");Application.Quit(2);}}
}
