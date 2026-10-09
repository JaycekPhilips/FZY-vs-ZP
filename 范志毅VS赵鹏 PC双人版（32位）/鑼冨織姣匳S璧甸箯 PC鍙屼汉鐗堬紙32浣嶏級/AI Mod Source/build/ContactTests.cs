using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public sealed class SkillTests : MonoBehaviour
{
    private static bool started;
    private BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
    public static void Boot()
    {
        if (started) return;
        started = true;
        GameObject obj = new GameObject("ContactVerification");
        UnityEngine.Object.DontDestroyOnLoad(obj);
        obj.AddComponent<SkillTests>();
    }
    private void Check(bool pass, string name) { Debug.Log((pass ? "CONTACT PASS " : "CONTACT FAIL ") + name); }
    private Vector2 FootEnd(Collider2D collider)
    {
        BoxCollider2D box = collider as BoxCollider2D;
        CapsuleCollider2D capsule = collider as CapsuleCollider2D;
        Vector2 offset = box != null ? box.offset : capsule.offset;
        Vector2 size = box != null ? box.size : capsule.size;
        return collider.transform.TransformPoint(offset + new Vector2(0f, -size.y * .45f));
    }
    private IEnumerator Start()
    {
        Application.targetFrameRate = 60;
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        yield return new WaitForSeconds(.3f);
        PlayerSkills.Enabled = true;
        typeof(GameAIMod).GetMethod("StartGame", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.25f);
        Component fan = GameObject.Find("Fan").GetComponent(Type.GetType("PlayerController, Assembly-CSharp"));
        PlayerSkills skills = fan.GetComponent<PlayerSkills>();
        Rigidbody2D body = fan.GetType().GetField("rb").GetValue(fan) as Rigidbody2D;
        Animator animator = fan.GetType().GetField("anim").GetValue(fan) as Animator;
        Rigidbody2D ball = (UnityEngine.Object.FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        ball.position = new Vector2(body.position.x + .8f, -.2f);
        ball.velocity = Vector2.zero;
        PlayerSkills.SetAction(animator, "Kick", fan);
        yield return new WaitForSeconds(.06f);
        float[] effects = typeof(PlayerSkills).GetField("effects", flags).GetValue(skills) as float[];
        Check(Time.time - effects[5] > 1f, "near-body ball and kick cannot trigger volley without foot contact");
        PlayerSkills.ResetAll();
        Collider2D foot = null;
        foreach (Collider2D collider in fan.GetComponentsInChildren<Collider2D>())
        {
            Debug.Log("CONTACT COLLIDER " + collider.name + " " + collider.GetType().Name);
            if (collider.name.IndexOf("Low_Leg", StringComparison.OrdinalIgnoreCase) >= 0 || collider.name.IndexOf("LowLeg", StringComparison.OrdinalIgnoreCase) >= 0) foot = collider;
        }
        Check(foot != null, "actual lower-leg foot collider found");
        if (foot == null) yield break;
        MethodInfo onContact = typeof(PlayerSkills).GetMethod("VolleyOnContact", flags);
        onContact.Invoke(skills, new object[] { fan.transform.Find("Head").GetComponent<Collider2D>(), (Vector2)fan.transform.Find("Head").position });
        Check(Time.time - effects[5] > 1f, "head contact is rejected");
        ball.position = new Vector2(0f, 3.27f);
        ball.velocity = Vector2.zero;
        fan.GetType().GetField("doJump").SetValue(fan, true);
        float until = Time.time + 1.2f;
        while (Time.time < until && FootEnd(foot).y <= -.6f) yield return new WaitForFixedUpdate();
        Debug.Log("CONTACT FOOT POSITION " + FootEnd(foot));
        Check(FootEnd(foot).y > -.6f, "original jumping physics lifts real foot into air");
        PlayerSkills.SetAction(animator, "Kick", fan);
        ball.position = FootEnd(foot) + Vector2.right * .22f;
        ball.velocity = new Vector2(-1f, 0f);
        Physics2D.SyncTransforms();
        for (int i = 0; i < 10 && Time.time - effects[5] > .5f; i++) yield return new WaitForFixedUpdate();
        Check(Time.time - effects[5] < .5f, "real aerial foot-ball collision with kick triggers volley");
        Check(ball.velocity.y > 4f, "real contact produces elevated shot");
        yield return new WaitForSeconds(.05f);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.dataPath, "../AI Mod Source/build/skill-real-foot-contact.png"));
        yield return new WaitForSeconds(.7f);
        Check(fan.GetComponentsInChildren<Joint2D>().Length == 9, "all original player joints survive contact skill");
        Check((fan.GetComponent(Type.GetType("StickManController, Assembly-CSharp")) as Behaviour).enabled, "original posture control remains enabled");
        PlayerSkills.ResetAll();
        Component zhao = GameObject.Find("Zhao").GetComponent(fan.GetType());
        Rigidbody2D zbody = zhao.GetType().GetField("rb").GetValue(zhao) as Rigidbody2D;
        ball.position = new Vector2(zbody.position.x - 2.5f, -1.5f);
        ball.velocity = new Vector2(-6f, 0f);
        Physics2D.SyncTransforms();
        float before = Time.time;
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        PlayerSkills zskills = zhao.GetComponent<PlayerSkills>();
        float[] zfx = typeof(PlayerSkills).GetField("effects", flags).GetValue(zskills) as float[];
        Check(zfx[3] >= before, "Zhao auto-intercepts ground ball at 2.5 units without kick command");
        Check(ball.velocity.x > 2f, "automatic intercept actually controls incoming ball");
        PlayerSkills.ResetAll();
        PlayerSkills.Enabled = false;
        ball.position = new Vector2(zbody.position.x - 2.5f, -1.5f);
        ball.velocity = new Vector2(-6f, 0f);
        yield return new WaitForFixedUpdate();
        Check(ball.velocity.x < -5f, "classic mode has no automatic interception");
        PlayerSkills.Enabled = true;
        Debug.Log("CONTACT VERIFICATION COMPLETE");
    }
}
