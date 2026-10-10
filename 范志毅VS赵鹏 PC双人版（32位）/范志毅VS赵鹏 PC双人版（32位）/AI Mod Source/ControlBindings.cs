using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public enum GameControlAction { Left, Right, Jump, Down, Kick, Head, PowerKick }

// The action slots stay stable for AI and native animations; only human keys
// are remapped. All existing keys are the defaults, plus Zhao's keypad header.
public static class ControlBindings
{
    private static readonly KeyCode[,] defaults = {
        { KeyCode.A, KeyCode.D, KeyCode.W, KeyCode.S, KeyCode.K, KeyCode.J, KeyCode.L },
        { KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.KeypadEnter, KeyCode.RightShift, KeyCode.Keypad0 }
    };
    private static readonly KeyCode[,] keys = new KeyCode[2, 7];
    internal static string settingsPath;
    public static string SettingsPath { get { return settingsPath ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "按键设置.ini")); } }
    private static bool loaded;
    public static void EnsureLoaded()
    {
        if (loaded) return;
        for (int p = 0; p < 2; p++) for (int a = 0; a < 7; a++) keys[p, a] = defaults[p, a];
        if (File.Exists(SettingsPath))
        {
            int player = -1;
            foreach (string entry in File.ReadAllLines(SettingsPath, Encoding.UTF8))
            {
                string line = entry.Trim();
                if (line == "[Fan]") { player = 0; continue; }
                if (line == "[Zhao]") { player = 1; continue; }
                int split = line.IndexOf('=');
                if (player < 0 || split < 1 || line.StartsWith("#")) continue;
                GameControlAction action; KeyCode key;
                if (Enum.TryParse<GameControlAction>(line.Substring(0, split).Trim(), true, out action) &&
                    (int)action >= 0 && (int)action < 7 &&
                    Enum.TryParse<KeyCode>(line.Substring(split + 1).Trim(), true, out key) &&
                    Enum.IsDefined(typeof(KeyCode), key) && key != KeyCode.None && key != KeyCode.Escape)
                    keys[player, (int)action] = key;
            }
        }
        if (keys[1, (int)GameControlAction.Head] == KeyCode.Keypad0 && keys[1, (int)GameControlAction.PowerKick] == KeyCode.Keypad0)
            keys[1, (int)GameControlAction.Head] = KeyCode.RightShift;
        loaded = true;
    }
    public static void Reload() { loaded = false; EnsureLoaded(); }
    public static KeyCode Get(bool fan, GameControlAction action) { EnsureLoaded(); return keys[fan ? 0 : 1, (int)action]; }
    public static bool Set(bool fan, GameControlAction action, KeyCode key)
    {
        if (key == KeyCode.None || key == KeyCode.Escape || !Enum.IsDefined(typeof(KeyCode), key)) return false;
        EnsureLoaded();
        int player = fan ? 0 : 1;
        for (int p = 0; p < 2; p++) for (int a = 0; a < 7; a++)
            if ((p != player || a != (int)action) && keys[p, a] == key) return false;
        keys[player, (int)action] = key;
        return true;
    }
    public static void Save()
    {
        EnsureLoaded();
        StringBuilder text = new StringBuilder("# 在游戏的键位设置中修改，或编辑下方按键名称。\n# Left=向左 Right=向右 Jump=跳跃 Down=向下 Kick=射门 Head=头球 PowerKick=大力射门\n");
        for (int p = 0; p < 2; p++)
        {
            text.AppendLine(p == 0 ? "\n[Fan]" : "\n[Zhao]");
            for (int a = 0; a < 7; a++) text.AppendLine(((GameControlAction)a) + "=" + keys[p, a]);
        }
        File.WriteAllText(SettingsPath, text.ToString(), new UTF8Encoding(false));
    }
    public static void ResetDefaults()
    {
        for (int p = 0; p < 2; p++) for (int a = 0; a < 7; a++) keys[p, a] = defaults[p, a];
        loaded = true;
        Save();
    }
    private static bool ReadKey(KeyCode key) { return Input.GetKey(key); }
    private static bool ReadDown(KeyCode key) { return Input.GetKeyDown(key); }
    public static float GetHorizontal(Component player)
    {
        float remoteAxis;
        if (LanMultiplayer.TryGetAxis(player, out remoteAxis)) return remoteAxis;
        bool fan = player != null && player.name == "Fan";
        return (ReadKey(Get(fan, GameControlAction.Right)) ? 1f : 0f) - (ReadKey(Get(fan, GameControlAction.Left)) ? 1f : 0f);
    }
    public static bool GetActionDown(GameControlAction action, Component player)
    {
        bool remoteDown;
        if (LanMultiplayer.TryGetActionDown(action, player, out remoteDown)) return remoteDown;
        return ReadDown(Get(player != null && player.name == "Fan", action));
    }
    public static bool GetNativeButtonDown(KeyCode original, Component player)
    {
        if (player == null) return Input.GetKeyDown(original);
        bool fan = player.name == "Fan";
        if (original == (fan ? KeyCode.W : KeyCode.UpArrow)) return GetActionDown(GameControlAction.Jump, player);
        if (original == (fan ? KeyCode.K : KeyCode.KeypadEnter))
        {
            bool power = GetActionDown(GameControlAction.PowerKick, player);
            if (power) PowerShot.Request(player);
            return power || GetActionDown(GameControlAction.Kick, player);
        }
        if (original == (fan ? KeyCode.J : KeyCode.Keypad0) || (!fan && original == KeyCode.None)) return GetActionDown(GameControlAction.Head, player);
        return Input.GetKeyDown(original);
    }
    public static void ConfigureNativeInput(Component input)
    {
        // Zhao's controller already has the native Head command and animation.
        // Give it an action slot instead of the unassigned KeyCode.None.
        if (input != null && input.name == "Zhao") input.GetType().GetField("head").SetValue(input, KeyCode.Keypad0);
    }
    public static string KeyName(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.LeftArrow: return "←";
            case KeyCode.RightArrow: return "→";
            case KeyCode.UpArrow: return "↑";
            case KeyCode.DownArrow: return "↓";
            case KeyCode.KeypadEnter: return "小键盘回车";
            case KeyCode.Keypad0: return "小键盘 0";
            case KeyCode.RightShift: return "右 Shift";
            case KeyCode.Space: return "空格";
            case KeyCode.Return: return "回车";
        }
        string text = key.ToString();
        if (text.StartsWith("Alpha")) return text.Substring(5);
        if (text.StartsWith("Keypad")) return "小键盘 " + text.Substring(6);
        return text.Length == 1 ? text.ToUpperInvariant() : text;
    }
}

