using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
public sealed class GoalBarProbe : MonoBehaviour
{
    public bool touched;
    void OnCollisionEnter2D(Collision2D collision) { if (collision.collider.GetComponentInParent<ExperimentGoal>() != null) touched = true; }
}
public sealed class GoalHeightTests : MonoBehaviour
{
    static bool booted;
    int checks, failures;
    Component manager;
    Rigidbody2D ball;
    readonly Dictionary<string, Bounds> original = new Dictionary<string, Bounds>();
    readonly Dictionary<string, float> floors = new Dictionary<string, float>();
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Boot() { if (booted) return; booted = true; var host = new GameObject("GoalHeightTests"); DontDestroyOnLoad(host); host.AddComponent<GoalHeightTests>(); }
    string Path(Transform t) { return t.parent == null ? t.name : Path(t.parent) + "/" + t.name; }
    void Check(bool ok, string label) { checks++; if (!ok) failures++; Debug.Log((ok ? "GOALHEIGHT PASS " : "GOALHEIGHT FAIL ") + label); }
    bool Near(float a, float b) { return Mathf.Abs(a - b) < .002f; }
    int Score() { return (int)manager.GetType().GetField("p1Score").GetValue(manager) + (int)manager.GetType().GetField("p2Score").GetValue(manager); }
    bool Out() { return (bool)typeof(GameAIMod).GetField("ballOutThisRally", PrivateStatic).GetValue(null); }
    Component Goal(float side) { foreach (UnityEngine.Object o in FindObjectsOfType(Type.GetType("GoalTrigger, Assembly-CSharp"))) if (Mathf.Sign(((Component)o).transform.position.x) == side) return (Component)o; return null; }
    Dictionary<string, Bounds> Shapes()
    {
        var data = new Dictionary<string, Bounds>();
        foreach (Collider2D c in GameObject.Find("Scene").GetComponentsInChildren<Collider2D>()) data["collider:" + Path(c.transform)] = c.bounds;
        foreach (SpriteRenderer r in GameObject.Find("Scene").GetComponentsInChildren<SpriteRenderer>()) data["sprite:" + Path(r.transform)] = r.bounds;
        return data;
    }
    IEnumerator Scene(bool scaled, bool skills, int mode)
    {
        Time.timeScale = 1; ExperimentScale.Enabled = scaled; PlayerSkills.Enabled = skills;
        typeof(GameAIMod).GetMethod("StartGame", PrivateStatic).Invoke(null, new object[] { mode });
        yield return new WaitForSeconds(.25f);
        manager = FindObjectOfType(Type.GetType("GameManager, Assembly-CSharp")) as Component;
        ball = (FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        ball.position = new Vector2(0, 2); ball.velocity = Vector2.zero; ball.gravityScale = 0;
        Physics2D.SyncTransforms();
    }
    void FreezePlayers()
    {
        foreach (string name in new[] { "Fan", "Zhao" }) foreach (Rigidbody2D body in GameObject.Find(name).GetComponentsInChildren<Rigidbody2D>()) body.simulated = false;
    }
    IEnumerator Geometry()
    {
        yield return Scene(false, false, 0);
        foreach (var entry in Shapes()) original[entry.Key] = entry.Value;
        foreach (float side in new[] { -1f, 1f }) { Component g = Goal(side); floors[g.transform.parent.name] = BallBoundaryGuard.FindGoalFloor(g.GetComponent<Collider2D>()); }
        foreach (bool skills in new[] { false, true }) foreach (int mode in new[] { 0, 1, 2 })
        {
            yield return Scene(true, skills, mode);
            foreach (var entry in Shapes())
            {
                Bounds before = original[entry.Key], after = entry.Value;
                string root = entry.Key.Contains("/Player1Goal/") ? "Player1Goal" : entry.Key.Contains("/Player2Goal/") ? "Player2Goal" : null;
                if (root == null) Check(before == after, "field geometry unchanged " + entry.Key + " mode=" + mode + " skills=" + skills);
                else
                {
                    float floor = floors[root];
                    Check(Near(after.min.y, floor + (before.min.y - floor) * .8f) && Near(after.max.y, floor + (before.max.y - floor) * .8f) && Near(after.min.x, before.min.x) && Near(after.max.x, before.max.x) && Near(after.center.z, before.center.z), "goal-only vertical resize " + entry.Key + " mode=" + mode + " skills=" + skills);
                }
            }
            foreach (float side in new[] { -1f, 1f })
            {
                Component g = Goal(side); Collider2D c = g.GetComponent<Collider2D>();
                float floor = BallBoundaryGuard.FindGoalFloor(c);
                Bounds before = original["collider:" + Path(g.transform)];
                Check(Near(floor, floors[g.transform.parent.name]) && Near((c.bounds.max.y - floor) / (before.max.y - floor), .8f), "actual goal opening 80%, floor fixed side=" + side);
                Vector3 scale = g.transform.parent.localScale; Bounds opening = c.bounds;
                Component nativeBall = ball.GetComponent(Type.GetType("Ball, Assembly-CSharp"));
                ExperimentScale.ScaleGoals(nativeBall); ExperimentScale.ScaleGoals(nativeBall);
                Check(scale == g.transform.parent.localScale && opening == c.bounds, "reset cannot shrink goal repeatedly side=" + side);
            }
        }
    }
    IEnumerator AboveNewBar(bool skills, float side)
    {
        yield return Scene(true, skills, 0); FreezePlayers();
        Component goal = Goal(side); Bounds mouth = goal.GetComponent<Collider2D>().bounds;
        float oldTop = original["collider:" + Path(goal.transform)].max.y;
        // Stay wholly inside the old opening but above the lowered solid bar.
        // A ball clipping the real bar can legitimately rebound onto the pitch.
        float y = oldTop - ball.GetComponent<Collider2D>().bounds.extents.y - .01f;
        ball.position = new Vector2(side > 0 ? mouth.min.x - .6f : mouth.max.x + .6f, y);
        ball.velocity = Vector2.right * side * 8f; Physics2D.SyncTransforms();
        Check(!GameAIMod.CanScoreGoal(goal, ball.GetComponent<Collider2D>()), "old goal area above new bar rejected side=" + side + " skills=" + skills);
        yield return new WaitForSeconds(.45f);
        if (Score() != 0 || !Out()) Debug.Log("GOALHEIGHT STATE above bar ball=" + ball.position + " velocity=" + ball.velocity + " score=" + Score() + " out=" + Out() + " top=" + mouth.max.y + " launch=" + y);
        Check(Score() == 0 && Out(), "above new bar is out, not goal side=" + side + " skills=" + skills);
    }
    IEnumerator BarCollision(bool skills, float side)
    {
        yield return Scene(true, skills, 0); FreezePlayers(); ball.GetComponent<BallBoundaryGuard>().enabled = false;
        Transform root = Goal(side).transform.parent;
        RaycastHit2D roof = new RaycastHit2D();
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(new Vector2(root.position.x, 10), Vector2.down, 20)) if (!hit.collider.isTrigger && hit.collider.GetComponentInParent<ExperimentGoal>() != null) { roof = hit; break; }
        Check(roof.collider != null, "lowered roof has actual collider side=" + side);
        if (roof.collider == null) yield break;
        float radius = ball.GetComponent<Collider2D>().bounds.extents.x;
        ball.position = roof.point + roof.normal * (radius + .05f); ball.velocity = -roof.normal * 3f;
        GoalBarProbe probe = ball.gameObject.AddComponent<GoalBarProbe>(); Physics2D.SyncTransforms();
        yield return new WaitForSeconds(.12f);
        Check(probe.touched && Score() == 0, "ball really collides with lowered bar side=" + side + " skills=" + skills);
    }
    IEnumerator Curve()
    {
        foreach (float x in new[] { -3f, 0f })
        {
            yield return Scene(true, true, 0); FreezePlayers();
            ball.position = new Vector2(x, -.6f); ball.velocity = new Vector2(8, 0); ball.gravityScale = 1; Physics2D.SyncTransforms();
            PlayerSkills skill = GameObject.Find("Fan").GetComponent<PlayerSkills>(); Collider2D goal = Goal(1).GetComponent<Collider2D>();
            typeof(PlayerSkills).GetMethod("PrepareGoalCurve", PrivateInstance).Invoke(skill, new object[] { goal });
            Vector2 target = (Vector2)typeof(PlayerSkills).GetField("curveDestination", PrivateInstance).GetValue(skill);
            Check(target.y + ball.GetComponent<Collider2D>().bounds.extents.y < goal.bounds.max.y, "leaf shot aims below new bar x=" + x);
            typeof(PlayerSkills).GetField("curveStarted", PrivateInstance).SetValue(skill, Time.time);
            typeof(PlayerSkills).GetField("shotUntil", PrivateInstance).SetValue(skill, Time.time + 2f);
            yield return new WaitForSeconds(1.1f);
            Check(Score() == 1 && !Out(), "leaf shot flies into lowered goal x=" + x);
        }
    }
    IEnumerator Capture()
    {
        yield return Scene(true, true, 0);
        Camera camera = Camera.main; RenderTexture render = new RenderTexture(1280, 720, 24);
        RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
        camera.targetTexture = render; camera.Render(); RenderTexture.active = render;
        Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "scale-lowered-goals.png")), image.EncodeToPNG());
        camera.targetTexture = previousTarget; RenderTexture.active = previousActive; render.Release(); Destroy(render); Destroy(image);
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; Application.targetFrameRate = 120; QualitySettings.vSyncCount = 0; AudioListener.volume = 0;
        yield return new WaitForSeconds(.2f); yield return Geometry();
        foreach (bool skills in new[] { false, true }) foreach (float side in new[] { -1f, 1f }) { yield return AboveNewBar(skills, side); yield return BarCollision(skills, side); }
        yield return Curve(); yield return Capture();
        Debug.Log("GOALHEIGHT COMPLETE checks=" + checks + " failures=" + failures); Application.Quit(failures == 0 ? 0 : 1);
    }
    void Update() { if (Time.realtimeSinceStartup > 80) { Debug.LogError("GOALHEIGHT TIMEOUT"); Application.Quit(2); } }
}
