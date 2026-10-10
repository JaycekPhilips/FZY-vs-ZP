using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

// Compiled only into the isolated test player, never the installed game.
public sealed class BehindGoalTests : MonoBehaviour
{
    static bool booted;
    public static bool CornerOnly;
    int checks, failures;
    Component manager;
    Rigidbody2D ball;
    Collider2D ballCollider;
    Component left, right;
    float originalGravity;
    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Boot()
    {
        if (booted) return;
        booted = true;
        var host = new GameObject("BehindGoalTests");
        DontDestroyOnLoad(host);
        host.AddComponent<BehindGoalTests>();
    }

    void Check(bool ok, string label)
    {
        checks++;
        if (!ok) failures++;
        Debug.Log((ok ? "BOUNDARY PASS " : "BOUNDARY FAIL ") + label);
    }
    bool Flag(string name) { return (bool)manager.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager); }
    int Score() { return (int)manager.GetType().GetField("p1Score").GetValue(manager) + (int)manager.GetType().GetField("p2Score").GetValue(manager); }
    bool IsOut() { return (bool)typeof(GameAIMod).GetField("ballOutThisRally", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null); }
    void Place(Vector2 position, Vector2 velocity, float gravity)
    {
        ball.position = position;
        ball.velocity = velocity;
        ball.angularVelocity = 0;
        ball.gravityScale = gravity;
        Physics2D.SyncTransforms();
    }
    Component Goal(float side) { return side < 0 ? left : right; }
    float Line(float side)
    {
        Bounds mouth = Goal(side).GetComponent<Collider2D>().bounds;
        return side > 0 ? mouth.min.x : mouth.max.x;
    }
    IEnumerator Scene(bool skills)
    {
        Time.timeScale = 1;
        PlayerSkills.Enabled = skills;
        typeof(GameAIMod).GetMethod("StartGame", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.2f);
        Type gameManager = Type.GetType("GameManager, Assembly-CSharp");
        manager = FindObjectOfType(gameManager) as Component;
        Component nativeBall = FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component;
        ball = nativeBall.GetComponent<Rigidbody2D>();
        ballCollider = nativeBall.GetComponent<Collider2D>();
        originalGravity = ball.gravityScale;
        foreach (UnityEngine.Object item in FindObjectsOfType(Type.GetType("GoalTrigger, Assembly-CSharp")))
        {
            Component goal = item as Component;
            if (goal.transform.position.x < 0) left = goal; else right = goal;
        }
        // Keep actual scene geometry and ball physics, isolate it from player hits.
        foreach (string name in new[] { "Fan", "Zhao" })
            foreach (Rigidbody2D limb in GameObject.Find(name).GetComponentsInChildren<Rigidbody2D>()) limb.simulated = false;
        Place(new Vector2(0, 1), Vector2.zero, 0);
        Check(nativeBall.GetComponent<BallBoundaryGuard>() != null, "native Ball.Start attaches guard; skills=" + skills);
        Check(!Flag("isStopping") && !IsOut() && Score() == 0, "fresh rally is clear; skills=" + skills);
    }
    IEnumerator WaitForRestart(string label)
    {
        ball.gravityScale = originalGravity;
        yield return new WaitForSeconds(2.2f);
        if (Flag("isStopping") || Flag("isGoaling") || IsOut())
            Debug.Log("BOUNDARY STATE " + label + " stopping=" + Flag("isStopping") + " goaling=" + Flag("isGoaling") + " out=" + IsOut() + " ball=" + ball.position + " collider=" + ballCollider.bounds);
        Check(!Flag("isStopping") && !Flag("isGoaling") && !IsOut(), label + " clears out/stop state after restart");
        Check(ball.position.x > Line(-1) && ball.position.x < Line(1), label + " ball returns to pitch");
        Check(Score() == 0, label + " restart keeps score unchanged");
    }
    IEnumerator NativeOut(bool skills, float side)
    {
        yield return Scene(skills);
        string label = "native out skills=" + skills + " side=" + side;
        Place(new Vector2(Line(side) - side * .8f, 4), Vector2.right * side * 8, 0);
        yield return new WaitForSeconds(.2f);
        Check(Flag("isStopping") && IsOut() && Score() == 0, label + " actual OutTrigger starts restart");
        yield return WaitForRestart(label);
    }
    IEnumerator Behind(bool skills, float side, bool elevated)
    {
        yield return Scene(skills);
        string label = "behind goal skills=" + skills + " side=" + side + " elevated=" + elevated;
        Place(new Vector2(side * 8.4f, elevated ? 3.5f : -1.35f), elevated ? Vector2.down * 3 : Vector2.zero, originalGravity);
        yield return new WaitForSeconds(.12f);
        Check(Flag("isStopping") && IsOut() && !Flag("isGoaling"), label + " is out instead of trapped");
        Check(Score() == 0, label + " awards no goal");
        // A dead ball bouncing back through the goal must remain dead.
        Place(new Vector2(Line(side) + side * .65f, -.55f), Vector2.left * side * 8, 0);
        yield return new WaitForSeconds(.18f);
        Check(Score() == 0 && !Flag("isGoaling"), label + " physical rebound cannot score");
        yield return WaitForRestart(label);
    }
    IEnumerator ValidGoal(bool skills, float side)
    {
        yield return Scene(skills);
        string label = "valid goal skills=" + skills + " side=" + side;
        Place(new Vector2(Line(side) - side * .9f, -.6f), Vector2.right * side * 8, 0);
        yield return new WaitForSeconds(.3f);
        Check(Score() == 1 && Flag("isGoaling"), label + " native trigger counts once");
        Check(!IsOut() && !Flag("isStopping"), label + " net entry is not mistaken for out");
        ball.gravityScale = originalGravity;
        yield return new WaitForSeconds(2.5f);
        Check(Score() == 1 && !Flag("isGoaling") && !Flag("isStopping") && !IsOut(), label + " native restart completes");
        Check(ball.position.x > Line(-1) && ball.position.x < Line(1), label + " ball returns inside pitch");
    }
    IEnumerator GroundGoal(bool skills, float side)
    {
        yield return Scene(skills);
        Collider2D mouth = Goal(side).GetComponent<Collider2D>();
        float floor = BallBoundaryGuard.FindGoalFloor(mouth);
        float radius = ballCollider.bounds.extents.y;
        string label = "rolling goal skills=" + skills + " side=" + side;
        Place(new Vector2(Line(side) - side * .6f, floor + radius + .01f), Vector2.right * side * 3f, originalGravity);
        ball.angularVelocity = -side * 3f / radius * Mathf.Rad2Deg;
        yield return new WaitForSeconds(.6f);
        Debug.Log("BOUNDARY GROUND " + label + " floor=" + floor + " mouth=" + mouth.bounds + " ball=" + ballCollider.bounds);
        Check(Score() == 1 && Flag("isGoaling"), label + " counts real floor-level entry");
        Check(!IsOut() && !Flag("isStopping"), label + " does not whistle out");
        yield return new WaitForSeconds(2.5f);
        Check(Score() == 1 && !IsOut() && !Flag("isGoaling") && !Flag("isStopping"), label + " scores once and restarts");
    }
    IEnumerator PitchAndPause(bool skills)
    {
        yield return Scene(skills);
        foreach (float side in new[] { -1f, 1f })
        {
            Place(new Vector2(Line(side) - side * .65f, -.6f), Vector2.zero, 0);
            yield return new WaitForSeconds(.1f);
            Check(!IsOut() && !Flag("isStopping") && Score() == 0, "near goal inside pitch stays live skills=" + skills + " side=" + side);
        }
        Time.timeScale = 0;
        Place(new Vector2(8.4f, -1.35f), Vector2.zero, originalGravity);
        ball.GetComponent<BallBoundaryGuard>().GetType().GetMethod("FixedUpdate", PrivateInstance).Invoke(ball.GetComponent<BallBoundaryGuard>(), null);
        Check(!IsOut() && !Flag("isStopping"), "pause suspends new boundary decisions skills=" + skills);
        Time.timeScale = 1;
        yield return new WaitForSeconds(.1f);
        Check(IsOut() && Flag("isStopping"), "resume catches ball behind goal skills=" + skills);
        yield return WaitForRestart("resume skills=" + skills);
    }
    IEnumerator UpperCorner(bool skills, float side, float speed, bool descending)
    {
        yield return Scene(skills);
        Collider2D mouth = Goal(side).GetComponent<Collider2D>();
        float ceiling = BallBoundaryGuard.FindGoalCeiling(mouth);
        float radius = ballCollider.bounds.extents.y;
        float line = Line(side);
        string label = "upper corner skills=" + skills + " side=" + side + " speed=" + speed + " descending=" + descending;
        Debug.Log("BOUNDARY CEILING " + label + " physical=" + ceiling + " trigger=" + mouth.bounds.max.y);
        Check(ceiling > BallBoundaryGuard.FindGoalFloor(mouth) + radius * 2f && ceiling <= mouth.bounds.max.y + .1f, label + " measures physical bar in current edition");
        float y = ceiling - radius - .10f;
        float vy = descending ? -1f : 0f;
        float distance = .9f;
        float travel = (distance + radius) / speed;
        Place(new Vector2(line - side * distance, y - vy * travel), new Vector2(side * speed, vy), 0f);
        yield return new WaitForSeconds(.35f);
        Debug.Log("BOUNDARY CORNER " + label + " ball=" + ball.position + " velocity=" + ball.velocity + " score=" + Score() + " out=" + IsOut());
        Check(Score() == 1 && Flag("isGoaling"), label + " real flight scores exactly once");
        Check(!IsOut() && !Flag("isStopping"), label + " is not mistaken for out");
        yield return new WaitForSeconds(2.5f);
        Check(Score() == 1 && !Flag("isGoaling") && !IsOut(), label + " restarts without duplicate score");
    }
    IEnumerator CornerDescent(bool skills, float side)
    {
        yield return Scene(skills);
        Collider2D mouth = Goal(side).GetComponent<Collider2D>();
        float ceiling = BallBoundaryGuard.FindGoalCeiling(mouth), radius = ballCollider.bounds.extents.y;
        string label = "descending upper corner skills=" + skills + " side=" + side;
        Place(new Vector2(Line(side) - side * (radius + .05f), ceiling - radius + .24f), new Vector2(side * 5f, -8f), 0f);
        yield return new WaitForSeconds(.3f);
        Debug.Log("BOUNDARY DESCENT " + label + " ball=" + ball.position + " score=" + Score() + " out=" + IsOut());
        Check(Score() == 1 && Flag("isGoaling"), label + " initial high overlap can descend into net");
        Check(!IsOut() && !Flag("isStopping"), label + " is live until valid entry");
        yield return new WaitForSeconds(2.5f);
        Check(Score() == 1 && !Flag("isGoaling") && !IsOut(), label + " restarts and scores only once");
    }
    IEnumerator Run()
    {
        yield return new WaitForSeconds(.2f);
        if (CornerOnly)
        {
            foreach (bool skills in new[] { false, true }) foreach (float side in new[] { -1f, 1f })
            {
                yield return UpperCorner(skills, side, 8f, false);
                yield return UpperCorner(skills, side, 22f, true);
                yield return UpperCorner(skills, side, 45f, false);
                yield return CornerDescent(skills, side);
            }
            Debug.Log("BOUNDARY COMPLETE checks=" + checks + " failures=" + failures);
            Application.Quit(failures == 0 ? 0 : 1); yield break;
        }
        foreach (bool skills in new[] { false, true })
        {
            yield return PitchAndPause(skills);
            foreach (float side in new[] { -1f, 1f })
            {
                yield return NativeOut(skills, side);
                yield return Behind(skills, side, false);
                yield return Behind(skills, side, true);
                yield return ValidGoal(skills, side);
                yield return GroundGoal(skills, side);
                yield return UpperCorner(skills, side, 8f, false);
                yield return UpperCorner(skills, side, 22f, true);
                yield return UpperCorner(skills, side, 45f, false);
                yield return CornerDescent(skills, side);
            }
        }
        Debug.Log("BOUNDARY COMPLETE checks=" + checks + " failures=" + failures);
        Application.Quit(failures == 0 ? 0 : 1);
    }
    IEnumerator Start()
    {
        Application.runInBackground = true;
        Application.targetFrameRate = 120;
        QualitySettings.vSyncCount = 0;
        AudioListener.volume = 0;
        IEnumerator tests = Run();
        // Unity drives nested iterators; a watchdog handles any unexpected fault.
        yield return tests;
    }
    void Update()
    {
        if (Time.realtimeSinceStartup > 140) { Debug.LogError("BOUNDARY TIMEOUT"); Application.Quit(2); }
    }
}
