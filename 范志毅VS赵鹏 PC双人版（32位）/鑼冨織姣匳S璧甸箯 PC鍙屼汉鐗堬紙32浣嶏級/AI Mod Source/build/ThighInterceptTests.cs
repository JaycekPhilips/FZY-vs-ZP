using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

// Only compiled into an isolated player. Exercises native key polling,
// real descent and landing; never included in either release.
public sealed class ThighInterceptTests : MonoBehaviour
{
    public static bool ReadKey(KeyCode key) { return false; }
    public static bool Down;
    public static bool ReadDown(KeyCode key) { return Down && key == ControlBindings.Get(true, GameControlAction.Down); }
    static bool booted;
    Component fan, manager;
    Rigidbody2D body, ball;
    PlayerSkills skill;
    Type playerType;
    int checks, failures;
    const BindingFlags IP = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Boot()
    {
        if (booted) return; booted = true;
        ControlBindings.settingsPath = System.IO.Path.Combine(Application.dataPath, "..", "drop-test-bindings.ini");
        var host = new GameObject("ThighInterceptTests"); DontDestroyOnLoad(host); host.AddComponent<ThighInterceptTests>();
    }
    void Check(bool ok, string label) { checks++; if (!ok) failures++; Debug.Log((ok ? "DROP PASS " : "DROP FAIL ") + label); }
    object Field(string name) { return typeof(PlayerSkills).GetField(name, IP).GetValue(skill); }
    float Effect() { return skill == null ? -10f : ((float[])Field("effects"))[9]; }
    bool Automatic() { return (bool)typeof(PlayerSkills).GetMethod("ShouldAutomaticQuickDrop", IP).Invoke(skill, null); }
    bool Grounded() { return (bool)typeof(PlayerSkills).GetMethod("TouchesGround", IP).Invoke(skill, null); }
    float Joints()
    {
        float error = 0f;
        foreach (HingeJoint2D joint in fan.GetComponentsInChildren<HingeJoint2D>())
            if (joint.connectedBody != null) error = Mathf.Max(error, Vector2.Distance(joint.transform.TransformPoint(joint.anchor), joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)));
        return error;
    }
    void PlaceBall(float gap, float height, Vector2 velocity)
    {
        ball.position = new Vector2(body.position.x + gap, height);
        ball.velocity = velocity; ball.angularVelocity = 0f; ball.gravityScale = 0f; Physics2D.SyncTransforms();
    }
    float Floor()
    {
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(body.position, Vector2.down, 30f))
            if (hit.collider != null && !hit.collider.isTrigger && hit.normal.y > .5f && !hit.collider.transform.IsChildOf(fan.transform) && hit.collider != ball.GetComponent<Collider2D>() && hit.collider.transform.root.name != "Zhao") return hit.point.y;
        throw new Exception("No pitch floor");
    }
    IEnumerator Scene(bool skills, bool aerial)
    {
        Down = false; Time.timeScale = 1f; PlayerSkills.Enabled = skills; ControlBindings.ResetDefaults();
        typeof(GameAIMod).GetMethod("StartGame", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.2f);
        playerType = Type.GetType("PlayerController, Assembly-CSharp"); fan = GameObject.Find("Fan").GetComponent(playerType);
        body = (Rigidbody2D)playerType.GetField("rb").GetValue(fan); skill = fan.GetComponent<PlayerSkills>();
        manager = FindObjectOfType(Type.GetType("GameManager, Assembly-CSharp")) as Component;
        ball = (FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        PlaceBall(3f, 10f, Vector2.zero);
        fan.transform.position += (Vector3)(new Vector2(-2f, aerial ? 1.8f : body.position.y) - body.position);
        foreach (Rigidbody2D limb in fan.GetComponentsInChildren<Rigidbody2D>()) { limb.velocity = aerial ? Vector2.up * 2f : Vector2.zero; limb.angularVelocity = 0f; }
        if (aerial) fan.GetComponent<Animator>().SetTrigger("Jump");
        Physics2D.SyncTransforms(); yield return new WaitForFixedUpdate();
    }
    IEnumerator Guards()
    {
        yield return Scene(true, true);
        float floor = Floor(), shin = (float)Field("standingInterceptHeight"), radius = ball.GetComponent<Collider2D>().bounds.extents.y;
        float low = floor + (radius + shin - radius) * .5f;
        Debug.Log("DROP HEIGHT floor=" + floor + " shin=" + shin + " radius=" + radius + " low=" + low);
        Check(shin > radius * 2f && shin < 2.8f, "standing thigh reference follows edition size");
        PlaceBall(1.2f, low, new Vector2(6f, 0)); Check(!Automatic(), "front ball flying away does not auto drop");
        PlaceBall(1.2f, low, Vector2.zero); Check(!Automatic(), "stationary ball does not auto drop");
        PlaceBall(-1.2f, low, new Vector2(6f, 0)); Check(!Automatic(), "rear incoming ball requires manual key");
        PlaceBall(1.2f, floor + shin + radius, new Vector2(-6f, 0)); Check(!Automatic(), "high incoming ball stays above fixed standing thigh threshold");
        PlaceBall(1.2f, low, new Vector2(-6f, -5f)); Check(!Automatic(), "steep downward ball is not low flat flight");
        PlaceBall(4f, low, new Vector2(-6f, 0)); Check(!Automatic(), "distant low ball does not cause premature drop");
        PlaceBall(1.2f, low, new Vector2(-6f, 0)); Check(Automatic(), "near front low flat incoming ball is eligible");
        PlaceBall(1.2f, floor + shin - radius - .025f, new Vector2(-6f, 0)); Check(Automatic(), "upper thigh ball is eligible below standing top");
        PlaceBall(1.2f, floor + shin - radius + .025f, new Vector2(-6f, 0)); Check(!Automatic(), "ball crossing thigh upper edge is rejected");
        Time.timeScale = 0f; Check(!Automatic(), "paused game does not auto drop"); Time.timeScale = 1f;
        manager.GetType().GetField("isStopping").SetValue(manager, true); Check(!Automatic(), "stopped rally does not auto drop"); manager.GetType().GetField("isStopping").SetValue(manager, false);
        PlaceBall(1.2f, floor + shin + radius, Vector2.zero); Down = true;
        yield return new WaitForSeconds(.05f); Down = false;
        Check(Effect() < 0f, "manual S with front ball cannot bypass automatic conditions");
    }
    IEnumerator Drop(bool skills, bool rear, bool press, bool remapped)
    {
        yield return Scene(skills, true);
        if (remapped) { Check(ControlBindings.Set(true, GameControlAction.Down, KeyCode.F), "custom down key accepted"); }
        float floor = Floor(), radius = ball.GetComponent<Collider2D>().bounds.extents.y;
        float shin = skills ? (float)Field("standingInterceptHeight") : .9f;
        PlaceBall(rear ? -1.2f : 1.2f, floor + shin * .5f, new Vector2(rear ? 6f : -6f, 0f));
        Down = press;
        float maxError = 0f, minVelocity = 0f; bool activated = false; float landing = -1f, start = Time.time;
        for (int i = 0; i < 70; i++)
        {
            yield return new WaitForFixedUpdate();
            if (i == 3) { Down = false; PlaceBall(rear ? -2f : 2f, 10f, Vector2.zero); }
            activated |= Effect() >= start;
            maxError = Mathf.Max(maxError, Joints()); minVelocity = Mathf.Min(minVelocity, body.velocity.y);
            if (skill != null && Grounded() && landing < 0f) landing = Time.time - start;
        }
        string label = "skills=" + skills + " rear=" + rear + " press=" + press + " remap=" + remapped;
        bool expected = skills && (!rear || press);
        Check(activated == expected, label + " semi-auto activation follows front/rear rules");
        Check(maxError < .35f, label + " descent preserves connected skeleton");
        Check(fan.GetComponentsInChildren<Joint2D>().Length == 9, label + " all native joints retained");
        if (expected)
        {
            Check(minVelocity < -6f && minVelocity > -10f, label + " fast descent stays bounded");
            Check(landing > 0f && landing < .75f, label + " lands through actual ground collision");
            Check(!(bool)Field("quickDrop"), label + " landing ends assistance");
            PlayerSkills.ResetAll(); Check(Effect() < 0f, label + " reset clears animation");
        }
    }
    IEnumerator GroundGuard()
    {
        yield return Scene(true, false);
        yield return new WaitForSeconds(.3f);
        PlaceBall(1f, Floor() + (float)Field("standingInterceptHeight") * .5f, new Vector2(-6f, 0));
        yield return new WaitForSeconds(.05f); Check(Effect() < 0f, "grounded player does not trigger air descent");
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; Application.targetFrameRate = 120; QualitySettings.vSyncCount = 0; AudioListener.volume = 0f;
        yield return new WaitForSeconds(.2f);
        yield return Guards(); yield return GroundGuard();
        yield return Drop(false, false, false, false); yield return Drop(false, true, true, false);
        yield return Drop(true, false, false, false); yield return Drop(true, true, false, false);
        yield return Drop(true, true, true, false); yield return Drop(true, true, true, true);
        Debug.Log("DROP COMPLETE checks=" + checks + " failures=" + failures); Application.Quit(failures == 0 ? 0 : 1);
    }
    void Update() { if (Time.realtimeSinceStartup > 60f) { Debug.LogError("DROP TIMEOUT"); Application.Quit(2); } }
}
