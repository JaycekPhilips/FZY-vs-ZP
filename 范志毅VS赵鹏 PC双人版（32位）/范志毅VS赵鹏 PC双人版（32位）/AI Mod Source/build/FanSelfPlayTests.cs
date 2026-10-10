using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

public sealed class FanSelfPlayBallContactProbe : MonoBehaviour
{
    void OnCollisionEnter2D(Collision2D c) { Observe(c); }
    void OnCollisionStay2D(Collision2D c) { Observe(c); }
    void Observe(Collision2D c) { if(c.collider!=null)FanSelfPlayTests.Contact(c.collider.transform.root.name); }
}
// Isolated test assembly only. Both actors use native inputs and physics.
public sealed class FanSelfPlayTests : MonoBehaviour
{
    public static bool UseAI;
    public static bool ReadKey(KeyCode key) { return false; }
    public static bool ReadDown(KeyCode key) { return false; }
    static bool booted; static FanSelfPlayTests instance;
    public static void Action(Component player, string action)
    {
        if(instance==null||!instance.ready)return;
        if(player.name=="Fan"){if(action=="Jump")instance.results.fanJumps++;if(action=="Head")instance.results.fanHeaders++;if(action=="Kick")instance.results.fanKicks++;return;}
        if(player.name!="Zhao")return;
        if(action=="Jump")instance.results.jumps++;
        if(action=="Head")instance.results.headers++;
    }
    public static void Contact(string name) { if(instance!=null && instance.ready && (name=="Fan"||name=="Zhao")) { instance.lastTouch=name;instance.lastTouchTime=Time.time;instance.lastTouchRescue=name=="Zhao" && (instance.SkillField("retreatUntil")>=Time.time || instance.SkillField("rescueRecoveryUntil")>=Time.time);instance.lastTouchVx=instance.ball.velocity.x; } }
    public static void RescueStarted() { if(instance!=null && instance.ready) { instance.results.rescues++;instance.lastRescue=Time.time; } }
    public static void RescueStrike() { Contact("Zhao"); }
    const BindingFlags SP = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags IP = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [Serializable] public sealed class Results
    {
        public string edition, policyHash, validationVersion;
        public int workerId, physicsSteps, renderedFrames, maxPhysicsStepsPerFrame, target, goals, fanGoals, zhaoGoals, matches, draws, failures, jumps, headers, tackles, rescues, fanJumps, fanHeaders, fanKicks;
        public float simulatedSeconds, realSeconds, maxJointError, timelineSeconds;
    }
    Results results = new Results();
    Component fan, zhao, manager; Rigidbody2D ball, zhaoBody, fanBody;
    FieldInfo p1, p2; StreamWriter events, trace;
    string progressPath; int target = 2000;float playbackSpeed=24f;
    float epochOffset, lastTrace, lastProgress, began, priorReal, lastGoal, tackleStamp = -1f;
    int physicalFrame=-1,stepsThisFrame;bool ready,lastTouchRescue;float lastTouchVx;string lastTouch="None";float lastTouchTime=-100f,lastRescue=-100f;
    public static void Boot()
    {
        if (booted) return; booted = true;
        ControlBindings.settingsPath = Path.Combine(Application.dataPath, "..", "selfplay-test-bindings.ini");
        GameObject host = new GameObject("FanSelfPlayTests"); DontDestroyOnLoad(host); host.AddComponent<FanSelfPlayTests>();
    }
    object State(Component p) { return typeof(GameAIMod).GetMethod("GetState", SP).Invoke(null, new object[] { p }); }
    object Read(object s, string n) { FieldInfo f=s.GetType().GetField(n,IP);return f!=null?f.GetValue(s):null; }
    float SkillField(string n) { var s=zhao.GetComponent<PlayerSkills>();if(s==null)return -1;FieldInfo f=typeof(PlayerSkills).GetField(n,IP);return f!=null?(float)f.GetValue(s):-1; }
    void Save()
    {
        results.timelineSeconds=epochOffset+Time.time;
        results.realSeconds = priorReal + Time.realtimeSinceStartup - began;
        File.WriteAllText(progressPath, JsonUtility.ToJson(results,true));
        events.Flush(); trace.Flush();
    }
    void Fail(string text)
    {
        results.failures++; Debug.LogError("SELFPLAY FAIL "+text);Save();Application.Quit(1);
    }
    void ObserveGoal(bool zhaoScored, int localFan, int localZhao)
    {
        if(results.goals>=target)return;
        results.goals++; if(zhaoScored)results.zhaoGoals++;else results.fanGoals++;
        lastGoal=Time.time;
        object s=State(zhao);object fs=State(fan);
        string row=results.goals+","+results.matches+","+(zhaoScored?"Zhao":"Fan")+","+(zhaoScored?1:-1)+","+localFan+","+localZhao+","+(epochOffset+Time.time).ToString("F3")+","+ball.position.x+","+ball.position.y+","+ball.velocity.x+","+ball.velocity.y+","+zhaoBody.position.x+","+zhaoBody.position.y+","+Read(s,"highBallPlan")+","+Read(s,"interceptX")+","+Read(s,"interceptTime")+","+(SkillField("retreatUntil")>=Time.time)+","+lastTouch+","+(lastTouchTime+epochOffset)+","+(lastRescue+epochOffset)+","+lastTouchRescue+","+lastTouchVx+","+fanBody.position.x+","+fanBody.position.y+","+Read(fs,"highBallPlan")+","+Read(fs,"contesting")+","+Read(fs,"kick")+","+Read(fs,"head");
        events.WriteLine(row);
        Debug.Log("SELFPLAY GOAL "+results.goals+" scorer="+(zhaoScored?"Zhao":"Fan")+" ZhaoReward="+(zhaoScored?1:-1)+" total="+results.fanGoals+":"+results.zhaoGoals);
        Save();
    }
    void ObserveRig(Component p)
    {
        foreach(HingeJoint2D j in p.GetComponentsInChildren<HingeJoint2D>())
        {
            if(j.connectedBody==null) { Fail("disconnected joint "+p.name+" "+j.name);return; }
            float e=Vector2.Distance(j.transform.TransformPoint(j.anchor),j.connectedBody.transform.TransformPoint(j.connectedAnchor));
            if(float.IsNaN(e)||float.IsInfinity(e)) { Fail("nonfinite rig "+p.name);return; }
            results.maxJointError=Mathf.Max(results.maxJointError,e);
        }
    }
    void FixedUpdate()
    {
        if(!ready || manager==null || zhao==null || fan==null || ball==null)return;
        results.physicsSteps++;if(physicalFrame==Time.frameCount)stepsThisFrame++;else { physicalFrame=Time.frameCount;stepsThisFrame=1; } results.maxPhysicsStepsPerFrame=Math.Max(results.maxPhysicsStepsPerFrame,stepsThisFrame);
        int a=(int)p1.GetValue(manager),b=(int)p2.GetValue(manager);
        while(lastFan<a) { lastFan++;ObserveGoal(false,a,b); }
        while(lastZhao<b) { lastZhao++;ObserveGoal(true,a,b); }
        if(Time.fixedTime-lastTrace<.1f)return;lastTrace=Time.fixedTime;
        ObserveRig(fan);ObserveRig(zhao);
        object s=State(zhao);object fs=State(fan);
        float rescue=SkillField("retreatUntil");
        float[] effects=(float[])typeof(PlayerSkills).GetField("effects",IP).GetValue(zhao.GetComponent<PlayerSkills>());
        if(effects[3]>tackleStamp){if(effects[3]>0)results.tackles++;tackleStamp=effects[3];}

        trace.WriteLine((epochOffset+Time.time).ToString("F3")+","+results.matches+","+ball.position.x+","+ball.position.y+","+ball.velocity.x+","+ball.velocity.y+","+zhaoBody.position.x+","+zhaoBody.position.y+","+Read(s,"axis")+","+Read(s,"highBallPlan")+","+Read(s,"interceptX")+","+Read(s,"interceptTime")+","+Read(s,"jump")+","+Read(s,"head")+","+Read(s,"contesting")+","+(rescue>=Time.time)+","+Read(fs,"axis")+","+fanBody.position.x+","+fanBody.position.y+","+fanBody.velocity.x+","+fanBody.velocity.y+","+Read(fs,"jump")+","+Read(fs,"head")+","+Read(fs,"kick")+","+Read(fs,"power")+","+Read(fs,"down")+","+Read(fs,"highBallPlan")+","+Read(fs,"contesting")+","+Read(fs,"interceptX")+","+Read(fs,"interceptTime"));
        if(Time.realtimeSinceStartup-lastProgress>30f){lastProgress=Time.realtimeSinceStartup;Save();Debug.Log("SELFPLAY PROGRESS goals="+results.goals+"/"+target+" matches="+results.matches+" score="+results.fanGoals+":"+results.zhaoGoals+" real="+results.realSeconds);}
    }
    int lastFan,lastZhao;
    IEnumerator Start()
    {
        instance=this; began=Time.realtimeSinceStartup;Application.runInBackground=true;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;AudioListener.volume=0;
        Time.maximumDeltaTime=.02f;
        string[] args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++) { if(args[i]=="-selfplayWorker")int.TryParse(args[i+1],out results.workerId); if(args[i]=="-selfplaySpeed")float.TryParse(args[i+1],out playbackSpeed); if(args[i]=="-selfplayGoals")int.TryParse(args[i+1],out target); if(args[i]=="-policyHash")results.policyHash=args[i+1]; }
        if(target<1)target=2000;
        string root=Path.Combine(Application.dataPath,"..");progressPath=Path.Combine(root,"selfplay-progress.json");
        results.target=target;results.edition=Application.dataPath;results.validationVersion="fan-v1-current-opponent";
        if(string.IsNullOrEmpty(results.policyHash))throw new InvalidOperationException("Missing stable source policy hash");
        if(File.Exists(progressPath)) { Results previous=JsonUtility.FromJson<Results>(File.ReadAllText(progressPath));if(previous.workerId==results.workerId && previous.policyHash==results.policyHash && previous.validationVersion==results.validationVersion && previous.failures==0){results=previous;results.target=target;priorReal=results.realSeconds;epochOffset=results.timelineSeconds;} }
        events=new StreamWriter(Path.Combine(root,"selfplay-goals.csv"),results.goals>0);trace=new StreamWriter(Path.Combine(root,"selfplay-trace.csv"),results.goals>0);
        if(results.goals==0){events.WriteLine("goal,match,scorer,zhao_reward,fan_score,zhao_score,time,ball_x,ball_y,ball_vx,ball_vy,zhao_x,zhao_y,high_plan,target_x,eta,rescue,last_touch,last_touch_time,last_rescue_time,last_touch_rescue,last_touch_vx,fan_x,fan_y,fan_high_plan,fan_contest,fan_kick,fan_head");trace.WriteLine("time,match,ball_x,ball_y,ball_vx,ball_vy,zhao_x,zhao_y,axis,high_plan,target_x,eta,jump,head,contest,rescue,fan_axis,fan_x,fan_y,fan_vx,fan_vy,fan_jump,fan_head,fan_kick,fan_power,fan_down,fan_high_plan,fan_contest,fan_target_x,fan_eta");}
        yield return null;
        while(results.goals<target)
        {
            ready=false;UseAI=false;PlayerSkills.Enabled=true;Time.timeScale=1f;ControlBindings.ResetDefaults();UnityEngine.Random.InitState(19100+results.workerId*100000+results.matches);
            typeof(GameAIMod).GetMethod("StartGame",SP).Invoke(null,new object[]{0});yield return new WaitForSeconds(.3f);
            Type pt=Type.GetType("PlayerController, Assembly-CSharp");
            fan=GameObject.Find("Fan").GetComponent(pt);zhao=GameObject.Find("Zhao").GetComponent(pt);zhaoBody=(Rigidbody2D)pt.GetField("rb").GetValue(zhao);fanBody=(Rigidbody2D)pt.GetField("rb").GetValue(fan);
            manager=FindObjectOfType(pt.Assembly.GetType("GameManager")) as Component;ball=(FindObjectOfType(pt.Assembly.GetType("Ball")) as Component).GetComponent<Rigidbody2D>();
            ball.gameObject.AddComponent<FanSelfPlayBallContactProbe>();lastTouch="None";lastTouchTime=lastRescue=-100f;lastTouchRescue=false;lastTouchVx=0;
            foreach(UnityEngine.Object goalObject in FindObjectsOfType(pt.Assembly.GetType("GoalTrigger"))) { Component goal=(Component)goalObject; Debug.Log("SELFPLAY GOALMAP x="+goal.transform.position.x+" isPlayer1Goal="+goal.GetType().GetField("isPlayer1Goal").GetValue(goal)); }
            p1=manager.GetType().GetField("p1Score",IP);p2=manager.GetType().GetField("p2Score",IP);lastFan=(int)p1.GetValue(manager);lastZhao=(int)p2.GetValue(manager);
            foreach(Animator a in FindObjectsOfType<Animator>())a.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            results.matches++;lastGoal=Time.time;float start=Time.time;int before=results.goals;tackleStamp=-1;
            ready=UseAI=true;
            while(results.goals<target && Time.time-start<90f)
            {
                // Speed up wall-clock observation, preserving the native .02s
                // physics steps, collisions, inputs, animation and restarts.
                Time.timeScale=playbackSpeed;yield return null;
                if(manager==null||zhao==null||fan==null)break;
            }
            results.simulatedSeconds+=Time.time-start;
            if(results.goals==before)results.draws++;
            Debug.Log("SELFPLAY MATCH "+results.matches+" goals="+(results.goals-before)+" total="+results.goals+" score="+lastFan+":"+lastZhao);
            Save();
            if(results.matches>10000){Fail("too many matches without 2000 actual goals");yield break;}
        }
        ready=UseAI=false;Time.timeScale=1f;Save();events.Dispose();trace.Dispose();events=trace=null;
        Debug.Log("SELFPLAY COMPLETE goals="+results.goals+" fan="+results.fanGoals+" zhao="+results.zhaoGoals+" matches="+results.matches+" failures="+results.failures);Application.Quit(results.failures==0?0:1);
    }
    void Update(){ if(ready)results.renderedFrames++; }
    void OnApplicationQuit(){if(events!=null){events.Flush();}if(trace!=null){trace.Flush();}}
}