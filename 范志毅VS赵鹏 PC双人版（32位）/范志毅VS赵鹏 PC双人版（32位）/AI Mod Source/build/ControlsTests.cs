using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public sealed class HeaderTouchProbe : MonoBehaviour
{
    public bool touched;
    void OnCollisionEnter2D(Collision2D collision)
    { if (collision.collider != null && collision.collider.name == "Head" && collision.collider.transform.root.name == "Zhao") touched = true; }
}

public sealed class ControlsTests : MonoBehaviour
{
    public static readonly HashSet<KeyCode> Held = new HashSet<KeyCode>();
    public static int DownFrame = -1;
    public static KeyCode DownKey;
    static bool booted;
    int checks, failures;
    Component fan, zhao;
    Rigidbody2D ball;
    const BindingFlags SP = BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags IP = BindingFlags.NonPublic | BindingFlags.Instance;
    public static bool ReadKey(KeyCode key) { return Held.Contains(key); }
    public static bool ReadDown(KeyCode key) { return DownFrame == Time.frameCount && DownKey == key; }
    public static void Boot()
    {
        if (booted) return; booted = true;
        ControlBindings.settingsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "controls-test-bindings.ini"));
        if (File.Exists(ControlBindings.SettingsPath)) File.Delete(ControlBindings.SettingsPath);
        ControlBindings.Reload();
        var obj = new GameObject("ControlsTests"); DontDestroyOnLoad(obj); obj.AddComponent<ControlsTests>();
    }
    void Check(bool ok, string label) { checks++; if (!ok) failures++; Debug.Log((ok ? "CONTROLS PASS " : "CONTROLS FAIL ") + label); }
    IEnumerator Scene(bool skills)
    {
        Held.Clear(); DownFrame = -1; ControlBindings.ResetDefaults(); PlayerSkills.Enabled = skills; Time.timeScale = 1;
        typeof(GameAIMod).GetMethod("StartGame", SP).Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.2f);
        Type pt = Type.GetType("PlayerController, Assembly-CSharp");
        fan = GameObject.Find("Fan").GetComponent(pt); zhao = GameObject.Find("Zhao").GetComponent(pt);
        ball = (FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();
        ball.position = new Vector2(0, 2); ball.velocity = Vector2.zero; ball.gravityScale = 0;
    }
    IEnumerator UI()
    {
        Component main = FindObjectOfType(Type.GetType("MainPanel, Assembly-CSharp")) as Component;
        Button start = null; foreach (Button button in main.GetComponentsInChildren<Button>(true)) if (button.name == "btn_Start") start = button;
        start.onClick.Invoke();
        yield return null;
        Button settings = null; foreach (Button button in main.GetComponentInParent<Canvas>().GetComponentsInChildren<Button>(true)) if (button.name == "mode_键位设置") settings = button;
        Check(settings != null, "start menu offers binding settings");
        settings.onClick.Invoke(); yield return new WaitForSeconds(.1f);
        ControlsSettingsPanel panel = FindObjectOfType<ControlsSettingsPanel>();
        Check(panel != null, "menu button opens settings overlay");
        Button zhaoHead = null; int bindings = 0;
        foreach (Button button in panel.GetComponentsInChildren<Button>()) { if (button.name.StartsWith("bind_")) bindings++; if (button.name == "bind_Zhao_Head") zhaoHead = button; }
        Check(bindings == 14 && zhaoHead.GetComponentInChildren<Text>().text.Contains("右 Shift"), "all fourteen default slots include Zhao right Shift header");
        foreach (Vector2 size in new[] { new Vector2(1280, 720), new Vector2(800, 600) })
        {
            Screen.SetResolution((int)size.x, (int)size.y + 1, false);
            yield return new WaitForSeconds(.15f);
            Screen.SetResolution((int)size.x, (int)size.y, false);
            yield return new WaitForSeconds(.75f);
            bool fits = true;
            foreach (Button button in panel.GetComponentsInChildren<Button>())
            {
                Vector3[] corners = new Vector3[4]; button.GetComponent<RectTransform>().GetWorldCorners(corners);
                foreach (Vector3 corner in corners) { Vector2 point = RectTransformUtility.WorldToScreenPoint(null, corner); if (point.x < 0 || point.y < 0 || point.x > Screen.width || point.y > Screen.height) fits = false; }
            }
            Check(fits, "settings buttons remain inside viewport " + size);
            string screenshot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "controls-" + (int)size.x + ".png"));
            ScreenCapture.CaptureScreenshot(screenshot);
            yield return new WaitForSeconds(.35f);
            Check(File.Exists(screenshot), "settings screenshot captured " + size.x);
        }
        zhaoHead.onClick.Invoke();
        panel.GetType().GetMethod("HandleKey", IP).Invoke(panel, new object[] { KeyCode.J });
        Check(ControlBindings.Get(false, GameControlAction.Head) == KeyCode.RightShift, "UI rejects another player's occupied key");
        panel.GetType().GetMethod("HandleKey", IP).Invoke(panel, new object[] { KeyCode.Keypad5 });
        Check(ControlBindings.Get(false, GameControlAction.Head) == KeyCode.Keypad5 && zhaoHead.GetComponentInChildren<Text>().text.Contains("5"), "UI captures and displays new key");
        ControlBindings.Reload(); Check(ControlBindings.Get(false, GameControlAction.Head) == KeyCode.Keypad5, "UI binding persists after reload");
        foreach (Button button in panel.GetComponentsInChildren<Button>()) if (button.GetComponentInChildren<Text>().text == "恢复默认") button.onClick.Invoke();
        Check(ControlBindings.Get(false, GameControlAction.Head) == KeyCode.RightShift && ControlBindings.Get(true, GameControlAction.Kick) == KeyCode.K, "UI restores current defaults");
        foreach (Button button in panel.GetComponentsInChildren<Button>()) if (button.GetComponentInChildren<Text>().text == "完成") { button.onClick.Invoke(); break; }
        yield return null; Check(FindObjectOfType<ControlsSettingsPanel>() == null, "done closes overlay");
    }
    void Mapping()
    {
        KeyCode[,] expected = { { KeyCode.A, KeyCode.D, KeyCode.W, KeyCode.S, KeyCode.K, KeyCode.J, KeyCode.L }, { KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.KeypadEnter, KeyCode.RightShift, KeyCode.Keypad0 } };
        for (int p = 0; p < 2; p++) for (int a = 0; a < 7; a++) Check(ControlBindings.Get(p == 0, (GameControlAction)a) == expected[p, a], "default binding p=" + p + " action=" + a);
        ControlBindings.Set(true, GameControlAction.Left, KeyCode.Q); ControlBindings.Set(true, GameControlAction.Right, KeyCode.E);
        Held.Add(KeyCode.Q); Check(GameAIMod.GetAxis("Horizontal", fan) == -1, "custom left drives native movement");
        Held.Add(KeyCode.E); Check(GameAIMod.GetAxis("Horizontal", fan) == 0, "opposite movement keys cancel");
        Held.Remove(KeyCode.Q); Check(GameAIMod.GetAxis("Horizontal", fan) == 1, "custom right drives native movement"); Held.Clear();
        ControlBindings.Set(true, GameControlAction.Kick, KeyCode.F); ControlBindings.Set(true, GameControlAction.Down, KeyCode.X);
        DownFrame = Time.frameCount; DownKey = KeyCode.F; Check(GameAIMod.GetButtonDown(KeyCode.K, fan), "custom shooting key reaches native Kick slot");
        DownKey = KeyCode.K; Check(!GameAIMod.GetButtonDown(KeyCode.K, fan), "old shooting key stops after rebinding");
        DownKey = KeyCode.X; Check(GameAIMod.GetDownCommand(fan), "custom down reaches Fan quick drop");
        ControlBindings.Set(false, GameControlAction.Jump, KeyCode.Keypad8); DownKey = KeyCode.Keypad8;
        Check(GameAIMod.GetButtonDown(KeyCode.UpArrow, zhao), "custom jump reaches native jump slot");
        ControlBindings.Save(); ControlBindings.Reload(); Check(ControlBindings.Get(true, GameControlAction.Kick) == KeyCode.F && ControlBindings.Get(false, GameControlAction.Jump) == KeyCode.Keypad8, "both players' changes persist");
        Check(!ControlBindings.Set(false, GameControlAction.Head, KeyCode.F), "cross-player key conflicts rejected");
        DownFrame = -1; ControlBindings.ResetDefaults();
    }
    IEnumerator Header(bool skills, bool remapped)
    {
        yield return Scene(skills);
        object input = zhao.GetType().GetField("input").GetValue(zhao);
        Check((KeyCode)input.GetType().GetField("head").GetValue(input) == KeyCode.Keypad0, "native Zhao head slot assigned skills=" + skills);
        Animator animator = zhao.GetComponent<Animator>();
        string clipNames = ""; foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips) clipNames += clip.name + ",";
        Debug.Log("CONTROLS CLIPS Zhao " + clipNames);
        if (remapped) ControlBindings.Set(false, GameControlAction.Head, KeyCode.Keypad5);
        Transform head = zhao.transform.Find("Head"); Collider2D hc = head.GetComponent<Collider2D>();
        ball.position = (Vector2)hc.bounds.center + Vector2.left * (hc.bounds.extents.x + ball.GetComponent<Collider2D>().bounds.extents.x + .04f);
        ball.velocity = Vector2.zero; Physics2D.SyncTransforms();
        HeaderTouchProbe probe = ball.gameObject.AddComponent<HeaderTouchProbe>();
        DownKey = remapped ? KeyCode.Keypad5 : KeyCode.RightShift; DownFrame = Time.frameCount + 1;
        bool headAnimation = false; float maxError = 0;
        Component zhaoStick = zhao.GetComponent(Type.GetType("StickManController, Assembly-CSharp"));
        FieldInfo headField = zhaoStick.GetType().GetField("head");
        float idleTarget = (float)headField.GetValue(zhaoStick);
        SpriteRenderer[] sprites = zhao.GetComponentsInChildren<SpriteRenderer>();
        Vector3[] scales = new Vector3[sprites.Length]; bool[] flips = new bool[sprites.Length];
        for (int s = 0; s < sprites.Length; s++) { scales[s] = sprites[s].transform.localScale; flips[s] = sprites[s].flipX; }
        bool orientationStable = true;
        float minTarget = 1000, maxTarget = -1000, maxSwing = 0, strongestShot = 0;
        float startRotation = head.GetComponent<Rigidbody2D>().rotation;
        for (int i = 0; i < 22; i++)
        {
            yield return null;
            for (int s = 0; s < sprites.Length; s++)
                if (sprites[s].name.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0 || sprites[s].name.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0)
                    orientationStable &= Vector3.Distance(sprites[s].transform.localScale, scales[s]) < .001f && sprites[s].flipX == flips[s];
            float target = (float)headField.GetValue(zhaoStick);
            minTarget = Mathf.Min(minTarget, target); maxTarget = Mathf.Max(maxTarget, target);
            maxSwing = Mathf.Max(maxSwing, Mathf.Abs(Mathf.DeltaAngle(startRotation, head.GetComponent<Rigidbody2D>().rotation)));
            strongestShot = Mathf.Min(strongestShot, ball.velocity.x);
            if (i % 3 == 0) Debug.Log("HEADER LIVE frame=" + i + " head=" + target + " body=" + zhaoStick.GetType().GetField("body").GetValue(zhaoStick) + " headRot=" + head.eulerAngles.z + " ballV=" + ball.velocity);
            if (!skills && !remapped && i == 9) ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "zhao-header-motion.png")));
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            if (current.IsName("Head")) headAnimation = true;
            foreach (AnimatorClipInfo clip in animator.GetCurrentAnimatorClipInfo(0)) if (clip.clip.name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0) headAnimation = true;
            foreach (HingeJoint2D j in zhao.GetComponentsInChildren<HingeJoint2D>()) if (j.connectedBody != null) maxError = Mathf.Max(maxError, Vector2.Distance(j.transform.TransformPoint(j.anchor), j.connectedBody.transform.TransformPoint(j.connectedAnchor)));
        }
        Check(headAnimation, "human key executes Zhao header animation skills=" + skills + " remapped=" + remapped);
        Check(orientationStable, "header keeps Zhao lower-body sprite scale and facing");
        Check(maxTarget - minTarget > 35f, "header has real varying neck targets range=" + (maxTarget - minTarget));
        Check(maxSwing > 20f, "header visibly swings physical head degrees=" + maxSwing);
        Check(strongestShot < -1f, "stationary front ball headed toward opponent vx=" + strongestShot);
        Check(probe.touched, "header preserves actual head-ball collision skills=" + skills + " remapped=" + remapped);
        Check(maxError < .35f, "header retains connected model error=" + maxError);
        DownFrame = -1;
        yield return new WaitForSeconds(.6f);
        Check(!animator.GetCurrentAnimatorStateInfo(0).IsName("Head"), "header returns to normal state");
        Check(Mathf.Abs((float)headField.GetValue(zhaoStick) - idleTarget) < .2f, "header releases mirrored goals after recovery idle=" + idleTarget + " actual=" + headField.GetValue(zhaoStick));
        DownFrame = Time.frameCount + 1;
        float repeatStart = head.GetComponent<Rigidbody2D>().rotation, repeatSwing = 0;
        for (int i = 0; i < 25; i++) { yield return null; repeatSwing = Mathf.Max(repeatSwing, Mathf.Abs(Mathf.DeltaAngle(repeatStart, head.GetComponent<Rigidbody2D>().rotation))); }
        Check(repeatSwing > 20f, "next heading command swings again degrees=" + repeatSwing);
        DownFrame = -1;
    }
    IEnumerator Start()
    {
        Application.targetFrameRate = 60; Application.runInBackground = true; QualitySettings.vSyncCount = 0; AudioListener.volume = 0;
        yield return new WaitForSeconds(.25f); yield return UI(); yield return Scene(false); Mapping();
        yield return Header(false, false); yield return Header(true, false); yield return Header(true, true);
        if (File.Exists(ControlBindings.SettingsPath)) File.Delete(ControlBindings.SettingsPath);
        Debug.Log("CONTROLS COMPLETE checks=" + checks + " failures=" + failures); Application.Quit(failures == 0 ? 0 : 1);
    }
    void Update() { if (Time.realtimeSinceStartup > 60) { Debug.LogError("CONTROLS TIMEOUT"); Application.Quit(2); } }
}
