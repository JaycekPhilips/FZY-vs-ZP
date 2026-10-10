using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
public sealed class MagneticSpeedContactProbe : MonoBehaviour
{
    public int contacts;
    void OnCollisionEnter2D(Collision2D c){Observe(c);}
    void OnCollisionStay2D(Collision2D c){Observe(c);}
    void Observe(Collision2D c){if(c.collider.GetComponentInParent<MagneticFoot>()!=null && c.collider.name.IndexOf("LowLeg")>=0)contacts++;}
}
public sealed class MagneticSpeedTests : MonoBehaviour
{
    public static HashSet<KeyCode> Held = new HashSet<KeyCode>();
    public static KeyCode DownKey; public static int DownFrame = -1;
    public static bool ReadKey(KeyCode key) { return Held.Contains(key); }
    public static bool ReadDown(KeyCode key) { return Time.frameCount == DownFrame && key == DownKey; }
    static bool booted;
    public static void Boot() { if(booted)return;booted=true;ControlBindings.settingsPath=System.IO.Path.Combine(Application.dataPath,"..","magnetic-test-bindings.ini");var o=new GameObject("MagneticSpeedTests");DontDestroyOnLoad(o);o.AddComponent<MagneticSpeedTests>(); }
    const BindingFlags IP=BindingFlags.Instance|BindingFlags.NonPublic,SP=BindingFlags.Static|BindingFlags.NonPublic;
    Component fan,zhao,manager,stick; Rigidbody2D ball; MagneticFoot skill; Type pt;
    int checks,failures;
    object Field(object o,string name){return o.GetType().GetField(name,IP).GetValue(o);}
    void Check(bool ok,string name){checks++;if(!ok)failures++;Debug.Log((ok?"MAGNETIC PASS ":"MAGNETIC FAIL ")+name);}
    Rigidbody2D Body(Component p){return pt.GetField("rb").GetValue(p) as Rigidbody2D;}
    void Move(Component p,Vector2 target){p.transform.position+=(Vector3)(target-Body(p).position);foreach(Rigidbody2D b in p.GetComponentsInChildren<Rigidbody2D>()){b.velocity=Vector2.zero;b.angularVelocity=0;}Physics2D.SyncTransforms();}
    float Error(Component p){float error=0;foreach(HingeJoint2D j in p.GetComponentsInChildren<HingeJoint2D>())if(j.connectedBody!=null)error=Mathf.Max(error,Vector2.Distance(j.transform.TransformPoint(j.anchor),j.connectedBody.transform.TransformPoint(j.connectedAnchor)));return error;}
    float Floor(){return GameObject.Find("Down").GetComponent<Collider2D>().bounds.max.y;}
    void RenderRig(string name)
    {
        Camera camera=Camera.main;if(camera==null)return;
        RenderTexture rt=new RenderTexture(1280,720,24);RenderTexture oldTarget=camera.targetTexture,oldActive=RenderTexture.active;
        camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
        Texture2D image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath,"..",name+".png"),image.EncodeToPNG());
        camera.targetTexture=oldTarget;RenderTexture.active=oldActive;Destroy(image);Destroy(rt);
    }
    IEnumerator Scene(bool enabled)
    {
        Held.Clear();DownFrame=-1;PlayerSkills.Enabled=enabled;Time.timeScale=1;ControlBindings.ResetDefaults();
        typeof(GameAIMod).GetMethod("StartGame",SP).Invoke(null,new object[]{0});yield return new WaitForSeconds(.25f);
        pt=Type.GetType("PlayerController, Assembly-CSharp");fan=GameObject.Find("Fan").GetComponent(pt);zhao=GameObject.Find("Zhao").GetComponent(pt);
        manager=FindObjectOfType(pt.Assembly.GetType("GameManager")) as Component;stick=zhao.GetComponent(pt.Assembly.GetType("StickManController"));skill=zhao.GetComponent<MagneticFoot>();
        ball=(FindObjectOfType(pt.Assembly.GetType("Ball")) as Component).GetComponent<Rigidbody2D>();ball.position=new Vector2(0,3);ball.gravityScale=0;ball.velocity=Vector2.zero;
        Array muscleArray=stick.GetType().GetField("muscles").GetValue(stick) as Array;
        for(int i=6;i<10;i++)Debug.Log("MAGNETIC MUSCLE "+i+" "+((Rigidbody2D)muscleArray.GetValue(i).GetType().GetField("muscle").GetValue(muscleArray.GetValue(i))).name);
        Move(zhao,new Vector2(2,Body(zhao).position.y));Move(fan,new Vector2(-5,Body(fan).position.y));yield return new WaitForSeconds(.55f);
    }
    IEnumerator Run(string scenario)
    {
        yield return Scene(true);float scale=Mathf.Abs(zhao.transform.lossyScale.x)/.8f;
        if(scenario=="contest"||scenario=="air")Move(fan,new Vector2(Body(zhao).position.x-1.15f*scale,Body(fan).position.y));
        if(scenario=="air"){Move(zhao,new Vector2(Body(zhao).position.x,Body(zhao).position.y+.65f));Move(fan,new Vector2(Body(fan).position.x,Body(fan).position.y+.65f));}
        float radius=ball.GetComponent<Collider2D>().bounds.extents.x;
        ball.position=new Vector2(Body(zhao).position.x-(scenario=="dribble-close"?.4f:.6f)*scale,Floor()+radius+.008f+(scenario=="air"?.5f:0f));ball.gravityScale=1;
        ball.velocity=scenario=="receive"?Vector2.right*2f:scenario=="dribble-roll"?Vector2.left*1.5f:Vector2.zero;Physics2D.SyncTransforms();
        MagneticSpeedContactProbe probe=ball.gameObject.AddComponent<MagneticSpeedContactProbe>();
        if(scenario.StartsWith("dribble")||scenario=="contest")Held.Add(KeyCode.LeftArrow);
        if(scenario=="contest")Held.Add(KeyCode.D);
        float minAhead=100,maxGap=0,maxError=0,maxTilt=0,maxCrouch=0,maxKnee=0,maxActiveSpeed=0;int active=0,forwardSamples=0;bool equalLimit=true,equalForce=true;Vector2 initial=Body(zhao).position;
        for(int i=0;i<80;i++)
        {
            yield return new WaitForFixedUpdate();float ahead=Body(zhao).position.x-ball.position.x;
            minAhead=Mathf.Min(minAhead,ahead);maxGap=Mathf.Max(maxGap,ahead);maxError=Mathf.Max(maxError,Error(zhao));
            float torsoTarget=(float)stick.GetType().GetField("body").GetValue(stick);
            maxTilt=Mathf.Max(maxTilt,Mathf.Abs(Mathf.DeltaAngle(Body(zhao).rotation,torsoTarget)));
            // Once the ball is released the original running animation owns
            // the legs again. Measure the technique's support pose only while
            // it owns the feet; joint continuity is checked throughout.
                        if(skill.Active){active++;
                if(Held.Contains(KeyCode.LeftArrow)&&(bool)typeof(MagneticFoot).GetMethod("Eligible",IP).Invoke(skill,null))
                {
                    forwardSamples++;
                    float fanLimit=(float)pt.GetField("maxVelocity").GetValue(fan);
                    float zhaoLimit=(float)pt.GetField("maxVelocity").GetValue(zhao);
                    equalLimit&=Mathf.Abs(PlayerMovement.GetMovementLimit(zhaoLimit,zhao)-fanLimit)<.001f;
                    if(!PlayerSkills.IsGroundBallContest(zhao))equalForce&=Mathf.Abs(PlayerMovement.GetMovementForce((float)pt.GetField("playerSpeed").GetValue(zhao),zhao)-(float)pt.GetField("playerSpeed").GetValue(fan))<.01f;
                    maxActiveSpeed=Mathf.Max(maxActiveSpeed,-Body(zhao).velocity.x);
                }maxCrouch=Mathf.Max(maxCrouch,initial.y-Body(zhao).position.y);
                foreach(string side in new[]{"L","R"})maxKnee=Mathf.Max(maxKnee,Mathf.Abs(Mathf.DeltaAngle(zhao.transform.Find(side+"_Up_Leg").GetComponent<Rigidbody2D>().rotation,zhao.transform.Find(side+"_LowLeg").GetComponent<Rigidbody2D>().rotation)));}
            if(i%10==0)Debug.Log("MAGNETIC TRACE "+scenario+" i="+i+" ahead="+ahead+" ball="+ball.position+" velocity="+ball.velocity+" active="+skill.Active+" legs="+zhao.transform.Find("L_LowLeg").GetComponent<Rigidbody2D>().rotation+","+zhao.transform.Find("R_LowLeg").GetComponent<Rigidbody2D>().rotation+" targets="+stick.GetType().GetField("L_Low_Leg").GetValue(stick)+","+stick.GetType().GetField("R_Low_Leg").GetValue(stick)+" scale="+zhao.transform.Find("L_LowLeg").lossyScale);
            if(i==35||i==75)RenderRig("magnetic-"+scenario+"-"+i);
        }
        if(Held.Contains(KeyCode.LeftArrow)){
            Check(forwardSamples>5,"multiple real eligible magnetic forward samples "+scenario);
            Check(equalLimit&&active>0,"active magnetic forward cap equals Fan without sprint "+scenario);
            Check(equalForce,"active ordinary movement force equals Fan; contest pressure retained "+scenario);
            Debug.Log("MAGNETIC SPEED scenario="+scenario+" maxActiveSpeed="+maxActiveSpeed+" fanLimit="+pt.GetField("maxVelocity").GetValue(fan));
        }
        Held.Clear();Debug.Log("MAGNETIC RUN "+scenario+" minAhead="+minAhead+" maxGap="+maxGap+" active="+active+" contacts="+probe.contacts+" tilt="+maxTilt+" error="+maxError+" travel="+(Body(zhao).position.x-initial.x));
        Check(active>5,"technique activates on nearby ball "+scenario);Check(probe.contacts>0,"physical instep contact "+scenario);
        Check(minAhead>=0f,"never stamps ball behind own body "+scenario+" ahead="+minAhead);
        Check(maxError<.3f,"connected skeleton "+scenario+" error="+maxError);Check(maxTilt<30f,"torso follows native posture "+scenario+" deviation="+maxTilt);
        Check(((float[])Field(zhao.GetComponent<PlayerSkills>(),"effects"))[8]>=0,"magnetic foot animation appears "+scenario);
        if(scenario.StartsWith("dribble")){Check(Body(zhao).position.x<initial.x-.5f*scale,"dribbles through real movement "+scenario);
            // A short bent-knee lunge is valid ball control; deep kneeling or
            // folding a shin onto its thigh is not. Check both height and bend.
            Check(maxCrouch<.4f*scale,"support leg prevents deep kneeling "+scenario+" crouch="+maxCrouch);
            Check(maxKnee<100f,"knees do not fold onto thighs "+scenario+" bend="+maxKnee);}
    }
    IEnumerator Guards()
    {
        yield return Scene(false);Check(skill==null&&fan.GetComponent<MagneticFoot>()==null,"classic has no magnetic skill");
        Held.Add(KeyCode.LeftArrow);Check(Mathf.Abs(PlayerMovement.GetMultiplier(zhao)-.9f)<.001f,"classic forward speed remains unchanged");Held.Clear();
        PlayerSkills.SetAction(zhao.GetComponent<Animator>(),"Kick",zhao);Check(Mathf.Abs(zhao.GetComponent<Animator>().speed-1f)<.01f,"classic native shooting rate");
        yield return Scene(true);Check(skill!=null&&fan.GetComponent<MagneticFoot>()==null,"only Zhao receives skill");
        float radius=ball.GetComponent<Collider2D>().bounds.extents.x;
        ball.position=new Vector2(Body(zhao).position.x-2.5f,Floor()+radius+.01f);ball.velocity=Vector2.zero;ball.gravityScale=0;Physics2D.SyncTransforms();
        Vector2 original=ball.position;for(int i=0;i<10;i++)yield return new WaitForFixedUpdate();
        Check(!skill.Active,"distant ball never triggers skill");Held.Add(KeyCode.LeftArrow);Check(Mathf.Abs(PlayerMovement.GetMultiplier(zhao)-.9f)<.001f,"inactive forward speed stays at baseline");Held.Clear();Held.Add(KeyCode.RightArrow);Check(Mathf.Abs(PlayerMovement.GetMultiplier(zhao)-1.5f)<.001f,"backward speed stays unchanged");Held.Clear();Check(Vector2.Distance(ball.position,original)<.001f&&ball.velocity.magnitude<.001f,"no attraction or remote ball motion");
        ball.position=new Vector2(Body(zhao).position.x+.45f,Floor()+radius+.01f);Physics2D.SyncTransforms();yield return null;MagneticFoot.BeforeMuscles(stick);Check(!skill.Active,"behind ball not retrieved");
        foreach(string action in new[]{"Kick","Head","Jump"})
        {
            ball.position=new Vector2(Body(zhao).position.x-.55f,Floor()+radius+.01f);Physics2D.SyncTransforms();
            PlayerSkills.SetAction(zhao.GetComponent<Animator>(),action,zhao);MagneticFoot.BeforeMuscles(stick);
            Check(!skill.Active,"explicit "+action+" releases technique");
            if(action=="Kick")Check(Mathf.Abs(zhao.GetComponent<Animator>().speed-1f)<.01f,"removed old +25% quick shot acceleration");
            yield return new WaitForSeconds(.8f);
        }
        ball.position=new Vector2(Body(zhao).position.x-.55f,Floor()+radius+.01f);Physics2D.SyncTransforms();yield return null;
        Time.timeScale=0;MagneticFoot.BeforeMuscles(stick);Check(!skill.Active,"pause releases foot");Time.timeScale=1;
        manager.GetType().GetField("isStopping").SetValue(manager,true);MagneticFoot.BeforeMuscles(stick);Check(!skill.Active,"goal/out stopping releases foot");manager.GetType().GetField("isStopping").SetValue(manager,false);
        PlayerSkills.ResetAll();Check(!skill.Active,"round reset releases foot");
        Array muscles=stick.GetType().GetField("muscles").GetValue(stick) as Array;bool restored=true;for(int i=6;i<10;i++)restored&=(float)muscles.GetValue(i).GetType().GetField("force").GetValue(muscles.GetValue(i))>100f;
        Check(restored,"native leg gains restored after reset");Check(zhao.GetComponentsInChildren<Joint2D>().Length==9,"no ball tether or added skeleton joints");
        ball.position=new Vector2(Body(zhao).position.x-.55f,Floor()+radius+.01f);ball.velocity=Vector2.right*20f;Physics2D.SyncTransforms();MagneticFoot.BeforeMuscles(stick);Check(!skill.Active,"fast incoming shot cannot be magically caught");
    }
    IEnumerator Start()
    {
        Application.runInBackground=true;Application.targetFrameRate=120;QualitySettings.vSyncCount=0;AudioListener.volume=0;
        yield return new WaitForSeconds(.2f);yield return Guards();foreach(string s in new[]{"dribble","dribble-close","dribble-roll","receive","contest","air"})yield return Run(s);
        Debug.Log("MAGNETIC COMPLETE checks="+checks+" failures="+failures);Application.Quit(failures==0?0:1);
    }
    void Update(){if(Time.realtimeSinceStartup>90){Debug.LogError("MAGNETIC TIMEOUT");Application.Quit(2);}}
}
