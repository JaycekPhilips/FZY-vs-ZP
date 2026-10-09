using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class ScaleShotProbe : MonoBehaviour
{
    public string shooter;
    public bool touched;
    public Vector2 velocity;
    void OnCollisionEnter2D(Collision2D c) { Observe(c); }
    void OnCollisionStay2D(Collision2D c) { Observe(c); }
    void Observe(Collision2D c)
    {
        if (c.collider.transform.root.name != shooter) return;
        Vector2 current = GetComponent<Rigidbody2D>().velocity;
        if (current.sqrMagnitude > velocity.sqrMagnitude) velocity = current;
        touched = true;
    }
}
public sealed class ScaleTests : MonoBehaviour
{
    static bool booted;
    public static int DownFrame = -1;
    public static HashSet<KeyCode> Down = new HashSet<KeyCode>();
    public static bool ReadKey(KeyCode key) { return false; }
    public static bool ReadDown(KeyCode key) { return Time.frameCount == DownFrame && Down.Contains(key); }
    int checks, failures;
    Component fan, zhao, manager;
    Rigidbody2D ball;
    Type pt;
    float[] baselineHeights;
    Vector3[] baselineScales;
    Bounds[] baselineGoals;
    public static void Boot() { if (booted) return; booted = true; var host = new GameObject("ScaleTests"); DontDestroyOnLoad(host); host.AddComponent<ScaleTests>(); }
    void Check(bool ok, string label) { checks++; if (!ok) failures++; Debug.Log((ok ? "SCALE PASS " : "SCALE FAIL ") + label); }
    Rigidbody2D Body(Component p) { return (Rigidbody2D)pt.GetField("rb").GetValue(p); }
    float Error(Component p) { float max = 0; foreach (HingeJoint2D j in p.GetComponentsInChildren<HingeJoint2D>()) if (j.connectedBody != null) max = Mathf.Max(max, Vector2.Distance(j.transform.TransformPoint(j.anchor), j.connectedBody.transform.TransformPoint(j.connectedAnchor))); return max; }
    Bounds Shape(Component p) { Bounds b = p.GetComponentsInChildren<Collider2D>()[0].bounds; foreach (Collider2D c in p.GetComponentsInChildren<Collider2D>()) if (!c.isTrigger) b.Encapsulate(c.bounds); return b; }
    Bounds[] Goals() { var list = new List<Bounds>(); foreach (UnityEngine.Object o in FindObjectsOfType(Type.GetType("GoalTrigger, Assembly-CSharp"))) list.Add(((Component)o).GetComponent<Collider2D>().bounds); list.Sort(delegate(Bounds a, Bounds b) { return a.center.x.CompareTo(b.center.x); }); return list.ToArray(); }
    IEnumerator Scene(bool scaled, bool skills, int mode = 0)
    {
        ExperimentScale.Enabled = scaled; PlayerSkills.Enabled = skills; DownFrame = -1; Down.Clear(); Time.timeScale = 1;
        typeof(GameAIMod).GetMethod("StartGame", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { mode });
        yield return new WaitForSeconds(.3f);
        pt = Type.GetType("PlayerController, Assembly-CSharp");
        fan = GameObject.Find("Fan").GetComponent(pt); zhao = GameObject.Find("Zhao").GetComponent(pt);
        manager = FindObjectOfType(Type.GetType("GameManager, Assembly-CSharp")) as Component;
        ball = ((Component)FindObjectOfType(Type.GetType("Ball, Assembly-CSharp"))).GetComponent<Rigidbody2D>();
        ball.position = new Vector2(0, 2); ball.velocity = Vector2.zero; ball.gravityScale = 0;
        Physics2D.SyncTransforms();
    }
    IEnumerator Geometry()
    {
        yield return Scene(false, false);
        baselineHeights = new[] { Shape(fan).size.y, Shape(zhao).size.y, ball.GetComponent<Collider2D>().bounds.size.y };
        baselineScales = new[] { fan.transform.localScale, zhao.transform.localScale, ball.transform.localScale };
        baselineGoals = Goals();
        foreach (bool skills in new[] { false, true }) foreach (int mode in new[] { 0, 1, 2 })
        {
            yield return Scene(true, skills, mode);
            Component[] actors = { fan, zhao, ball };
            for (int i = 0; i < 3; i++)
            {
                Check(Vector3.Distance(actors[i].transform.localScale, baselineScales[i] * .75f) < .001f, "uniform size 75% " + actors[i].name + " skills=" + skills + " mode=" + mode);
                ExperimentActor marker = actors[i].GetComponent<ExperimentActor>();
                Rigidbody2D rb = i == 2 ? ball : Body(actors[i]);
                Check(Mathf.Abs(rb.mass - marker.OriginalMass) < .001f, "mass preserved " + actors[i].name);
                if (i == 2) Check(Mathf.Abs(actors[i].GetComponent<Collider2D>().bounds.size.y / baselineHeights[i] - .75f) < .001f, "ball collision size follows model");
                else Check(Error(actors[i]) < .2f, "scaled joints remain connected " + actors[i].name);
            }
            Bounds[] goals = Goals();
            bool correctGoals = true;
            for (int g = 0; g < 2; g++) correctGoals &= Mathf.Abs(goals[g].size.y / baselineGoals[g].size.y - .8f) < .001f && Mathf.Abs(goals[g].min.x - baselineGoals[g].min.x) < .001f && Mathf.Abs(goals[g].max.x - baselineGoals[g].max.x) < .001f;
            Check(correctGoals, "goal height 80%, width and lines unchanged");
            Vector3 scale = fan.transform.localScale;
            fan.GetComponent(Type.GetType("StickManController, Assembly-CSharp")).GetType().GetMethod("Reset").Invoke(fan.GetComponent(Type.GetType("StickManController, Assembly-CSharp")), null);
            Check(fan.transform.localScale == scale && Error(fan) < .2f, "native reset retains scaled skeleton");
        }
    }
    IEnumerator Jump(bool scaled, bool skills, float[] output)
    {
        yield return Scene(scaled, skills);
        Move(fan, -2.5f); Move(zhao, 2.5f);
        yield return new WaitForSeconds(.7f);
        float[] start = { Body(fan).position.y, Body(zhao).position.y };
        Down.Add(KeyCode.W); Down.Add(KeyCode.UpArrow); DownFrame = Time.frameCount + 1;
        float error = 0;
        for (int i = 0; i < 115; i++)
        {
            yield return new WaitForFixedUpdate();
            output[0] = Mathf.Max(output[0], Body(fan).position.y - start[0]);
            output[1] = Mathf.Max(output[1], Body(zhao).position.y - start[1]);
            error = Mathf.Max(error, Error(fan), Error(zhao));
        }
        Check(output[0] > .3f && output[1] > .3f, "real jump occurs scaled=" + scaled + " skills=" + skills);
        Check(error < .25f, "jump keeps scaled model connected error=" + error);
    }
    IEnumerator Jumps()
    {
        foreach (bool skills in new[] { false, true })
        {
            float[] original = new float[2], scaled = new float[2];
            yield return Jump(false, skills, original); yield return Jump(true, skills, scaled);
            for (int i = 0; i < 2; i++) Check(Mathf.Abs(original[i] - scaled[i]) < Mathf.Max(.1f, original[i] * .05f), "jump height preserved player=" + i + " skills=" + skills + " original=" + original[i] + " scaled=" + scaled[i]);
        }
    }
    void Move(Component p, float x) { p.transform.position += Vector3.right * (x - Body(p).position.x); foreach (Rigidbody2D rb in p.GetComponentsInChildren<Rigidbody2D>()) { rb.velocity = Vector2.zero; rb.angularVelocity = 0; } Physics2D.SyncTransforms(); }
    IEnumerator Shot(bool scaled, string name, Vector2[] output, int index)
    {
        yield return Scene(scaled, false);
        Component p = name == "Fan" ? fan : zhao;
        Animator animator = p.GetComponent<Animator>();
        // Compare contact at the same physical animation step. Rendering frame
        // jitter must not select different phases of the two kicking motions.
        animator.updateMode = AnimatorUpdateMode.AnimatePhysics;
        Move(p, name == "Fan" ? -2 : 2);
        yield return new WaitForSeconds(.5f);
        Down.Add(name == "Fan" ? KeyCode.K : KeyCode.KeypadEnter); DownFrame = Time.frameCount + 1;
        for (int i = 0; i < 25; i++) { yield return new WaitForFixedUpdate(); if (animator.GetCurrentAnimatorStateInfo(0).IsName("Kick") && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= .2f) break; }
        float direction = name == "Fan" ? 1 : -1;
        Vector2 bestPosition = Vector2.zero; float bestSpeed = float.NegativeInfinity;
        foreach (string leg in new[] { "L_LowLeg", "R_LowLeg" })
        {
            BoxCollider2D foot = p.transform.Find(leg).GetComponent<BoxCollider2D>();
            foreach (float side in new[] { -1f, 1f })
            {
                Vector2 normal = foot.transform.TransformVector(Vector2.right * side).normalized;
                if (direction * normal.x < .1f) continue;
                Vector2 contact = foot.transform.TransformPoint(foot.offset + new Vector2(foot.size.x * side * .5f, foot.size.y * .1f));
                Vector2 candidate = contact + normal * ball.GetComponent<Collider2D>().bounds.extents.x * .85f;
                float speed = foot.attachedRigidbody.GetPointVelocity(contact).x * direction;
                if (speed > bestSpeed) { bestSpeed = speed; bestPosition = candidate; }
            }
        }
        ball.position = bestPosition;
        ball.velocity = Vector2.zero; Physics2D.SyncTransforms();
        ScaleShotProbe probe = ball.gameObject.AddComponent<ScaleShotProbe>(); probe.shooter = name;
        float error = 0;
        for (int i = 0; i < 30; i++) { yield return new WaitForFixedUpdate(); error = Mathf.Max(error, Error(p)); }
        output[index] = probe.velocity;
        Check(probe.touched && probe.velocity.x * direction > 1f, "actual forward ordinary foot strike " + name + " scaled=" + scaled + " velocity=" + probe.velocity + " footSpeed=" + bestSpeed);
        // Allow a small floating point margin at the existing joint tolerance.
        Check(error < (scaled ? .2501f : .3501f), "ordinary strike keeps joints connected " + name + " error=" + error);
    }
    IEnumerator Shots()
    {
        foreach (string name in new[] { "Fan", "Zhao" })
        {
            Vector2[] output = new Vector2[2];
            yield return Shot(false, name, output, 0); yield return Shot(true, name, output, 1);
            Check(Vector2.Angle(output[0],output[1]) < 12f, "ordinary shot direction preserved " + name + " angle=" + Vector2.Angle(output[0],output[1]));
            Check(Mathf.Abs(output[1].magnitude / output[0].magnitude - 1f) < .15f, "ordinary shot speed preserved " + name + " ratio=" + output[1].magnitude / output[0].magnitude);
        }
    }
    IEnumerator Curve()
    {
        float[] arcs = new float[2];
        for (int i = 0; i < 2; i++)
        {
            yield return Scene(i == 1, true);
            ball.position = new Vector2(-3, 1); ball.velocity = new Vector2(8, 0); Physics2D.SyncTransforms();
            Component goal = null; foreach (UnityEngine.Object o in FindObjectsOfType(Type.GetType("GoalTrigger, Assembly-CSharp"))) if (((Component)o).transform.position.x > 0) goal = (Component)o;
            PlayerSkills skills = fan.GetComponent<PlayerSkills>();
            typeof(PlayerSkills).GetMethod("PrepareGoalCurve", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(skills, new object[] { goal.GetComponent<Collider2D>() });
            arcs[i] = (float)typeof(PlayerSkills).GetField("curveArcHeight", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(skills);
        }
        Check(Mathf.Abs(arcs[1] / arcs[0] - .9f) < .002f, "leaf shot arc slightly lower ratio=" + arcs[1] / arcs[0]);
        yield return Scene(true, true);
        // Render directly so a hidden isolated test window still produces a
        // useful scene image instead of a black minimized-window capture.
        Camera camera = Camera.main;
        RenderTexture target = new RenderTexture(1280, 720, 24);
        RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
        camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
        Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"..","..","scale-experiment.png")), image.EncodeToPNG());
        camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
        target.Release(); Destroy(target); Destroy(image);
        yield return new WaitForSeconds(.2f);
    }
    IEnumerator Start()
    {
        Application.runInBackground = true; Application.targetFrameRate = 120; QualitySettings.vSyncCount = 0; AudioListener.volume = 0;
        yield return new WaitForSeconds(.2f);
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-scale-shots-only") >= 0) yield return Shots();
        else { yield return Geometry(); yield return Jumps(); yield return Shots(); yield return Curve(); }
        Debug.Log("SCALE COMPLETE checks=" + checks + " failures=" + failures); Application.Quit(failures == 0 ? 0 : 1);
    }
    void Update() { if (Time.realtimeSinceStartup > 120) { Debug.LogError("SCALE TIMEOUT"); Application.Quit(2); } }
}
