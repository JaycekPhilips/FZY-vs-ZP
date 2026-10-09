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
        DumpRules.Boot(); states.Clear();
        ballBody = null;
        menuPanel = panel;
        modeOverlay = null;
        Button start = null;
        Button[] buttons = panel.GetComponentsInChildren<Button>(true);
        foreach (Button candidate in buttons)
        {
            if (candidate.gameObject.name != "btn_Teach") continue;
            candidate.onClick.RemoveAllListeners();
            candidate.onClick.AddListener(delegate { GameRulesView.Open(panel); });
        }
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
        DumpRules.Boot(); states.Clear();
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
        if (!IsAI(controller)) return Input.GetAxisRaw(axisName);
        AIState state = GetState(controller);
        UpdateDecision(controller, state);
        return state.axis;
    }

    // The original FixedUpdate has an extra horizontal-force branch. Give both
    // AI sides the same extra push only during a reachable, close contest.
    // Preserve the original check for human input.
    public static float GetExtraPushGate(string axisName, Component controller)
    {
        if (!IsAI(controller)) return Input.GetAxisRaw(axisName);
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

    public static bool GetDownCommand(Component controller)
    {
        if (!IsAI(controller)) return Input.GetKeyDown(KeyCode.S);
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

// Opened from the original main-menu rules button. Pages share one scroll area
// so all instructions stay readable at the game's smallest window size.
public sealed class GameRulesView : MonoBehaviour
{
    private Text content;
    private Text pageTitle;
    private ScrollRect scroll;
    private readonly Button[] tabs = new Button[3];
    private readonly string[] titles = { "基本规则与按键", "范志毅 · 特技技能", "赵鹏 · 特技技能" };
    private static readonly string[] pages = {
        "<b>玩法与对战</b>\n将球踢进对方球门得分。点击“开始”，先选经典模式或特技模式，再选双人对战或操控任一球员挑战 AI。重新开始保留所选模式。\n\n" +
        "<b>两种模式通用的操作</b>\n范志毅：A / D 向左 / 向右移动；W 跳跃；K 射门；<b>J 头球</b>（经典模式也可使用）。\n赵鹏：← / → 向左 / 向右移动；↑ 跳跃；<b>小键盘 Enter 射门</b>。\n射门可单独按，也可配合任意方向。单击是普通射门，长按不算连续两次。\n\n" +
        "<b>经典模式</b>\n使用原有射门、头球和跳跃，没有特殊技能与技能高光。双方从各自门线开球，足球下落位置不变。两人的基础回防速度调整在经典与特技模式中都生效。\n\n" +
        "<b>特技模式 · 新指令</b>\n范志毅：<b>0.35 秒内连续按两次 K</b>，准备弧线落叶射门；空中按 <b>S</b> 快速落地。\n赵鹏：<b>0.35 秒内连续按两次小键盘 Enter</b>，准备掠地瞬击。单次射门也有瞬发射门的摆腿加速。\n第二次射门后，0.75 秒内脚背或小腿下段真实接触足球才能发动相应球路技能。脚底板、膝盖、上段小腿、头部或身体碰球不算。技能细则见两位球员的介绍。\n\n" +
        "<b>技能提示</b>\n技能触发时，球员身边显示动画与名称。炮弹式头球的球为橙红高光，掠地瞬击为金黄，弧线落叶射门为紫色。球在身后时，不触发射门、头球、盘带或进攻提速技能；空中堡垒、强力抢断、紧急回防、快速落地不受球位限制。",
        "<b>弧线落叶射门 · 连按 K 两次</b>\n0.35 秒内连续按两次 K；第二次按下后的 0.75 秒内，脚背或小腿下段真实碰到身前的空中球，打出更高上飘、随后明显下坠的球路。可配合任意方向，也可在 W 起跳过程中双击 K。单击 K 或只按 W 不发动；脚底板踹球不算；地面球不发动。每次双击最多成功发动一次，足球显示紫色高光。\n\n" +
        "<b>伸脚抢截 · 空中按 S</b>\n空中按 S（下指令）快速落地，接近地面时减速，实际落地后结束。地面按 S 不发动；球在身后也可使用。\n\n" +
        "<b>炮弹式头球 · 按 J</b>\n特技模式按 J 头球，球进入头部接触范围后，顶出更有力的球并显示橙红高光。出球速度已在此前技能基础上降低 5%。<b>经典模式按 J 仍是普通头球</b>。\n\n" +
        "<b>旋风盘带 · 自动</b>\n地面移动盘带时，摆腿频率提高 50%，通过实际脚球碰撞控球。停止盘带、跳跃或射门时恢复。\n\n" +
        "<b>进攻提速 · 无球向前</b>\n球在身前、未在脚下控制时，按 D 向前冲的速度提高 10%。接近球、停止前冲或射门时结束。",
        "<b>掠地瞬击 · 连按射门两次</b>\n0.35 秒内连续按两次<b>小键盘 Enter</b>；第二次按下后的 0.75 秒内，脚背或小腿下段真实碰到身前球，打出高速低平球并显示金黄高光。<b>地面球和凌空球都可发动，没有高度限制</b>；凌空出球会迅速向下压低。脚底板踹球不算；双方顶牛争近身球时不发动。可配合任意方向，每次双击最多成功发动一次。\n\n" +
        "<b>瞬发射门 · 按射门</b>\n小键盘 Enter 执行射门，摆腿速度提高 25%。地面和凌空普通射门都可用，可与双击发动的掠地瞬击同时使用；动作结束后恢复。\n\n" +
        "<b>空中堡垒 · 按 ↑</b>\n跳起比普通跳跃更高；高度已在此前技能基础上降低 5%。球在身后也可发动。\n\n" +
        "<b>强力抢断 · 身体接触自动</b>\n与身前范志毅的上半身发生侧向身体接触时，在地面或空中对抗中更易胜出，让对方短暂轻微失衡。单独碰球或腿脚接触不发动，球在赵鹏身后也可使用。\n\n" +
        "<b>紧急回防 · 向后移动自动</b>\n按 → 后退时显示回防技能动画，不受球位限制。当前总回退速度为削弱前范志毅的回退水平；此速度也已作为经典模式的基础调整。"
    };

    public static void Open(Component panel)
    {
        if (panel == null) return;
        Canvas canvas = panel.GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : panel.transform;
        Transform previous = parent.Find("Game Rules");
        if (previous != null) { previous.SetAsLastSibling(); return; }
        GameObject overlay = new GameObject("Game Rules", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlay.transform.SetParent(parent, false);
        RectTransform root = (RectTransform)overlay.transform;
        Stretch(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        overlay.GetComponent<Image>().color = new Color(.025f, .055f, .09f, .98f);
        GameRulesView view = overlay.AddComponent<GameRulesView>();
        Font font = Font.CreateDynamicFontFromOSFont(new string[] { "Microsoft YaHei", "SimHei", "Arial" }, 24);
        Text heading = MakeText("Rules Heading", root, font, 30, TextAnchor.MiddleLeft);
        Stretch(heading.rectTransform, new Vector2(.06f, 1f), new Vector2(.94f, 1f), new Vector2(0f, -58f), new Vector2(-110f, -8f));
        heading.text = "游戏规则与操作";
        Button close = MakeButton("Rules Close", root, font, "关闭", delegate { overlay.SetActive(false); UnityEngine.Object.Destroy(overlay); });
        Stretch(close.GetComponent<RectTransform>(), new Vector2(.94f, 1f), new Vector2(.94f, 1f), new Vector2(-100f, -53f), new Vector2(0f, -13f));
        for (int i = 0; i < view.tabs.Length; i++)
        {
            int page = i;
            Button tab = MakeButton("Rules Tab " + i, root, font, i == 0 ? "基本规则 / 按键" : i == 1 ? "范志毅技能" : "赵鹏技能", delegate { view.SelectPage(page); });
            float left = .06f + .88f * i / 3f;
            Stretch(tab.GetComponent<RectTransform>(), new Vector2(left, 1f), new Vector2(left + .88f / 3f, 1f), new Vector2(0f, -106f), new Vector2(-8f, -66f));
            view.tabs[i] = tab;
        }
        view.pageTitle = MakeText("Rules Page Title", root, font, 21, TextAnchor.MiddleLeft);
        Stretch(view.pageTitle.rectTransform, new Vector2(.06f, 1f), new Vector2(.94f, 1f), new Vector2(0f, -146f), new Vector2(0f, -112f));
        GameObject viewport = new GameObject("Rules Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        viewport.transform.SetParent(root, false);
        RectTransform viewportRect = (RectTransform)viewport.transform;
        Stretch(viewportRect, new Vector2(.06f, 0f), new Vector2(.94f, 1f), new Vector2(0f, 48f), new Vector2(0f, -152f));
        viewport.GetComponent<Image>().color = new Color(.05f, .09f, .14f, 1f);
        view.content = MakeText("Rules Content", viewportRect, font, 22, TextAnchor.UpperLeft);
        view.content.supportRichText = true;
        view.content.horizontalOverflow = HorizontalWrapMode.Wrap;
        view.content.verticalOverflow = VerticalWrapMode.Overflow;
        view.content.lineSpacing = 1.18f;
        RectTransform textRect = view.content.rectTransform;
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(.5f, 1f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(-28f, 0f);
        ContentSizeFitter fitter = view.content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        view.scroll = viewport.GetComponent<ScrollRect>();
        view.scroll.viewport = viewportRect;
        view.scroll.content = textRect;
        view.scroll.horizontal = false;
        view.scroll.movementType = ScrollRect.MovementType.Clamped;
        view.scroll.scrollSensitivity = 32f;
        Text hint = MakeText("Rules Hint", root, font, 18, TextAnchor.MiddleCenter);
        Stretch(hint.rectTransform, new Vector2(.06f, 0f), new Vector2(.94f, 0f), new Vector2(0f, 8f), new Vector2(0f, 40f));
        hint.text = "滚轮或拖动正文查看完整介绍 · 点击上方切换球员技能";
        view.SelectPage(0);
        overlay.transform.SetAsLastSibling();
    }

    private void SelectPage(int page)
    {
        pageTitle.text = titles[page];
        content.text = pages[page];
        for (int i = 0; i < tabs.Length; i++)
            tabs[i].GetComponent<Image>().color = i == page ? new Color(.15f, .42f, .55f) : new Color(.12f, .18f, .24f);
        Canvas.ForceUpdateCanvases();
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1f;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 lower, Vector2 upper)
    { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = lower; rect.offsetMax = upper; }

    private static Text MakeText(string name, Transform parent, Font font, int size, TextAnchor alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);
        Text text = obj.GetComponent<Text>();
        text.font = font; text.fontSize = size; text.color = Color.white; text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static Button MakeButton(string name, Transform parent, Font font, string label, UnityEngine.Events.UnityAction action)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);
        Button button = obj.GetComponent<Button>();
        button.targetGraphic = obj.GetComponent<Image>();
        Text text = MakeText(name + " Label", obj.transform, font, 20, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        text.text = label;
        button.onClick.AddListener(action);
        return button;
    }
}

