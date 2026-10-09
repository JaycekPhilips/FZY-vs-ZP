using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public sealed class SkillTests : MonoBehaviour
{
    public static bool TestAxes = true;
    public static float TestAxis;
    public static bool JumpPulse;
    private static bool started;
    private readonly BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private int failures;
    private float classicReach, enhancedReach;
    private Component fan, zhao;
    private PlayerSkills fs, zs;
    private Rigidbody2D fb, zb, ball;
    private Animator fa, za;
    private void Check(bool pass, string label) { Debug.Log((pass ? "PHYSICAL PASS " : "PHYSICAL FAIL ") + label); if (!pass) failures++; }
    private object Field(object obj, string field) { return obj.GetType().GetField(field, flags).GetValue(obj); }
    private void Set(object obj, string field, object value) { obj.GetType().GetField(field, flags).SetValue(obj, value); }
    private object Call(object obj, string method, params object[] args) { return obj.GetType().GetMethod(method, flags).Invoke(obj, args); }
    public static void Boot()
    {
        if (started) return;
        started = true;
        GameObject obj = new GameObject("PhysicalSkillVerification");
        UnityEngine.Object.DontDestroyOnLoad(obj);
        obj.AddComponent<SkillTests>();
    }
    private Vector2 End(Transform limb)
    {
        BoxCollider2D collider = limb.GetComponent<BoxCollider2D>();
        return limb.TransformPoint(collider.offset + new Vector2(0f, -collider.size.y * .45f));
    }
    private IEnumerator Scene(bool enabled)
    {
        PlayerSkills.Enabled = enabled;
        TestAxis = 0f; JumpPulse = false;
        typeof(GameAIMod).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.3f);
        Type type = Type.GetType("PlayerController, Assembly-CSharp");
        fan = GameObject.Find("Fan").GetComponent(type); zhao = GameObject.Find("Zhao").GetComponent(type);
        fs = fan.GetComponent<PlayerSkills>(); zs = zhao.GetComponent<PlayerSkills>();
        fb = type.GetField("rb").GetValue(fan) as Rigidbody2D; zb = type.GetField("rb").GetValue(zhao) as Rigidbody2D;
        fa = type.GetField("anim").GetValue(fan) as Animator; za = type.GetField("anim").GetValue(zhao) as Animator;
        ball = (UnityEngine.Object.FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
    }
    private IEnumerator KickReach(bool enabled)
    {
        yield return Scene(enabled);
        PlayerSkills.SetAction(za, "Kick", zhao);
        float reach = 0f;
        Component stick = zhao.GetComponent(Type.GetType("StickManController, Assembly-CSharp"));
        for (int i = 0; i < 32; i++)
        {
            reach = Mathf.Max(reach, zb.position.x - End(zhao.transform.Find("L_LowLeg")).x, zb.position.x - End(zhao.transform.Find("R_LowLeg")).x);
            if (i % 8 == 0) Debug.Log("PHYSICAL LEG " + enabled + " " + i + " " + stick.GetType().GetField("L_Up_Leg").GetValue(stick) + " " + stick.GetType().GetField("R_Up_Leg").GetValue(stick));
            if (!Application.isBatchMode && i == 12)
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.dataPath, "../AI Mod Source/build/physical-kick-" + enabled + ".png"));
            yield return null;
        }
        Debug.Log("PHYSICAL REACH " + enabled + " " + reach);
        if (enabled) enhancedReach = reach; else classicReach = reach;
        Check(zhao.GetComponentsInChildren<Joint2D>().Length == 9, "Zhao native joints intact after real kick " + enabled);
    }
    private IEnumerator BurstCase(bool far, bool grounded)
    {
        yield return Scene(true);
        float half = (float)Field(zs, "halfCourtDistance");
        Check(!float.IsInfinity(half) && half > 2f, "half-court measured from actual goal mouths");
        Debug.Log("PHYSICAL HALF COURT " + half);
        Set(zs, "lastSteal", Time.time);
        ball.position = new Vector2(zb.position.x - .75f, grounded ? -1.55f : -1.1f);
        ball.velocity = Vector2.zero;
        float gap = half + (far ? .35f : -.35f);
        fan.transform.position += Vector3.right * (ball.position.x - gap - fb.position.x);
        Physics2D.SyncTransforms();
        yield return new WaitForSeconds(grounded ? .1f : .02f);
        bool eligible = (bool)Call(zs, "CanGroundBurst");
        Check(eligible == (far && grounded), "burst eligibility far=" + far + " grounded=" + grounded);
        if (grounded && far)
        {
            float actual = Mathf.Abs(ball.position.x - fb.position.x);
            Set(zs, "halfCourtDistance", actual);
            Check(!(bool)Call(zs, "CanGroundBurst"), "distance exactly half-court remains ineligible");
            Set(zs, "halfCourtDistance", half);
        }
        PlayerSkills.SetAction(za, "Kick", zhao);
        Call(zs, "FixedUpdate");
        float[] fx = Field(zs, "effects") as float[];
        Check((Time.time - fx[4] < .1f) == (far && grounded), "burst executes only for far, truly grounded ball");
        Check(fan.GetComponentsInChildren<Joint2D>().Length == 9 && zhao.GetComponentsInChildren<Joint2D>().Length == 9, "burst scenario preserves both skeletons");
    }
    private IEnumerator Start()
    {
        Application.targetFrameRate = 60; Application.runInBackground = true; QualitySettings.vSyncCount = 0; AudioListener.volume = 0f;
        if (!Application.isBatchMode) Screen.SetResolution(960, 600, false);
        yield return new WaitForSeconds(.2f);
        yield return KickReach(false); yield return KickReach(true);
        Check(enhancedReach > classicReach + .1f, "enhanced actual foot reach exceeds native kick");
        yield return BurstCase(false, true); yield return BurstCase(true, false); yield return BurstCase(true, true);

        yield return Scene(true);
        JumpPulse = true;
        ball.position = new Vector2(0f, 3.27f); ball.velocity = Vector2.zero;
        float until = Time.time + .8f;
        while (Time.time < until && (!(bool)Field(fs, "jumpLeftGround") || End(fan.transform.Find("R_LowLeg")).y < -1.2f)) yield return new WaitForFixedUpdate();
        Check((bool)Field(fs, "jumpVolleyActive") && (bool)Field(fs, "jumpLeftGround"), "native W input arms actual airborne jump volley");
        Check((float)Field(fs, "kickUntil") < 0f, "jump volley works without separate K command");
        Transform foot = fan.transform.Find("R_LowLeg");
        Debug.Log("PHYSICAL W FOOT " + End(foot));
        ball.position = End(foot) + Vector2.right * .22f; ball.velocity = new Vector2(-1f, 0f); Physics2D.SyncTransforms();
        float[] ffx = Field(fs, "effects") as float[];
        for (int i = 0; i < 10 && ffx[5] < 0f; i++) yield return new WaitForFixedUpdate();
        Check(ffx[5] > 0f && ball.velocity.y > 3f, "W jump plus real foot contact produces falling shot");
        if (!Application.isBatchMode)
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.dataPath, "../AI Mod Source/build/physical-w-volley.png"));
        Check(!(bool)Field(fs, "jumpVolleyActive"), "jump activation consumed after one successful kick");

        yield return Scene(true);
        JumpPulse = true;
        yield return new WaitForSeconds(1.4f);
        Check(!(bool)Field(fs, "jumpVolleyActive"), "unused W jump window ends on landing");

        yield return Scene(true);
        ball.position = new Vector2(fb.position.x + .7f, -1.55f); ball.velocity = Vector2.zero;
        yield return new WaitForSeconds(.1f);
        TestAxis = 1f;
        float topSpeed = 0f;
        for (int i = 0; i < 12; i++) { topSpeed = Mathf.Max(topSpeed, fa.speed); yield return null; }
        Check(topSpeed >= 1.49f, "ground dribbling increases native animation cadence");
        TestAxis = 0f; yield return new WaitForSeconds(.06f);
        Check(Mathf.Abs(fa.speed - 1f) < .01f, "stopping dribble restores native animation speed");
        Check(fan.GetComponentsInChildren<Joint2D>().Length == 9, "faster dribble preserves limb constraints");

        PlayerSkills.ResetAll();
        Transform head = fan.transform.Find("Head");
        ball.position = (Vector2)head.position + new Vector2(.5f, .05f); ball.velocity = Vector2.zero; Physics2D.SyncTransforms();
        GameObject wall = new GameObject("HeaderCollisionTestWall", typeof(BoxCollider2D));
        float wallX = head.position.x + 1f; wall.transform.position = new Vector3(wallX, head.position.y, 0f); wall.GetComponent<BoxCollider2D>().size = new Vector2(.1f, 1.5f); Physics2D.SyncTransforms();
        PlayerSkills.SetAction(fa, "Head", fan); Call(fs, "FixedUpdate");
        Check(ball.collisionDetectionMode == CollisionDetectionMode2D.Continuous, "header uses continuous ball collision detection");
        bool noIgnore = true;
        foreach (Collider2D other in zhao.GetComponentsInChildren<Collider2D>()) if (Physics2D.GetIgnoreCollision(ball.GetComponent<Collider2D>(), other)) noIgnore = false;
        Check(noIgnore, "header keeps every opponent collision enabled");
        yield return new WaitForSeconds(.12f);
        Check(ball.position.x < wallX + .08f, "fast header cannot pass through thin physical obstacle");
        UnityEngine.Object.Destroy(wall);
        Check(fan.GetComponentsInChildren<Joint2D>().Length == 9, "header contact preserves player skeleton");
        PlayerSkills.ResetAll();
        Check(!((bool)typeof(PlayerSkills).GetField("changedBallDetection", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)), "reset restores collision parameters");
        Debug.Log("PHYSICAL SKILLS COMPLETE failures=" + failures);
        Application.Quit();
    }
}
