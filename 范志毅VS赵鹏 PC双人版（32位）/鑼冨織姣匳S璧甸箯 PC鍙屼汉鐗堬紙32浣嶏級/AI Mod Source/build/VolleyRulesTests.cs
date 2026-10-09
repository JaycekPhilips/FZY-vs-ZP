using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public sealed class SkillTests : MonoBehaviour
{
    private static bool started;
    public static bool TestAxes;
    public static float TestAxis;
    private readonly BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private int failures;
    private void Check(bool pass, string name)
    {
        Debug.Log((pass ? "VOLLEY PASS " : "VOLLEY FAIL ") + name);
        if (!pass) failures++;
    }
    public static void Boot()
    {
        if (started) return;
        started = true;
        GameObject obj = new GameObject("VolleyRulesVerification");
        UnityEngine.Object.DontDestroyOnLoad(obj);
        obj.AddComponent<SkillTests>();
    }
    private Vector2 FootEnd(BoxCollider2D foot)
    {
        return foot.transform.TransformPoint(foot.offset + new Vector2(0f, -foot.size.y * .45f));
    }
    private IEnumerator Start()
    {
        Application.targetFrameRate = 60;
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        AudioListener.volume = 0f;
        yield return new WaitForSeconds(.2f);
        for (int scenario = 0; scenario < 3; scenario++)
        {
            PlayerSkills.Enabled = true;
            TestAxes = true;
            TestAxis = scenario == 0 ? 0f : scenario == 1 ? -1f : 1f;
            typeof(GameAIMod).GetMethod("StartGame", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { 0 });
            yield return new WaitForSeconds(.25f);
            Component fan = GameObject.Find("Fan").GetComponent(Type.GetType("PlayerController, Assembly-CSharp"));
            PlayerSkills skills = fan.GetComponent<PlayerSkills>();
            Animator animator = fan.GetType().GetField("anim").GetValue(fan) as Animator;
            BoxCollider2D foot = fan.transform.Find("R_LowLeg").GetComponent<BoxCollider2D>();
            Rigidbody2D ball = (UnityEngine.Object.FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
            float[] fx = typeof(PlayerSkills).GetField("effects", flags).GetValue(skills) as float[];
            MethodInfo airborne = typeof(PlayerSkills).GetMethod("IsBallAirborne", flags);
            MethodInfo onContact = typeof(PlayerSkills).GetMethod("VolleyOnContact", flags);
            MethodInfo footFilter = typeof(PlayerSkills).GetMethod("IsFootContact", flags);
            FieldInfo window = typeof(PlayerSkills).GetField("kickUntil", flags);
            if (scenario == 0)
            {
                ball.position = new Vector2(0f, -1.1f);
                ball.velocity = Vector2.zero;
                yield return new WaitForFixedUpdate();
                Check((bool)airborne.Invoke(skills, null), "low aerial ball below old height threshold is airborne");
                Vector2 side = foot.transform.TransformPoint(foot.offset + new Vector2(foot.size.x / 2f, -foot.size.y * .05f));
                Check((bool)footFilter.Invoke(skills, new object[] { foot, side }), "side of foot accepted beyond old narrow end strip");
                Vector2 knee = foot.transform.TransformPoint(foot.offset + new Vector2(0f, foot.size.y * .4f));
                Check(!(bool)footFilter.Invoke(skills, new object[] { foot, knee }), "upper shin and knee still excluded");
                PlayerSkills.ResetAll();
                onContact.Invoke(skills, new object[] { foot, FootEnd(foot) });
                Check(fx[5] < 0f, "foot contact without a kick command remains inactive");
                PlayerSkills.SetAction(animator, "Kick", fan);
                float first = (float)window.GetValue(skills);
                yield return new WaitForSeconds(.1f);
                PlayerSkills.SetAction(animator, "Kick", fan);
                Check((float)window.GetValue(skills) > first + .07f, "rapid second kick refreshes contact window");
                onContact.Invoke(skills, new object[] { fan.transform.Find("Head").GetComponent<Collider2D>(), (Vector2)fan.transform.Find("Head").position });
                Check(fx[5] < 0f, "head contact with a kick command does not trigger volley");
                ball.position = new Vector2(0f, -1.55f);
                ball.velocity = Vector2.zero;
                yield return new WaitForSeconds(.14f);
                Check(!(bool)airborne.Invoke(skills, null), "resting ground ball is not airborne");
                onContact.Invoke(skills, new object[] { foot, FootEnd(foot) });
                Check(fx[5] < 0f, "grounded ball cannot trigger aerial skill");
            }
            PlayerSkills.ResetAll();
            ball.position = new Vector2(0f, 3.27f);
            ball.velocity = Vector2.zero;
            fan.GetType().GetField("doJump").SetValue(fan, true);
            PlayerSkills.SetAction(animator, "Kick", fan);
            yield return new WaitForSeconds(.42f);
            Debug.Log("VOLLEY FOOT " + scenario + " " + FootEnd(foot));
            Check((float)window.GetValue(skills) > Time.time, "kick remains valid beyond old .32 second window, direction " + TestAxis);
            Check(FootEnd(foot).y > -1.25f, "real foot lifted by original jump physics, direction " + TestAxis);
            ball.position = FootEnd(foot) + Vector2.right * .22f;
            ball.velocity = new Vector2(-1f, 0f);
            Physics2D.SyncTransforms();
            for (int i = 0; i < 10 && fx[5] < 0f; i++) yield return new WaitForFixedUpdate();
            Check(fx[5] > 0f, "real aerial foot collision triggers kick with direction " + TestAxis);
            Check(ball.velocity.y > 3f, "contact supplies lifted shot, direction " + TestAxis);
            float fired = fx[5];
            onContact.Invoke(skills, new object[] { foot, FootEnd(foot) });
            Check(fx[5] == fired && (float)window.GetValue(skills) < 0f, "one activation per kick command");
            yield return new WaitForSeconds(.2f);
            Check(fan.GetComponentsInChildren<Joint2D>().Length == 9, "all limb joints remain intact");
        }
        TestAxes = false;
        Debug.Log("VOLLEY RULES COMPLETE failures=" + failures);
        Application.Quit();
    }
}
