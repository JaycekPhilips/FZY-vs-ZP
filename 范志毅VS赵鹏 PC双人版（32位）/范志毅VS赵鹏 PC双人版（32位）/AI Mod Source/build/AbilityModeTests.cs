using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
// Focused regression for the current allocation update, rather than the historical full suite.
public sealed class AbilityModeTests : MonoBehaviour
{
    public static bool UseAI;static bool booted;static readonly HashSet<KeyCode> keys=new HashSet<KeyCode>();
    public static bool ReadKey(KeyCode k){return keys.Contains(k);}public static bool ReadDown(KeyCode k){if(!keys.Contains(k))return false;int frame;if(!downFrames.TryGetValue(k,out frame)||frame<0){frame=Time.frameCount;downFrames[k]=frame;}return frame==Time.frameCount;}
    static readonly Dictionary<string,Vector2> launches=new Dictionary<string,Vector2>();
    public static void Launched(Component p,Vector2 velocity){launches[p.name]=velocity;if(observedBall!=null)incoming[p.name]=observedBall.velocity;}
    public static void Boot(){if(booted)return;booted=true;ControlBindings.settingsPath=System.IO.Path.Combine(Application.dataPath,"..","ability-test-bindings.ini");GameObject o=new GameObject("AbilityModeTests");DontDestroyOnLoad(o);o.AddComponent<AbilityModeTests>();}
    int checks,failures;Component fan,zhao,manager;Type pt;Rigidbody2D ball,fb,zb;BuildPlayer fp,zp;float scale;const BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;
    void Check(bool ok,string name){checks++;if(!ok)failures++;Debug.Log((ok?"ABILITY PASS ":"ABILITY FAIL ")+name);}
    object Read(BuildPlayer p,string name){return typeof(BuildPlayer).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p);}
    void Write(BuildPlayer p,string name,object value){typeof(BuildPlayer).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,value);}
    void Move(Component p,Vector2 v){var body=(Rigidbody2D)pt.GetField("rb").GetValue(p);p.transform.position+=(Vector3)(v-body.position);foreach(var b in p.GetComponentsInChildren<Rigidbody2D>()){b.velocity=Vector2.zero;b.angularVelocity=0;}Physics2D.SyncTransforms();}
    float Error(Component p){float e=0;foreach(var j in p.GetComponentsInChildren<HingeJoint2D>())if(j.connectedBody!=null)e=Mathf.Max(e,Vector2.Distance(j.transform.TransformPoint(j.anchor),j.connectedBody.transform.TransformPoint(j.connectedAnchor)));return e;}
    IEnumerator Scene(int[] f,int[] z,int fs,int zs){keys.Clear();downFrames.Clear();Time.timeScale=1;UseAI=false;PlayerSkills.Enabled=false;AbilityMode.Enabled=true;AbilityMode.Reset();for(int i=0;i<3;i++){AbilityMode.Builds[0].Levels[i]=f[i];AbilityMode.Builds[1].Levels[i]=z[i];}AbilityMode.Builds[0].Purchased=fs;AbilityMode.Builds[1].Purchased=zs;typeof(GameAIMod).GetMethod("BeginGame",Hidden).Invoke(null,new object[]{0});yield return new WaitForSeconds(.3f);pt=Type.GetType("PlayerController, Assembly-CSharp");fan=GameObject.Find("Fan").GetComponent(pt);zhao=GameObject.Find("Zhao").GetComponent(pt);manager=FindObjectOfType(pt.Assembly.GetType("GameManager")) as Component;ball=(FindObjectOfType(pt.Assembly.GetType("Ball")) as Component).GetComponent<Rigidbody2D>();fb=(Rigidbody2D)pt.GetField("rb").GetValue(fan);zb=(Rigidbody2D)pt.GetField("rb").GetValue(zhao);fp=fan.GetComponent<BuildPlayer>();zp=zhao.GetComponent<BuildPlayer>();observedBall=ball;scale=Mathf.Abs(fan.transform.lossyScale.x)/.8f;Move(fan,new Vector2(-3f*scale,fb.position.y));Move(zhao,new Vector2(3f*scale,zb.position.y));ball.position=new Vector2(0,10);ball.gravityScale=0;yield return new WaitForSeconds(.2f);GameAIMod.OnBallReset();}

    void Rules(){
        AbilityMode.Reset();var b=AbilityMode.Builds[0];
        Check(b.Valid&&b.Used==12&&AbilityMode.Budget==16&&b.Levels[0]==4&&b.Levels[1]==4&&b.Levels[2]==4,"16 budget keeps 4/4/4 with four unspent points");
        b.Change(0,4);bool cap=b.Levels[0]==8&&!b.Change(0,1)&&!b.Toggle(4);b.Change(0,-2);bool buy=b.Toggle(4)&&b.Valid&&b.Used==16;b.Toggle(4);
        Check(cap&&buy&&b.Used==14&&!b.Change(1,-5),"cap eight, cost two, refund and invalid allocations");
        bool legal=true;for(int i=0;i<20;i++){AbilityMode.Randomize(i%2);var c=AbilityMode.Builds[i%2];legal&=c.Valid&&c.Used==16;}
        Check(legal,"AI random allocations obey new budget and cap");
        bool schedule=true;for(int i=0;i<=8;i++)schedule&=Mathf.Abs(AbilityMode.Strength(i)-(.6f+i*.05f))<.0001f;
        Check(schedule&&AbilityMode.SkillStrength(0)==.25f&&AbilityMode.SkillStrength(4)==1f&&Mathf.Abs(AbilityMode.SkillStrength(8)-1.1f)<.001f,"retained ordinary floor with four-point canonical skills and asymmetric skill scaling");
    }
    IEnumerator UI(){
        AbilityMode.Enabled=false;AbilityMode.Reset();Component menu=FindObjectOfType(Type.GetType("MainPanel, Assembly-CSharp")) as Component;
        Button entry=null;foreach(var b in menu.GetComponentsInChildren<Button>(true))if(b.name=="btn_Start")entry=b;entry.onClick.Invoke();yield return null;
        foreach(var b in FindObjectsOfType<Button>())if(b.GetComponentInChildren<Text>()!=null&&b.GetComponentInChildren<Text>().text.Contains("加点模式")){b.onClick.Invoke();break;}yield return null;
        foreach(var b in FindObjectsOfType<Button>())if(b.GetComponentInChildren<Text>()!=null&&b.GetComponentInChildren<Text>().text=="双人对战"){b.onClick.Invoke();break;}yield return null;
        var panel=FindObjectOfType<AbilitySetupPanel>();bool clean=panel!=null,ready=false;int left=0;foreach(Text t in panel.GetComponentsInChildren<Text>()){clean&=!t.text.Contains("%")&&!t.text.Contains("0点")&&!t.text.Contains("0跳跃");if(t.text.Contains("剩余 4 点"))left++;}
        foreach(var button in panel.GetComponentsInChildren<Button>())if(button.GetComponentInChildren<Text>().text=="开始比赛")ready=button.interactable;
        Check(clean&&ready&&left==2,"allocation UI hides percentages/zero-point effects and default can start");
        yield return new WaitForEndOfFrame();Snapshot();
        foreach(var button in panel.GetComponentsInChildren<Button>())if(button.GetComponentInChildren<Text>().text=="返回"){button.onClick.Invoke();break;}yield return null;
    }
    IEnumerator Mobility(bool isFan,int contest,int jump){
        yield return Scene(new int[]{4,contest,jump},new int[]{4,contest,jump},0,0);
        Component actor=isFan?fan:zhao;BuildPlayer owner=isFan?fp:zp;Rigidbody2D body=isFan?fb:zb;float dir=isFan?1f:-1f;Move(isFan?zhao:fan,new Vector2(body.position.x-dir*4f*scale,body.position.y));
        float angle=0,err=0;bool stagger=false;for(int i=0;i<25;i++){yield return new WaitForFixedUpdate();angle=Mathf.Max(angle,Mathf.Abs(Mathf.DeltaAngle(body.rotation,0)));err=Mathf.Max(err,Error(actor));stagger|=owner.Staggered;}
        float idleAngle=angle;float walkStart=body.position.x;keys.Add(ControlBindings.Get(isFan,isFan?GameControlAction.Right:GameControlAction.Left));for(int i=0;i<35;i++){yield return new WaitForFixedUpdate();angle=Mathf.Max(angle,Mathf.Abs(Mathf.DeltaAngle(body.rotation,0)));err=Mathf.Max(err,Error(actor));}keys.Clear();float walked=dir*(body.position.x-walkStart);float gaitAngle=angle;
        for(int i=0;i<15;i++)yield return new WaitForFixedUpdate();
        float radius=ball.GetComponent<Collider2D>().bounds.extents.y;ball.position=new Vector2(body.position.x+dir*.65f*scale,(float)Read(owner,"groundY")+radius+.025f);ball.velocity=Vector2.zero;if(contest==8){float speed=(float)pt.GetField("maxVelocity").GetValue(actor);foreach(var limb in actor.GetComponentsInChildren<Rigidbody2D>())limb.velocity=Vector2.right*dir*speed;ball.velocity=Vector2.right*dir*speed;}ball.gravityScale=1;Physics2D.SyncTransforms();float start=body.position.x,ballStart=ball.position.x,minAhead=100;
        keys.Add(ControlBindings.Get(isFan,isFan?GameControlAction.Right:GameControlAction.Left));for(int i=0;i<45;i++){yield return new WaitForFixedUpdate();minAhead=Mathf.Min(minAhead,dir*(ball.position.x-body.position.x));angle=Mathf.Max(angle,Mathf.Abs(Mathf.DeltaAngle(body.rotation,0)));err=Mathf.Max(err,Error(actor));stagger|=owner.Staggered;}keys.Clear();
        float carry=dir*(body.position.x-start),movedBall=dir*(ball.position.x-ballStart);float target=(float)((FieldInfo[])Read(owner,"targetFields"))[0].GetValue((Component)Read(owner,"stick"));Debug.Log("ABILITY POSE "+actor.name+" idle="+idleAngle+" gait="+gaitAngle+" carry="+angle+" bodyTarget="+target+" body="+body.rotation);
        Check(!stagger&&idleAngle<15f&&angle<40f&&(contest<8||minAhead>-.08f*scale)&&err<.5f&&walked>.4f*scale&&carry>.25f*scale&&movedBall>.15f*scale,"stable stand/walk/dribble "+actor.name+" contest="+contest+" jump="+jump+" walk="+walked+" carry="+carry+" ball="+movedBall+" angle="+angle+" joints="+err+" minAhead="+minAhead+" fastStart="+(contest==8));
    }
    static Rigidbody2D observedBall;static readonly Dictionary<string,Vector2> incoming=new Dictionary<string,Vector2>();static readonly Dictionary<string,int> headContacts=new Dictionary<string,int>();static readonly Dictionary<KeyCode,int> downFrames=new Dictionary<KeyCode,int>();
    public static void Contacted(Collision2D c){BuildPlayer owner=c.collider.GetComponentInParent<BuildPlayer>();if(owner!=null&&c.collider.name=="Head"){int n;headContacts.TryGetValue(owner.name,out n);headContacts[owner.name]=n+1;}}
    void Press(KeyCode key){keys.Add(key);downFrames[key]=-1;}
    Rigidbody2D Body(Component actor){return (Rigidbody2D)pt.GetField("rb").GetValue(actor);}
    IEnumerator JumpAndIntercept(bool isFan)
    {
        yield return Scene(new int[]{4,0,0},new int[]{4,0,0},1<<2,1<<2);Component actor=isFan?fan:zhao;BuildPlayer owner=actor.GetComponent<BuildPlayer>();Rigidbody2D body=Body(actor);float native=(float)pt.GetField("jumpForce").GetValue(actor),multiplier=PlayerSkills.GetJumpForce(native,actor)/native;
        Move(isFan?zhao:fan,body.position+Vector2.right*(isFan?-4f:4f)*scale);float start=body.position.y,peak=start;Press(ControlBindings.Get(isFan,GameControlAction.Jump));
        for(int i=0;i<35;i++){yield return new WaitForFixedUpdate();if(i==2)keys.Clear();peak=Mathf.Max(peak,body.position.y);}
        Check(Mathf.Abs(multiplier-PlayerSkills.ZhaoJumpMultiplier*.5f)<.001f&&((float[])Read(owner,"effects"))[2]>=0&&peak>start+.15f*scale&&Error(actor)<.5f,"zero-point fortress really jumps and animates "+actor.name+" rise="+(peak-start));
        AbilityMode.Builds[isFan?0:1].Levels[2]=4;owner.Clear();Check(Mathf.Abs(PlayerSkills.GetJumpForce(native,actor)/native-PlayerSkills.ZhaoJumpMultiplier)<.001f,"four-point fortress matches original jump impulse "+actor.name);
        foreach(int level in new int[]{0,8}){
            yield return Scene(new int[]{4,0,level},new int[]{4,0,level},0,0);actor=isFan?fan:zhao;owner=actor.GetComponent<BuildPlayer>();body=Body(actor);Move(isFan?zhao:fan,body.position+Vector2.right*(isFan?-4f:4f)*scale);Transform head=actor.transform.Find("Head");float height=(float)Read(owner,"height");
            ball.position=(Vector2)head.position+new Vector2((isFan?1f:-1f)*(level==0?.12f:.6f)*scale,height*(level==0?.3f:1.6f));ball.velocity=Vector2.zero;ball.gravityScale=1;Physics2D.SyncTransforms();headContacts[actor.name]=0;Press(ControlBindings.Get(isFan,GameControlAction.Jump));bool planned=false;float closest=100,err=0;
            for(int i=0;i<70;i++){yield return new WaitForFixedUpdate();if(i==2)keys.Clear();planned|=owner.Intercepting;closest=Mathf.Min(closest,Vector2.Distance(ball.position,head.position));err=Mathf.Max(err,Error(actor));}
            int hits;headContacts.TryGetValue(actor.name,out hits);Check(planned&&hits>0&&err<.5f,"jump command intercepts "+(level==0?"near restricted":"very high")+" ball "+actor.name+" contact="+hits+" nearest="+closest+" joints="+err);
        }
    }
    void BringTogether(Component actor,Component other){Collider2D ours=actor.transform.Find("Body").GetComponent<Collider2D>(),theirs=other.transform.Find("Body").GetComponent<Collider2D>();float dir=actor.name=="Fan"?1f:-1f;Move(other,new Vector2(Body(actor).position.x+dir*(ours.bounds.extents.x+theirs.bounds.extents.x-.025f*scale),Body(actor).position.y));}
    IEnumerator Contest(bool isFan)
    {
        yield return Scene(isFan?new int[]{4,0,8}:new int[]{4,8,4},isFan?new int[]{4,8,4}:new int[]{4,0,8},0,0);Component actor=isFan?fan:zhao,other=isFan?zhao:fan;BuildPlayer owner=actor.GetComponent<BuildPlayer>(),opponent=other.GetComponent<BuildPlayer>();
        float original=Body(actor).position.x;opponent.PlayerContact(owner,false);owner.Pose();Check(!owner.JumpBlocked&&!owner.Staggered&&Mathf.Abs(Body(actor).position.x-original)<.001f,"contest advantage cannot act from a distance "+actor.name);
        BringTogether(actor,other);for(int i=0;i<3;i++)yield return new WaitForFixedUpdate();bool blocked=owner.JumpBlocked;float native=(float)pt.GetField("jumpForce").GetValue(actor),jump=PlayerSkills.GetJumpForce(native,actor);owner.Pose();object[] muscles=(object[])Read(owner,"muscles");float[] normal=(float[])Read(owner,"normalGains");bool legs=true;for(int i=6;i<muscles.Length;i++)legs&=Mathf.Abs((float)muscles[i].GetType().GetField("force").GetValue(muscles[i])-normal[i])<.01f;
        Check(blocked&&jump==0&&!owner.Staggered&&legs,"actual high-contest contact blocks jump without soft legs "+actor.name+" blocked="+blocked+" jump="+jump);
        Move(other,Body(actor).position+Vector2.right*(isFan?4f:-4f)*scale);yield return new WaitForFixedUpdate();Check(!owner.JumpBlocked&&PlayerSkills.GetJumpForce(native,actor)>0&&!owner.Staggered,"separation immediately restores unpressured jump/support "+actor.name);
        bool tackle=true;float[] distances=new float[3];int[] levels={0,4,8};for(int trial=0;trial<3;trial++){
            yield return Scene(isFan?new int[]{3,levels[trial],3}:new int[]{4,4,4},isFan?new int[]{4,4,4}:new int[]{3,levels[trial],3},isFan?1<<3:0,isFan?0:1<<3);actor=isFan?fan:zhao;other=isFan?zhao:fan;owner=actor.GetComponent<BuildPlayer>();opponent=other.GetComponent<BuildPlayer>();BringTogether(actor,other);yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();distances[trial]=(float)Read(opponent,"knockDistance");bool hit=((float[])Read(owner,"effects"))[3]>=0;opponent.Pose();object[] m=(object[])Read(opponent,"muscles");float[] n=(float[])Read(opponent,"normalGains");for(int i=6;i<m.Length;i++)tackle&=Mathf.Abs((float)m[i].GetType().GetField("force").GetValue(m[i])-n[i])<.01f;tackle&=hit&&Error(other)<.5f;
        }
        Check(tackle&&distances[0]<distances[1]&&distances[1]<distances[2]&&Mathf.Abs(distances[2]/distances[1]-1.1f*AbilityMode.TackleResistance(8,4))<.001f,"real tackle 0/4/8 scales recoil and knockback, preserves legs "+actor.name);
    }
    IEnumerator BallContest(bool isFan)
    {
        yield return Scene(isFan?new int[]{4,8,0}:new int[]{4,0,0},isFan?new int[]{4,0,0}:new int[]{4,8,0},0,0);
        Component actor=isFan?fan:zhao;BuildPlayer owner=actor.GetComponent<BuildPlayer>();float dir=isFan?1f:-1f;Move(fan,new Vector2(-.72f*scale,fb.position.y));Move(zhao,new Vector2(.72f*scale,zb.position.y));
        ball.position=new Vector2(0,(float)Read(owner,"groundY")+ball.GetComponent<Collider2D>().bounds.extents.y+.025f);ball.velocity=Vector2.zero;ball.gravityScale=1;Physics2D.SyncTransforms();
        Press(ControlBindings.Get(true,GameControlAction.Right));Press(ControlBindings.Get(false,GameControlAction.Left));bool contact=false;float minAhead=100,angle=0,joints=0;
        for(int i=0;i<40;i++){yield return new WaitForFixedUpdate();contact|=owner.GroundBallContest;minAhead=Mathf.Min(minAhead,dir*(ball.position.x-Body(actor).position.x));angle=Mathf.Max(angle,Mathf.Abs(Mathf.DeltaAngle(Body(actor).rotation,0)));joints=Mathf.Max(joints,Error(actor));}keys.Clear();
        yield return new WaitForSeconds(.6f);float settled=Mathf.Abs(Mathf.DeltaAngle(Body(actor).rotation,0));object[] native=(object[])Read(owner,"muscles");float[] gains=(float[])Read(owner,"normalGains");FieldInfo force=(FieldInfo)Read(owner,"gain");bool legs=true;for(int i=6;i<native.Length;i++)legs&=Mathf.Abs((float)force.GetValue(native[i])-gains[i])<.001f;
        Check(contact&&!owner.Staggered&&minAhead>-.08f*scale&&angle<60f&&settled<15f&&legs&&joints<.5f,"high contest retains front ball, real leg support and recovers balance after shoving "+actor.name+" contact="+contact+" minAhead="+minAhead+" peakLean="+angle+" settled="+settled+" legs="+legs+" joints="+joints);
    }
    IEnumerator ShotProfiles()
    {
        yield return Scene(new int[]{4,4,4},new int[]{4,4,4},0,0);foreach(bool isFan in new bool[]{true,false}){
            Component actor=isFan?fan:zhao;BuildPlayer owner=actor.GetComponent<BuildPlayer>();float dir=isFan?1f:-1f;int side=isFan?0:1;float[] burst=new float[3],lift=new float[3];int[] levels={0,4,8};bool parity=true;
            Collider2D goal=(Collider2D)Read(owner,"goal");for(int i=0;i<3;i++){
                AbilityMode.Builds[side].Levels[0]=levels[i];AbilityMode.Builds[side].Purchased=1<<4;owner.Clear();Write(owner,"power",true);ball.position=new Vector2(Body(actor).position.x+dir*1.8f*scale,3f*scale);ball.velocity=Vector2.right*dir*10f;typeof(BuildPlayer).GetMethod("Strike",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,null);burst[i]=Mathf.Abs(ball.velocity.x);
                parity&=Mathf.Abs(burst[i]-PowerShot.SkillBurstSpeed(10f)*AbilityMode.SkillStrength(levels[i]))<.01f;
                AbilityMode.Builds[side].Purchased=1<<5;owner.Clear();Write(owner,"power",false);ball.position=new Vector2(Body(actor).position.x+dir*1.8f*scale,3f*scale);ball.velocity=Vector2.right*dir*10f;LeafShotProfile canonical=new LeafShotProfile(ball,ball.GetComponent<Collider2D>(),goal,dir,(int)(LayerMask)pt.GetField("ground").GetValue(actor),1f);Vector2 canonicalVelocity=canonical.Velocity(0,ball.position);
                typeof(BuildPlayer).GetMethod("Strike",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,null);lift[i]=(float)Read(owner,"flightLift");if(levels[i]==4){LeafShotProfile actual=(LeafShotProfile)Read(owner,"leafCurve");parity&=actual!=null&&Mathf.Abs(actual.curveArcHeight-canonical.curveArcHeight)<.001f&&Mathf.Abs(actual.curveTravelTime-canonical.curveTravelTime)<.001f&&Vector2.Distance(actual.Velocity(.25f,actual.curveOrigin),canonical.Velocity(.25f,canonical.curveOrigin))<.001f;}
            }
            Check(parity&&burst[0]<burst[1]*.3f&&burst[2]>burst[1]&&burst[2]<burst[1]*1.11f&&AbilityMode.LeafSpeed(0)==AbilityMode.LeafSpeed(4)&&AbilityMode.LeafSpeed(8)>AbilityMode.LeafSpeed(4),"shooting skills steep below four, match canonical four, modest above four "+actor.name);
            AbilityMode.Builds[side].Purchased=0;float[] error=new float[2];int[] shooting={4,8};for(int i=0;i<2;i++){AbilityMode.Builds[side].Levels[0]=shooting[i];float sum=0;for(int sample=0;sample<12;sample++){
                owner.Clear();Write(owner,"power",false);ball.position=new Vector2(dir*-2f*scale,2.5f*scale);ball.velocity=Vector2.right*dir*20f;UnityEngine.Random.InitState(413+sample);aimTargets.Remove(actor.name);typeof(BuildPlayer).GetMethod("Strike",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,null);Vector2 target=aimTargets[actor.name];float ideal=BallBoundaryGuard.FindGoalCeiling(goal)-ball.GetComponent<Collider2D>().bounds.extents.y-.10f*scale;float e=target.y-ideal;
                if((float)Read(owner,"flightStart")<0){float time=Mathf.Abs(target.x-ball.position.x)/Mathf.Abs(ball.velocity.x);float crossing=ball.position.y+ball.velocity.y*time+.5f*Physics2D.gravity.y*ball.gravityScale*time*time;if(Mathf.Abs(crossing-target.y)>=.01f)Debug.Log("ABILITY AIM DIAGNOSTIC "+actor.name+" level="+shooting[i]+" v="+ball.velocity+" target="+target+" crossing="+crossing+" origin="+ball.position);parity&=Mathf.Abs(crossing-target.y)<.01f;}sum+=e*e;
            }error[i]=Mathf.Sqrt(sum/12);}
            Check(parity&&error[1]<error[0]*.1f,"unbought high shooting genuinely reduces ballistic/curve aiming error "+actor.name+" error4="+error[0]+" error8="+error[1]);
        }
    }
    static readonly Dictionary<string,Vector2> aimTargets=new Dictionary<string,Vector2>();public static void Aimed(Component actor,Vector2 target){aimTargets[actor.name]=target;}
    IEnumerator LiveShot(bool isFan,int skill,int level)
    {
        yield return Scene(new int[]{level,3,3},new int[]{level,3,3},1<<skill,1<<skill);Component actor=isFan?fan:zhao;BuildPlayer owner=actor.GetComponent<BuildPlayer>();float dir=isFan?1f:-1f;launches.Remove(actor.name);float gap=ball.GetComponent<Collider2D>().bounds.extents.x;
        if(skill==1){Transform head=actor.transform.Find("Head");gap+=head.GetComponent<Collider2D>().bounds.extents.x+.02f;ball.position=(Vector2)head.position+Vector2.right*dir*gap;ball.velocity=Vector2.right*-dir;ball.gravityScale=1;PlayerSkills.SetAction((Animator)pt.GetField("anim").GetValue(actor),"Head",actor);}
        else{Collider2D shin=actor.transform.Find("R_LowLeg").GetComponent<Collider2D>();Vector2 edge=shin.ClosestPoint((Vector2)shin.transform.position+Vector2.right*dir*2f);ball.position=edge+Vector2.right*dir*(gap+.012f);ball.velocity=Vector2.right*-dir*.8f;ball.gravityScale=1;Physics2D.SyncTransforms();if(skill==4)PowerShot.Request(actor);PlayerSkills.SetAction((Animator)pt.GetField("anim").GetValue(actor),"Kick",actor);}
        bool glow=false;for(int i=0;i<35;i++){yield return new WaitForFixedUpdate();glow|=object.ReferenceEquals(typeof(BuildPlayer).GetField("glowOwner",Hidden).GetValue(null),owner);}
        bool active=((float[])Read(owner,"effects"))[skill]>=0,power=true;Vector2 v,prior;if(skill==1){power=launches.TryGetValue(actor.name,out v)&&incoming.TryGetValue(actor.name,out prior)&&Vector2.Distance(v,PlayerSkills.CannonVelocity(dir,prior))<.01f;}if(skill==4){power=launches.TryGetValue(actor.name,out v)&&incoming.TryGetValue(actor.name,out prior)&&Mathf.Abs(Mathf.Abs(v.x)-PowerShot.SkillBurstSpeed(prior.magnitude)*AbilityMode.SkillStrength(level))<.01f;}
        if(!active||!glow||!power)Debug.Log("ABILITY SHOT DIAGNOSTIC "+actor.name+" skill="+skill+" active="+active+" glow="+glow+" power="+power+" launched="+launches.ContainsKey(actor.name));Check(active&&glow&&power&&Error(actor)<.5f,"real shot + animation "+actor.name+" skill="+skill+" points="+level);
    }
    IEnumerator RemainingSkills()
    {
        yield return Scene(new int[]{4,4,4},new int[]{4,4,4},0,0);bool correct=true;foreach(int index in new int[]{0,6,7,8,9}){AbilityMode.Builds[0].Purchased=1<<index;correct&=Mathf.Approximately(AbilityMode.SkillStrength(fan,index),1f);}
        AbilityMode.Builds[0].Purchased=1<<7;ball.position=fb.position+Vector2.right*4f*scale;correct&=Mathf.Abs(fp.MovementMultiplier(1)-PlayerSkills.FanAttackSprintMultiplier)<.001f;
        AbilityMode.Builds[0].Purchased=1<<8;MagneticFoot.Attach(fan);ball.position=new Vector2(fb.position.x+.5f*scale,(float)Read(fp,"groundY")+ball.GetComponent<Collider2D>().bounds.extents.y+.025f);ball.gravityScale=1;ball.velocity=Vector2.zero;Physics2D.SyncTransforms();MagneticFoot.BeforeMuscles(fan.GetComponent(pt.Assembly.GetType("StickManController")));correct&=fan.GetComponent<MagneticFoot>().Active;
        Check(correct,"four-point movement/control skill baselines and paid physical magnetic foot preserved");
        yield return Scene(new int[]{4,4,4},new int[]{4,4,4},1<<9,0);Move(fan,fb.position+Vector2.up*2f*scale);Press(ControlBindings.Get(true,GameControlAction.Down));for(int i=0;i<5;i++)yield return new WaitForFixedUpdate();keys.Clear();Check(fb.velocity.y<0&&Mathf.Abs(fb.velocity.y)<=PlayerSkills.FanDropSpeed+.5f&&Error(fan)<.5f,"four-point interception skill drops at original eight-unit cap");
        yield return Scene(new int[]{4,4,4},new int[]{4,4,4},1<<6,0);ball.position=new Vector2(fb.position.x-.9f*scale,(float)Read(fp,"groundY")+ball.GetComponent<Collider2D>().bounds.extents.y+.02f);ball.gravityScale=1;ball.velocity=Vector2.left;Physics2D.SyncTransforms();fp.RescueAxis(-1);Check(BuildPlayer.Rescuing(fan)&&(float)Read(fp,"rescueSpeed")<=(float)pt.GetField("maxVelocity").GetValue(fan)*PlayerMovement.BackwardMultiplier*PlayerMovement.FanRetreatReduction*3+.01f,"four-point emergency rescue retains activation, safety and adaptive speed ceiling");
        Time.timeScale=0;yield return null;fp.Pose();bool pause=fp.Stopped();Time.timeScale=1;AbilityMode.Enabled=false;PlayerSkills.Enabled=false;Check(pause&&PlayerSkills.GetJumpForce(100,fan)==100&&typeof(PlayerSkills).Assembly.GetType("GameStyleIndicator")==null,"pause, classic mode and removed permanent match text preserved");
        PlayerSkills.Enabled=true;typeof(GameAIMod).GetMethod("BeginGame",Hidden).Invoke(null,new object[]{0});yield return new WaitForSeconds(.4f);Component nativeFan=GameObject.Find("Fan").GetComponent(pt);PlayerSkills skills=nativeFan.GetComponent<PlayerSkills>();
        bool mode=skills!=null&&GameObject.Find("Zhao").GetComponent<BuildPlayer>()==null;Check(mode,"original skill mode loads after shared profile refactor");
    }
    void Snapshot()
    {
        var panel=FindObjectOfType<AbilitySetupPanel>();Canvas canvas=panel.GetComponentInParent<Canvas>();
        RenderMode mode=canvas.renderMode;Camera old=canvas.worldCamera;
        GameObject obj=new GameObject("Allocation Preview Camera");Camera camera=obj.AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=5;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.05f,.09f);camera.cullingMask=~0;
        RenderTexture rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        Canvas.ForceUpdateCanvases();typeof(AbilitySetupPanel).GetMethod("Resize",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(panel,null);Canvas.ForceUpdateCanvases();camera.Render();
        RenderTexture prev=RenderTexture.active;RenderTexture.active=rt;Texture2D tex=new Texture2D(1280,720,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath,"..","ability-setup.png"),tex.EncodeToPNG());RenderTexture.active=prev;
        canvas.renderMode=mode;canvas.worldCamera=old;camera.targetTexture=null;Destroy(tex);rt.Release();Destroy(rt);Destroy(obj);
    }

    IEnumerator Start(){Application.runInBackground=true;Application.targetFrameRate=120;QualitySettings.vSyncCount=0;AudioListener.volume=0;yield return new WaitForSeconds(.2f);Rules();yield return UI();
        foreach(bool actor in new bool[]{true,false}){yield return Mobility(actor,0,0);yield return Mobility(actor,8,0);yield return JumpAndIntercept(actor);}
        yield return Contest(true);yield return Contest(false);yield return BallContest(true);yield return BallContest(false);yield return ShotProfiles();foreach(bool actor in new bool[]{true,false}){yield return LiveShot(actor,1,4);yield return LiveShot(actor,4,0);yield return LiveShot(actor,5,4);}
        yield return RemainingSkills();Debug.Log("ABILITY COMPLETE checks="+checks+" failures="+failures);Application.Quit(failures==0?0:1);}
    void Update(){if(Time.realtimeSinceStartup>130){Debug.LogError("ABILITY TIMEOUT");Application.Quit(2);}}
}
