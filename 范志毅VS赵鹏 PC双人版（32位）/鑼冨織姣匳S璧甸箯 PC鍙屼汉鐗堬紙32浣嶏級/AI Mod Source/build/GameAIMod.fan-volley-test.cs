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
        public Rigidbody2D opponentBody;
        public Transform foot;
        public Transform headTransform;
        public FieldInfo groundField;
        public int frame = -1;
        public float axis;
        public bool jump;
        public bool kick;
        public bool head;
        public bool down;
        public float lastDrop = -10f;
        public float lastJump = -10f;
        public float lastKick = -10f;
        public float pendingKickTap = -10f;
        public float lastHead = -10f;
        public bool waitingForServe = true;
        public float lastServeReset = -10f;
        public float serveReleaseTime = -10f;
        public bool serveApproach = true;
    }

    public static void SetupMenu(Component panel)
    {
        FanVolleyTests.Boot(); states.Clear();
        ballBody = null;
        menuPanel = panel;
        modeOverlay = null;
        Button start = null;
        Button[] buttons = panel.GetComponentsInChildren<Button>(true);
        UpdateRulesIntroduction();

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

    private static void UpdateRulesIntroduction()
    {
        // Keep the original rules panel, its artwork and its close button.
        GameObject prefab = Resources.Load<GameObject>("UI/TeachPanel");
        if (prefab == null) return;
        prefab.transform.localScale = Vector3.one * 1.4f;
        foreach (Text text in prefab.GetComponentsInChildren<Text>(true))
        {
            if (text.name != "text") continue;
            text.text = "玩家1（范）：\n移动跳跃：WASD\n射门：K\n头球：J（经典模式也可使用）\n\n玩家2（赵）：\n移动跳跃：↑↓←→\n射门：小键盘（数字键盘）回车键\n\n在特技模式下连按两下射门键，有奇效！";
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 22;
            text.fontSize = 28;
            text.resizeTextMaxSize = 28;
            break;
        }
    }

    private static void ShowModeMenu(Component panel, Button template)
    {
        ShowSelection(panel, template, false);
    }

    private static void ShowSelection(Component panel, Button template, bool chooseOpponent)
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
            title.text = chooseOpponent ? (PlayerSkills.Enabled ? "特技模式 · 选择对战方式" : "经典模式 · 选择对战方式") : "选择玩法";
        }

        if (chooseOpponent)
        {
            AddChoice(template, overlay.transform, "双人对战", 90f, delegate { StartGame(0); });
            AddChoice(template, overlay.transform, "操控范志毅 · 挑战赵鹏 AI", 10f, delegate { StartGame(1); });
            AddChoice(template, overlay.transform, "操控赵鹏 · 挑战范志毅 AI", -70f, delegate { StartGame(2); });
        }
        else
        {
            AddChoice(template, overlay.transform, "经典模式 · 原版玩法", 70f, delegate { ChooseStyle(panel, template, false); });
            AddChoice(template, overlay.transform, "特技模式 · 球员技能", -20f, delegate { ChooseStyle(panel, template, true); });
        }
        AddChoice(template, overlay.transform, "返回", chooseOpponent ? -160f : -120f, delegate
        {
            overlay.name = "Retired Selection";
            overlay.SetActive(false);
            UnityEngine.Object.Destroy(overlay);
            modeOverlay = null;
            if (chooseOpponent) ShowModeMenu(panel, template);
        });
    }

    private static void ChooseStyle(Component panel, Button template, bool enabled)
    {
        PlayerSkills.Enabled = enabled;
        if (modeOverlay != null)
        {
            modeOverlay.name = "Retired Selection";
            modeOverlay.SetActive(false);
            UnityEngine.Object.Destroy(modeOverlay);
            modeOverlay = null;
        }
        ShowSelection(panel, template, true);
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
        FanVolleyTests.Boot(); states.Clear();
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

    public static void OnBallReset()
    {
        PlayerSkills.ResetAll();
        foreach (AIState state in states.Values)
        {
            state.waitingForServe = true;
            state.lastServeReset = Time.time;
            state.serveReleaseTime = -10f;
            state.frame = -1;
            state.serveApproach = true;
            state.down = false;
            state.lastDrop = -10f;
            state.lastKick = state.pendingKickTap = -10f;
        }
    }

    // Run before the native muscle controller records its reset pose. Translate
    // the whole actor once, preserving all limb offsets and native reset logic.
    public static void PlaceAtGoalLine(Component stick)
    {
        if (stick == null || (stick.name != "Fan" && stick.name != "Zhao")) return;
        Transform body = stick.transform.Find("Body");
        if (body == null) return;
        bool fan = stick.name == "Fan";
        Type goalType = stick.GetType().Assembly.GetType("GoalTrigger");
        if (goalType == null) return;
        Component selected = null;
        foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(goalType))
        {
            Component goal = item as Component;
            if (goal != null && (goal.transform.position.x < 0f) == fan)
            { selected = goal; break; }
        }
        if (selected == null) return;
        Collider2D mouth = selected.GetComponent<Collider2D>();
        float goalX = mouth != null ? (fan ? mouth.bounds.max.x : mouth.bounds.min.x) : selected.transform.position.x;
        stick.transform.position += Vector3.right * (goalX - body.position.x);
        Physics2D.SyncTransforms();
        Component player = stick.GetComponent(stick.GetType().Assembly.GetType("PlayerController"));
        if (player != null)
            player.GetType().GetProperty("OriginPos").SetValue(player, body.position, null);
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
        if (!FanVolleyTests.UseAI) return controller.name == "Fan" ? FanVolleyTests.FanAxis : FanVolleyTests.ZhaoAxis;
        if (!IsAI(controller)) return 0f;
        AIState state = GetState(controller);
        UpdateDecision(controller, state);
        return state.axis;
    }

    // The original FixedUpdate has an extra horizontal-force branch. Give both
    // AI sides the same extra push only during a reachable, close contest.
    // Preserve the original check for human input.
    public static float GetExtraPushGate(string axisName, Component controller)
    {
        if (!FanVolleyTests.UseAI) return controller.name == "Fan" ? FanVolleyTests.FanAxis : FanVolleyTests.ZhaoAxis;
        if (!IsAI(controller)) return 0f;
        AIState state = GetState(controller);
        Rigidbody2D ball = FindBall();
        if (ball == null || state.body == null || state.opponentBody == null) return 0f;
        float ballGap = Mathf.Abs(ball.position.x - state.body.position.x);
        float opponentGap = Mathf.Abs(ball.position.x - state.opponentBody.position.x);
        float headY = state.headTransform != null ? state.headTransform.position.y : state.body.position.y + 0.9f;
        return ballGap < 1.85f && opponentGap < 1.9f && ball.position.y < headY + 1.5f ? -1f : 0f;
    }

    public static bool GetButtonDown(KeyCode key, Component controller)
    {
        if (!FanVolleyTests.UseAI || !IsAI(controller)) return Time.frameCount == FanVolleyTests.InputFrame && controller.name == FanVolleyTests.InputPlayer && key == FanVolleyTests.InputKey;
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

    public static bool GetDownCommand(Component controller)
    {
        if (!FanVolleyTests.UseAI || !IsAI(controller)) return false;
        AIState state = GetState(controller);
        UpdateDecision(controller, state);
        return controller.name == "Fan" && state.down;
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
            state.lastServeReset = Time.time;
            Type type = controller.GetType();
            FieldInfo bodyField = type.GetField("rb", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo footField = type.GetField("footTrans", BindingFlags.Instance | BindingFlags.Public);
            state.groundField = type.GetField("isOnGround", BindingFlags.Instance | BindingFlags.Public);
            if (bodyField != null) state.body = bodyField.GetValue(controller) as Rigidbody2D;
            if (footField != null) state.foot = footField.GetValue(controller) as Transform;
            state.headTransform = controller.transform.Find("Head");
            GameObject opponent = GameObject.Find(controller.gameObject.name == "Fan" ? "Zhao" : "Fan");
            if (opponent != null)
            {
                Transform otherBody = opponent.transform.Find("Body");
                if (otherBody != null) state.opponentBody = otherBody.GetComponent<Rigidbody2D>();
            }
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
        state.down = false;

        Rigidbody2D ball = FindBall();
        Rigidbody2D player = state.body;
        if (ball == null || player == null) return;

        Vector2 ballPosition = ball.position;
        Vector2 ballVelocity = ball.velocity;
        Vector2 playerPosition = player.position;
        float attackDirection = controller.gameObject.name == "Fan" ? 1f : -1f;

        // Lead a moving ball, with a shorter horizon when the ball is close.
        float distance = Mathf.Abs(ballPosition.x - playerPosition.x);
        float horizon = Mathf.Clamp(distance / 9f, 0.06f, 0.48f);
        float interceptX = Mathf.Clamp(ballPosition.x + ballVelocity.x * horizon, -7.8f, 7.8f);
        float gravity = Physics2D.gravity.y * ball.gravityScale;
        float interceptY = ballPosition.y + ballVelocity.y * horizon + 0.5f * gravity * horizon * horizon;

        float headY = state.headTransform != null ? state.headTransform.position.y : playerPosition.y + 0.9f;
        float headX = state.headTransform != null ? state.headTransform.position.x : playerPosition.x;
        float footY = state.foot != null ? state.foot.position.y : playerPosition.y - 2.2f;
        float forwardBall = attackDirection * (ballPosition.x - playerPosition.x);
        float opponentGap = state.opponentBody != null
            ? Mathf.Abs(ballPosition.x - state.opponentBody.position.x) : 100f;
        bool duel = distance < 2.0f && opponentGap < 2.0f && ballPosition.y < headY + 1.5f;
        float now = Time.time;

        // Ball.Reset calls OnBallReset after goals and out-of-bounds restarts.
        // Advance from the goal line, stopping on the attacking side of the
        // catch point. No backward input during an untouched serve approach.
        if (state.waitingForServe &&
            (ballPosition.y < -1.05f || Mathf.Abs(ballVelocity.x) > 1.8f || now - state.lastServeReset > 1.3f))
        {
            state.waitingForServe = false;
            state.serveReleaseTime = now;
        }
        if (state.serveApproach && (Mathf.Abs(ballVelocity.x) > 1.8f || duel ||
            distance < 1.1f || now - state.lastServeReset > 3.5f)) state.serveApproach = false;
        bool serving = state.serveApproach;
        bool highUncontested = !serving && !duel && opponentGap > 2f &&
            Mathf.Abs(ballVelocity.x) < 1.8f && ballPosition.y > 0.3f;
        float targetX = interceptX - attackDirection * 0.9f;
        if (highUncontested)
        {
            targetX = interceptX - attackDirection * 1.1f;
        }
        if (duel && forwardBall > -0.4f)
        {
            state.axis = attackDirection;
        }
        else if (!serving && !highUncontested &&
            distance < 1.1f && forwardBall > -0.3f)
        {
            state.axis = attackDirection;
        }
        else
        {
            float predictedSelfX = playerPosition.x + Mathf.Clamp(player.velocity.x, -5f, 5f) * 0.2f;
            float difference = targetX - predictedSelfX;
            if (Mathf.Abs(difference) > 0.2f)
            {
                float limit = serving ? 1f : (highUncontested ? 0.7f :
                    (Mathf.Abs(ballVelocity.x) < 1.8f && opponentGap > 2f ? 0.85f : 1f));
                state.axis = Mathf.Clamp(difference * 0.75f, -limit, limit);
            }
        }
        if (serving && state.axis * attackDirection < 0f) state.axis = 0f;

        float footGap = ballPosition.y - footY;
        float headGap = ballPosition.y - headY;
        float forwardHead = attackDirection * (ballPosition.x - headX);
        bool grounded = state.groundField != null && (bool)state.groundField.GetValue(controller);

        float jumpHorizon = .22f;
        float jumpBallY = ballPosition.y + ballVelocity.y * jumpHorizon + .5f * gravity * jumpHorizon * jumpHorizon;
        float jumpBallX = ballPosition.x + ballVelocity.x * jumpHorizon;
        float jumpPlayerX = playerPosition.x + Mathf.Clamp(player.velocity.x, -5f, 5f) * jumpHorizon;
        bool alignedForJump = Mathf.Abs(jumpBallX - jumpPlayerX) < 1.05f && forwardBall > -.25f;
        bool descendingToReach = ballVelocity.y <= .5f && ballPosition.y > headY + .55f &&
            jumpBallY > headY + .25f && jumpBallY < headY + 1.65f;
        bool contestedJump = duel && distance < 1.05f && footGap > 1.8f &&
            ballPosition.y > headY - .4f && jumpBallY > headY - .45f && jumpBallY < headY + 1.65f;
        bool reachableAirBall = alignedForJump && (descendingToReach || contestedJump);
        if (grounded && now - state.lastJump > 0.72f &&
            reachableAirBall)
        {
            state.jump = true;
            state.lastJump = now;
        }
        float kickCooldown = duel ? 0.28f : 0.4f;
        float kickReach = 1.65f;
        bool pendingSecondKick = PlayerSkills.Enabled && state.pendingKickTap >= 0f &&
            now - state.pendingKickTap <= PlayerSkills.ShotDoubleTapWindow;
        if (!pendingSecondKick) state.pendingKickTap = -10f;
        if (distance < kickReach && forwardBall > -0.65f &&
            footGap > -0.3f && footGap < 2.15f &&
            (pendingSecondKick ? now - state.lastKick >= .14f : now - state.lastKick > kickCooldown))
        {
            state.kick = true;
            state.lastKick = now;
            state.pendingKickTap = !pendingSecondKick && PlayerSkills.Enabled && forwardBall >= 0f ? now : -10f;
        }
        float headCooldown = duel ? 0.3f : 0.45f;
        if ((!PlayerSkills.Enabled || (!state.kick && !pendingSecondKick)) && Mathf.Abs(forwardHead) < 1.45f && forwardHead > -0.55f &&
            headGap > -1.35f && headGap < 1.35f && now - state.lastHead > headCooldown)
        {
            state.head = true;
            state.lastHead = now;
        }
        if (PlayerSkills.Enabled && controller.name == "Fan" && !grounded && !state.head && !serving &&
            footGap < -.5f && distance < 1.6f && now - state.lastDrop > .85f && PlayerSkills.CanQuickDrop(controller))
        {
            state.down = true;
            state.lastDrop = now;
        }
    }
}

