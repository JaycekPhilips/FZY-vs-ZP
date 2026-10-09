using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public sealed class ZhaoVolleyTouchProbe : MonoBehaviour
{
    public bool touched;
    void OnCollisionEnter2D(Collision2D collision) { Observe(collision); }
    void OnCollisionStay2D(Collision2D collision) { Observe(collision); }
    void Observe(Collision2D collision)
    {
        if (collision.collider != null && collision.collider.transform.root.name == "Zhao") touched = true;
    }
}

// Isolated test player only: real skill contact, flight, AI movement and collisions.
public sealed class ZhaoVolleyTests : MonoBehaviour
{
    public static bool UseAI, Baseline;
    static bool booted;
    const BindingFlags StaticPrivate = BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags InstancePrivate = BindingFlags.NonPublic | BindingFlags.Instance;
    Component fan, zhao, manager;
    Rigidbody2D fb, zb, ball;
    PlayerSkills fs;
    Type playerType;
    int checks, failures, launches, touches, saves;
    float originalGravity;
    public static void Boot()
    {
        if (booted) return;
        booted = true;
        var host = new GameObject("ZhaoVolleyTests");
        DontDestroyOnLoad(host);
        host.AddComponent<ZhaoVolleyTests>();
    }
    void Check(bool ok, string label) { checks++; if (!ok) failures++; Debug.Log((ok ? "DEFENSE PASS " : "DEFENSE FAIL ") + label); }
    object State(Component p) { return typeof(GameAIMod).GetMethod("GetState", StaticPrivate).Invoke(null, new object[] { p }); }
    object Read(object state, string name) { FieldInfo f = state.GetType().GetField(name); return f != null ? f.GetValue(state) : null; }
    int Score() { return (int)manager.GetType().GetField("p1Score").GetValue(manager); }
    void Move(Component p, Rigidbody2D body, float x)
    {
        p.transform.position += Vector3.right * (x - body.position.x);
        foreach (Rigidbody2D limb in p.GetComponentsInChildren<Rigidbody2D>()) { limb.velocity = Vector2.zero; limb.angularVelocity = 0; }
        Physics2D.SyncTransforms();
    }
    float JointError(Component p)
    {
        float error = 0;
        foreach (HingeJoint2D joint in p.GetComponentsInChildren<HingeJoint2D>())
            if (joint.connectedBody != null) error = Mathf.Max(error, Vector2.Distance(joint.transform.TransformPoint(joint.anchor), joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)));
        if (error > .35f)
        {
            Animator a = p.GetComponent<Animator>(); string clips = ""; foreach (AnimatorClipInfo c in a.GetCurrentAnimatorClipInfo(0)) clips += c.clip.name + ",";
            Debug.Log("DEFENSE JOINT DETAIL error=" + error + " clips=" + clips + " velocity=" + p.transform.Find("Body").GetComponent<Rigidbody2D>().velocity);
        }
        return error;
    }
    IEnumerator Scene(bool skills, int mode = 1)
    {
        UseAI = false;
        Time.timeScale = 1;
        PlayerSkills.Enabled = skills;
        typeof(GameAIMod).GetMethod("StartGame", StaticPrivate).Invoke(null, new object[] { mode });
        yield return new WaitForSeconds(.2f);
        playerType = Type.GetType("PlayerController, Assembly-CSharp");
        fan = GameObject.Find("Fan").GetComponent(playerType); zhao = GameObject.Find("Zhao").GetComponent(playerType);
        fb = (Rigidbody2D)playerType.GetField("rb").GetValue(fan); zb = (Rigidbody2D)playerType.GetField("rb").GetValue(zhao);
        fs = fan.GetComponent<PlayerSkills>();
        manager = FindObjectOfType(Type.GetType("GameManager, Assembly-CSharp")) as Component;
        ball = (FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        originalGravity = ball.gravityScale;
        ball.position = new Vector2(0, 1); ball.velocity = Vector2.zero; ball.gravityScale = 0;
    }
    IEnumerator Shot(int index, float shooter, float keeper, float lift = 0f)
    {
        yield return Scene(true);
        Move(fan, fb, shooter); Move(zhao, zb, keeper);
        if (lift > 0f) { fan.transform.position += Vector3.up * lift; Physics2D.SyncTransforms(); }
        yield return new WaitForSeconds(.15f);
        Collider2D bc = ball.GetComponent<Collider2D>();
        float best = float.NegativeInfinity;
        Vector2 position = Vector2.zero;
        foreach (string name in new[] { "L_LowLeg", "R_LowLeg" })
        {
            BoxCollider2D limb = fan.transform.Find(name).GetComponent<BoxCollider2D>();
            foreach (float side in new[] { -1f, 1f })
            {
                Vector2 contact = limb.transform.TransformPoint(limb.offset + new Vector2(limb.size.x * side * .5f, limb.size.y * -.2f));
                Vector2 normal = limb.transform.TransformDirection(Vector2.right * side);
                Vector2 candidate = contact + normal * bc.bounds.extents.x * .8f;
                if (normal.x > .5f && candidate.x > best) { best = candidate.x; position = candidate; }
            }
        }
        ball.position = position; ball.velocity = Vector2.zero; ball.gravityScale = originalGravity;
        Physics2D.SyncTransforms();
        PlayerSkills.SetAction(fan.GetComponent<Animator>(), "Kick", fan);
        var probe = ball.gameObject.AddComponent<ZhaoVolleyTouchProbe>();
        UseAI = true;
        bool launched = false;
        for (int i = 0; i < 12; i++)
        {
            yield return new WaitForFixedUpdate();
            launched = (float)typeof(PlayerSkills).GetField("curveStarted", InstancePrivate).GetValue(fs) >= 0;
            if (launched) break;
        }
        Check(launched, "real Fan foot contact launches skill case=" + index);
        if (!launched) yield break;
        launches++;
        foreach (Rigidbody2D limb in fan.GetComponentsInChildren<Rigidbody2D>()) limb.simulated = false;
        float started = Time.time;
        float firstJump = -1, firstHead = -1, maxError = 0;
        bool defenseSeen = false;
        object state = State(zhao);
        Vector2 forecast, forecastVelocity;
        bool forecastAvailable = PlayerSkills.TryPredictFanVolley(.12f, out forecast, out forecastVelocity);
        bool forecastChecked = false;
        Debug.Log("DEFENSE PARAM case=" + index + " nativeForce=" + playerType.GetField("jumpForce").GetValue(zhao) + " jumpEstimate=" + Read(state, "jumpSpeed") + " start=" + zb.position + " shot=" + ball.position);
        for (int i = 0; i < 95 && Time.time - started < 1.5f; i++)
        {
            yield return new WaitForFixedUpdate();
            if ((bool)Read(state, "jump") && firstJump < 0) firstJump = Time.time - started;
            if ((bool)Read(state, "head") && firstHead < 0) firstHead = Time.time - started;
            if (Read(state, "volleyDefense") != null && (bool)Read(state, "volleyDefense")) defenseSeen = true;
            maxError = Mathf.Max(maxError, JointError(zhao));
            if (index == 0 && !forecastChecked && Time.time - started >= .12f)
            {
                forecastChecked = true;
                Check(forecastAvailable && !probe.touched && Vector2.Distance(forecast, ball.position) < .4f,
                    "skill forecast follows real unobstructed physics error=" + Vector2.Distance(forecast, ball.position));
            }
        }
        bool saved = probe.touched && Score() == 0;
        if (probe.touched) touches++;
        if (saved) saves++;
        if (!Baseline) Check(defenseSeen, "Zhao enters dedicated curve defense case=" + index);
        Check(maxError < .35f, "native Zhao skeleton stays connected case=" + index + " error=" + maxError);
        Debug.Log("DEFENSE RESULT case=" + index + " lift=" + lift + " baseline=" + Baseline + " touched=" + probe.touched + " saved=" + saved + " score=" + Score() + " firstJump=" + firstJump + " firstHead=" + firstHead + " end=" + zb.position);
        UseAI = false;
    }
    IEnumerator Opening(bool skills, int mode)
    {
        yield return Scene(skills, mode);
        ball.position = new Vector2(0, 3.27f); ball.velocity = Vector2.zero; ball.gravityScale = originalGravity;
        Physics2D.SyncTransforms();
        Component p = mode == 1 ? zhao : fan;
        Rigidbody2D body = mode == 1 ? zb : fb;
        float direction = mode == 1 ? -1 : 1, start = body.position.x;
        UseAI = true;
        object state = State(p);
        bool retreat = false, distantJump = false;
        for (int i = 0; i < 80; i++)
        {
            yield return new WaitForFixedUpdate();
            if ((bool)Read(state, "serveApproach") && (float)Read(state, "axis") * direction < -.01f) retreat = true;
            if ((bool)Read(state, "jump") && Mathf.Abs(ball.position.x - body.position.x) > 1.5f) distantJump = true;
        }
        Check(!retreat && !distantJump, "serve avoids retreat and distant jump skills=" + skills + " mode=" + mode);
        Check((body.position.x - start) * direction > 1.5f, "serve still attacks skills=" + skills + " mode=" + mode);
        Check(JointError(p) < .35f, "serve keeps player joints intact skills=" + skills + " mode=" + mode);
        UseAI = false;
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; Application.targetFrameRate = 120; QualitySettings.vSyncCount = 0; AudioListener.volume = 0;
        yield return new WaitForSeconds(.2f);
        float[] shooters = { -3.8f, -3f, -1.8f, -1f, 0f, 1f };
        float[] keepers = { 1f, 4.5f, 2.3f, 4.5f, 4.5f, 4.8f };
        for (int i = 0; i < shooters.Length; i++) yield return Shot(i, shooters[i], keepers[i]);
        yield return Shot(6, -3.8f, 2f, 1.4f);
        yield return Shot(7, -2.2f, 3.8f, 1.4f);
        yield return Shot(8, -.8f, 4.6f, 1.4f);
        if (!Baseline) foreach (bool skills in new[] { false, true }) foreach (int mode in new[] { 1, 2 }) yield return Opening(skills, mode);
        Debug.Log("DEFENSE COMPLETE baseline=" + Baseline + " checks=" + checks + " launches=" + launches + " touches=" + touches + " saves=" + saves + " failures=" + failures);
        Application.Quit(failures == 0 ? 0 : 1);
    }
    void Update() { if (Time.realtimeSinceStartup > 90) { Debug.LogError("DEFENSE TIMEOUT"); Application.Quit(2); } }
}
