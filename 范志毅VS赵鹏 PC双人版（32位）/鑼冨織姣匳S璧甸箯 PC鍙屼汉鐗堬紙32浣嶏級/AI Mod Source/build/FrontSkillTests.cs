using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public sealed class FrontContactProbe : MonoBehaviour
{
    public PlayerSkills player;
    public bool sawAccepted;
    private void OnCollisionEnter2D(Collision2D c) { Observe(c); }
    private void OnCollisionStay2D(Collision2D c) { Observe(c); }
    private void Observe(Collision2D c)
    {
        if (c.collider.GetComponentInParent<PlayerSkills>() != player) return;
        for (int i = 0; i < c.contactCount; i++)
        {
            ContactPoint2D p = c.GetContact(i); Vector2 n = p.normal;
            if (Vector2.Dot(n, GetComponent<Rigidbody2D>().position - p.point) < 0f) n = -n;
            bool accepted = (bool)typeof(PlayerSkills).GetMethod("IsFootContact", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, new object[] { c.collider, p.point, n });
            sawAccepted |= accepted;
            Debug.Log("FRONT CONTACT " + player.name + " limb=" + c.collider.name + " accepted=" + accepted);
        }
    }
}

public sealed class SkillTests : MonoBehaviour
{
    public static bool TestAxes = true, JumpPulse;
    public static float TestAxis;
    private static bool started;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private int failures;
    private Component fan, zhao;
    private PlayerSkills fs, zs;
    private Animator fa, za;
    private Rigidbody2D fb, zb, ball;
    private float oldPeak, newPeak, oldFall, newFall;
    private object Field(PlayerSkills s, string n) { return typeof(PlayerSkills).GetField(n, Flags).GetValue(s); }
    private void Set(PlayerSkills s, string n, object value) { typeof(PlayerSkills).GetField(n, Flags).SetValue(s, value); }
    private object Call(PlayerSkills s, string n, params object[] args) { return typeof(PlayerSkills).GetMethod(n, Flags).Invoke(s, args); }
    private void Check(bool pass, string label) { Debug.Log((pass ? "FRONT PASS " : "FRONT FAIL ") + label); if (!pass) failures++; }
    public static void Boot()
    {
        if (started) return;
        started = true;
        GameObject obj = new GameObject("FrontAndFlightVerification"); UnityEngine.Object.DontDestroyOnLoad(obj); obj.AddComponent<SkillTests>();
    }
    private IEnumerator Scene()
    {
        PlayerSkills.Enabled = true; TestAxis = 0f; JumpPulse = false;
        typeof(GameAIMod).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.3f);
        Type pc = Type.GetType("PlayerController, Assembly-CSharp");
        fan = GameObject.Find("Fan").GetComponent(pc); zhao = GameObject.Find("Zhao").GetComponent(pc);
        fs = fan.GetComponent<PlayerSkills>(); zs = zhao.GetComponent<PlayerSkills>();
        fb = pc.GetField("rb").GetValue(fan) as Rigidbody2D; zb = pc.GetField("rb").GetValue(zhao) as Rigidbody2D;
        fa = pc.GetField("anim").GetValue(fan) as Animator; za = pc.GetField("anim").GetValue(zhao) as Animator;
        ball = (UnityEngine.Object.FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        ball.position = new Vector2(0f, 3.27f); ball.velocity = Vector2.zero;
    }
    private Vector2 Point(BoxCollider2D limb, float y) { return limb.transform.TransformPoint(limb.offset + new Vector2(limb.size.x * .5f, limb.size.y * y)); }
    private void Touch(PlayerSkills s, Component pc, string method)
    {
        BoxCollider2D limb = pc.transform.Find("R_LowLeg").GetComponent<BoxCollider2D>();
        Call(s, method, limb, Point(limb, -.3f), (Vector2)limb.transform.TransformDirection(Vector2.right));
    }
    private IEnumerator RearTests(bool isFan)
    {
        yield return Scene();
        PlayerSkills s = isFan ? fs : zs; Component pc = isFan ? fan : zhao;
        Rigidbody2D body = isFan ? fb : zb; Animator anim = isFan ? fa : za;
        float direction = isFan ? 1f : -1f;
        ball.position = new Vector2(body.position.x - direction * .15f, -1.2f); ball.velocity = Vector2.zero;
        Check(!(bool)Call(s, "BallInFront"), pc.name + " rear ball identified");
        PlayerSkills.SetAction(anim, "Kick", pc);
        Check((float)Field(s, "kickUntil") < 0f, pc.name + " rear shot does not arm skill");
        Touch(s, pc, isFan ? "VolleyOnContact" : "BurstOnContact");
        Check(((float[])Field(s, "effects"))[isFan ? 5 : 4] < 0f, pc.name + " rear real-contact pathway rejects special shot");
        Set(s, "kickUntil", Time.time + 1f);
        Touch(s, pc, isFan ? "VolleyOnContact" : "BurstOnContact");
        Check(((float[])Field(s, "effects"))[isFan ? 5 : 4] < 0f, pc.name + " armed window still cannot hit rear ball");
        Call(s, "FixedUpdate");
        Check((float)Field(s, "kickUntil") < 0f, pc.name + " rear ball clears old command window");
        if (isFan)
        {
            PlayerSkills.SetAction(anim, "Head", pc);
            Check((float)Field(s, "headUntil") < 0f && !(bool)Call(s, "HeadReachable"), "rear header is not armed or reachable");
            Set(s, "headUntil", Time.time + 1f); Call(s, "FixedUpdate");
            Check(ball.velocity == Vector2.zero, "rear header supplies no special impulse");
            Call(s, "Dribble"); Check(!(bool)Field(s, "fasterDribble"), "rear dribble stays at ordinary cadence");
            PlayerSkills.SetAction(anim, "Jump", pc);
            Check(!(bool)Field(s, "jumpVolleyActive"), "rear W jump does not arm falling shot");
        }
        else
        {
            Call(s, "AutoIntercept", Time.time);
            Check((float)Field(s, "extendedKickUntil") < 0f, "rear ball cannot start extended interception");
            Set(s, "extendedKickUntil", Time.time + 1f); Call(s, "AdjustKickPose");
            Check(!(bool)Field(s, "strongerLegControl") && !(bool)Field(s, "adjustedLegPose"), "rear ball cannot extend leg pose or force");
            Check(!(bool)Call(s, "CanBurst"), "rear burst eligibility rejected before distance check");
            PlayerSkills.SetAction(anim, "Jump", pc);
            Check(((float[])Field(s, "effects"))[2] >= 0f, "Aerial Fortress animation triggers with rear ball");
            Check(Mathf.Abs(PlayerSkills.GetJumpForce(100f, pc) - 135f) < .01f, "Aerial Fortress boosts jump with rear ball");
        }
        foreach (int index in isFan ? new[] { 0, 1, 5 } : new[] { 3, 4 })
        {
            Call(s, "Show", index);
            Check(((float[])Field(s, "effects"))[index] < 0f, pc.name + " rear skill animation excluded " + index);
        }
        ball.position = new Vector2(body.position.x, 2f);
        Check((bool)Call(s, "BallInFront"), pc.name + " directly overhead remains eligible");
        ball.position = new Vector2(body.position.x + direction * .001f, -1.2f);
        Check((bool)Call(s, "BallInFront"), pc.name + " front boundary eligible");
        ball.position = new Vector2(body.position.x - direction * .001f, -1.2f);
        Check(!(bool)Call(s, "BallInFront"), pc.name + " slight rear offset excluded");
        Check(pc.GetComponentsInChildren<Joint2D>().Length == 9, pc.name + " all native joints preserved");
    }
    private IEnumerator LiveFoot(bool isFan, bool front)
    {
        yield return Scene();
        Component pc = isFan ? fan : zhao; PlayerSkills s = isFan ? fs : zs;
        Rigidbody2D body = isFan ? fb : zb; Animator anim = isFan ? fa : za;
        float facing = isFan ? 1f : -1f;
        pc.GetType().GetField("doJump").SetValue(pc, true);
        PlayerSkills.SetAction(anim, "Jump", pc); PlayerSkills.SetAction(anim, "Kick", pc);
        yield return new WaitForSeconds(.42f);
        float radius = ball.GetComponent<CircleCollider2D>().radius * Mathf.Abs(ball.transform.lossyScale.x);
        Vector2 selectedPoint = Vector2.zero, selectedNormal = Vector2.zero, selectedPosition = Vector2.zero;
        BoxCollider2D selected = null; float best = float.NegativeInfinity;
        foreach (string name in new[] { "L_LowLeg", "R_LowLeg" })
        {
            BoxCollider2D limb = pc.transform.Find(name).GetComponent<BoxCollider2D>();
            foreach (float side in new[] { -1f, 1f })
            {
                Vector2 point = limb.transform.TransformPoint(limb.offset + new Vector2(side * limb.size.x * .5f, -limb.size.y * .3f));
                Vector2 normal = limb.transform.TransformDirection(Vector2.right * side);
                Vector2 position = point + normal * (radius * .85f);
                float score = facing * (position.x - body.position.x) * (front ? 1f : -1f);
                if (score > best) { best = score; selected = limb; selectedPoint = point; selectedNormal = normal; selectedPosition = position; }
            }
        }
        FrontContactProbe probe = ball.gameObject.AddComponent<FrontContactProbe>(); probe.player = s;
        ball.position = selectedPosition; ball.velocity = selected.attachedRigidbody.GetPointVelocity(selectedPoint) - selectedNormal * 2f;
        Debug.Log("FRONT PLACEMENT " + pc.name + " " + front + " limb=" + selected.name + " ball=" + selectedPosition + " body=" + body.position + " normal=" + selectedNormal);
        Physics2D.SyncTransforms();
        Check((bool)Call(s, "BallInFront") == front, pc.name + " actual test ball is " + (front ? "front" : "rear"));
        for (int i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
        Check(probe.sawAccepted, pc.name + " actual instep/leg collision recorded front=" + front);
        Check((((float[])Field(s, "effects"))[isFan ? 5 : 4] >= 0f) == front, pc.name + " actual special shot requires front ball");
        Check(pc.GetComponentsInChildren<Joint2D>().Length == 9, pc.name + " real collision keeps connected skeleton");
    }
    private IEnumerator Flight(bool enhanced)
    {
        yield return Scene();
        ball.position = new Vector2(fb.position.x + .8f, -.5f); ball.velocity = Vector2.zero;
        if (enhanced)
        {
            PlayerSkills.SetAction(fa, "Kick", fan); Touch(fs, fan, "VolleyOnContact");
            Check(((float[])Field(fs, "effects"))[5] >= 0f, "front volley still activates");
            Check(Mathf.Abs(ball.velocity.y - PlayerSkills.FanVolleyLiftSpeed) < .01f, "new volley has stronger initial lift");
        }
        else ball.velocity = new Vector2(8f, 8.5f);
        float start = Time.time, peak = ball.position.y, fall = 0f;
        bool sampledFall = false;
        for (int i = 0; i < 48; i++)
        {
            float age = Time.time - start;
            if (!enhanced && age < .85f)
                ball.AddForce(new Vector2(Mathf.Sin(age / .85f * Mathf.PI * 2f) * 5f, age < .32f ? 5.8f : -14.5f) * ball.mass);
            yield return new WaitForFixedUpdate();
            peak = Mathf.Max(peak, ball.position.y);
            if (!sampledFall && Time.time - start >= .78f) { fall = ball.velocity.y; sampledFall = true; }
        }
        Debug.Log("FRONT FLIGHT enhanced=" + enhanced + " peak=" + peak + " fallAt078=" + fall + " final=" + ball.position);
        if (enhanced) { newPeak = peak; newFall = fall; } else { oldPeak = peak; oldFall = fall; }
    }
    private IEnumerator Start()
    {
        Application.targetFrameRate = 60; Application.runInBackground = true; QualitySettings.vSyncCount = 0; AudioListener.volume = 0f;
        yield return new WaitForSeconds(.2f);
        yield return RearTests(true); yield return RearTests(false);
        yield return Scene();
        ball.position = new Vector2(zb.position.x - .6f, -1.55f); ball.velocity = Vector2.zero;
        Set(zs, "lastSteal", Time.time + 1f);
        yield return new WaitForSeconds(.1f);
        Check((bool)Call(zs, "GroundBallInReach", PlayerSkills.ZhaoInterceptReach), "front grounded ball remains in interception range");
        Call(zs, "AutoIntercept", Time.time);
        Check((float)Field(zs, "extendedKickUntil") > Time.time && ((float[])Field(zs, "effects"))[3] >= 0f, "front extended interception still works");
        PlayerSkills.SetAction(za, "Jump", zhao);
        Check(Mathf.Abs(PlayerSkills.GetJumpForce(100f, zhao) - 135f) < .01f, "Aerial Fortress also boosts front-ball jump");
        PlayerSkills.Enabled = false;
        Check(Mathf.Abs(PlayerSkills.GetJumpForce(100f, zhao) - 100f) < .01f, "classic jump force stays original");
        PlayerSkills.Enabled = true;
        yield return Scene();
        ball.position = new Vector2(fb.position.x + .7f, -1.55f); ball.velocity = Vector2.zero;
        yield return new WaitForSeconds(.1f); TestAxis = 1f;
        Call(fs, "Dribble"); Check((bool)Field(fs, "fasterDribble") && fa.speed >= 1.49f, "front dribble remains faster");
        ball.position = new Vector2(fb.position.x - .1f, -1.5f);
        Call(fs, "Update"); Check(!(bool)Field(fs, "fasterDribble") && Mathf.Abs(fa.speed - 1f) < .01f, "crossing behind restores dribble speed");
        yield return Scene();
        Vector2 hp = fan.transform.Find("Head").position;
        ball.position = new Vector2(Mathf.Max(hp.x + .35f, fb.position.x + .15f), hp.y + .05f); ball.velocity = Vector2.zero;
        Physics2D.SyncTransforms(); PlayerSkills.SetAction(fa, "Head", fan); Call(fs, "FixedUpdate");
        Check((float)Field(fs, "headUntil") < 0f && ball.velocity.x > 12f, "front cannon header still works");
        yield return LiveFoot(true, true); yield return LiveFoot(true, false);
        yield return LiveFoot(false, true); yield return LiveFoot(false, false);
        yield return Flight(false); yield return Flight(true);
        Check(newPeak > oldPeak + .5f, "new falling shot flies measurably higher");
        Check(newFall < oldFall - 2f && newFall < -6f, "new falling shot descends measurably more sharply");
        PlayerSkills.ResetAll(); Check((float)Field(fs, "curveStarted") < -1f, "reset clears enhanced flight assist");
        Debug.Log("FRONT SKILLS COMPLETE failures=" + failures); Application.Quit();
    }
}
