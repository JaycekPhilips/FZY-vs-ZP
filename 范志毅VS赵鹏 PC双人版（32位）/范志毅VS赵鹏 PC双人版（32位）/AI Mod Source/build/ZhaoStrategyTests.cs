using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class AIAirContactProbe : MonoBehaviour
{
    public string playerName;
    public bool touched;
    void OnCollisionEnter2D(Collision2D c) { Observe(c); }
    void OnCollisionStay2D(Collision2D c) { Observe(c); }
    void Observe(Collision2D c) { if (c.collider != null && c.collider.transform.root.name == playerName) touched = true; }
}

// This host is compiled into an isolated test DLL, never into the game release.
public sealed class ZhaoStrategyTests : MonoBehaviour
{
    public static bool UseAI;
    public static int NativeJumps, NativeHeads;
    public static void Action(Component p,string action) { if(!UseAI || p.name!="Zhao")return;if(action=="Jump")NativeJumps++;if(action=="Head")NativeHeads++; }
    public static HashSet<KeyCode> Held = new HashSet<KeyCode>();
    public static bool ReadKey(KeyCode key) { return Held.Contains(key); }
    public static bool ReadDown(KeyCode key) { return false; }
    static bool booted;
    const BindingFlags SP = BindingFlags.Static | BindingFlags.NonPublic;
    Component fan, zhao, player, opponent;
    Rigidbody2D ball, body, other;
    Type pt;
    float scale, direction, floor;
    int checks, failures, lobTouches;
    object state;
    public static void Boot()
    {
        if (booted) return; booted = true;
        ControlBindings.settingsPath = System.IO.Path.Combine(Application.dataPath, "..", "ai-test-bindings.ini");
        var host = new GameObject("ZhaoStrategyTests"); DontDestroyOnLoad(host); host.AddComponent<ZhaoStrategyTests>();
    }
    void Check(bool ok, string label) { checks++; if (!ok) failures++; Debug.Log((ok ? "STRATEGY PASS " : "STRATEGY FAIL ") + label); }
    object Read(string name) { return state.GetType().GetField(name).GetValue(state); }
    void Put(string name, object value) { state.GetType().GetField(name).SetValue(state, value); }
    Rigidbody2D Body(Component p) { return pt.GetField("rb").GetValue(p) as Rigidbody2D; }
    void Move(Component p, Vector2 position)
    {
        p.transform.position += (Vector3)(position - Body(p).position);
        foreach (Rigidbody2D limb in p.GetComponentsInChildren<Rigidbody2D>()) { limb.velocity = Vector2.zero; limb.angularVelocity = 0; }
        Physics2D.SyncTransforms();
    }
    float JointError(Component p)
    {
        float error = 0f;
        foreach (HingeJoint2D j in p.GetComponentsInChildren<HingeJoint2D>())
            if (j.connectedBody != null) error = Mathf.Max(error, Vector2.Distance(j.transform.TransformPoint(j.anchor), j.connectedBody.transform.TransformPoint(j.connectedAnchor)));
        return error;
    }
    void Decide()
    {
        Put("frame", -1); GameAIMod.GetAxis("Horizontal", player);
    }
    void FreeDecision()
    {
        Put("serveApproach", false); Put("waitingForServe", false); Put("contestUntil", -1f);
        Put("lastJump", -10f); Put("lastKick", -10f); Put("lastHead", -10f);
        pt.GetField("isOnGround").SetValue(player, true); Decide();
    }
    IEnumerator Scene(bool skills, bool fanAI)
    {
        UseAI = false;NativeJumps=NativeHeads=0; Held.Clear(); Time.timeScale = 1f; PlayerSkills.Enabled = skills;
        typeof(GameAIMod).GetMethod("StartGame", SP).Invoke(null, new object[] { fanAI ? 2 : 1 });
        yield return new WaitForSeconds(.25f);
        pt = Type.GetType("PlayerController, Assembly-CSharp");
        fan = GameObject.Find("Fan").GetComponent(pt); zhao = GameObject.Find("Zhao").GetComponent(pt);
        player = fanAI ? fan : zhao; opponent = fanAI ? zhao : fan;
        body = Body(player); other = Body(opponent); direction = fanAI ? 1f : -1f;
        scale = Mathf.Abs(player.transform.lossyScale.x) / .8f;
        ball = (FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        floor = GameObject.Find("Down").GetComponent<Collider2D>().bounds.max.y;
        ball.position = new Vector2(0, 3f); ball.velocity = Vector2.zero; ball.gravityScale = 0f;
        Move(player, new Vector2(-direction * 2f, body.position.y)); Move(opponent, new Vector2(direction * 5f, other.position.y));
        yield return new WaitForSeconds(.3f);
        state = typeof(GameAIMod).GetMethod("GetState", SP).Invoke(null, new object[] { player });
        Debug.Log("STRATEGY PARAM side=" + player.name + " skills=" + skills + " scale=" + scale + " jump=" + Read("jumpSpeed") + " gravity=" + body.gravityScale + " head=" + player.transform.Find("Head").position.y);
    }
    IEnumerator Contest(bool skills, bool fanAI, bool airborne)
    {
        yield return Scene(skills, fanAI);
        Move(player, new Vector2(-direction * .65f * scale, body.position.y));
        Move(opponent, new Vector2(direction * .65f * scale, other.position.y));
        ball.position = new Vector2(0, floor + ball.GetComponent<Collider2D>().bounds.extents.y + .004f);
        ball.gravityScale = 1f;
        if (airborne)
        {
            foreach (Component p in new[] { player, opponent })
            {
                p.transform.position += Vector3.up * 1.4f;
                foreach (Rigidbody2D limb in p.GetComponentsInChildren<Rigidbody2D>()) limb.gravityScale = 0f;
            }
            ball.position += Vector2.up * 1.4f; ball.gravityScale = 0f;
        }
        Physics2D.SyncTransforms(); Put("serveApproach", false); Put("waitingForServe", false);
        UseAI = true; Held.Add(fanAI ? KeyCode.LeftArrow : KeyCode.D);
        int contestFrames = 0, kicks = 0, heads = 0, wrongDirections = 0; float maxError = 0f;
        for (int i = 0; i < 100; i++)
        {
            yield return new WaitForFixedUpdate(); Decide();
            if ((bool)Read("contesting"))
            {
                contestFrames++;
                if ((bool)Read("kick") || (bool)Read("power")) kicks++;
                if ((bool)Read("head")) heads++;
                if (Mathf.Abs(other.position.x - body.position.x) > .05f && (float)Read("axis") * Mathf.Sign(other.position.x - body.position.x) <= .1f) wrongDirections++;
            }
            maxError = Mathf.Max(maxError, JointError(player), JointError(opponent));
        }
        string label = player.name + " skills=" + skills + " airborne=" + airborne;
        Check(contestFrames > 0, "real contest recognized " + label + " frames=" + contestFrames);
        Check(kicks == 0 && heads == 0, "contest uses directions instead of striking " + label);
        Check(wrongDirections == 0, "direction opposes the human player " + label);
        Check(maxError < .35f, "direction pushing preserves joints " + label + " error=" + maxError);
        // A separated opponent must not keep suppressing normal free shots.
        Held.Clear(); UseAI = false; Move(opponent, new Vector2(direction * 5f, other.position.y));
        ball.position = new Vector2(body.position.x + direction * .7f * scale, player.transform.Find("L_LowLeg").position.y);
        ball.velocity = Vector2.zero; Physics2D.SyncTransforms(); UseAI = true; FreeDecision();
        Check(!(bool)Read("contesting"), "separation releases contest suppression " + label);
        UseAI = false;
    }
    IEnumerator Decisions(bool skills, bool fanAI)
    {
        yield return Scene(skills, fanAI); UseAI = true;
        float headY = player.transform.Find("Head").position.y;
        ball.gravityScale = 1f;
        Transform foot = Read("foot") as Transform;
        ball.position = new Vector2(body.position.x + direction * .7f * scale, foot.position.y + .5f * scale);
        ball.velocity = Vector2.zero; FreeDecision();
        Check(!(bool)Read("contesting") && (bool)Read("kick"), "normal free ball shooting remains available " + player.name + " skills=" + skills);
        // The ball is rising far above attainable height. Run for its later
        // descent, never jump or strike while it is unreachable.
        ball.position = new Vector2(body.position.x + direction * .3f, headY + 3f);
        ball.velocity = new Vector2(direction * 3f, 6f); FreeDecision();
        Check((bool)Read("highBallPlan") && !(bool)Read("jump") && !(bool)Read("kick") && !(bool)Read("head"), "unreachable rising ball avoids premature actions " + player.name + " skills=" + skills);
        Check(direction * ((float)Read("interceptX") - ball.position.x) > .4f, "moving high ball leads future descent " + player.name);
        // An overhead descending ball that is physically within jump reach.
        ball.position = new Vector2(body.position.x + direction * .25f * scale, headY + 1.3f * scale);
        ball.velocity = new Vector2(0f, -1f); FreeDecision();
        Check((bool)Read("jump") && !(bool)Read("kick"), "reachable descending ball schedules jump " + player.name + " skills=" + skills);
        // A fast ball will have passed overhead before this player can rise.
        ball.position = new Vector2(body.position.x, headY + 1.4f);
        ball.velocity = new Vector2(direction * 16f, -1f); FreeDecision();
        Check(!(bool)Read("jump"), "fast overhead pass avoids futile jump " + player.name);
        // Serve approach keeps its existing attack-only rule.
        ball.position = new Vector2(0f, 3.27f); ball.velocity = Vector2.zero;
        Move(player, new Vector2(-direction * 3f, body.position.y)); Put("serveApproach", true); Put("waitingForServe", true); Put("lastServeReset", Time.time); Decide();
        Check((float)Read("axis") * direction >= 0f && !(bool)Read("jump"), "serve advances without retreat or meaningless jump " + player.name);
        Time.timeScale = 0f; Decide();
        Check((float)Read("axis") == 0f && !(bool)Read("kick") && !(bool)Read("jump"), "paused AI has no commands " + player.name);
        Time.timeScale = 1f; UseAI = false;
    }
    IEnumerator Lob(bool skills, bool fanAI)
    {
        yield return Scene(skills, fanAI);
        Put("serveApproach", false); Put("waitingForServe", false);
        float headY = player.transform.Find("Head").position.y;
        // Natural descending lob, with all player limbs and ball simulated.
        ball.position = new Vector2(body.position.x - direction * .6f, headY + 2.3f);
        ball.velocity = new Vector2(direction * 3.2f, -.5f); ball.gravityScale = 1f;
        AIAirContactProbe probe = ball.gameObject.AddComponent<AIAirContactProbe>(); probe.playerName = player.name;
        UseAI = true; bool planned = false, jumpSeen = false, distantJump = false, skillHeader = false; float error = 0f;
        for (int i = 0; i < 100; i++)
        {
            yield return new WaitForFixedUpdate();
            if (skills && i % 8 == 0) Debug.Log("STRATEGY LOB step=" + i + " ball=" + ball.position + " velocity=" + ball.velocity + " player=" + body.position + " run=" + body.velocity + " head=" + player.transform.Find("Head").position + " target=" + Read("interceptX") + " eta=" + Read("interceptTime") + " jump=" + Read("jump") + " heading=" + Read("head") + " plan=" + Read("highBallPlan") + " rescue=" + Read("rescuing") + " standing=" + Read("standingHeadY") + " axis=" + Read("axis"));
            planned |= (bool)Read("highBallPlan"); jumpSeen |= (bool)Read("jump");
            if (skills && fanAI)
            {
                var fanSkill = player.GetComponent<PlayerSkills>();
                const BindingFlags IP = BindingFlags.Instance | BindingFlags.NonPublic;
                object glowOwner = typeof(PlayerSkills).GetField("glowOwner", SP).GetValue(null);
                float headUntil = (float)typeof(PlayerSkills).GetField("headUntil", IP).GetValue(fanSkill);
                float headDistance = Vector2.Distance(player.transform.Find("Head").GetComponent<Collider2D>().ClosestPoint(ball.position), ball.position);
                skillHeader |= object.ReferenceEquals(glowOwner, fanSkill) && headUntil < 0f && ball.velocity.x * direction > 10f && headDistance < .65f;
            }
            if ((bool)Read("jump") && Mathf.Abs(ball.position.x - body.position.x) > 2f) distantJump = true;
            error = Mathf.Max(error, JointError(player));
        }
        Check(planned && !distantJump, "live lob plans reachable contact " + player.name + " skills=" + skills + " jump=" + jumpSeen);
        Check(probe.touched || skillHeader, "live lob intercepted by native collision or existing head skill " + player.name + " skills=" + skills + " collision=" + probe.touched + " header=" + skillHeader);
        Check(error < .35f, "lob interception preserves skeleton " + player.name + " error=" + error);
        if (probe.touched || skillHeader) lobTouches++;
        UseAI = false;
    }
    object Skill(string name) { return typeof(PlayerSkills).GetField(name, BindingFlags.Instance|BindingFlags.NonPublic).GetValue(player.GetComponent<PlayerSkills>()); }
    IEnumerator Recovery(string kind, float gap=1.2f, float rolling=2f)
    {
        yield return Scene(true,false);
        Put("serveApproach",false);Put("waitingForServe",false);
        Move(player,new Vector2(0,body.position.y));
        float height=(float)Skill("standingHeight"), headHeight=(float)Skill("standingHeadHeight");
        float radius=ball.GetComponent<Collider2D>().bounds.extents.y;
        float y=kind=="low" ? floor+radius+.015f : kind=="head" ? floor+headHeight : floor+height+1.8f;
        ball.position=new Vector2(body.position.x+(kind=="head"?.5f:gap),y);
        ball.velocity=kind=="low"?Vector2.right*rolling:Vector2.zero;ball.gravityScale=kind=="low"?1f:0f;
        Physics2D.SyncTransforms();UseAI=true;FreeDecision();
        Check((bool)Read("rescuing") && PlayerSkills.RescueActive(player),kind+" AI starts native emergency recovery without human input");
        bool jumped=false, headed=false, passed=false, interrupted=false;float error=0, tilt=0, longestAttempt=0;
        Component gm=FindObjectOfType(pt.Assembly.GetType("GameManager")) as Component;
        var score=gm.GetType().GetField("p1Score",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);int initial=(int)score.GetValue(gm);
        bool goal=false;
        kind += " gap="+gap+" rolling="+rolling;
        for(int i=0;i<215;i++)
        {
            yield return new WaitForFixedUpdate();
            jumped|=(bool)Skill("retreatJumped");headed|=(bool)Skill("retreatHeaded");
            passed|=body.position.x>ball.position.x+radius;
            if(UseAI && !PlayerSkills.RescueActive(player) && (passed||headed))UseAI=false;
            if(i%20==0)Debug.Log("STRATEGY RECOVERY "+kind+" step="+i+" ball="+ball.position+" vel="+ball.velocity+" body="+body.position+" running="+body.velocity+" rescue="+PlayerSkills.RescueActive(player)+" passed="+passed);
            error=Mathf.Max(error,JointError(player));
            goal|=(int)score.GetValue(gm)>initial;
            if(PlayerSkills.RescueActive(player)) { interrupted|=(bool)Read("kick");longestAttempt=Mathf.Max(longestAttempt,Time.time-(float)Skill("rescueStarted")); }
            if(i>185)tilt=Mathf.Max(tilt,Mathf.Abs(Mathf.DeltaAngle(body.rotation,0)));
        }
        Check(kind.StartsWith("low")?jumped:kind.StartsWith("head")?headed:!jumped,kind+" AI uses correct jump/backbend/run branch");
        Check(kind.StartsWith("head")?headed:passed,kind+" recovery reaches goal side or clears head ball");
        Check(!goal,kind+" recovery avoids scoring into own goal");
        Check(error<.35f,kind+" real skeleton preserved error="+error);
        Check(tilt<35f,kind+" balanced after recovery tilt="+tilt);
        Check(!interrupted,kind+" committed recovery is not interrupted by shooting");Check(longestAttempt<=3.6f,kind+" each attempt has a bounded duration="+longestAttempt);UseAI=false;
    }
    IEnumerator HighJudgment()
    {
        yield return Scene(true,false);UseAI=true;
        float standing=(float)Read("standingHeadY");
        ball.position=new Vector2(body.position.x-1.4f*scale,standing+8f*scale);ball.velocity=new Vector2(-2f,3f);ball.gravityScale=1f;
        FreeDecision();Check((bool)Read("highBallPlan") && !(bool)Read("jump") && Mathf.Abs((float)Read("axis"))>.05f,"unreachable rising high ball prepositions instead of jumping");
        Check(Mathf.Abs((float)Read("standingHeadY")-standing)<.001f,"aerial plan retains standing reference height");
        ball.position=new Vector2(body.position.x,standing+1.4f*scale);ball.velocity=Vector2.left*16f+Vector2.down;
        FreeDecision();Check(!(bool)Read("jump"),"fast overhead pass avoids futile late jump");UseAI=false;
    }
    IEnumerator RisingShot()
    {
        yield return Scene(true,false);Put("serveApproach",false);Put("waitingForServe",false);
        float headY=player.transform.Find("Head").position.y;
        ball.position=new Vector2(body.position.x-1.4f*scale,headY+.7f*scale);ball.velocity=new Vector2(5f*scale,2f);ball.gravityScale=1f;
        var probe=ball.gameObject.AddComponent<AIAirContactProbe>();probe.playerName="Zhao";
        UseAI=true;bool planned=false, acted=false;float error=0;
        for(int i=0;i<90;i++) { yield return new WaitForFixedUpdate();planned|=(bool)Read("highBallPlan");acted|=(bool)Read("jump")||(bool)Read("head");error=Mathf.Max(error,JointError(player)); }
        Check(planned && NativeJumps+NativeHeads>0,"rising incoming shot executes interception action jumps="+NativeJumps+" heads="+NativeHeads);
        Check(probe.touched,"rising incoming shot reaches native player collision");
        Check(error<.35f,"rising interception retains physical skeleton error="+error);UseAI=false;
    }
    IEnumerator ReturningContact(bool low)
    {
        yield return Scene(true,false);Move(player,new Vector2(0,body.position.y));
        float height=(float)Skill("standingHeight"),radius=ball.GetComponent<Collider2D>().bounds.extents.y;
        ball.position=new Vector2(body.position.x+.4f*scale,low?floor+radius+.01f:floor+height*.7f);
        ball.velocity=Vector2.left*2f;ball.gravityScale=1f;Physics2D.SyncTransforms();
        var probe=ball.gameObject.AddComponent<AIAirContactProbe>();probe.playerName="Zhao";
        Held.Add(KeyCode.RightArrow);GameAIMod.GetAxis("Horizontal2",player);Held.Clear();
        UseAI=true;float dangerous=0,error=0;bool contacted=false;
        for(int i=0;i<60;i++){
            yield return new WaitForFixedUpdate();contacted|=probe.touched; if(!low)Debug.Log("STRATEGY CONTACT step="+i+" ball="+ball.position+" vx="+ball.velocity.x+" body="+body.position+" rescue="+PlayerSkills.RescueActive(player)+" recovery="+((float)Skill("rescueRecoveryUntil")-Time.time)+" kick="+(bool)Read("kick"));
            if(contacted && ((float)Skill("retreatUntil")>=Time.time || (float)Skill("rescueRecoveryUntil")>=Time.time))dangerous=Mathf.Max(dangerous,ball.velocity.x);
            error=Mathf.Max(error,JointError(player));
        }
        Check(contacted,"returning "+(low?"foot":"torso")+" ball uses real collision fixture");
        Check(dangerous<.15f,"returning contact does not receive an own-goal push vx="+dangerous);
        Check(error<.35f,"returning-contact absorption retains rig error="+error);UseAI=false;
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; Application.targetFrameRate = 120; QualitySettings.vSyncCount = 0; AudioListener.volume = 0f;
        yield return new WaitForSeconds(.2f);
        yield return Recovery("low");
        yield return Recovery("low",.8f,3f);
        yield return Recovery("low",1.6f,3f);
        yield return Recovery("head");
        yield return Recovery("high");
        yield return HighJudgment();
        yield return RisingShot();
        yield return ReturningContact(true);
        yield return ReturningContact(false);
        yield return Lob(true,false);
        Debug.Log("STRATEGY COMPLETE checks=" + checks + " lobTouches=" + lobTouches + " failures=" + failures);
        Application.Quit(failures == 0 ? 0 : 1);
    }
    void Update() { if (Time.realtimeSinceStartup > 110f) { Debug.LogError("STRATEGY TIMEOUT"); Application.Quit(2); } }
}
