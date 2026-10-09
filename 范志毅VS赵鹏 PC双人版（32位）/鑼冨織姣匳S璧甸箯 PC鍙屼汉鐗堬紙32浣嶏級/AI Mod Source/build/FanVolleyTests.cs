using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public sealed class FanVolleyContactProbe : MonoBehaviour
{
    public PlayerSkills owner;
    public bool accepted;
    void OnCollisionEnter2D(Collision2D c) { Observe(c); }
    void OnCollisionStay2D(Collision2D c) { Observe(c); }
    void Observe(Collision2D c)
    {
        if (c.collider.GetComponentInParent<PlayerSkills>() != owner) return;
        foreach (ContactPoint2D p in c.contacts)
        {
            Vector2 n = p.normal;
            if (Vector2.Dot(n, GetComponent<Rigidbody2D>().position - p.point) < 0f) n = -n;
            accepted |= (bool)typeof(PlayerSkills).GetMethod("IsFootContact", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, new object[] { c.collider, p.point, n });
        }
    }
}

public sealed class FanVolleyTests : MonoBehaviour
{
    public static bool UseAI;
    public static int InputFrame = -1;
    public static string InputPlayer;
    public static KeyCode InputKey;
    public static float FanAxis, ZhaoAxis;
    static bool booted;
    const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
    Component fan, zhao;
    PlayerSkills fs, zs;
    Rigidbody2D fb, zb, ball;
    Animator fa;
    Type pt;
    int checks, failures;
    float groundLift, airLift, groundFall, airFall, groundRise, airRise;

