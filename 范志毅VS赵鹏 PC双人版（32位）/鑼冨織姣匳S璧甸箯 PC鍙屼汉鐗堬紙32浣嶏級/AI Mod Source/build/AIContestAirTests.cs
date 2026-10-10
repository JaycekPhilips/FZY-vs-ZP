using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class AIAirContactProbe : MonoBehaviour
{
    public string playerName;
    public bool touched;
    void OnCollisionEnter2D(Collision2D c) { Observe(c); }
    void OnCollisionStay2D(Collision2D c) { Observe(c); }
    void Observe(Collision2D c) { if (c.collider != null && c.collider.transform.root.name == playerName) touched = true; }
}

// This host is compiled into an isolated test DLL, never into the game release.
public sealed class AIContestAirTests : MonoBehaviour
{
    public static bool UseAI;
    public static HashSet<KeyCode> Held = new HashSet<KeyCode>();
    public static bool ReadKey(KeyCode key) { return Held.Contains(key); }
    public static bool ReadDown(KeyCode key) { return false; }
    static bool booted;
    const BindingFlags SP = BindingFlags.Static | BindingFlags.NonPublic;
    Component fan, zhao, player, opponent;
    Rigidbody2D ball, body, other;
    Type pt;
    float scale, direction, floor;
    int checks, failures, lobTouches;
    object state;
    public static void Boot()
    {
        if (booted) return; booted = true;
        ControlBindings.settingsPath = System.IO.Path.Combine(Application.dataPath, "..", "ai-test-bindings.ini");
        var host = new GameObject("AIContestAirTests"); DontDestroyOnLoad(host); host.AddComponent<AIContestAirTests>();
    }
    void Check(bool ok, string label) { checks++; if (!ok) failures++; Debug.Log((ok ? "AIMATCH PASS " : "AIMATCH FAIL ") + label); }
    object Read(string name) { return state.GetType().GetField(name).GetValue(state); }
    void Put(string name, object value) { state.GetType().GetField(name).SetValue(state, value); }
    Rigidbody2D Body(Component p) { return pt.GetField("rb").GetValue(p) as Rigidbody2D; }
    void Move(Component p, Vector2 position)
    {
        p.transform.position += (Vector3)(position - Body(p).position);
        foreach (Rigidbody2D limb in p.GetComponentsInChildren<Rigidbody2D>()) { limb.velocity = Vector2.zero; limb.angularVelocity = 0; }
        Physics2D.SyncTransforms();
    }
    float JointError(Component p)
    {
        float error = 0f;
        foreach (HingeJoint2D j in p.GetComponentsInChildren<HingeJoint2D>())
            if (j.connectedBody != null) error = Mathf.Max(error, Vector2.Distance(j.transform.TransformPoint(j.anchor), j.connectedBody.transform.TransformPoint(j.connectedAnchor)));
        return error;
    }
    void Decide()
    {
        Put("frame", -1); GameAIMod.GetAxis("Horizontal", player);
    }
    void FreeDecision()
    {
        Put("serveApproach", false); Put("waitingForServe", false); Put("contestUntil", -1f);
        Put("lastJump", -10f); Put("lastKick", -10f); Put("lastHead", -10f);
        pt.GetField("isOnGround").SetValue(player, true); Decide();
    }
    IEnumerator Scene(bool skills, bool fanAI)
    {
        UseAI = false; Held.Clear(); Time.timeScale = 1f; PlayerSkills.Enabled = skills;
        typeof(GameAIMod).GetMethod("StartGame", SP).Invoke(null, new object[] { fanAI ? 2 : 1 });
        yield return new WaitForSeconds(.25f);
        pt = Type.GetType("PlayerController, Assembly-CSharp");
        fan = GameObject.Find("Fan").GetComponent(pt); zhao = GameObject.Find("Zhao").GetComponent(pt);
        player = fanAI ? fan : zhao; opponent = fanAI ? zhao : fan;
        body = Body(player); other = Body(opponent); direction = fanAI ? 1f : -1f;
        scale = Mathf.Abs(player.transform.lossyScale.x) / .8f;
        ball = (FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        floor = GameObject.Find("Down").GetComponent<Collider2D>().bounds.max.y;
        ball.position = new Vector2(0, 3f); ball.velocity = Vector2.zero; ball.gravityScale = 0f;
        Move(player, new Vector2(-direction * 2f, body.position.y)); Move(opponent, new Vector2(direction * 5f, other.position.y));
        yield return new WaitForSeconds(.3f);
        state = typeof(GameAIMod).GetMethod("GetState", SP).Invoke(null, new object[] { player });
        Debug.Log("AIMATCH PARAM side=" + player.name + " skills=" + skills + " scale=" + scale + " jump=" + Read("jumpSpeed") + " gravity=" + body.gravityScale + " head=" + player.transform.Find("Head").position.y);
    }
    IEnumerator Contest(bool skills, bool fanAI, bool airborne)
    {
        yield return Scene(skills, fanAI);
        Move(player, new Vector2(-direction * .65f * scale, body.position.y));
        Move(opponent, new Vector2(direction * .65f * scale, other.position.y));
        ball.position = new Vector2(0, floor + ball.GetComponent<Collider2D>().bounds.extents.y + .004f);
        ball.gravityScale = 1f;
        if (airborne)
        {
            foreach (Component p in new[] { player, opponent })
            {
                p.transform.position += Vector3.up * 1.4f;
                foreach (Rigidbody2D limb in p.GetComponentsInChildren<Rigidbody2D>()) limb.gravityScale = 0f;
            }
            ball.position += Vector2.up * 1.4f; ball.gravityScale = 0f;
        }
        Physics2D.SyncTransforms(); Put("serveApproach", false); Put("waitingForServe", false);
        UseAI = true; Held.Add(fanAI ? KeyCode.LeftArrow : KeyCode.D);
        int contestFrames = 0, kicks = 0, heads = 0, wrongDirections = 0; float maxError = 0f;
        for (int i = 0; i < 100; i++)
        {
            yield return new WaitForFixedUpdate(); Decide();
            if ((bool)Read("contesting"))
            {
                contestFrames++;
                if ((bool)Read("kick") || (bool)Read("power")) kicks++;
                if ((bool)Read("head")) heads++;
                if (Mathf.Abs(other.position.x - body.position.x) > .05f && (float)Read("axis") * Mathf.Sign(other.position.x - body.position.x) <= .1f) { wrongDirections++; Debug.Log("AIMATCH DIRECTION i="+i+" axis="+Read("axis")+" rescue="+Read("rescuing")+" gap="+(other.position.x-body.position.x)+" ball="+ball.position); }
            }
            maxError = Mathf.Max(maxError, JointError(player), JointError(opponent));
        }
        string label = player.name + " skills=" + skills + " airborne=" + airborne;
        Check(contestFrames > 0, "real contest recognized " + label + " frames=" + contestFrames);
        Check(kicks == 0 && heads == 0, "contest uses directions instead of striking " + label);
        Check(wrongDirections == 0, "direction opposes the human player " + label);
        Check(maxError < .35f, "direction pushing preserves joints " + label + " error=" + maxError);
        // A separated opponent must not keep suppressing normal free shots.
        Held.Clear(); UseAI = false; Move(opponent, new Vector2(direction * 5f, other.position.y));
        ball.position = new Vector2(body.position.x + direction * .7f * scale, player.transform.Find("L_LowLeg").position.y);
        ball.velocity = Vector2.zero; Physics2D.SyncTransforms(); UseAI = true; FreeDecision();
        Check(!(bool)Read("contesting"), "separation releases contest suppression " + label);
        UseAI = false;
    }
    IEnumerator Decisions(bool skills, bool fanAI)
    {
        yield return Scene(skills, fanAI); UseAI = true;
        float headY = player.transform.Find("Head").position.y;
        ball.gravityScale = 1f;
        Transform foot = Read("foot") as Transform;
        ball.position = new Vector2(body.position.x + direction * .7f * scale, foot.position.y + .5f * scale);
        ball.velocity = Vector2.zero; FreeDecision();
        Check(!(bool)Read("contesting") && (bool)Read("kick"), "normal free ball shooting remains available " + player.name + " skills=" + skills);
        // The ball is rising far above attainable height. Run for its later
        // descent, never jump or strike while it is unreachable.
        ball.position = new Vector2(body.position.x + direction * .3f, headY + 3f);
        ball.velocity = new Vector2(direction * 3f, 6f); FreeDecision();
        Check((bool)Read("highBallPlan") && !(bool)Read("jump") && !(bool)Read("kick") && !(bool)Read("head"), "unreachable rising ball avoids premature actions " + player.name + " skills=" + skills);
        Check(direction * ((float)Read("interceptX") - ball.position.x) > .4f, "moving high ball leads future descent " + player.name);
        // An overhead descending ball that is physically within jump reach.
        ball.position = new Vector2(body.position.x + direction * .25f * scale, headY + 1.3f * scale);
        ball.velocity = new Vector2(0f, -1f); FreeDecision();
        Check((bool)Read("jump") && !(bool)Read("kick"), "reachable descending ball schedules jump " + player.name + " skills=" + skills);
        // A fast ball will have passed overhead before this player can rise.
        ball.position = new Vector2(body.position.x, headY + 1.4f);
        ball.velocity = new Vector2(direction * 16f, -1f); FreeDecision();
        Check(!(bool)Read("jump"), "fast overhead pass avoids futile jump " + player.name);
        // Serve approach keeps its existing attack-only rule.
        ball.position = new Vector2(0f, 3.27f); ball.velocity = Vector2.zero;
        Move(player, new Vector2(-direction * 3f, body.position.y)); Put("serveApproach", true); Put("waitingForServe", true); Put("lastServeReset", Time.time); Decide();
        Check((float)Read("axis") * direction >= 0f && !(bool)Read("jump"), "serve advances without retreat or meaningless jump " + player.name);
        Time.timeScale = 0f; Decide();
        Check((float)Read("axis") == 0f && !(bool)Read("kick") && !(bool)Read("jump"), "paused AI has no commands " + player.name);
        Time.timeScale = 1f; UseAI = false;
    }
    IEnumerator Lob(bool skills, bool fanAI)
    {
        yield return Scene(skills, fanAI);
        Put("serveApproach", false); Put("waitingForServe", false);
        float headY = player.transform.Find("Head").position.y;
        // Natural descending lob, with all player limbs and ball simulated.
        ball.position = new Vector2(body.position.x - direction * .6f, headY + 2.3f);
        ball.velocity = new Vector2(direction * 3.2f, -.5f); ball.gravityScale = 1f;
        AIAirContactProbe probe = ball.gameObject.AddComponent<AIAirContactProbe>(); probe.playerName = player.name;
        UseAI = true; bool planned = false, jumpSeen = false, distantJump = false, skillHeader = false; float error = 0f;
        for (int i = 0; i < 100; i++)
        {
            yield return new WaitForFixedUpdate();
            if (player.name == "Fan" && skills && i % 8 == 0) Debug.Log("AIMATCH LOB step=" + i + " ball=" + ball.position + " velocity=" + ball.velocity + " player=" + body.position + " run=" + body.velocity + " head=" + player.transform.Find("Head").position + " target=" + Read("interceptX") + " eta=" + Read("interceptTime") + " jump=" + Read("jump") + " heading=" + Read("head") + " plan=" + Read("highBallPlan"));
            planned |= (bool)Read("highBallPlan"); jumpSeen |= (bool)Read("jump");
            if (skills && fanAI)
            {
                var fanSkill = player.GetComponent<PlayerSkills>();
                const BindingFlags IP = BindingFlags.Instance | BindingFlags.NonPublic;
                object glowOwner = typeof(PlayerSkills).GetField("glowOwner", SP).GetValue(null);
                float headUntil = (float)typeof(PlayerSkills).GetField("headUntil", IP).GetValue(fanSkill);
                float headDistance = Vector2.Distance(player.transform.Find("Head").GetComponent<Collider2D>().ClosestPoint(ball.position), ball.position);
                skillHeader |= object.ReferenceEquals(glowOwner, fanSkill) && headUntil < 0f && ball.velocity.x * direction > 10f && headDistance < .65f;
            }
            if ((bool)Read("jump") && Mathf.Abs(ball.position.x - body.position.x) > 2f) distantJump = true;
            error = Mathf.Max(error, JointError(player));
        }
        Check(planned && !distantJump, "live lob plans reachable contact " + player.name + " skills=" + skills + " jump=" + jumpSeen);
        Check(probe.touched || skillHeader, "live lob intercepted by native collision or existing head skill " + player.name + " skills=" + skills + " collision=" + probe.touched + " header=" + skillHeader);
        Check(error < .35f, "lob interception preserves skeleton " + player.name + " error=" + error);
        if (probe.touched || skillHeader) lobTouches++;
        UseAI = false;
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; Application.targetFrameRate = 120; QualitySettings.vSyncCount = 0; AudioListener.volume = 0f;
        yield return new WaitForSeconds(.2f);
        foreach (bool skills in new[] { false, true }) foreach (bool fanAI in new[] { false, true })
        {
            yield return Contest(skills, fanAI, false);
            yield return Contest(skills, fanAI, true);
            yield return Decisions(skills, fanAI);
            yield return Lob(skills, fanAI);
        }
        Debug.Log("AIMATCH COMPLETE checks=" + checks + " lobTouches=" + lobTouches + " failures=" + failures);
        Application.Quit(failures == 0 ? 0 : 1);
    }
    void Update() { if (Time.realtimeSinceStartup > 110f) { Debug.LogError("AIMATCH TIMEOUT"); Application.Quit(2); } }
}
