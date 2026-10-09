using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public sealed class VolleySurfaceProbe : MonoBehaviour
{
    public BoxCollider2D limb;
    public PlayerSkills skills;
    public bool sawContact, sawAccepted, sawNewShin;
    private void OnCollisionEnter2D(Collision2D c) { Observe(c); }
    private void OnCollisionStay2D(Collision2D c) { Observe(c); }
    private void Observe(Collision2D c)
    {
        if (c.collider != limb) return;
        for (int i = 0; i < c.contactCount; i++)
        {
            ContactPoint2D p = c.GetContact(i);
            Vector2 n = p.normal;
            if (Vector2.Dot(n, GetComponent<Rigidbody2D>().position - p.point) < 0f) n = -n;
            Vector2 local = limb.transform.InverseTransformPoint(p.point);
            bool accepted = (bool)typeof(PlayerSkills).GetMethod("IsFootContact", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(skills, new object[] { limb, p.point, n });
            sawContact = true; sawAccepted |= accepted;
            sawNewShin |= accepted && local.y > limb.offset.y + limb.size.y * .05f;
            Debug.Log("SURFACE COLLISION " + limb.name + " y=" + ((local.y - limb.offset.y) / limb.size.y) + " normal=" + limb.transform.InverseTransformDirection(n) + " accepted=" + accepted);
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
    private Component fan;
    private PlayerSkills skills;
    private Animator animator;
    private Rigidbody2D ball;
    private VolleySurfaceProbe probe;
    private object Field(string name) { return typeof(PlayerSkills).GetField(name, Flags).GetValue(skills); }
    private object Call(string name, params object[] args) { return typeof(PlayerSkills).GetMethod(name, Flags).Invoke(skills, args); }
    private void Check(bool pass, string label) { Debug.Log((pass ? "SURFACE PASS " : "SURFACE FAIL ") + label); if (!pass) failures++; }
    public static void Boot()
    {
        if (started) return;
        started = true;
        GameObject obj = new GameObject("VolleySurfaceVerification");
        UnityEngine.Object.DontDestroyOnLoad(obj);
        obj.AddComponent<SkillTests>();
    }
    private IEnumerator Scene()
    {
        PlayerSkills.Enabled = true; TestAxis = 0f; JumpPulse = false;
        typeof(GameAIMod).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.3f);
        fan = GameObject.Find("Fan").GetComponent(Type.GetType("PlayerController, Assembly-CSharp"));
        skills = fan.GetComponent<PlayerSkills>();
        animator = fan.GetType().GetField("anim").GetValue(fan) as Animator;
        ball = (UnityEngine.Object.FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        probe = ball.gameObject.AddComponent<VolleySurfaceProbe>(); probe.skills = skills;
        ball.position = new Vector2(0f, 3.27f); ball.velocity = Vector2.zero;
    }
    private Vector2 Point(BoxCollider2D limb, float x, float y)
    {
        return limb.transform.TransformPoint(limb.offset + new Vector2(limb.size.x * x, limb.size.y * y));
    }
    private Vector2 Normal(BoxCollider2D limb, Vector2 local) { return limb.transform.TransformDirection(local); }
    private bool Filter(BoxCollider2D limb, float x, float y, Vector2 localNormal)
    {
        return (bool)Call("IsFootContact", limb, Point(limb, x, y), Normal(limb, localNormal));
    }
    private void GeometryChecks(string stage)
    {
        foreach (string name in new[] { "L_LowLeg", "R_LowLeg" })
        {
            BoxCollider2D limb = fan.transform.Find(name).GetComponent<BoxCollider2D>();
            Check(Filter(limb, .5f, -.32f, Vector2.right), stage + " " + name + " instep accepted");
            Check(Filter(limb, -.5f, .12f, Vector2.left), stage + " " + name + " newly admitted lower shin accepted");
            Check(Filter(limb, .5f, .19f, Vector2.right), stage + " " + name + " lower shin boundary accepted");
            Check(!Filter(limb, .5f, .3f, Vector2.right), stage + " " + name + " upper shin excluded");
            Check(!Filter(limb, 0f, .5f, Vector2.up), stage + " " + name + " knee excluded");
            Check(!Filter(limb, 0f, -.5f, Vector2.down), stage + " " + name + " sole excluded");
            Check(!Filter(limb, .5f, -.45f, new Vector2(.4f, -.9f).normalized), stage + " " + name + " oblique sole contact excluded");
            Check(!Filter(limb, .5f, -.49f, Vector2.right), stage + " " + name + " bottom seam excluded");
        }
    }
    private IEnumerator LiveContact(string name, float y, Vector2 localNormal, bool withK, bool expected)
    {
        yield return Scene();
        JumpPulse = true;
        if (withK) PlayerSkills.SetAction(animator, "Kick", fan);
        yield return new WaitForSeconds(.30f);
        BoxCollider2D limb = fan.transform.Find(name).GetComponent<BoxCollider2D>();
        probe.limb = limb;
        if (!expected)
        {
            BoxCollider2D other = fan.transform.Find(name == "R_LowLeg" ? "L_LowLeg" : "R_LowLeg").GetComponent<BoxCollider2D>();
            if (Point(other, 0f, -.5f).y < Point(limb, 0f, -.5f).y) limb = other;
            probe.limb = limb;
        }
        Vector2 n = Normal(limb, localNormal).normalized;
        Vector2 point = Point(limb, localNormal.x * .5f, y);
        float radius = ball.GetComponent<CircleCollider2D>().radius * Mathf.Abs(ball.transform.lossyScale.x);
        ball.position = point + n * (radius + .002f);
        ball.velocity = limb.attachedRigidbody.GetPointVelocity(point) - n * 2f;
        Physics2D.SyncTransforms();
        float[] fx = Field("effects") as float[];
        for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
        Check(probe.sawContact, "actual " + limb.name + " contact y=" + y + " K=" + withK);
        Check((fx[5] > 0f) == expected, "actual collision skill=" + expected + " y=" + y + " K=" + withK);
        if (expected)
        {
            Check(probe.sawAccepted, "actual accepted surface recorded");
            if (y > .05f) Check(probe.sawNewShin, "actual collision on newly admitted lower shin");
            Check(ball.velocity.y > 3f, "accepted contact lifts ball");
            Check((float)Field("kickUntil") < 0f && !(bool)Field("jumpVolleyActive"), "activation consumed once");
        }
        else Check(!probe.sawAccepted, "actual sole collision rejected by surface filter");
        Check(fan.GetComponentsInChildren<Joint2D>().Length == 9, "native player joints remain connected");
    }
    private IEnumerator Start()
    {
        Application.targetFrameRate = 60; Application.runInBackground = true; QualitySettings.vSyncCount = 0; AudioListener.volume = 0f;
        yield return new WaitForSeconds(.2f);
        yield return Scene();
        GeometryChecks("standing");
        BoxCollider2D limb = fan.transform.Find("R_LowLeg").GetComponent<BoxCollider2D>();
        Vector2 p = Point(limb, .5f, .12f), n = Normal(limb, Vector2.right);
        PlayerSkills.ResetAll();
        Call("VolleyOnContact", limb, p, n);
        Check(((float[])Field("effects"))[5] < 0f, "airborne shin contact without K or W remains inactive");
        PlayerSkills.SetAction(animator, "Kick", fan);
        Call("VolleyOnContact", fan.transform.Find("Head").GetComponent<Collider2D>(), (Vector2)fan.transform.Find("Head").position, Vector2.right);
        Check(((float[])Field("effects"))[5] < 0f, "head contact remains inactive");
        ball.position = new Vector2(0f, -1.55f); ball.velocity = Vector2.zero;
        yield return new WaitForSeconds(.14f);
        Check(!(bool)Call("IsBallAirborne"), "grounded ball detected");
        Call("VolleyOnContact", limb, p, n);
        Check(((float[])Field("effects"))[5] < 0f, "grounded ball remains inactive");
        ball.position = new Vector2(0f, 3.27f); ball.velocity = Vector2.zero;
        JumpPulse = true; PlayerSkills.SetAction(animator, "Kick", fan);
        yield return new WaitForSeconds(.3f);
        GeometryChecks("rotated native jump/kick");
        yield return LiveContact("L_LowLeg", .18f, Vector2.left, true, true);
        yield return LiveContact("L_LowLeg", -.32f, Vector2.left, true, true);
        yield return LiveContact("R_LowLeg", .18f, Vector2.right, false, true);
        yield return LiveContact("R_LowLeg", -.5f, Vector2.down, true, false);
        Debug.Log("VOLLEY SURFACES COMPLETE failures=" + failures);
        Application.Quit();
    }
}
