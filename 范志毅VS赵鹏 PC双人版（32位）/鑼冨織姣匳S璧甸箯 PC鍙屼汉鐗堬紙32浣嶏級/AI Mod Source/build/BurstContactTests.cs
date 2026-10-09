using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public sealed class BurstContactProbe : MonoBehaviour
{
    public PlayerSkills skills;
    public bool acceptedContact;
    private void OnCollisionEnter2D(Collision2D c) { Observe(c); }
    private void OnCollisionStay2D(Collision2D c) { Observe(c); }
    private void Observe(Collision2D c)
    {
        if (c.collider.GetComponentInParent<PlayerSkills>() != skills) return;
        for (int i = 0; i < c.contactCount; i++)
        {
            ContactPoint2D p = c.GetContact(i);
            Vector2 n = p.normal;
            if (Vector2.Dot(n, GetComponent<Rigidbody2D>().position - p.point) < 0f) n = -n;
            bool accepted = (bool)typeof(PlayerSkills).GetMethod("IsFootContact", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(skills, new object[] { c.collider, p.point, n });
            acceptedContact |= accepted;
            Debug.Log("BURST COLLISION " + c.collider.name + " accepted=" + accepted);
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
    private PlayerSkills skills;
    private Rigidbody2D ball;
    private Animator animator;
    private BurstContactProbe probe;
    private object Field(string name) { return typeof(PlayerSkills).GetField(name, Flags).GetValue(skills); }
    private void Set(string name, object value) { typeof(PlayerSkills).GetField(name, Flags).SetValue(skills, value); }
    private object Call(string name, params object[] args) { return typeof(PlayerSkills).GetMethod(name, Flags).Invoke(skills, args); }
    private void Check(bool pass, string label) { Debug.Log((pass ? "BURST PASS " : "BURST FAIL ") + label); if (!pass) failures++; }
    private float Fx { get { return ((float[])Field("effects"))[4]; } }
    public static void Boot()
    {
        if (started) return;
        started = true;
        GameObject obj = new GameObject("BurstVerification"); UnityEngine.Object.DontDestroyOnLoad(obj); obj.AddComponent<SkillTests>();
    }
    private IEnumerator Scene()
    {
        PlayerSkills.Enabled = true; TestAxis = 0f; JumpPulse = false;
        typeof(GameAIMod).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.3f);
        Type pc = Type.GetType("PlayerController, Assembly-CSharp");
        fan = GameObject.Find("Fan").GetComponent(pc); zhao = GameObject.Find("Zhao").GetComponent(pc);
        skills = zhao.GetComponent<PlayerSkills>(); animator = pc.GetField("anim").GetValue(zhao) as Animator;
        ball = (UnityEngine.Object.FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        probe = ball.gameObject.AddComponent<BurstContactProbe>(); probe.skills = skills;
        ball.position = new Vector2(0f, 3.27f); ball.velocity = Vector2.zero;
    }
    private Vector2 Point(BoxCollider2D limb, float x, float y) { return limb.transform.TransformPoint(limb.offset + new Vector2(limb.size.x * x, limb.size.y * y)); }
    private Vector2 Normal(BoxCollider2D limb, Vector2 local) { return limb.transform.TransformDirection(local); }
    private void Touch(BoxCollider2D limb, float x, float y, Vector2 normal) { Call("BurstOnContact", limb, Point(limb, x, y), Normal(limb, normal)); }
    private IEnumerator LiveAerial(bool withKick, bool sole, float axis)
    {
        yield return Scene(); TestAxis = axis;
        zhao.GetType().GetField("doJump").SetValue(zhao, true);
        PlayerSkills.SetAction(animator, "Jump", zhao);
        if (withKick) PlayerSkills.SetAction(animator, "Kick", zhao);
        yield return new WaitForSeconds(.3f);
        BoxCollider2D limb = zhao.transform.Find("R_LowLeg").GetComponent<BoxCollider2D>();
        if (sole)
        {
            BoxCollider2D other = zhao.transform.Find("L_LowLeg").GetComponent<BoxCollider2D>();
            if (Point(other, 0f, -.5f).y < Point(limb, 0f, -.5f).y) limb = other;
        }
        Vector2 n = Normal(limb, sole ? Vector2.down : Vector2.right).normalized;
        Vector2 point = Point(limb, sole ? 0f : .5f, sole ? -.5f : -.28f);
        float radius = ball.GetComponent<CircleCollider2D>().radius * Mathf.Abs(ball.transform.lossyScale.x);
        ball.position = point + n * (radius + .002f);
        ball.velocity = limb.attachedRigidbody.GetPointVelocity(point) - n * 2f;
        Physics2D.SyncTransforms();
        for (int i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
        Check((Fx > 0f) == (withKick && !sole), "real aerial skill K=" + withKick + " sole=" + sole + " axis=" + axis);
        if (withKick && !sole)
        {
            Check(probe.acceptedContact, "real accepted aerial leg contact recorded");
            Check(ball.velocity.x < -12f, "aerial strike supplies high forward speed");
            Check(ball.velocity.y <= .5f, "aerial strike is pressed down, never lobbed");
            Check((float)Field("kickUntil") < 0f, "aerial activation consumed once");
        }
        Check(zhao.GetComponentsInChildren<Joint2D>().Length == 9, "aerial shot keeps native Zhao joints");
    }
    private IEnumerator LiveGround(bool close, float axis)
    {
        yield return Scene(); TestAxis = axis;
        Set("lastSteal", Time.time + 2f);
        Rigidbody2D zb = zhao.GetType().GetField("rb").GetValue(zhao) as Rigidbody2D;
        ball.position = new Vector2(zb.position.x - .65f, -1.55f); ball.velocity = Vector2.zero;
        if (close)
        {
            Rigidbody2D fb = fan.GetType().GetField("rb").GetValue(fan) as Rigidbody2D;
            fan.transform.position += Vector3.right * (ball.position.x - (float)Field("halfCourtDistance") * .5f + .35f - fb.position.x);
        }
        Physics2D.SyncTransforms();
        yield return new WaitForSeconds(.1f);
        Check(!(bool)Call("IsBallAirborne"), "actual shot starts with a grounded ball");
        PlayerSkills.SetAction(animator, "Kick", zhao);
        for (int i = 0; i < 20 && Fx < 0f; i++) yield return new WaitForFixedUpdate();
        Check((Fx > 0f) == !close, "real ground skill close=" + close + " axis=" + axis);
        if (!close)
        {
            Check(probe.acceptedContact, "ground shot requires real accepted leg collision");
            Check(ball.velocity.x < -12f && ball.velocity.y < 1f, "ground shot stays fast and low");
        }
    }
    private IEnumerator Start()
    {
        Application.targetFrameRate = 60; Application.runInBackground = true; QualitySettings.vSyncCount = 0; AudioListener.volume = 0f;
        yield return new WaitForSeconds(.2f); yield return Scene();
        float half = (float)Field("halfCourtDistance"), quarter = half * .5f;
        Rigidbody2D fb = fan.GetType().GetField("rb").GetValue(fan) as Rigidbody2D;
        Check(quarter > 2f && quarter < 4f, "quarter court measured from native goal mouths");
        Debug.Log("BURST QUARTER COURT " + quarter);
        ball.position = new Vector2(fb.position.x + quarter - .01f, 1f);
        Check(!(bool)Call("CanBurst"), "less than quarter court rejected");
        float actualGap = Mathf.Abs(ball.position.x - fb.position.x);
        Set("halfCourtDistance", actualGap * 2f);
        Check(!(bool)Call("CanBurst"), "exactly quarter court rejected");
        Set("halfCourtDistance", half);
        ball.position = new Vector2(fb.position.x + quarter + .01f, 1f);
        Check((bool)Call("CanBurst"), "more than quarter court accepted while airborne");
        ball.position = new Vector2(2f, 1f); ball.velocity = new Vector2(0f, 8f);
        foreach (string name in new[] { "L_LowLeg", "R_LowLeg" })
        {
            BoxCollider2D limb = zhao.transform.Find(name).GetComponent<BoxCollider2D>();
            foreach (float y in new[] { -.3f, .12f })
                Check((bool)Call("IsFootContact", limb, Point(limb, .5f, y), Normal(limb, Vector2.right)), name + " instep/lower shin accepted y=" + y);
            Check(!(bool)Call("IsFootContact", limb, Point(limb, 0f, -.5f), Normal(limb, Vector2.down)), name + " sole excluded");
            Check(!(bool)Call("IsFootContact", limb, Point(limb, .5f, .35f), Normal(limb, Vector2.right)), name + " upper shin excluded");
        }
        BoxCollider2D foot = zhao.transform.Find("R_LowLeg").GetComponent<BoxCollider2D>();
        PlayerSkills.ResetAll(); Touch(foot, .5f, -.3f, Vector2.right);
        Check(Fx < 0f, "contact without explicit shot rejected");
        PlayerSkills.SetAction(animator, "Jump", zhao); Touch(foot, .5f, -.3f, Vector2.right);
        Check(Fx < 0f, "jump alone rejected");
        Call("AutoIntercept", Time.time); Touch(foot, .5f, -.3f, Vector2.right);
        Check(Fx < 0f, "automatic interception does not arm burst");
        PlayerSkills.SetAction(animator, "Kick", zhao);
        Call("FixedUpdate"); Check(Fx < 0f, "kick/proximity alone cannot activate before contact");
        Touch(foot, 0f, -.5f, Vector2.down); Check(Fx < 0f, "sole with command rejected");
        Call("BurstOnContact", zhao.transform.Find("Head").GetComponent<Collider2D>(), (Vector2)zhao.transform.Find("Head").position, Vector2.left);
        Check(Fx < 0f, "head with command rejected");
        Touch(foot, .5f, .12f, Vector2.right);
        Check(Fx > 0f && ball.velocity.x <= -17f && ball.velocity.y < 0f, "airborne lower shin activates fast downward shot");
        Check((float)Field("kickUntil") < 0f, "one activation per explicit shot");
        Check(typeof(PlayerSkills).GetField("glowOwner", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == skills, "successful burst retains gold ball highlight");
        float floor = (float)Field("burstFloorHeight");
        Check(floor < -1f && floor > -1.8f, "low trajectory uses actual pitch floor");
        float highest = ball.position.y;
        for (int i = 0; i < 16; i++) { yield return new WaitForFixedUpdate(); highest = Mathf.Max(highest, ball.position.y); }
        Debug.Log("BURST LOW PATH y=" + ball.position.y + " floor=" + floor + " highest=" + highest);
        Check(highest <= 1.02f, "upward incoming ball never becomes a lob");
        Check(ball.position.y < floor + .25f, "aerial burst reaches low skim trajectory physically");
        PlayerSkills.ResetAll(); Check((float)Field("burstUntil") < 0f, "reset clears trajectory assist");
        PlayerSkills.SetAction(animator, "Kick", zhao);
        PlayerSkills.Enabled = false; Touch(foot, .5f, -.3f, Vector2.right); Check(Fx < 0f, "classic mode has no burst");
        PlayerSkills.Enabled = true;
        yield return LiveAerial(true, false, 0f);
        yield return LiveAerial(true, false, -1f);
        yield return LiveAerial(true, false, 1f);
        yield return LiveAerial(false, false, 0f);
        yield return LiveAerial(true, true, 0f);
        yield return LiveGround(false, 0f);
        yield return LiveGround(true, 0f);
        Debug.Log("BURST CONTACTS COMPLETE failures=" + failures); Application.Quit();
    }
}
