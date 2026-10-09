using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Isolated runtime only: native commands, real collisions and intact joints.
public sealed class RescueBalanceTests : MonoBehaviour
{
    public static HashSet<KeyCode> Held = new HashSet<KeyCode>();
    public static int DownFrame = -1; public static KeyCode DownKey;
    public static bool ReadKey(KeyCode key) { return Held.Contains(key); }
    public static bool ReadDown(KeyCode key) { return DownFrame == Time.frameCount && DownKey == key; }
    static bool booted; static int reduced, assisted, rescued, touches;
    static bool ratios;
    Component zhao, fan, manager; Rigidbody2D body, ball; PlayerSkills skill;
    Collider2D ownGoal; int checks, failures;
    const BindingFlags IP = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags SP = BindingFlags.Static | BindingFlags.NonPublic;
    public static void HeaderScaled(Component player, Vector2 before, Vector2 after)
    { reduced++; ratios &= Vector2.Distance(after, before * .9f) < .002f; Debug.Log("BALANCE HEADER " + player.name + " before=" + before + " after=" + after); }
    public static void AssistedHeader(Vector2 target, Vector2 after)
    { assisted++; ratios &= Vector2.Distance(after, target) < .002f && Mathf.Abs(PlayerSkills.FanHeaderSpeedMultiplier - .855f) < .0001f; }
    public static void RescueStrike(Vector2 velocity) { rescued++; Debug.Log("BALANCE CLEARANCE " + velocity); }
    public static void Contact(Collision2D c)
    { if (c.collider != null && c.collider.name == "Head") touches++; }
    public static void Boot()
    {
        if (booted) return; booted = true;
        ControlBindings.settingsPath = System.IO.Path.Combine(Application.dataPath, "..", "rescue-test-bindings.ini");
        var host = new GameObject("RescueBalanceTests"); DontDestroyOnLoad(host); host.AddComponent<RescueBalanceTests>();
    }
    void Check(bool ok, string label) { checks++; if (!ok) failures++; Debug.Log((ok ? "BALANCE PASS " : "BALANCE FAIL ") + label); }
    object Field(string name) { return typeof(PlayerSkills).GetField(name, IP).GetValue(skill); }
    float Floor() { return (float)typeof(PlayerSkills).GetMethod("RescueFloor", IP).Invoke(skill, null); }
    void Move(Component player, Vector2 position)
    {
        Rigidbody2D rb = (Rigidbody2D)player.GetType().GetField("rb").GetValue(player);
        player.transform.position += (Vector3)(position - rb.position);
        foreach (Rigidbody2D limb in player.GetComponentsInChildren<Rigidbody2D>()) { limb.velocity = Vector2.zero; limb.angularVelocity = 0; }
        Physics2D.SyncTransforms();
    }
    float Joints(Component player)
    {
        float max = 0;
        foreach (HingeJoint2D j in player.GetComponentsInChildren<HingeJoint2D>()) if (j.connectedBody != null)
            max = Mathf.Max(max, Vector2.Distance(j.transform.TransformPoint(j.anchor), j.connectedBody.transform.TransformPoint(j.connectedAnchor)));
        return max;
    }
    IEnumerator Scene(bool skills)
    {
        Held.Clear(); DownFrame = -1; Time.timeScale = 1; PlayerSkills.Enabled = skills; ControlBindings.ResetDefaults();
        typeof(GameAIMod).GetMethod("StartGame", SP).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.2f);
        Type pt = Type.GetType("PlayerController, Assembly-CSharp");
        zhao = GameObject.Find("Zhao").GetComponent(pt); fan = GameObject.Find("Fan").GetComponent(pt);
        body = (Rigidbody2D)pt.GetField("rb").GetValue(zhao); skill = zhao.GetComponent<PlayerSkills>();
        ball = (FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        manager = FindObjectOfType(Type.GetType("GameManager, Assembly-CSharp")) as Component;
        foreach (UnityEngine.Object o in FindObjectsOfType(Type.GetType("GoalTrigger, Assembly-CSharp")))
        { Component c = o as Component; if (c.transform.position.x > 0) ownGoal = c.GetComponent<Collider2D>(); }
        ball.position = new Vector2(0, 10); ball.velocity = Vector2.zero; ball.gravityScale = 0;
        Move(zhao, new Vector2(-2, body.position.y)); Move(fan, new Vector2(-7, body.position.y));
        yield return new WaitForSeconds(.25f);
        reduced = assisted = rescued = touches = 0; ratios = true;
    }
    void Place(float gap, float y, Vector2 velocity, float gravity)
    { ball.position = new Vector2(body.position.x + gap, y); ball.velocity = velocity; ball.angularVelocity = 0; ball.gravityScale = gravity; Physics2D.SyncTransforms(); }
    IEnumerator Headers(bool skills, bool isFan, bool rear, bool command)
    {
        yield return Scene(skills);
        Component p = isFan ? fan : zhao; Move(p, new Vector2(0, body.position.y)); Move(isFan ? zhao : fan, new Vector2(isFan ? 4 : -4, body.position.y)); Transform head = p.transform.Find("Head");
        Collider2D h = head.GetComponent<Collider2D>(), b = ball.GetComponent<Collider2D>();
        float dir = isFan ? 1 : -1; if (rear) dir = -dir;
        ball.position = (Vector2)h.bounds.center + Vector2.right * dir * (h.bounds.extents.x + b.bounds.extents.x + .02f);
        ball.velocity = command && !rear ? Vector2.zero : Vector2.left * dir * 2; Physics2D.SyncTransforms();
        if (command) { DownKey = isFan ? KeyCode.J : KeyCode.RightShift; DownFrame = Time.frameCount + 1; }
        float error = 0;
        for (int i = 0; i < 35; i++) { yield return null; error = Mathf.Max(error, Joints(p)); }
        string label = "header skills=" + skills + " fan=" + isFan + " rear=" + rear + " command=" + command;
        bool special = skills && isFan && !rear;
        if (command)
        {
            Check(special ? assisted == 1 && reduced == 0 : reduced == 1 && assisted == 0, label + " one reduction only reduced=" + reduced + " assisted=" + assisted);
            Check(ratios, label + " velocity is exactly 90% of prior header");
            Check(touches > 0 || special, label + " real head contact or reachable native assisted strike");
        }
        else { Check(reduced == 0 && assisted == 0, label + " passive head collision is unchanged"); Check(touches > 0, label + " passive physical fixture touched head"); }
        Check(error < .35f, label + " skeleton stable error=" + error);
        DownFrame = -1;
    }
    IEnumerator OngoingHeader(bool isFan)
    {
        yield return Scene(false);
        Component p = isFan ? fan : zhao;
        Move(p, new Vector2(0, body.position.y)); Move(isFan ? zhao : fan, new Vector2(isFan ? 4 : -4, body.position.y));
        Collider2D head = p.transform.Find("Head").GetComponent<Collider2D>();
        ball.position = (Vector2)head.bounds.center + Vector2.up * (head.bounds.extents.y + ball.GetComponent<Collider2D>().bounds.extents.y - .015f);
        ball.velocity = Vector2.zero; ball.gravityScale = 1; Physics2D.SyncTransforms();
        bool contact = false;
        for (int i=0; i<60; i++) { yield return new WaitForFixedUpdate(); if (head.IsTouching(ball.GetComponent<Collider2D>())) { contact = true; break; } }
        Check(contact, "existing real head contact before command fan=" + isFan);
        PlayerSkills.SetAction(p.GetComponent<Animator>(), "Head", p);
        yield return new WaitForSeconds(.6f);
        Check(reduced == 1 && assisted == 0, "ongoing head contact reduced once on release fan=" + isFan + " reduced=" + reduced);
        Check(ratios, "ongoing head release velocity is exactly 90% fan=" + isFan);
        Check(Joints(p) < .35f, "ongoing head contact keeps native skeleton fan=" + isFan);
    }
    IEnumerator Guards()
    {
        yield return Scene(false); Place(2, body.position.y, Vector2.zero, 0);
        Held.Add(KeyCode.RightArrow); Check(Mathf.Abs(PlayerMovement.GetMultiplier(zhao) - 1.5f) < .001f, "classic manual backward speed retained");
        yield return Scene(true);
        float height = (float)Field("standingHeight"), floor = Floor();
        Place(-2, floor + height + 2, Vector2.zero, 0); Held.Add(KeyCode.RightArrow);
        Check(GameAIMod.GetAxis("Horizontal2", zhao) == 1 && (float)Field("retreatUntil") < 0, "front ball keeps original manual retreat");
        Check(Mathf.Abs(PlayerMovement.GetMultiplier(zhao) - 1.5f) < .001f, "original skill retreat speed retained");
        Held.Clear(); GameAIMod.GetAxis("Horizontal2", zhao);
        Place(2, floor + height + 2, Vector2.zero, 0);
        Check(GameAIMod.GetAxis("Horizontal2", zhao) == 0 && (float)Field("retreatUntil") < 0, "rear ball alone cannot auto activate");
        Held.Add(KeyCode.RightArrow); GameAIMod.GetAxis("Horizontal2", zhao);
        Check(PlayerMovement.GetMultiplier(zhao) > 0f && PlayerMovement.GetMultiplier(zhao) <= 2f, "auto retreat never exceeds twice ordinary native speed");
        Check(PlayerMovement.GetMultiplier(zhao) < 2f, "stationary reachable ball uses less than maximum speed");
        ball.velocity = Vector2.right * 8f; GameAIMod.GetAxis("Horizontal2", zhao);
        Check(Mathf.Abs(PlayerMovement.GetMultiplier(zhao) - 2f) < .001f, "fast rear ball reaches the two-times cap");
        ball.velocity = Vector2.zero;
        Held.Clear(); Check(GameAIMod.GetAxis("Horizontal2", zhao) == 1, "tap continues bounded automatic retreat");
        Held.Add(KeyCode.LeftArrow); Check(GameAIMod.GetAxis("Horizontal2", zhao) == -1 && (float)Field("retreatUntil") < 0, "opposite steering cancels rescue");
        Held.Clear(); GameAIMod.GetAxis("Horizontal2", zhao); Held.Add(KeyCode.RightArrow); GameAIMod.GetAxis("Horizontal2", zhao);
        PlayerSkills.SetAction(zhao.GetComponent<Animator>(), "Kick", zhao);
        Check((float)Field("retreatUntil") < 0, "manual kick cancels automatic retreat");
        Held.Clear(); GameAIMod.GetAxis("Horizontal2", zhao); Held.Add(KeyCode.RightArrow); GameAIMod.GetAxis("Horizontal2", zhao);
        Time.timeScale = 0; GameAIMod.GetAxis("Horizontal2", zhao);
        Check((float)Field("retreatUntil") < 0 && !PlayerSkills.RescueButton(zhao, true), "pause clears auto commands"); Time.timeScale = 1;
        Held.Clear(); GameAIMod.GetAxis("Horizontal2", zhao); Held.Add(KeyCode.RightArrow); GameAIMod.GetAxis("Horizontal2", zhao);
        PlayerSkills.ResetAll(); Check((float)Field("retreatUntil") < 0 && (float)Field("rescueRecoveryUntil") < 0, "round reset clears rescue and balance");
        Held.Clear(); GameAIMod.GetAxis("Horizontal2", zhao); Held.Add(KeyCode.RightArrow); GameAIMod.GetAxis("Horizontal2", zhao); Held.Clear();
        yield return new WaitForSeconds(2.8f); Check((float)Field("retreatUntil") < Time.time, "unreachable rescue has a time limit");
        Held.Clear(); PlayerSkills.ResetAll(); Move(zhao, new Vector2(ownGoal.bounds.min.x - .3f, body.position.y));
        Place(.2f, floor + height, Vector2.zero, 0);
        Held.Add(KeyCode.RightArrow);
        Check(GameAIMod.GetAxis("Horizontal2", zhao) == 0 && (float)Field("retreatUntil") > Time.time, "tight own-goal space brakes instead of running through rear ball");
    }
    IEnumerator Retreat(string kind, float gap, bool nearGoal, bool falling, float rolling = 0f)
    {
        yield return Scene(true);
        float floor = Floor(), height = (float)Field("standingHeight"), headHeight = (float)Field("standingHeadHeight");
        if (nearGoal) Move(zhao, new Vector2(ownGoal.bounds.min.x - gap - 1.5f, body.position.y));
        float radius = ball.GetComponent<Collider2D>().bounds.extents.y;
        float y = kind == "low" ? floor + radius + .04f : kind == "head" ? floor + headHeight : floor + height + 1.8f;
        Place(gap, y, falling ? new Vector2(0, -2) : Vector2.right * rolling, kind == "low" || falling ? 1 : 0);
        int initialScore = (int)manager.GetType().GetField("p1Score", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(manager);
        float originalX = ball.position.x, startX = body.position.x, startY = body.position.y;
        Held.Add(KeyCode.RightArrow);
        float tilt = 0, error = 0, peakY = startY, peakSpeed = 0, arch = 0; bool renderedArch = false; bool goal = false, jumped = false, headed = false, passed = false, cleared = false;
        for (int i = 0; i < 215; i++)
        {
            yield return new WaitForFixedUpdate();
            if (i == 4) Held.Clear();
            arch = Mathf.Min(arch, Mathf.DeltaAngle(0, body.rotation));
            if (kind == "head" && !falling && arch < -10f && !renderedArch) { RenderBackward(nearGoal ? "near" : "mid"); renderedArch = true; }
            peakY = Mathf.Max(peakY, body.position.y); peakSpeed = Mathf.Max(peakSpeed, body.velocity.x);
            error = Mathf.Max(error, Joints(zhao));
            jumped |= (bool)Field("retreatJumped"); headed |= (bool)Field("retreatHeaded");
            passed |= body.position.x > ball.position.x + radius;
            goal |= (int)manager.GetType().GetField("p1Score", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(manager) > initialScore;
            cleared |= GameAIMod.BallOutThisRally || (rescued > 0 && ball.position.x < originalX - .5f) || (ball.position.x > ownGoal.bounds.min.x && ball.GetComponent<Collider2D>().bounds.min.y > BallBoundaryGuard.FindGoalCeiling(ownGoal));
            if (i > 185) tilt = Mathf.Max(tilt, Mathf.Abs(Mathf.DeltaAngle(body.rotation, 0)));
            if (i % 20 == 0) Debug.Log("BALANCE RETREAT " + kind + " frame=" + i + " body=" + body.position + " ball=" + ball.position + " velocity=" + body.velocity + " tilt=" + body.rotation + " headed=" + Field("retreatHeaded") + " waiting=" + Field("rescueWaiting") + " height=" + height + " width=" + Field("standingBodyWidth") + " head=" + zhao.transform.Find("Head").position + " deadline=" + ((float)Field("retreatUntil") - Time.time));
        }
        string label = kind + " gap=" + gap + " nearGoal=" + nearGoal + " falling=" + falling;
        Check(!goal, label + " no own goal during rescue");
        Check(error < .35f, label + " all joints stay connected error=" + error);
        Check(zhao.GetComponentsInChildren<Joint2D>().Length == 9, label + " native skeleton retained");
        Check(tilt < 35, label + " balanced after recovery tilt=" + tilt);
        if (kind == "low")
        { Check(jumped && peakY > startY + .6f, label + " native jump clears low ball"); Check(passed, label + " reaches goal side of ball"); }
        else if (kind == "head")
        { if (!falling) Check(arch < -10f, label + " real backward waist arch degrees=" + arch); Check((headed && rescued == 1) || (falling && jumped && passed), label + " native header and real safe-clearance collision count=" + rescued); Check(cleared || (falling && jumped && passed), label + " heads ball out or safely away"); }
        else
        { if (!falling) Check(!headed, label + " above-height ball does not request header"); if (!falling) { Check(!jumped, label + " high ball keeps ground running"); Check(passed && peakSpeed > 2, label + " fast ground run reaches goal side peak=" + peakSpeed); } }
        Check((float)Field("retreatUntil") < Time.time, label + " bounded auto action ended");
        Held.Clear();
    }
    void RenderBackward(string name)
    {
        Camera c=Camera.main;if(c==null)return;RenderTexture rt=new RenderTexture(1280,720,24),old=c.targetTexture,active=RenderTexture.active;c.targetTexture=rt;c.Render();RenderTexture.active=rt;
        Texture2D t=new Texture2D(1280,720,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,1280,720),0,0);t.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath,"..","rescue-backward-"+name+".png"),t.EncodeToPNG());c.targetTexture=old;RenderTexture.active=active;Destroy(t);Destroy(rt);
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; Application.targetFrameRate = 120; QualitySettings.vSyncCount = 0; AudioListener.volume = 0;
        yield return new WaitForSeconds(.2f);
        yield return Headers(false, false, false, true); yield return Headers(true, false, false, true);
        yield return Headers(false, true, false, true); yield return Headers(true, true, false, true);
        yield return Headers(true, true, true, true); yield return Headers(false, false, false, false);
        yield return OngoingHeader(false); yield return OngoingHeader(true);
        yield return Guards();
        yield return Retreat("low", 1.8f, false, false); yield return Retreat("low", 1.1f, true, false);
        yield return Retreat("head", 1.8f, false, false); yield return Retreat("head", 1.1f, true, false);
        yield return Retreat("low", 1.8f, false, false, 2f);
        yield return Retreat("head", 1.8f, false, true);
        yield return Retreat("high", 2f, false, false); yield return Retreat("high", 2f, true, true);
        Debug.Log("BALANCE COMPLETE checks=" + checks + " failures=" + failures); Application.Quit(failures == 0 ? 0 : 1);
    }
    void Update() { if (Time.realtimeSinceStartup > 120) { Debug.LogError("BALANCE TIMEOUT"); Application.Quit(2); } }
}
