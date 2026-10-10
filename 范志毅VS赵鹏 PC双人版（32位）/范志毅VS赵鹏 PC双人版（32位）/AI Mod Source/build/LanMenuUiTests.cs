using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

// This component is compiled only into an isolated test player.
public sealed class LanMenuUiTests : MonoBehaviour
{
    private int checks, failures;
    private string screenshots;
    public static void Boot() { new GameObject("LAN Menu UI Tests").AddComponent<LanMenuUiTests>(); }
    private IEnumerator Start()
    {
        screenshots = Path.Combine(Path.GetDirectoryName(Application.dataPath), "UI-Screenshots");
        Directory.CreateDirectory(screenshots);
        IEnumerator run = Run();
        while (true)
        {
            bool next = false;
            try { next = run.MoveNext(); } catch (Exception error) { Check(false, "exception " + error); }
            if (!next) break;
            yield return run.Current;
        }
        Debug.Log("LAN_TEST COMPLETE checks=" + checks + " failures=" + failures);
        yield return new WaitForSeconds(.5f);
        Application.Quit();
    }
    private IEnumerator Run()
    {
        Screen.SetResolution(1280, 800, false);
        yield return new WaitForSeconds(.7f);
        Button start = FindButton("btn_Start");
        start.onClick.Invoke(); yield return null;
        FindButton("mode_经典模式 · 原版玩法").onClick.Invoke(); yield return null;
        Button lan = FindButton("mode_局域网对战"), back = FindButton("mode_返回");
        Rect lanRect = Bounds(lan.GetComponent<RectTransform>()), backRect = Bounds(back.GetComponent<RectTransform>());
        Check(!lanRect.Overlaps(backRect), "LAN and return buttons do not overlap");
        Check(lanRect.yMin > backRect.yMax, "return below LAN with a gap");
        Check(OnScreen(lanRect) && OnScreen(backRect), "menu actions on screen");
        yield return new WaitForEndOfFrame(); Capture("opponent-menu.png"); yield return new WaitForSeconds(.4f);
        lan.onClick.Invoke(); yield return null;
        GameObject root = GameObject.Find("LAN Lobby");
        Check(root != null, "full-page lobby exists");
        Check(root.GetComponent<Image>().color.a == 0f, "background transparent");
        Rect viewport = Bounds(root.GetComponent<RectTransform>());
        Check(Mathf.Abs(viewport.width - Screen.width) < 2 && Mathf.Abs(viewport.height - Screen.height) < 2, "lobby fills viewport");
        Check(!lan.gameObject.activeInHierarchy && !start.gameObject.activeInHierarchy, "underlying menu controls hidden");
        Check(FindButton("Lobby Create").GetComponent<Image>().sprite == start.GetComponent<Image>().sprite, "native button artwork reused");
        Check(GameObject.Find("Lobby Status").GetComponent<Text>().text == "", "idle page has no status instructions");
        Check(root.GetComponentsInChildren<Button>().Length == 3, "idle page has only three buttons");
        Check(root.GetComponentsInChildren<InputField>().Length == 1, "one IP input");
        int[,] sizes = { {1280, 720}, {1280, 800}, {800, 600} };
        for (int i = 0; i < sizes.GetLength(0); i++)
        {
            Screen.SetResolution(sizes[i, 0], sizes[i, 1], false);
            yield return new WaitForSeconds(.5f); yield return new WaitForEndOfFrame();
            Rect create = Bounds(FindButton("Lobby Create").GetComponent<RectTransform>());
            Rect join = Bounds(FindButton("Lobby Join").GetComponent<RectTransform>());
            Rect returnRect = Bounds(FindButton("Lobby Back").GetComponent<RectTransform>());
            Rect ip = Bounds(GameObject.Find("Lobby IP").GetComponent<RectTransform>());
            Check(OnScreen(create) && OnScreen(join) && OnScreen(returnRect) && OnScreen(ip), "all controls visible at " + Screen.width + "x" + Screen.height);
            Check(!create.Overlaps(ip) && !ip.Overlaps(join) && !join.Overlaps(returnRect), "controls separated at " + Screen.width + "x" + Screen.height);
            Capture("lobby-" + Screen.width + "x" + Screen.height + ".png"); yield return new WaitForSeconds(.3f);
        }
        GameObject.Find("Lobby IP").GetComponent<InputField>().text = "invalid";
        FindButton("Lobby Join").onClick.Invoke(); yield return null;
        Check(GameObject.Find("Lobby Status").GetComponent<Text>().text.Contains("IP"), "invalid IP shows short error");
        FindButton("Lobby Create").onClick.Invoke(); yield return null;
        Check(GameObject.Find("Lobby Status").GetComponent<Text>().text == "等待加入", "host shows short wait state");
        Check(FindButton("Lobby Copy") != null && GameObject.Find("Lobby Address") != null, "host address and copy visible");
        yield return new WaitForEndOfFrame(); Capture("host-waiting.png"); yield return new WaitForSeconds(.4f);
        FindButton("Lobby Back").onClick.Invoke(); yield return null;
        Check(GameObject.Find("LAN Lobby") == null, "return closes lobby");
        Check(start.gameObject.activeInHierarchy && lan.gameObject.activeInHierarchy, "return restores menu controls");
        lan.onClick.Invoke(); yield return null;
        Check(GameObject.Find("LAN Lobby") != null, "lobby can reopen");
        FindButton("Lobby Back").onClick.Invoke(); yield return null;
        FindButton("mode_返回").onClick.Invoke(); yield return null;
        FindButton("mode_加点模式 · 自选能力与特技").onClick.Invoke(); yield return null;
        FindButton("mode_局域网对战").onClick.Invoke(); yield return null;
        FindButton("Lobby Create").onClick.Invoke(); yield return null;
        AbilitySetupPanel allocation = UnityEngine.Object.FindObjectOfType<AbilitySetupPanel>();
        Check(allocation != null, "allocation LAN host opens current allocation panel");
        Check(GameObject.Find("LAN Lobby") == null, "lobby controls hidden while allocating");
        ClickText(allocation.gameObject, "返回"); yield return null;
        Check(GameObject.Find("LAN Lobby") != null && UnityEngine.Object.FindObjectOfType<AbilitySetupPanel>() == null, "allocation cancellation restores lobby");
        FindButton("Lobby Create").onClick.Invoke(); yield return null;
        allocation = UnityEngine.Object.FindObjectOfType<AbilitySetupPanel>();
        AbilityMode.Builds[0].Change(0, 1);
        ClickText(allocation.gameObject, "开始比赛"); yield return null;
        Check(GameObject.Find("Lobby Status").GetComponent<Text>().text == "等待加入", "confirmed allocations create waiting room");
        Check(AbilityMode.Enabled && AbilityMode.Builds[0].Levels[0] == 5, "host allocation retained before guest joins");
        FindButton("Lobby Back").onClick.Invoke(); yield return null;
    }
    private void ClickText(GameObject root, string text)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>())
            if (button.GetComponentInChildren<Text>().text == text) { button.onClick.Invoke(); return; }
        throw new Exception("Missing allocation button " + text);
    }
    private void Capture(string name) { ScreenCapture.CaptureScreenshot(Path.Combine(screenshots, name)); }
    private Button FindButton(string name)
    {
        foreach (Button button in Resources.FindObjectsOfTypeAll<Button>())
            if (button.gameObject.scene.IsValid() && button.gameObject.name == name && button.gameObject.activeInHierarchy) return button;
        throw new Exception("Missing active button " + name);
    }
    private static Rect Bounds(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static bool OnScreen(Rect rect) { return rect.xMin >= -1 && rect.yMin >= -1 && rect.xMax <= Screen.width + 1 && rect.yMax <= Screen.height + 1; }
    private void Check(bool passed, string name)
    {
        checks++;
        if (passed) Debug.Log("LAN_TEST PASS " + name);
        else { failures++; Debug.LogError("LAN_TEST FAIL " + name); }
    }
}
