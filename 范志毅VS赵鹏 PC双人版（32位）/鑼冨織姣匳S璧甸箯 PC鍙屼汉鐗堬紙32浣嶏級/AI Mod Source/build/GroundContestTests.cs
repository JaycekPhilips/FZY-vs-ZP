using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class GroundContestTests : MonoBehaviour
{
    public static HashSet<KeyCode> Held=new HashSet<KeyCode>();
    public static bool ReadKey(KeyCode key){return Held.Contains(key);}
    public static bool ReadDown(KeyCode key){return false;}
    static bool booted;
    static GroundContestTests host;
    public static void Boot(){if(booted)return;booted=true;ControlBindings.settingsPath=System.IO.Path.Combine(Application.dataPath,"..","ground-test-bindings.ini");var o=new GameObject("GroundContestTests");DontDestroyOnLoad(o);host=o.AddComponent<GroundContestTests>();}
    const BindingFlags IP=BindingFlags.Instance|BindingFlags.NonPublic,SP=BindingFlags.Static|BindingFlags.NonPublic;
    Component fan,zhao,manager,stick;Rigidbody2D ball;Type pt;
    int checks,failures,bridgeHits,directHits;
    bool sharedAtHit,groundAtHit,separatedAtHit;
    float firstHit=-1f,lastHit=-10f,minInterval=100f;
    object Field(object o,string n){return o.GetType().GetField(n,IP).GetValue(o);}
    Rigidbody2D Body(Component p){return pt.GetField("rb").GetValue(p) as Rigidbody2D;}
    void Check(bool ok,string label){checks++;if(!ok)failures++;Debug.Log((ok?"CONTEST PASS ":"CONTEST FAIL ")+label);}
    public static void OnTackle(Component owner,Component opponent,bool air,bool bridge)
    {
        if(host==null)return;
        if(bridge){host.bridgeHits++;host.sharedAtHit|=host.SharedBall();host.groundAtHit|=host.BallOnFloor();host.separatedAtHit|=!host.UpperTouch();}
        else host.directHits++;
        if(host.firstHit<0)host.firstHit=Time.time;
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
        Held.Clear();Time.timeScale=1;PlayerSkills.Enabled=enabled;bridgeHits=directHits=0;sharedAtHit=groundAtHit=separatedAtHit=false;firstHit=-1;lastHit=-10;minInterval=100;
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
        yield return Scene(true);SetupGap();Held.Add(KeyCode.D);Held.Add(KeyCode.LeftArrow);
        float maxFanTilt=0,maxZhaoTilt=0,maxError=0,maxForce=0;bool weak=false,magnetic=false;float recovered=-1;
        for(int i=0;i<(sustained?150:95);i++)
        {
            yield return new WaitForFixedUpdate();maxError=Mathf.Max(maxError,Error(fan),Error(zhao));maxFanTilt=Mathf.Max(maxFanTilt,Tilt(fan));maxZhaoTilt=Mathf.Max(maxZhaoTilt,Tilt(zhao));
            magnetic|=zhao.GetComponent<MagneticFoot>().Active;
            if(PlayerSkills.IsGroundBallContest(zhao))maxForce=Mathf.Max(maxForce,PlayerMovement.GetMovementForce(1000,zhao));
            weak|=(bool)Field(fan.GetComponent<PlayerSkills>(),"weakenedBalance");
            if(!sustained&&bridgeHits>0&&Time.time-firstHit>.14f&&Held.Count>0){Held.Clear();Move(zhao,new Vector2(Body(zhao).position.x+2,Body(zhao).position.y));}
            if(firstHit>=0&&Time.time-firstHit>.3f&&!(bool)Field(fan.GetComponent<PlayerSkills>(),"weakenedBalance")&&recovered<0)recovered=Time.time-firstHit;
            if(i==25||i==65)Render("bridge-"+sustained+"-"+i);
        }
        Held.Clear();Debug.Log("CONTEST RUN sustained="+sustained+" bridge="+bridgeHits+" direct="+directHits+" force="+maxForce+" tilt="+maxFanTilt+","+maxZhaoTilt+" error="+maxError+" recovery="+recovered+" interval="+minInterval);
        Check(bridgeHits>0,"ground contest triggers through a football sustained="+sustained);
        Check(sharedAtHit&&groundAtHit,"both feet and pitch really touch same ball at trigger");
        Check(separatedAtHit,"bridge tackle works without upper-body contact");
        Check(weak&&maxFanTilt>2f,"Fan visibly loses balance briefly tilt="+maxFanTilt);
        Check(maxFanTilt<55f&&maxError<.35f,"impact never folds or disconnects either rig");
        Check(maxZhaoTilt<40f,"Zhao can brace during ground pushing");
        Check(magnetic&&maxForce>1200f,"magnetic foot preserves strong real pushing force="+maxForce);
        if(sustained){Check(bridgeHits+directHits<=3&&minInterval>=1.04f,"shared cooldown prevents continuous stun");}
        else{Check(recovered>=0&&recovered<.75f,"Fan recovers quickly after one hit="+recovered);Check(!(bool)Field(zhao.GetComponent<PlayerSkills>(),"bracedMasses"),"temporary ground brace mass restored");}
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
        yield return new WaitForSeconds(.2f);yield return Guards();yield return Bridge(false);yield return Bridge(true);
        Debug.Log("CONTEST COMPLETE checks="+checks+" failures="+failures);Application.Quit(failures==0?0:1);
    }
    void Update(){if(Time.realtimeSinceStartup>70){Debug.LogError("CONTEST TIMEOUT");Application.Quit(2);}}
}