    public static void Boot() { if (booted) return; booted = true; var obj = new GameObject("FanVolleyTests"); DontDestroyOnLoad(obj); obj.AddComponent<FanVolleyTests>(); }
    void Check(bool ok, string label) { checks++; if (!ok) failures++; Debug.Log((ok ? "FAN PASS " : "FAN FAIL ") + label); }
    object Field(PlayerSkills s, string name) { return typeof(PlayerSkills).GetField(name, P).GetValue(s); }
    object Call(PlayerSkills s, string name, params object[] args) { return typeof(PlayerSkills).GetMethod(name, P).Invoke(s, args); }
    float Fx(PlayerSkills s, int index) { return ((float[])Field(s, "effects"))[index]; }
    bool Grounded() { return (bool)Call(fs, "IsGroundedForVolley"); }
    void Move(Component p, Rigidbody2D b, Vector2 position) { p.transform.position += (Vector3)(position - b.position); foreach (var rb in p.GetComponentsInChildren<Rigidbody2D>()) rb.velocity = Vector2.zero; Physics2D.SyncTransforms(); }
    float JointError() { float max = 0; foreach (var j in fan.GetComponentsInChildren<HingeJoint2D>()) if (j.connectedBody != null) max = Mathf.Max(max, Vector2.Distance(j.transform.TransformPoint(j.anchor), j.connectedBody.transform.TransformPoint(j.connectedAnchor))); return max; }
    IEnumerator Scene(bool enabled = true)
    {
        UseAI = false; InputFrame = -1; FanAxis = ZhaoAxis = 0; PlayerSkills.Enabled = enabled;
        typeof(GameAIMod).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.25f);
        pt = Type.GetType("PlayerController, Assembly-CSharp");
        fan = GameObject.Find("Fan").GetComponent(pt); zhao = GameObject.Find("Zhao").GetComponent(pt);
        fs = fan.GetComponent<PlayerSkills>(); zs = zhao.GetComponent<PlayerSkills>();
        fb = (Rigidbody2D)pt.GetField("rb").GetValue(fan); zb = (Rigidbody2D)pt.GetField("rb").GetValue(zhao); fa = fan.GetComponent<Animator>();
        ball = (FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        ball.position = new Vector2(0, 1); ball.gravityScale = 0; ball.velocity = Vector2.zero;
    }
    IEnumerator Air() { Move(fan, fb, new Vector2(-3, 2.5f)); for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate(); Check(!Grounded(), "player is genuinely above the pitch"); }
    IEnumerator Tap(Component player) { InputPlayer = player.name; InputKey = player.name == "Fan" ? KeyCode.K : KeyCode.KeypadEnter; InputFrame = Time.frameCount + 1; yield return null; yield return null; }
    Vector2 Point(BoxCollider2D limb, float y, float x = .5f) { return limb.transform.TransformPoint(limb.offset + new Vector2(limb.size.x * x, limb.size.y * y)); }
    Vector2 Normal(BoxCollider2D limb, Vector2 local) { return limb.transform.TransformDirection(local); }
    void Touch(PlayerSkills s, float y = .35f, bool sole = false)
    {
        var limb = s.transform.Find("R_LowLeg").GetComponent<BoxCollider2D>();
        Call(s, s == fs ? "VolleyOnContact" : "BurstOnContact", limb, Point(limb, sole ? -.5f : y, sole ? 0 : .5f), Normal(limb, sole ? Vector2.down : Vector2.right));
    }
    IEnumerator Geometry()
    {
        yield return Scene();
        foreach (string name in new[] { "L_LowLeg", "R_LowLeg" })
        {
            var limb = fan.transform.Find(name).GetComponent<BoxCollider2D>();
            foreach (float y in new[] { -.3f, .15f, .35f }) Check((bool)Call(fs, "IsFootContact", limb, Point(limb, y), Normal(limb, Vector2.right)), name + " below-knee side accepted y=" + y);
            Check(!(bool)Call(fs, "IsFootContact", limb, Point(limb, -.5f, 0), Normal(limb, Vector2.down)), name + " sole rejected");
            Check(!(bool)Call(fs, "IsFootContact", limb, Point(limb, .5f), Normal(limb, Vector2.up)), name + " top/knee face rejected");
            var joint = limb.GetComponent<HingeJoint2D>();
            if (joint != null) Check(!(bool)Call(fs, "IsFootContact", limb, (Vector2)limb.transform.TransformPoint(joint.anchor), Normal(limb, Vector2.right)), name + " actual knee anchor rejected");
            var zl = zhao.transform.Find(name).GetComponent<BoxCollider2D>();
            Check(!(bool)Call(zs, "IsFootContact", zl, Point(zl, .35f), Normal(zl, Vector2.right)), name + " Zhao upper shin remains excluded");
            Check((bool)Call(zs, "IsFootContact", zl, Point(zl, -.3f), Normal(zl, Vector2.right)), name + " Zhao lower shin remains accepted");
        }
        var upper = fan.transform.Find("R_Up_Leg").GetComponent<Collider2D>();
        Check(!(bool)Call(fs, "IsFootContact", upper, (Vector2)upper.bounds.center, Vector2.right), "upper leg still rejected");
    }
    IEnumerator Commands()
    {
        yield return Scene(); Check(Grounded(), "ground single starts with actual player pitch contact"); Touch(fs); Check(Fx(fs, 5) < 0, "no shot command cannot trigger");
        yield return Tap(fan); Check((float)Field(fs, "kickUntil") > Time.time && !(bool)Field(fs, "airborneVolleyArmed"), "ground native single K arms ordinary curve");
        Touch(fs, -.5f, true); Check(Fx(fs, 5) < 0, "ground shot does not accept sole");
        Touch(fs); Check(Fx(fs, 5) >= 0 && (float)Field(fs, "curveStrength") == 1f, "ground single accepts upper shin and uses base curve");
        float fx = Fx(fs, 5); Touch(fs); Check(Fx(fs, 5) == fx && (float)Field(fs, "kickUntil") < 0, "one success consumes ground single");
        yield return Scene(); yield return Air(); yield return Tap(fan); Touch(fs); Check(Fx(fs, 5) < 0 && (float)Field(fs, "kickUntil") < 0, "single K in air cannot activate");
        yield return new WaitForSeconds(.1f); yield return Tap(fan); Touch(fs, -.5f, true); Check(Fx(fs, 5) < 0, "air double still excludes sole"); Touch(fs); Check(Fx(fs, 5) >= 0 && Mathf.Abs((float)Field(fs, "curveStrength") - 1.25f) < .001f, "air double K activates stronger upper-shin curve");
        yield return Scene(); yield return Tap(fan); yield return Air(); Touch(fs); Check(Fx(fs, 5) < 0, "ground single cannot be carried into an aerial contact"); yield return Tap(fan); Touch(fs); Check(Fx(fs, 5) < 0, "one ground tap and one air tap cannot form air double"); yield return Tap(fan); Touch(fs); Check(Fx(fs, 5) >= 0, "two distinct aerial taps complete air pair");
        yield return Scene(); yield return Air(); yield return Tap(fan); yield return new WaitForSeconds(.4f); yield return Tap(fan); Touch(fs); Check(Fx(fs, 5) < 0, "slow aerial taps rejected");
        yield return Scene(); yield return Air(); Call(fs, "BeginAction", "Kick"); Call(fs, "BeginAction", "Kick"); Touch(fs); Check(Fx(fs, 5) < 0, "same-frame reads are not double taps");
        yield return Scene(); yield return Tap(fan); yield return new WaitForSeconds(.8f); Touch(fs); Check(Fx(fs, 5) < 0, "ground contact window expires");
        yield return Scene(); yield return Air(); yield return Tap(fan); PlayerSkills.ResetAll(); yield return Tap(fan); Touch(fs); Check(Fx(fs, 5) < 0, "reset clears air first tap");
        yield return Scene(); yield return Tap(fan); Time.timeScale = 0; yield return null; yield return null; Time.timeScale = 1; Touch(fs); Check(Fx(fs, 5) < 0, "pause clears ground single window");
        yield return Scene(); ball.position = new Vector2(fb.position.x - 2, 1); yield return Tap(fan); Touch(fs); Check(Fx(fs, 5) < 0, "rear ball cannot activate ground single");
        yield return Scene(); yield return Air(); yield return Tap(fan); yield return Tap(fan); Move(fan, fb, new Vector2(-3, -.15f)); for (int i = 0; i < 6; i++) yield return new WaitForFixedUpdate(); Check(Grounded(), "air pair landing fixture touches ground"); ball.position = new Vector2(0, 1); Touch(fs); Check(Fx(fs, 5) >= 0 && (float)Field(fs, "curveStrength") == 1f, "landing before contact downgrades to normal curve");
    }
    IEnumerator Live(bool airPlayer, bool groundBall, float localY)
    {
        yield return Scene(); if (airPlayer) yield return Air();
        yield return Tap(fan); if (airPlayer) { yield return new WaitForSeconds(.1f); yield return Tap(fan); yield return new WaitForSeconds(.15f); }
        var probe = ball.gameObject.AddComponent<FanVolleyContactProbe>(); probe.owner = fs;
        if (groundBall) { ball.position = new Vector2(fb.position.x + .65f, -1.55f); ball.gravityScale = 1; ball.velocity = Vector2.zero; }
        else
        {
            float radius = ball.GetComponent<CircleCollider2D>().radius * Mathf.Abs(ball.transform.lossyScale.x), best = float.NegativeInfinity; Vector2 pos = Vector2.zero, cp = Vector2.zero, cn = Vector2.zero; BoxCollider2D chosen = null;
            foreach (string name in new[] { "L_LowLeg", "R_LowLeg" }) { var limb = fan.transform.Find(name).GetComponent<BoxCollider2D>(); foreach (float side in new[] { -1f, 1f }) { Vector2 p = Point(limb, localY, side * .5f), n = Normal(limb, Vector2.right * side); Vector2 candidate = p + n * radius * .85f; if (candidate.x > best) { best = candidate.x; chosen = limb; pos = candidate; cp = p; cn = n; } } }
            ball.position = pos; ball.velocity = chosen.attachedRigidbody.GetPointVelocity(cp) - cn * 2;
        }
        Physics2D.SyncTransforms();
        for (int i = 0; i < 12 && Fx(fs, 5) < 0; i++) yield return new WaitForFixedUpdate();
        Check(probe.accepted, "real below-knee contact airPlayer=" + airPlayer + " groundBall=" + groundBall + " y=" + localY);
        Check(Fx(fs, 5) >= 0, "native input and physical contact activate skill air=" + airPlayer + " groundBall=" + groundBall);
        Check((float)Field(fs, "curveStrength") == (airPlayer ? 1.25f : 1f), "real contact selects correct strength tier");
        Check(ball.velocity.y > (airPlayer ? 11f : 8f), "real strike lifts the ball");
        for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
        Check((float)Field(fs, "curveStarted") >= 0, "curve remains active after ground separation");
        Check(JointError() < .3f && fan.GetComponentsInChildren<Joint2D>().Length == 9, "native connected model retained");
    }
    IEnumerator Flight(bool air)
    {
        yield return Scene(); if (air) yield return Air(); yield return Tap(fan); if (air) yield return Tap(fan);
        ball.position = new Vector2(0, 1); ball.velocity = Vector2.zero; ball.gravityScale = 1; Touch(fs);
        Check(Fx(fs, 5) >= 0, "controlled flight starts air=" + air);
        float lift = ball.velocity.y, start = Time.time, rise = 0, fallA = 0, fallB = 0;
        bool gotRise = false, gotA = false, gotB = false;
        for (int i = 0; i < 36; i++) { yield return new WaitForFixedUpdate(); float age = Time.time - start; if (!gotRise && age >= .18f) { rise = ball.velocity.y; gotRise = true; } if (!gotA && age >= .42f) { fallA = ball.velocity.y; gotA = true; } if (!gotB && age >= .62f) { fallB = ball.velocity.y; gotB = true; } }
        Check(gotRise && gotA && gotB && fallB < fallA, "flight has a clear descent phase air=" + air);
        if (air) { airLift = lift; airRise = rise; airFall = fallA - fallB; } else { groundLift = lift; groundRise = rise; groundFall = fallA - fallB; }
        Debug.Log("FAN FLIGHT air=" + air + " lift=" + lift + " risingVy=" + rise + " fallDrop=" + (fallA - fallB));
    }
    IEnumerator Preserved()
    {
        yield return Scene(); yield return Tap(zhao); Touch(zs, -.3f); Check(Fx(zs, 4) < 0, "Zhao still needs double tap"); yield return Tap(zhao); Touch(zs, .35f); Check(Fx(zs, 4) < 0, "Zhao still rejects upper shin after double"); Touch(zs, -.3f); Check(Fx(zs, 4) >= 0 && ball.velocity.y <= .5f, "Zhao lower-shin burst remains low");
        Check(Mathf.Abs(PlayerSkills.GetJumpForce(100, zhao) - 117.6903f) < .01f, "latest Fortress height reduction preserved");
        yield return Scene(false); yield return Tap(fan); Check(fs == null && zs == null, "classic mode has no skill components"); Check(PlayerSkills.GetJumpForce(100, zhao) == 100, "classic jump unchanged");
        yield return Scene(); yield return Air(); ball.position = new Vector2(fb.position.x + .7f, fb.position.y - .7f);
        typeof(GameAIMod).GetField("mode", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 2); UseAI = true;
        bool armed = false; for (int i = 0; i < 20; i++) { yield return null; armed |= (bool)Field(fs, "airborneVolleyArmed"); } UseAI = false;
        Check(armed, "Fan AI uses airborne double commands"); PlayerSkills.ResetAll(); Check((float)Field(fs, "curveStrength") == 1f && !(bool)Field(fs, "airborneVolleyArmed"), "reset clears flight strength and armed tier");
    }
    IEnumerator Start()
    {
        Application.targetFrameRate = 60; Application.runInBackground = true; QualitySettings.vSyncCount = 0; AudioListener.volume = 0;
        yield return new WaitForSeconds(.2f); yield return Geometry(); yield return Commands();
        yield return Live(false, true, -.3f); yield return Live(false, false, .35f); yield return Live(true, false, -.3f);
        yield return Flight(false); yield return Flight(true);
        Check(Mathf.Abs(airLift / groundLift - 1.25f) < .01f, "air initial rise is 25 percent stronger"); Check(airRise > groundRise + 2, "air rising phase retains substantially more lift"); Check(airFall > groundFall * 1.15f, "air descent accelerates more strongly");
        yield return Preserved(); Debug.Log("FAN VOLLEY COMPLETE checks=" + checks + " failures=" + failures); Application.Quit();
    }
}