public sealed class ControlsSettingsPanel : MonoBehaviour
{
    private readonly Button[,] choices = new Button[2, 7];
    private static readonly string[] labels = { "向左", "向右", "跳跃", "向下", "射门", "头球", "大力射门" };
    private Text hint;
    private Action onClose;
    private int capturingPlayer = -1, capturingAction = -1;
    public static ControlsSettingsPanel Open(Transform parent, Button template, Action closed)
    {
        Transform existing = parent.Find("Controls Settings");
        if (existing != null) return existing.GetComponent<ControlsSettingsPanel>();
        var overlay = new GameObject("Controls Settings", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ControlsSettingsPanel));
        overlay.transform.SetParent(parent, false);
        overlay.transform.SetAsLastSibling();
        RectTransform rect = (RectTransform)overlay.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = new Color(.025f, .055f, .09f, .98f);
        MenuBackdrop.Apply(overlay);
        ControlsSettingsPanel panel = overlay.GetComponent<ControlsSettingsPanel>();
        panel.onClose = closed;
        panel.Build(template);
        return panel;
    }
    private void Build(Button template)
    {
        var content = new GameObject("Binding Content", typeof(RectTransform));
        content.transform.SetParent(transform, false);
        RectTransform rect = (RectTransform)content.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(640, 530);
        RectTransform parent = (RectTransform)transform;
        rect.localScale = Vector3.one * Mathf.Min(parent.rect.width / 680f, parent.rect.height / 570f);
        Font font = template.GetComponentInChildren<Text>(true).font;
        AddText(content.transform, font, "键位设置", new Vector2(0, 225), 30, new Vector2(600, 45));
        AddText(content.transform, font, "范志毅", new Vector2(-140, 173), 24, new Vector2(250, 35));
        AddText(content.transform, font, "赵鹏", new Vector2(140, 173), 24, new Vector2(250, 35));
        for (int p = 0; p < 2; p++) for (int a = 0; a < 7; a++)
        {
            int player = p, action = a;
            Button button = AddButton(template, content.transform, new Vector2(p == 0 ? -140 : 140, 120 - a * 40), "", delegate { BeginCapture(player, action); });
            button.name = "bind_" + (p == 0 ? "Fan_" : "Zhao_") + ((GameControlAction)a);
            choices[p, a] = button;
        }
        AddButton(template, content.transform, new Vector2(-140, -172), "恢复默认", delegate { ControlBindings.ResetDefaults(); capturingPlayer = -1; Refresh(); });
        AddButton(template, content.transform, new Vector2(140, -172), "完成", Close);
        hint = AddText(content.transform, font, "点击要修改的动作，再按新按键；Esc 取消。", new Vector2(0, -225), 18, new Vector2(620, 35));
        Refresh();
    }
    private Text AddText(Transform parent, Font font, string value, Vector2 position, int size, Vector2 dimensions)
    {
        var obj = new GameObject("Binding Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = dimensions;
        Text text = obj.GetComponent<Text>(); text.font = font; text.fontSize = size; text.text = value;
        text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        return text;
    }
    private Button AddButton(Button template, Transform parent, Vector2 position, string label, UnityEngine.Events.UnityAction action)
    {
        Button button = UnityEngine.Object.Instantiate<Button>(template);
        button.transform.SetParent(parent, false);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = new Vector2(260, 40); rect.localScale = Vector3.one;
        Text text = button.GetComponentInChildren<Text>(true); text.text = label; text.fontSize = 19;
        button.onClick.RemoveAllListeners(); button.onClick.AddListener(action);
        return button;
    }
    private void Refresh()
    {
        for (int p = 0; p < 2; p++) for (int a = 0; a < 7; a++)
            choices[p, a].GetComponentInChildren<Text>(true).text = labels[a] + "：" + ControlBindings.KeyName(ControlBindings.Get(p == 0, (GameControlAction)a));
        if (hint != null) hint.text = "点击要修改的动作，再按新按键；Esc 取消。";
    }
    private void BeginCapture(int player, int action)
    {
        capturingPlayer = player; capturingAction = action;
        hint.text = "请为" + (player == 0 ? "范志毅" : "赵鹏") + "的“" + labels[action] + "”按下新按键。";
    }
    private void OnGUI()
    {
        Event key = Event.current;
        if (key.type != EventType.KeyDown || !key.isKey) return;
        HandleKey(key.keyCode);
        key.Use();
    }
    private void HandleKey(KeyCode key)
    {
        if (key == KeyCode.Escape)
        {
            if (capturingPlayer >= 0) { capturingPlayer = -1; Refresh(); } else Close();
            return;
        }
        if (capturingPlayer < 0) return;
        if (!ControlBindings.Set(capturingPlayer == 0, (GameControlAction)capturingAction, key))
        { hint.text = "这个按键已被占用，请换一个按键。"; return; }
        ControlBindings.Save(); capturingPlayer = -1; Refresh();
    }
    private void Close()
    {
        gameObject.SetActive(false);
        UnityEngine.Object.Destroy(gameObject);
        if (onClose != null) onClose();
    }
}
