using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Kept in a separate assembly so the original game's serialized components stay intact.
public static class GameAIMod
{
    // 0: two players; 1: Fan is human; 2: Zhao is human.
    private static int mode;
    private static readonly Dictionary<int, AIState> states = new Dictionary<int, AIState>();
    private static Rigidbody2D ballBody;
    private static Type ballType;
    private static Component menuPanel;
    private static GameObject modeOverlay;

    private sealed class AIState
    {
        public Rigidbody2D body;
        public FieldInfo groundField;
        public int frame = -1;
        public float axis;
        public bool jump;
        public bool kick;
        public bool head;
        public float lastJump = -10f;
        public float lastKick = -10f;
        public float lastHead = -10f;
    }

    public static void SetupMenu(Component panel)
    {
        states.Clear();
        ballBody = null;
        menuPanel = panel;
        modeOverlay = null;
        Button start = null;
        Button[] buttons = panel.GetComponentsInChildren<Button>(true);
        foreach (Button candidate in buttons)
        {
            if (candidate.gameObject.name == "btn_Start")
            {
                start = candidate;
                break;
            }
        }
        if (start == null) return;
        start.onClick.RemoveAllListeners();
        Button startButton = start;
        start.onClick.AddListener(delegate { ShowModeMenu(panel, startButton); });
    }

    private static void ShowModeMenu(Component panel, Button template)
    {
        Canvas canvas = panel.GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : panel.transform;
        Transform existing = parent.Find("AI Mode Selection");
        if (existing != null)
        {
            modeOverlay = existing.gameObject;
            existing.gameObject.SetActive(true);
            existing.SetAsLastSibling();
            return;
        }

        GameObject overlay = new GameObject("AI Mode Selection", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        modeOverlay = overlay;
        overlay.transform.SetParent(parent, false);
        overlay.transform.SetAsLastSibling();
        RectTransform area = (RectTransform)overlay.transform;
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = Vector2.zero;
        area.offsetMax = Vector2.zero;
        Image shade = overlay.GetComponent<Image>();
        shade.color = new Color(0.025f, 0.055f, 0.09f, 0.94f);
        shade.raycastTarget = true;

        Text originalText = template.GetComponentInChildren<Text>(true);
        if (originalText != null)
        {
            GameObject titleObject = new GameObject("Mode Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            titleObject.transform.SetParent(overlay.transform, false);
            RectTransform titleRect = (RectTransform)titleObject.transform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(560f, 60f);
            titleRect.anchoredPosition = new Vector2(0f, 185f);
            Text title = titleObject.GetComponent<Text>();
            title.font = originalText.font;
            title.fontSize = 34;
            title.color = Color.white;
            title.alignment = TextAnchor.MiddleCenter;
            title.text = "选择对战模式";
        }

        AddChoice(template, overlay.transform, "双人对战", 90f, delegate { StartGame(0); });
        AddChoice(template, overlay.transform, "操控范志毅 · 挑战赵鹏 AI", 10f, delegate { StartGame(1); });
        AddChoice(template, overlay.transform, "操控赵鹏 · 挑战范志毅 AI", -70f, delegate { StartGame(2); });
        AddChoice(template, overlay.transform, "返回", -160f, delegate
        {
            overlay.SetActive(false);
            UnityEngine.Object.Destroy(overlay);
            modeOverlay = null;
        });
    }

    private static void AddChoice(Button template, Transform parent, string label, float y, UnityEngine.Events.UnityAction action)
    {
        Button choice = UnityEngine.Object.Instantiate<Button>(template);
        choice.transform.SetParent(parent, false);
        choice.gameObject.name = "mode_" + label;
        RectTransform rect = choice.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(460f, 65f);
        rect.localScale = Vector3.one;
        Text text = choice.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = label;
            text.fontSize = 25;
            text.alignment = TextAnchor.MiddleCenter;
        }
        choice.onClick.RemoveAllListeners();
        choice.onClick.AddListener(action);
    }

    private static void StartGame(int selectedMode)
    {
        mode = selectedMode;
        states.Clear();
        ballBody = null;
        if (modeOverlay != null)
        {
            modeOverlay.SetActive(false);
            UnityEngine.Object.Destroy(modeOverlay);
            modeOverlay = null;
        }
        HideMainMenu();
        SceneManager.LoadScene("GameScene");
    }

    private static void HideMainMenu()
    {
        if (menuPanel == null) return;
        // UIManager's canvas survives scene loads. Remove its panel dictionary entry
        // through the same HidePanel path used by the game's original Start button.
        menuPanel.gameObject.SetActive(false);
        Type uiType = menuPanel.GetType().Assembly.GetType("UIManager");
        Type singletonType = menuPanel.GetType().Assembly.GetType("SingletonBase`1");
        object manager = singletonType.MakeGenericType(uiType)
            .GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, null);
        uiType.GetMethod("HidePanel", BindingFlags.Public | BindingFlags.Instance)
            .Invoke(manager, new object[] { "MainPanel" });
        menuPanel = null;
    }

