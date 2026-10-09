using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class KnockbackTests : MonoBehaviour
{
    public static HashSet<KeyCode> Held=new HashSet<KeyCode>();
    public static bool ReadKey(KeyCode key){return Held.Contains(key);}
    public static bool ReadDown(KeyCode key){return Held.Contains(key)&&(key==KeyCode.W||key==KeyCode.UpArrow);}
    static bool booted;
    static KnockbackTests host;
    public static void Boot(){if(booted)return;booted=true;ControlBindings.settingsPath=System.IO.Path.Combine(Application.dataPath,"..","ground-test-bindings.ini");var o=new GameObject("KnockbackTests");DontDestroyOnLoad(o);host=o.AddComponent<KnockbackTests>();}
    const BindingFlags IP=BindingFlags.Instance|BindingFlags.NonPublic,SP=BindingFlags.Static|BindingFlags.NonPublic;
    Component fan,zhao,manager,stick;Rigidbody2D ball;Type pt;
    int checks,failures,bridgeHits,directHits;
    bool sharedAtHit,groundAtHit,separatedAtHit,airAtHit,directAtHit;
    float firstHit=-1f,lastHit=-10f,minInterval=100f;
    float hitOrigin, hitWidth, maxTravel;
    bool fixtureBraced; RigidbodyConstraints2D fanConstraint, zhaoConstraint;
    float Center(Component p) { float m=0,x=0; foreach(Rigidbody2D b in p.GetComponentsInChildren<Rigidbody2D>()){m+=b.mass;x+=b.mass*b.worldCenterOfMass.x;}return x/m; }
    object Field(object o,string n){return o.GetType().GetField(n,IP).GetValue(o);}
    Rigidbody2D Body(Component p){return pt.GetField("rb").GetValue(p) as Rigidbody2D;}
    void Check(bool ok,string label){checks++;if(!ok)failures++;Debug.Log((ok?"CONTEST PASS ":"CONTEST FAIL ")+label);}
    public static void OnTackle(Component owner,Component opponent,bool air,bool bridge)
    {
        if(host==null)return;host.airAtHit|=air;
        if(host.fixtureBraced) { host.Body(host.fan).constraints=host.fanConstraint; host.Body(host.zhao).constraints=host.zhaoConstraint; host.fixtureBraced=false; }
        if(bridge){host.bridgeHits++;host.sharedAtHit|=host.SharedBall();host.groundAtHit|=host.BallOnFloor();host.separatedAtHit|=!host.UpperTouch();}
        else { host.directHits++; host.directAtHit |= host.UpperTouch(); }
        if(host.firstHit<0) { host.firstHit=Time.time; host.hitOrigin=host.Center(opponent);host.hitWidth=(float)host.Field(opponent.GetComponent<PlayerSkills>(),"standingBodyWidth");host.maxTravel=0; }
        if(host.lastHit>=0)host.minInterval=Mathf.Min(host.minInterval,Time.time-host.lastHit);
        host.lastHit=Time.time;
        Debug.Log("CONTEST HIT bridge="+bridge+" air="+air+" gap="+Mathf.Abs(host.Body(owner).position.x-host.Body(opponent).position.x)+" shared="+host.SharedBall()+" upperTouch="+host.UpperTouch());
    }
    bool SharedBall()
    {
        bool f=false,z=false;
        Collider2D bc=ball.GetComponent<Collider2D>();ContactPoint2D[] contacts=new ContactPoint2D[32];int count=bc.GetContacts(contacts);
        for(int i=0;i<count;i++){Collider2D other=contacts[i].collider==bc?contacts[i].otherCollider:contacts[i].collider;if(other==null||other.name.IndexOf("Leg")<0)continue;
            f|=other.transform.IsChildOf(fan.transform);z|=other.transform.IsChildOf(zhao.transform);}
        return f&&z;
    }
    bool BallOnFloor(){return ball.GetComponent<Collider2D>().IsTouching(GameObject.Find("Down").GetComponent<Collider2D>());}
    bool UpperTouch()
    {
        foreach(Collider2D a in fan.GetComponentsInChildren<Collider2D>())foreach(Collider2D b in zhao.GetComponentsInChildren<Collider2D>())
            if((a.name=="Body"||a.name=="Head"||a.name.Contains("Arm"))&&(b.name=="Body"||b.name=="Head"||b.name.Contains("Arm"))&&a.IsTouching(b))return true;
        return false;
    }
    float Error(Component p){float e=0;foreach(HingeJoint2D j in p.GetComponentsInChildren<HingeJoint2D>())if(j.connectedBody!=null)e=Mathf.Max(e,Vector2.Distance(j.transform.TransformPoint(j.anchor),j.connectedBody.transform.TransformPoint(j.connectedAnchor)));return e;}
    float Tilt(Component p){Component s=p.GetComponent(pt.Assembly.GetType("StickManController"));return Mathf.Abs(Mathf.DeltaAngle(Body(p).rotation,(float)s.GetType().GetField("body").GetValue(s)));}
    void Move(Component p,Vector2 pos){p.transform.position+=(Vector3)(pos-Body(p).position);foreach(Rigidbody2D b in p.GetComponentsInChildren<Rigidbody2D>()){b.velocity=Vector2.zero;b.angularVelocity=0;}Physics2D.SyncTransforms();}
    IEnumerator Scene(bool enabled)
    {
        fixtureBraced=false;Held.Clear();Time.timeScale=1;PlayerSkills.Enabled=enabled;bridgeHits=directHits=0;sharedAtHit=groundAtHit=separatedAtHit=false;firstHit=-1;lastHit=-10;minInterval=100;airAtHit=directAtHit=false;maxTravel=0;hitWidth=1;
        typeof(GameAIMod).GetMethod("StartGame",SP).Invoke(null,new object[]{0});yield return new WaitForSeconds(.25f);
        pt=Type.GetType("PlayerController, Assembly-CSharp");fan=GameObject.Find("Fan").GetComponent(pt);zhao=GameObject.Find("Zhao").GetComponent(pt);stick=zhao.GetComponent(pt.Assembly.GetType("StickManController"));
        manager=FindObjectOfType(pt.Assembly.GetType("GameManager")) as Component;ball=(FindObjectOfType(pt.Assembly.GetType("Ball")) as Component).GetComponent<Rigidbody2D>();
        ball.position=new Vector2(0,3);ball.velocity=Vector2.zero;ball.gravityScale=0;
        Move(zhao,new Vector2(2,Body(zhao).position.y));Move(fan,new Vector2(-4,Body(fan).position.y));yield return new WaitForSeconds(.5f);
    }
    void SetupGap(float separation=1.3f)
    {
        float scale=Mathf.Abs(zhao.transform.lossyScale.x)/.8f;
        Move(fan,new Vector2(Body(zhao).position.x-separation*scale,Body(fan).position.y));
        ball.position=new Vector2(Body(zhao).position.x-.65f*scale,GameObject.Find("Down").GetComponent<Collider2D>().bounds.max.y+ball.GetComponent<Collider2D>().bounds.extents.y+.004f);
        ball.velocity=Vector2.zero;ball.gravityScale=1;Physics2D.SyncTransforms();
    }
    IEnumerator Bridge(bool sustained)
    {
        yield return Scene(true);SetupGap();
        fanConstraint=Body(fan).constraints; zhaoConstraint=Body(zhao).constraints;
        Body(fan).constraints|=RigidbodyConstraints2D.FreezePositionX;
        Body(zhao).constraints|=RigidbodyConstraints2D.FreezePositionX;fixtureBraced=true;
        Held.Add(KeyCode.D);Held.Add(KeyCode.LeftArrow);
        float maxFanTilt=0,maxZhaoTilt=0,maxError=0,maxForce=0;bool weak=false,magnetic=false;float recovered=-1;
        for(int i=0;i<(sustained?150:95);i++)
        {
            yield return new WaitForFixedUpdate();maxError=Mathf.Max(maxError,Error(fan),Error(zhao));maxFanTilt=Mathf.Max(maxFanTilt,Tilt(fan));maxZhaoTilt=Mathf.Max(maxZhaoTilt,Tilt(zhao));
            magnetic|=zhao.GetComponent<MagneticFoot>().Active;
            if(PlayerSkills.IsGroundBallContest(zhao))maxForce=Mathf.Max(maxForce,PlayerMovement.GetMovementForce(1000,zhao));
            if(firstHit>=0)maxTravel=Mathf.Max(maxTravel,hitOrigin-Center(fan));
            weak|=(bool)Field(fan.GetComponent<PlayerSkills>(),"weakenedBalance");
            if(!sustained&&bridgeHits>0&&Time.time-firstHit>.14f&&Held.Count>0){Held.Clear();Move(zhao,new Vector2(Body(zhao).position.x+2,Body(zhao).position.y));}
            if(firstHit>=0&&Time.time-firstHit>.3f&&!(bool)Field(fan.GetComponent<PlayerSkills>(),"weakenedBalance")&&recovered<0)recovered=Time.time-firstHit;
            if(i==25||i==65)Render("bridge-"+sustained+"-"+i);
        }
        if(!sustained)Check(maxTravel/hitWidth>=1.75f&&maxTravel/hitWidth<=2.4f,"through-ball pushes Fan two body widths actual="+(maxTravel/hitWidth));
        Held.Clear();Debug.Log("CONTEST RUN sustained="+sustained+" bridge="+bridgeHits+" direct="+directHits+" force="+maxForce+" tilt="+maxFanTilt+","+maxZhaoTilt+" error="+maxError+" recovery="+recovered+" interval="+minInterval);
        Check(sustained ? bridgeHits+directHits>0 : bridgeHits>0,"isolated ball tackle / sustained real physical pressure sustained="+sustained);
        Check(sustained && bridgeHits==0 ? directAtHit : sharedAtHit&&groundAtHit,"every shove starts from a real body hit or grounded foot-ball-foot chain");
        Check(sustained ? (sharedAtHit || directAtHit) : separatedAtHit,"isolated bridge needs no upper-body contact; sustained chain remains real");
        Check(weak&&maxFanTilt>2f,"Fan visibly loses balance briefly tilt="+maxFanTilt);
        Check(maxFanTilt<55f&&maxError<.35f,"impact never folds or disconnects either rig");
        Check(maxZhaoTilt<40f,"Zhao can brace during ground pushing");
        Check(magnetic&&maxForce>1200f,"magnetic foot preserves strong real pushing force="+maxForce);
        if(sustained){Check(bridgeHits+directHits<=3&&minInterval>=1.04f,"shared cooldown prevents continuous stun");}
        else{Check(recovered>=0&&recovered<.75f,"Fan recovers quickly after one hit="+recovered);Check(!(bool)Field(zhao.GetComponent<PlayerSkills>(),"bracedMasses"),"temporary ground brace mass restored");}
    }
    IEnumerator Direct(bool aerial)
    {
        yield return Scene(true);
        float scale=Mathf.Abs(zhao.transform.lossyScale.x)/.8f;
        Move(fan,new Vector2(Body(zhao).position.x-.7f*scale,Body(fan).position.y));
        if(aerial){Move(zhao,Body(zhao).position+Vector2.up*1.2f*scale);Move(fan,Body(fan).position+Vector2.up*1.2f*scale);}
        Held.Add(KeyCode.D);Held.Add(KeyCode.LeftArrow);
        float tilt=0,error=0,recovered=-1;bool weak=false;
        for(int i=0;i<120;i++)
        {
            yield return new WaitForFixedUpdate();
            if(firstHit>=0){maxTravel=Mathf.Max(maxTravel,hitOrigin-Center(fan));if(Time.time-firstHit>.08f)Held.Clear();}
            tilt=Mathf.Max(tilt,Tilt(fan));error=Mathf.Max(error,Error(fan),Error(zhao));
            weak|=(bool)Field(fan.GetComponent<PlayerSkills>(),"weakenedBalance");
            if(firstHit>=0&&Time.time-firstHit>.3f&&!(bool)Field(fan.GetComponent<PlayerSkills>(),"weakenedBalance")&&recovered<0)recovered=Time.time-firstHit;
        }
        Held.Clear();Check(directHits>0&&bridgeHits==0&&airAtHit==aerial,"real direct-body tackle air="+aerial);
        Check(maxTravel/hitWidth>=1.75f&&maxTravel/hitWidth<=2.4f,"direct pushes two body widths air="+aerial+" actual="+(maxTravel/hitWidth));
        Check(weak&&tilt>2f&&error<.35f,"direct tackle staggers intact rig air="+aerial+" tilt="+tilt+" error="+error);
        Check(recovered>=0&&recovered<.85f,"direct balance recovers air="+aerial+" time="+recovered);
        Check((float)Field(fan.GetComponent<PlayerSkills>(),"knockbackUntil")<0,"shove finishes air="+aerial);
        PlayerSkills.ResetAll();Check((float)Field(fan.GetComponent<PlayerSkills>(),"knockbackUntil")<0,"reset clears shove air="+aerial);
    }

    IEnumerator Lifecycle(bool pause, bool blocked)
    {
        yield return Scene(true);SetupGap();Held.Add(KeyCode.D);Held.Add(KeyCode.LeftArrow);
        for(int i=0;i<80&&firstHit<0;i++)yield return new WaitForFixedUpdate();
        Check(firstHit>=0,"lifecycle starts from real tackle pause="+pause+" blocked="+blocked);
        PlayerSkills skill=fan.GetComponent<PlayerSkills>();
        Check((float)Field(skill,"knockbackUntil")>Time.time,"shove is active before cancellation or obstruction");
        Held.Clear();Move(zhao,new Vector2(Body(zhao).position.x+3,Body(zhao).position.y));
        if(blocked)
        {
            GameObject wall=new GameObject("ShoveTestWall");BoxCollider2D shape=wall.AddComponent<BoxCollider2D>();
            wall.transform.position=new Vector3(hitOrigin-hitWidth*1.25f,Body(fan).position.y,0);shape.size=new Vector2(.15f,8f);Physics2D.SyncTransforms();
            float travel=0,error=0;for(int i=0;i<60;i++){yield return new WaitForFixedUpdate();travel=Mathf.Max(travel,hitOrigin-Center(fan));error=Mathf.Max(error,Error(fan));}
            Check(travel<hitWidth*1.75f,"real wall blocks full shove distance travel="+travel/hitWidth);
            Check(Body(fan).position.x>shape.bounds.max.x&&error<.35f,"shove cannot pass through obstruction or disconnect rig");
            Check((float)Field(skill,"knockbackUntil")<0,"blocked shove times out");Destroy(wall);
        }
        else
        {
            if(pause){Time.timeScale=0;yield return new WaitForSecondsRealtime(.15f);}
            else PlayerSkills.ResetAll();
            Check((float)Field(skill,"knockbackUntil")<0,"pause/reset clears active shove pause="+pause);
            Check(!(bool)Field(skill,"weakenedBalance")&&!(bool)Field(zhao.GetComponent<PlayerSkills>(),"bracedMasses"),"pause/reset restores muscle and mass parameters");
            Time.timeScale=1;yield return new WaitForFixedUpdate();Check((float)Field(skill,"knockbackUntil")<0,"shove does not restart after resume/reset");
        }
    }

    IEnumerator Guards()
    {
        yield return Scene(true);SetupGap();
        for(int i=0;i<20;i++)yield return new WaitForFixedUpdate();Check(bridgeHits==0,"idle ball control causes no remote shove");
        Move(fan,new Vector2(-4,Body(fan).position.y));Held.Add(KeyCode.LeftArrow);for(int i=0;i<25;i++)yield return new WaitForFixedUpdate();
        Check(bridgeHits==0,"single-player ball touch cannot stagger distant opponent");Held.Clear();
        yield return Scene(false);SetupGap();Held.Add(KeyCode.D);Held.Add(KeyCode.LeftArrow);for(int i=0;i<50;i++)yield return new WaitForFixedUpdate();
        Check(fan.GetComponent<PlayerSkills>()==null&&zhao.GetComponent<MagneticFoot>()==null&&bridgeHits==0,"classic contest uses only ordinary physics");Held.Clear();
        yield return Scene(true);SetupGap();PlayerSkills.ResetAll();
        Check(!PlayerSkills.IsGroundBallContest(zhao),"reset clears cached shared contact/pressure");
        Time.timeScale=0;Check(!PlayerSkills.IsGroundBallContest(zhao),"pause disables contest benefit");Time.timeScale=1;
        Check(Mathf.Abs(PlayerSkills.TackleAirSpeed-2.68125f)<.0001f&&PlayerSkills.TackleAirDuration==.32f,"aerial tackle strength/duration unchanged");
        Check(PlayerSkills.GroundTackleSpeedMultiplier==1.4f&&PlayerSkills.GroundTackleSpinMultiplier==1.35f,"ground direct linear +40%, tipping +35%");
    }
    void Render(string name)
    {
        Camera c=Camera.main;if(c==null)return;RenderTexture rt=new RenderTexture(1280,720,24),old=c.targetTexture,active=RenderTexture.active;c.targetTexture=rt;c.Render();RenderTexture.active=rt;
        Texture2D t=new Texture2D(1280,720,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1280,720),0,0);t.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath,"..","ground-"+name+".png"),t.EncodeToPNG());c.targetTexture=old;RenderTexture.active=active;Destroy(t);Destroy(rt);
    }
    IEnumerator Start()
    {
        Application.runInBackground=true;Application.targetFrameRate=120;QualitySettings.vSyncCount=0;AudioListener.volume=0;
        yield return new WaitForSeconds(.2f);yield return Guards();yield return Bridge(false);yield return Bridge(true);yield return Direct(false);yield return Direct(true);yield return Lifecycle(true,false);yield return Lifecycle(false,false);yield return Lifecycle(false,true);
        Debug.Log("CONTEST COMPLETE checks="+checks+" failures="+failures);Application.Quit(failures==0?0:1);
    }
    void Update(){if(Time.realtimeSinceStartup>70){Debug.LogError("CONTEST TIMEOUT");Application.Quit(2);}}
}
