using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SkillTests : MonoBehaviour
{
    private static bool started;
    private static BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private string folder;
    public static void Boot()
    {
        if (started) return;
        started = true;
        GameObject obj = new GameObject("SkillVerification");
        UnityEngine.Object.DontDestroyOnLoad(obj);
        obj.AddComponent<SkillTests>();
    }
    private void Check(bool value, string message)
    {
        Debug.Log((value ? "SKILL PASS " : "SKILL FAIL ") + message);
    }
    private void Invoke(object obj, string method, params object[] args)
    {
        obj.GetType().GetMethod(method, flags).Invoke(obj, args);
    }
    private IEnumerator Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Application.runInBackground = true;
        folder = System.IO.Path.Combine(Application.dataPath, "../AI Mod Source/build");
        yield return new WaitForSeconds(.5f);
        PlayerSkills.Enabled = true;
        typeof(GameAIMod).GetMethod("StartGame", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.6f);
        Component fan = GameObject.Find("Fan").GetComponent(Type.GetType("PlayerController, Assembly-CSharp"));
        Component zhao = GameObject.Find("Zhao").GetComponent(fan.GetType());
        Component manager = UnityEngine.Object.FindObjectOfType(Type.GetType("GameManager, Assembly-CSharp")) as Component;
        manager.GetType().GetField("isStopping").SetValue(manager, false);
        foreach (Component player in new Component[] { fan, zhao })
        {
            ((Behaviour)player).enabled = false;
            Behaviour stick = player.GetComponent(Type.GetType("StickManController, Assembly-CSharp")) as Behaviour;
            stick.enabled = false;
            foreach (Rigidbody2D rb in player.GetComponentsInChildren<Rigidbody2D>()) rb.bodyType = RigidbodyType2D.Kinematic;
            player.GetType().GetField("isOnGround").SetValue(player, true);
        }
        PlayerSkills fs = fan.GetComponent<PlayerSkills>();
        PlayerSkills zs = zhao.GetComponent<PlayerSkills>();
        Check(fs != null && zs != null, "both player Start hooks attached");
        Rigidbody2D ball = (UnityEngine.Object.FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        Rigidbody2D fb = fan.GetType().GetField("rb").GetValue(fan) as Rigidbody2D;
        Rigidbody2D zb = zhao.GetType().GetField("rb").GetValue(zhao) as Rigidbody2D;
        Transform foot = fan.GetType().GetField("footTrans").GetValue(fan) as Transform;
        Transform zfoot = zhao.GetType().GetField("footTrans").GetValue(zhao) as Transform;
        Debug.Log("SKILL POS fan=" + fb.position + " foot=" + foot.position + " zhao=" + zb.position + " zfoot=" + zfoot.position);
        Check(Mathf.Abs(PlayerSkills.GetJumpForce(100f, zhao) - 135f) < .01f && PlayerSkills.GetJumpForce(100f, fan) == 100f, "Zhao jump force 1.35x, Fan unchanged");

        // Ground control is allowed only for a low, reachable ball, never high serves.
        ball.position = new Vector2(fb.position.x + 1.1f, -1.5f);
        ball.velocity = new Vector2(5f, 0f);
        Physics2D.SyncTransforms();
        yield return new WaitForFixedUpdate();
        Check(ball.velocity.x < 5f, "Fan ground control brakes escaping ball");
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "skill-dribble.png"));
        yield return new WaitForSeconds(.12f);
        ball.position = new Vector2(fb.position.x + .8f, 3.27f);
        ball.velocity = Vector2.zero;
        Invoke(fs, "FixedUpdate");
        Check(Mathf.Abs(ball.velocity.x) < .01f, "ground control does not grab high kickoff ball");

        PlayerSkills.ResetAll();
        Transform head = fan.transform.Find("Head");
        ball.position = (Vector2)head.position + new Vector2(.5f, .05f);
        ball.velocity = new Vector2(-4f, 0f);
        Physics2D.SyncTransforms();
        Animator animator = fan.GetType().GetField("anim").GetValue(fan) as Animator;
        PlayerSkills.SetAction(animator, "Head", fan);
        Collider2D own = fan.transform.Find("Body").GetComponent<Collider2D>();
        Collider2D other = zhao.transform.Find("Body").GetComponent<Collider2D>();
        Check(!Physics2D.GetIgnoreCollision(own, other), "header preserves player body collisions");
        Invoke(fs, "FixedUpdate");
        Check(Physics2D.GetIgnoreCollision(ball.GetComponent<Collider2D>(), other), "header ball has brief opponent-interference priority");
        Check(ball.velocity.x >= 12.9f && ball.velocity.y >= 2.7f, "header creates powerful forward impulse");
        Vector2 first = ball.velocity;
        Invoke(fs, "FixedUpdate");
        Check((first - ball.velocity).sqrMagnitude < .001f, "one header impulse per command");
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "skill-header.png"));
        yield return new WaitForSeconds(.4f);
        Check(!Physics2D.GetIgnoreCollision(own, other), "opponent collision restored after action");

        PlayerSkills.ResetAll();
        Animator za = zhao.GetType().GetField("anim").GetValue(zhao) as Animator;
        PlayerSkills.SetAction(za, "Jump", zhao);
        ball.position = new Vector2(zb.position.x - 1.85f, -1.5f);
        ball.velocity = new Vector2(-7f, 0f);
        Physics2D.SyncTransforms();
        PlayerSkills.SetAction(za, "Kick", zhao);
        Invoke(zs, "FixedUpdate");
        Check(ball.velocity.x > 3f, "extended foot intercept controls ball at 1.85 units");
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "skill-zhao.png"));
        yield return new WaitForSeconds(.12f);
        PlayerSkills.ResetAll();
        ball.position = new Vector2(zb.position.x - 2.7f, -1.5f);
        ball.velocity = new Vector2(-7f, 0f);
        PlayerSkills.SetAction(za, "Kick", zhao);
        Invoke(zs, "FixedUpdate");
        Check(Mathf.Abs(ball.velocity.x + 7f) < .01f, "foot skill cannot intercept beyond range");

        PlayerSkills.ResetAll();
        ball.position = new Vector2(zb.position.x - .8f, -1.5f);
        ball.velocity = Vector2.zero;
        PlayerSkills.SetAction(za, "Kick", zhao);
        Invoke(zs, "FixedUpdate");
        Check(ball.velocity.x <= -16.9f && ball.velocity.y < 1.3f, "ground shot 17+ horizontal speed with low trajectory");
        Check((float)typeof(PlayerSkills).GetField("glowUntil", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) > Time.time, "ground shot highlights ball");
        yield return new WaitForSeconds(.09f);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "skill-ground-shot.png"));
        yield return new WaitForSeconds(.15f);

        PlayerSkills.ResetAll();
        ball.position = new Vector2(fb.position.x + .8f, -.2f);
        ball.velocity = new Vector2(-2f, 0f);
        PlayerSkills.SetAction(animator, "Kick", fan);
        Invoke(fs, "FixedUpdate");
        Check(ball.velocity.x >= 8f && ball.velocity.y >= 8.4f, "Fan aerial kick launches high floating arc");
        Check((float)typeof(PlayerSkills).GetField("glowUntil", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) > Time.time, "aerial shot highlights ball");
        yield return new WaitForSeconds(.09f);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "skill-falling-shot.png"));
        yield return new WaitForSeconds(.12f);
        PlayerSkills.OnBallCollision(other);
        Check((float)typeof(PlayerSkills).GetField("curveStarted", flags).GetValue(fs) < 0f, "opponent touch ends curved-flight assistance");

        // Capture another header with ball highlighter active.
        PlayerSkills.ResetAll();
        ball.position = (Vector2)head.position + new Vector2(.5f, .05f);
        ball.velocity = Vector2.zero;
        PlayerSkills.SetAction(animator, "Head", fan);
        Invoke(fs, "FixedUpdate");
        Check((float)typeof(PlayerSkills).GetField("glowUntil", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) > Time.time, "header highlights ball");
        yield return new WaitForSeconds(.09f);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "skill-header.png"));
        yield return new WaitForSeconds(.12f);

        PlayerSkills.SetAction(animator, "Head", fan);
        manager.GetType().GetField("isStopping").SetValue(manager, true);
        Invoke(fs, "Update");
        Check(!Physics2D.GetIgnoreCollision(own, other), "pause cleans interference immunity");
        manager.GetType().GetField("isStopping").SetValue(manager, false);
        PlayerSkills.ResetAll();
        PlayerSkills.SetAction(animator, "Head", fan);
        GameAIMod.OnBallReset();
        Check(!Physics2D.GetIgnoreCollision(own, other) && (float)typeof(PlayerSkills).GetField("headUntil", flags).GetValue(fs) < 0f, "reset cleans active effects and collision leases");
        Check((float)typeof(PlayerSkills).GetField("glowUntil", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) < 0f, "reset clears football highlight");

        // Exercise the AI through the real patched controller Update, not just wrappers.
        foreach (Component aiPlayer in new Component[] { fan, zhao })
        {
            PlayerSkills.ResetAll();
            bool isFan = aiPlayer == fan;
            typeof(GameAIMod).GetField("mode", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, isFan ? 2 : 1);
            ball.position = new Vector2((isFan ? fb : zb).position.x + (isFan ? .8f : -.8f), isFan ? -.2f : -1.5f);
            ball.velocity = Vector2.zero;
            bool kickInput = GameAIMod.GetButtonDown(isFan ? KeyCode.K : KeyCode.KeypadEnter, aiPlayer);
            Check(kickInput, (isFan ? "Fan" : "Zhao") + " AI selects kick for reachable ball");
            if (kickInput) PlayerSkills.SetAction(isFan ? animator : za, "Kick", aiPlayer);
            PlayerSkills skill = isFan ? fs : zs;
            Check((float)typeof(PlayerSkills).GetField("kickUntil", flags).GetValue(skill) > Time.time, (isFan ? "Fan" : "Zhao") + " AI action triggers skill hook");
            Invoke(skill, "FixedUpdate");
            Check(isFan ? ball.velocity.y > 8f : ball.velocity.x < -16f, (isFan ? "Fan volley" : "Zhao shot") + " AI applies skill physics");
            yield return new WaitForSeconds(.12f);
        }
        typeof(GameAIMod).GetField("mode", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 0);

        PlayerSkills.ResetAll();
        ball.position = new Vector2(fb.position.x + .8f, -.2f);
        ball.velocity = Vector2.zero;
        PlayerSkills.SetAction(animator, "Kick", fan);
        Invoke(fs, "FixedUpdate");
        yield return new WaitForSeconds(.22f);
        float rising = ball.velocity.y;
        yield return new WaitForSeconds(.32f);
        Check(ball.velocity.y < rising - 5f, "floating arc accelerates downward in the later phase");
        PlayerSkills.ResetAll();
        PlayerSkills.Enabled = false;
        Check(PlayerSkills.GetJumpForce(100f, zhao) == 100f, "classic mode retains original jump parameter");
        ball.position = (Vector2)head.position + new Vector2(.5f, .05f);
        ball.velocity = Vector2.zero;
        PlayerSkills.SetAction(animator, "Head", fan);
        Invoke(fs, "FixedUpdate");
        Check(ball.velocity.sqrMagnitude < .001f && !Physics2D.GetIgnoreCollision(own, other), "classic mode has no header force or immunity");
        Check((float)typeof(PlayerSkills).GetField("glowUntil", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) < 0f, "classic mode has no football highlighter");
        PlayerSkills.Enabled = true;
        Debug.Log("SKILL TEST COMPLETE");
        // Discard the controlled measurement setup. Visual QA always uses a fresh
        // scene with all original controllers, ragdoll bodies and joints enabled.
        typeof(GameAIMod).GetField("mode", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 2);
        SceneManager.LoadScene("GameScene");
        yield return new WaitForSeconds(.3f);
        Debug.Log("SKILL LIVE PHYSICS START");
        Component[] livePlayers = {
            GameObject.Find("Fan").GetComponent(Type.GetType("PlayerController, Assembly-CSharp")),
            GameObject.Find("Zhao").GetComponent(Type.GetType("PlayerController, Assembly-CSharp"))
        };
        float maxJointError = 0f;
        foreach (Component p in livePlayers)
        {
            Check(((Behaviour)p).enabled, p.name + " original controller active");
            Check((p.GetComponent(Type.GetType("StickManController, Assembly-CSharp")) as Behaviour).enabled, p.name + " original posture control active");
            Debug.Log("SKILL JOINTS " + p.name + " " + p.GetComponentsInChildren<Joint2D>().Length);
        }
        for (int frame = 0; frame < 900; frame++)
        {
            foreach (Component p in livePlayers)
                foreach (AnchoredJoint2D joint in p.GetComponentsInChildren<AnchoredJoint2D>())
                {
                    if (joint.connectedBody == null) continue;
                    Vector2 a = joint.transform.TransformPoint(joint.anchor);
                    Vector2 b = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
                    maxJointError = Mathf.Max(maxJointError, Vector2.Distance(a, b));
                }
            if (frame == 180 || frame == 480 || frame == 800)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "skill-live-" + frame + ".png"));
            }
            yield return null;
        }
        Debug.Log("SKILL LIVE MAX JOINT ERROR " + maxJointError);
        Debug.Log("SKILL LIVE PHYSICS COMPLETE");
        foreach (Component p in livePlayers)
            Check(p.GetComponentsInChildren<Joint2D>().Length == 9, p.name + " retains all 9 limb joints after live skills");
        typeof(GameAIMod).GetField("mode", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 1);
        SceneManager.LoadScene("GameScene");
        yield return new WaitForSeconds(.3f);
        Component liveZhao = GameObject.Find("Zhao").GetComponent(Type.GetType("PlayerController, Assembly-CSharp"));
        Rigidbody2D liveBall = (UnityEngine.Object.FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        liveBall.position = (Vector2)liveZhao.transform.Find("Head").position + new Vector2(-.4f, 1f);
        liveBall.velocity = new Vector2(-1f, 0f);
        bool jumped = false;
        for (int frame = 0; frame < 360; frame++)
        {
            PlayerSkills liveSkills = liveZhao.GetComponent<PlayerSkills>();
            float[] fx = typeof(PlayerSkills).GetField("effects", flags).GetValue(liveSkills) as float[];
            if (Time.time - fx[2] < .3f) jumped = true;
            if (frame == 20 || frame == 160)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "skill-live-zhao-" + frame + ".png"));
            }
            yield return null;
        }
        Check(jumped, "Zhao higher jump triggers in original live physics");
        foreach (string name in new string[] { "Fan", "Zhao" })
            Check(GameObject.Find(name).GetComponentsInChildren<Joint2D>().Length == 9, name + " retains joints after live Zhao skills");
        Debug.Log("SKILL ALL VERIFICATION COMPLETE");
    }
}