    public static float GetAxis(string axisName, Component controller)
    {
        if (!IsAI(controller)) return Input.GetAxisRaw(axisName);
        AIState state = GetState(controller);
        UpdateDecision(controller, state);
        return state.axis;
    }

    public static bool GetButtonDown(KeyCode key, Component controller)
    {
        if (!IsAI(controller)) return Input.GetKeyDown(key);
        AIState state = GetState(controller);
        UpdateDecision(controller, state);
        // The game's PlayerInput maps each side to these keys.
        if (key == KeyCode.W || key == KeyCode.UpArrow) return state.jump;
        if (key == KeyCode.K || key == KeyCode.KeypadEnter) return state.kick;
        if (key == KeyCode.J) return state.head;
        // The original Zhao input has no head key assigned, but its AI can use the move.
        if (key == KeyCode.None && controller.gameObject.name == "Zhao") return state.head;
        return false;
    }

    private static bool IsAI(Component controller)
    {
        if (controller == null) return false;
        string name = controller.gameObject.name;
        return (mode == 1 && name == "Zhao") || (mode == 2 && name == "Fan");
    }

    private static AIState GetState(Component controller)
    {
        int id = controller.GetInstanceID();
        AIState state;
        if (!states.TryGetValue(id, out state) || state.body == null)
        {
            state = new AIState();
            Type type = controller.GetType();
            FieldInfo bodyField = type.GetField("rb", BindingFlags.Instance | BindingFlags.Public);
            state.groundField = type.GetField("isOnGround", BindingFlags.Instance | BindingFlags.Public);
            if (bodyField != null) state.body = bodyField.GetValue(controller) as Rigidbody2D;
            states[id] = state;
        }
        return state;
    }

    private static Rigidbody2D FindBall()
    {
        if (ballBody != null) return ballBody;
        if (ballType == null) ballType = Type.GetType("Ball, Assembly-CSharp");
        if (ballType == null) return null;
        Component ball = UnityEngine.Object.FindObjectOfType(ballType) as Component;
        if (ball != null) ballBody = ball.GetComponent<Rigidbody2D>();
        return ballBody;
    }

    private static void UpdateDecision(Component controller, AIState state)
    {
        if (state.frame == Time.frameCount) return;
        state.frame = Time.frameCount;
        state.axis = 0f;
        state.jump = state.kick = state.head = false;

        Rigidbody2D ball = FindBall();
        Rigidbody2D player = state.body;
        if (ball == null || player == null) return;

        Vector2 ballPosition = ball.position;
        Vector2 ballVelocity = ball.velocity;
        Vector2 playerPosition = player.position;
        float attackDirection = controller.gameObject.name == "Fan" ? 1f : -1f;

        // Lead a moving ball, with a shorter horizon when the ball is close.
        float distance = Mathf.Abs(ballPosition.x - playerPosition.x);
        float horizon = Mathf.Clamp(distance / 9f, 0.08f, 0.55f);
        float interceptX = ballPosition.x + ballVelocity.x * horizon;
        float gravity = Physics2D.gravity.y * ball.gravityScale;
        float interceptY = ballPosition.y + ballVelocity.y * horizon + 0.5f * gravity * horizon * horizon;

        // Approach from the side of our goal so a kick sends the ball forward.
        float targetX = interceptX - attackDirection * 0.65f;
        float difference = targetX - playerPosition.x;
        if (Mathf.Abs(difference) > 0.22f) state.axis = Mathf.Sign(difference);

        float now = Time.time;
        float horizontalGap = Mathf.Abs(ballPosition.x - playerPosition.x);
        float heightGap = ballPosition.y - playerPosition.y;
        bool grounded = state.groundField != null && (bool)state.groundField.GetValue(controller);

        if (grounded && now - state.lastJump > 0.85f &&
            Mathf.Abs(interceptX - playerPosition.x) < 2.05f &&
            interceptY > playerPosition.y + 0.75f && interceptY < playerPosition.y + 3.5f)
        {
            state.jump = true;
            state.lastJump = now;
        }
        if (horizontalGap < 1.45f && heightGap > -0.7f && heightGap < 1.65f && now - state.lastKick > 0.38f)
        {
            state.kick = true;
            state.lastKick = now;
        }
        if (horizontalGap < 1.15f && heightGap >= 0.7f && heightGap < 2.8f && now - state.lastHead > 0.48f)
        {
            state.head = true;
            state.lastHead = now;
        }
    }
}
