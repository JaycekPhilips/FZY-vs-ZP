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
    public static bool ReadKey(KeyCode k){return keys.Contains(k);}public static bool ReadDown(KeyCode k){return keys.Contains(k);}
    static readonly Dictionary<string,Vector2> launches=new Dictionary<string,Vector2>();
    public static void Launched(Component p,Vector2 velocity){launches[p.name]=velocity;}
    public static void Boot(){if(booted)return;booted=true;ControlBindings.settingsPath=System.IO.Path.Combine(Application.dataPath,"..","ability-test-bindings.ini");GameObject o=new GameObject("AbilityModeTests");DontDestroyOnLoad(o);o.AddComponent<AbilityModeTests>();}
    int checks,failures;Component fan,zhao,manager;Type pt;Rigidbody2D ball,fb,zb;BuildPlayer fp,zp;float scale;const BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;
    void Check(bool ok,string name){checks++;if(!ok)failures++;Debug.Log((ok?"ABILITY PASS ":"ABILITY FAIL ")+name);}
    object Read(BuildPlayer p,string name){return typeof(BuildPlayer).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p);}
    void Write(BuildPlayer p,string name,object value){typeof(BuildPlayer).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,value);}
    void Move(Component p,Vector2 v){var body=(Rigidbody2D)pt.GetField("rb").GetValue(p);p.transform.position+=(Vector3)(v-body.position);foreach(var b in p.GetComponentsInChildren<Rigidbody2D>()){b.velocity=Vector2.zero;b.angularVelocity=0;}Physics2D.SyncTransforms();}
    float Error(Component p){float e=0;foreach(var j in p.GetComponentsInChildren<HingeJoint2D>())if(j.connectedBody!=null)e=Mathf.Max(e,Vector2.Distance(j.transform.TransformPoint(j.anchor),j.connectedBody.transform.TransformPoint(j.connectedAnchor)));return e;}
    IEnumerator Scene(int[] f,int[] z,int fs,int zs){keys.Clear();Time.timeScale=1;UseAI=false;PlayerSkills.Enabled=false;AbilityMode.Enabled=true;AbilityMode.Reset();for(int i=0;i<3;i++){AbilityMode.Builds[0].Levels[i]=f[i];AbilityMode.Builds[1].Levels[i]=z[i];}AbilityMode.Builds[0].Purchased=fs;AbilityMode.Builds[1].Purchased=zs;typeof(GameAIMod).GetMethod("BeginGame",Hidden).Invoke(null,new object[]{0});yield return new WaitForSeconds(.3f);pt=Type.GetType("PlayerController, Assembly-CSharp");fan=GameObject.Find("Fan").GetComponent(pt);zhao=GameObject.Find("Zhao").GetComponent(pt);manager=FindObjectOfType(pt.Assembly.GetType("GameManager")) as Component;ball=(FindObjectOfType(pt.Assembly.GetType("Ball")) as Component).GetComponent<Rigidbody2D>();fb=(Rigidbody2D)pt.GetField("rb").GetValue(fan);zb=(Rigidbody2D)pt.GetField("rb").GetValue(zhao);fp=fan.GetComponent<BuildPlayer>();zp=zhao.GetComponent<BuildPlayer>();scale=Mathf.Abs(fan.transform.lossyScale.x)/.8f;Move(fan,new Vector2(-3f*scale,fb.position.y));Move(zhao,new Vector2(3f*scale,zb.position.y));ball.position=new Vector2(0,10);ball.gravityScale=0;yield return new WaitForSeconds(.2f);GameAIMod.OnBallReset();}

    void Rules(){
        AbilityMode.Reset();var b=AbilityMode.Builds[0];
        Check(b.Valid&&b.Used==12&&AbilityMode.Budget==16&&b.Levels[0]==4&&b.Levels[1]==4&&b.Levels[2]==4,"16 budget keeps 4/4/4 with four unspent points");
        b.Change(0,4);bool cap=b.Levels[0]==8&&!b.Change(0,1)&&!b.Toggle(4);b.Change(0,-2);bool buy=b.Toggle(4)&&b.Valid&&b.Used==16;b.Toggle(4);
        Check(cap&&buy&&b.Used==14&&!b.Change(1,-5),"cap eight, cost two, refund and invalid allocations");
        bool legal=true;for(int i=0;i<20;i++){AbilityMode.Randomize(i%2);var c=AbilityMode.Builds[i%2];legal&=c.Valid&&c.Used==16;}
        Check(legal,"AI random allocations obey new budget and cap");
        bool schedule=true;for(int i=0;i<=8;i++)schedule&=Mathf.Abs(AbilityMode.Strength(i)-(.6f+i*.05f))<.0001f;
        Check(schedule,"equal five-percentage-point ability schedule with retained 60 percent floor");
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
        float radius=ball.GetComponent<Collider2D>().bounds.extents.y;ball.position=new Vector2(body.position.x+dir*.65f*scale,(float)Read(owner,"groundY")+radius+.025f);ball.velocity=Vector2.zero;ball.gravityScale=1;Physics2D.SyncTransforms();float start=body.position.x,ballStart=ball.position.x;
        keys.Add(ControlBindings.Get(isFan,isFan?GameControlAction.Right:GameControlAction.Left));for(int i=0;i<65;i++){yield return new WaitForFixedUpdate();angle=Mathf.Max(angle,Mathf.Abs(Mathf.DeltaAngle(body.rotation,0)));err=Mathf.Max(err,Error(actor));stagger|=owner.Staggered;}keys.Clear();
        float carry=dir*(body.position.x-start),movedBall=dir*(ball.position.x-ballStart);float target=(float)((FieldInfo[])Read(owner,"targetFields"))[0].GetValue((Component)Read(owner,"stick"));Debug.Log("ABILITY POSE "+actor.name+" idle="+idleAngle+" gait="+gaitAngle+" carry="+angle+" bodyTarget="+target+" body="+body.rotation);
        Check(!stagger&&idleAngle<15f&&angle<40f&&err<.5f&&walked>.4f*scale&&carry>.25f*scale&&movedBall>.15f*scale,"stable stand/walk/dribble "+actor.name+" contest="+contest+" jump="+jump+" walk="+walked+" carry="+carry+" ball="+movedBall+" angle="+angle+" joints="+err);
    }
    IEnumerator Jumping(bool isFan){
        yield return Scene(new int[]{4,0,0},new int[]{4,0,0},0,0);Component actor=isFan?fan:zhao;BuildPlayer owner=isFan?fp:zp;Rigidbody2D body=isFan?fb:zb;float native=(float)pt.GetField("jumpForce").GetValue(actor);
        float jf=PlayerSkills.GetJumpForce(native,actor)/native,start=body.position.y,peak=start;keys.Add(ControlBindings.Get(isFan,GameControlAction.Jump));
        for(int i=0;i<30;i++){yield return new WaitForFixedUpdate();if(i==2)keys.Clear();peak=Mathf.Max(peak,body.position.y);}
        Check(Mathf.Abs(jf*jf-.6f)<.001f&&peak>start+.15f*scale&&Error(actor)<.5f,"zero jump retains real reduced jump "+actor.name+" rise="+(peak-start));
        yield return Scene(new int[]{4,0,8},new int[]{4,0,8},0,0);actor=isFan?fan:zhao;body=isFan?fb:zb;start=body.position.y;peak=start;PlayerSkills.SetAction((Animator)pt.GetField("anim").GetValue(actor),"Jump",actor);for(int i=0;i<12;i++){yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,body.position.y);}keys.Add(ControlBindings.Get(isFan,GameControlAction.Down));for(int i=0;i<5;i++)yield return new WaitForFixedUpdate();keys.Clear();
        Check(peak>start+.3f*scale&&body.velocity.y<0&&Error(actor)<.5f,"new cap rapid jump/descent "+actor.name);
    }
    IEnumerator ShotProfiles(){
        yield return Scene(new int[]{4,4,4},new int[]{4,4,4},0,0);bool ordinary=true;
        foreach(int level in new int[]{0,4,7}){AbilityMode.Builds[0].Levels[0]=level;fp.Clear();ball.position=fb.position+Vector2.right*1.8f*scale;ball.velocity=Vector2.right*15f;UnityEngine.Random.InitState(2);typeof(BuildPlayer).GetMethod("Strike",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(fp,null);ordinary&=Mathf.Abs(ball.velocity.magnitude-15f*AbilityMode.Strength(level))<.01f;}
        Check(ordinary,"ordinary shots retain linear power rather than the old zero-point weak special case");
        float[] burst=new float[3],lift=new float[3],error=new float[3],angle=new float[3];int[] levels={0,4,8};Collider2D goal=(Collider2D)Read(fp,"goal");float ideal=BallBoundaryGuard.FindGoalCeiling(goal)-ball.GetComponent<Collider2D>().bounds.extents.y-.10f*scale;
        for(int i=0;i<3;i++){
            AbilityMode.Builds[0].Levels[0]=levels[i];AbilityMode.Builds[0].Purchased=1<<4;fp.Clear();Write(fp,"power",true);ball.position=fb.position+Vector2.right*1.8f*scale;ball.velocity=Vector2.zero;typeof(BuildPlayer).GetMethod("Strike",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(fp,null);burst[i]=ball.velocity.x;
            AbilityMode.Builds[0].Purchased=1<<5;Write(fp,"power",false);float sum=0,sumAngle=0;for(int trial=0;trial<20;trial++){
                fp.Clear();ball.position=fb.position+Vector2.right*1.8f*scale;ball.velocity=Vector2.zero;UnityEngine.Random.InitState(230+trial);typeof(BuildPlayer).GetMethod("Strike",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(fp,null);
                lift[i]=(float)Read(fp,"flightLift");Vector2 target=(Vector2)Read(fp,"flightTarget"),origin=(Vector2)Read(fp,"flightOrigin");float expected=Mathf.Lerp(origin.y,ideal,AbilityMode.ShotSkillStrength(levels[i]));sum+=(target.y-expected)*(target.y-expected);sumAngle+=Mathf.Atan2(target.y-origin.y,Mathf.Abs(target.x-origin.x));
            }error[i]=Mathf.Sqrt(sum/20);angle[i]=sumAngle/20;
        }
        Debug.Log("ABILITY PROFILE burst="+string.Join(",",Array.ConvertAll(burst,delegate(float f){return f.ToString();}))+" lift="+string.Join(",",Array.ConvertAll(lift,delegate(float f){return f.ToString();}))+" error="+string.Join(",",Array.ConvertAll(error,delegate(float f){return f.ToString();}))+" angle="+string.Join(",",Array.ConvertAll(angle,delegate(float f){return f.ToString();})));
        Check(burst[0]>0&&burst[0]<burst[1]&&burst[1]<burst[2]&&burst[0]<burst[2]*.3f,"burst shooting 0/4/8 markedly scales ball speed");
        Check(lift[0]<lift[1]&&lift[1]<lift[2]&&angle[0]<angle[1]&&angle[1]<angle[2]&&error[0]>error[1]&&error[1]>error[2]+.01f,"leaf shooting 0/4/8 scales height, angle and accuracy");
    }
    IEnumerator LiveShot(bool isFan,int skill,int level){
        yield return Scene(new int[]{level,3,3},new int[]{level,3,3},1<<skill,1<<skill);Component actor=isFan?fan:zhao;BuildPlayer owner=isFan?fp:zp;float dir=isFan?1f:-1f;launches.Remove(actor.name);float gap=ball.GetComponent<Collider2D>().bounds.extents.x;
        if(skill==1){Transform head=actor.transform.Find("Head");gap+=head.GetComponent<Collider2D>().bounds.extents.x+.02f;ball.position=(Vector2)head.position+Vector2.right*dir*gap;ball.velocity=Vector2.right*-dir;ball.gravityScale=1;PlayerSkills.SetAction((Animator)pt.GetField("anim").GetValue(actor),"Head",actor);}
        else {Collider2D shin=actor.transform.Find("R_LowLeg").GetComponent<Collider2D>();Vector2 edge=shin.ClosestPoint((Vector2)shin.transform.position+Vector2.right*dir*2f);ball.position=edge+Vector2.right*dir*(gap+.012f);ball.velocity=Vector2.right*-dir*.8f;ball.gravityScale=1;Physics2D.SyncTransforms();if(skill==4)PowerShot.Request(actor);PlayerSkills.SetAction((Animator)pt.GetField("anim").GetValue(actor),"Kick",actor);}
        bool glow=false;for(int i=0;i<35;i++){yield return new WaitForFixedUpdate();glow|=object.ReferenceEquals(typeof(BuildPlayer).GetField("glowOwner",Hidden).GetValue(null),owner);}
        bool active=((float[])Read(owner,"effects"))[skill]>=0;bool power=true;if(skill==1||skill==4){Vector2 v;power=launches.TryGetValue(actor.name,out v)&&Mathf.Abs(Mathf.Abs(v.x)-(skill==1?17f*PowerShot.HeaderSpeedMultiplier:22f)*AbilityMode.ShotSkillStrength(level))<.01f;}
        Check(active&&glow&&power&&Error(actor)<.5f,"real collision, scaled shot and preserved animation "+actor.name+" skill="+skill+" shooting="+level);
    }
    IEnumerator Contacts(){
        yield return Scene(new int[]{4,0,4},new int[]{4,0,4},0,0);fp.PlayerContact(zp,false);zp.PlayerContact(fp,false);
        Check(!fp.Staggered&&!zp.Staggered,"equal low contest does not auto-collapse on contact");AbilityMode.Builds[0].Levels[1]=8;fp.Clear();fp.PlayerContact(zp,false);Check(zp.Staggered&&!fp.Staggered,"cap eight contest advantage applies only in opponent contact");
        yield return Scene(new int[]{4,0,0},new int[]{4,0,0},0,0);typeof(GameAIMod).GetField("mode",Hidden).SetValue(null,2);UseAI=true;object state=typeof(GameAIMod).GetMethod("GetState",Hidden).Invoke(null,new object[]{fan});Check((float)state.GetType().GetField("jumpSpeed").GetValue(state)>0,"AI capability reads retained zero-point jump");UseAI=false;
        Time.timeScale=0;yield return null;fp.Pose();Check(fp.Stopped(),"pause safely suppresses build actions");Time.timeScale=1;AbilityMode.Enabled=false;
        Check(PlayerSkills.GetJumpForce(100,fan)==100&&AbilityMode.Movement(fan,1)==1&&typeof(PlayerSkills).Assembly.GetType("GameStyleIndicator")==null,"classic parameters and removed bottom text preserved");
        PlayerSkills.Enabled=true;typeof(GameAIMod).GetMethod("BeginGame",Hidden).Invoke(null,new object[]{0});yield return new WaitForSeconds(.4f);Check(GameObject.Find("Zhao").GetComponent<PlayerSkills>()!=null&&GameObject.Find("Zhao").GetComponent<BuildPlayer>()==null,"original special mode loads without allocation overrides");
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
        foreach(bool actor in new bool[]{true,false}){yield return Mobility(actor,0,4);yield return Mobility(actor,4,0);yield return Mobility(actor,0,0);yield return Jumping(actor);}
        yield return ShotProfiles();foreach(bool actor in new bool[]{true,false}){foreach(int level in new int[]{0,4,8})yield return LiveShot(actor,1,level);yield return LiveShot(actor,4,0);yield return LiveShot(actor,5,0);}
        yield return Contacts();Debug.Log("ABILITY COMPLETE checks="+checks+" failures="+failures);Application.Quit(failures==0?0:1);
    }
    void Update(){if(Time.realtimeSinceStartup>110){Debug.LogError("ABILITY TIMEOUT");Application.Quit(2);}}
}
