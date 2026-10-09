using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Runs in both styles. Continuous entry checks share the native scoring and
// restart coroutines, so one-shot trigger overlap cannot miss corner entries.
public sealed class BallBoundaryGuard : MonoBehaviour
{
    private Collider2D ballCollider;
    private MonoBehaviour manager;
    private FieldInfo stopping, goaling;
    private MethodInfo restartOut;
    private float enableAfter;
    private readonly List<Collider2D> goals = new List<Collider2D>();
    private readonly List<bool> playerOneGoals = new List<bool>();
    private readonly List<float> goalFloors = new List<float>();
    private readonly List<bool> enteringGoals = new List<bool>();
    private Bounds previousBall;
    private Vector2 previousVelocity;

    public void Initialize(Component ball)
    {
        enableAfter = Time.time + .1f;
        ballCollider = ball.GetComponent<Collider2D>();
        previousBall = ballCollider.bounds;
        previousVelocity = ballCollider.attachedRigidbody.velocity;
        Assembly game = ball.GetType().Assembly;
        Type managerType = game.GetType("GameManager");
        Type goalType = game.GetType("GoalTrigger");
        if (managerType == null || goalType == null) return;
        manager = UnityEngine.Object.FindObjectOfType(managerType) as MonoBehaviour;
        stopping = managerType.GetField("isStopping", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        goaling = managerType.GetField("isGoaling", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        restartOut = managerType.GetMethod("BallOut");
        FieldInfo owningSide = goalType.GetField("isPlayer1Goal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (owningSide == null) return;
        foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(goalType))
        {
            Component goal = item as Component;
            Collider2D mouth = goal != null ? goal.GetComponent<Collider2D>() : null;
            if (mouth == null) continue;
            goals.Add(mouth);
            playerOneGoals.Add((bool)owningSide.GetValue(goal));
            goalFloors.Add(FindGoalFloor(mouth));
            enteringGoals.Add(false);
        }
    }

    public void ResetRally()
    {
        // Native Ball.Reset moves its Transform after the physics step. Sync
        // the collider and let the new serve settle before testing the line.
        Physics2D.SyncTransforms();
        enableAfter = Time.time + .1f;
        if (ballCollider != null) previousBall = ballCollider.bounds;
        if (ballCollider != null) previousVelocity = ballCollider.attachedRigidbody.velocity;
        for (int i = 0; i < enteringGoals.Count; i++) enteringGoals[i] = false;
    }

    public static float FindGoalFloor(Collider2D mouth)
    {
        Bounds bounds = mouth.bounds;
        float side = mouth.transform.position.x > 0f ? 1f : -1f;
        float x = side > 0f ? bounds.min.x - .2f : bounds.max.x + .2f;
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(new Vector2(x, bounds.center.y), Vector2.down, 20f))
        {
            if (hit.collider == null || hit.collider.isTrigger || hit.normal.y < .5f) continue;
            string root = hit.collider.transform.root.name;
            if (root == "Fan" || root == "Zhao" || hit.collider.CompareTag("Ball")) continue;
            return hit.point.y;
        }
        return bounds.min.y;
    }

    public bool TryScoreCrossing()
    {
        if (Time.timeScale <= 0f || ballCollider == null || manager == null || GameAIMod.BallOutThisRally ||
            (bool)stopping.GetValue(manager) || (bool)goaling.GetValue(manager)) return false;
        Rigidbody2D moving = ballCollider.attachedRigidbody;
        Bounds current = ballCollider.bounds;
        for (int i = 0; i < goals.Count; i++)
        {
            Bounds goal = goals[i].bounds;
            float side = goals[i].transform.position.x > 0f ? 1f : -1f;
            float line = side > 0f ? goal.min.x : goal.max.x;
            float oldFront = side > 0f ? previousBall.max.x : previousBall.min.x;
            float newFront = side > 0f ? current.max.x : current.min.x;
            if (side * (newFront - line) < 0f) { enteringGoals[i] = false; continue; }
            if (moving == null) continue;
            bool crossingNow = side * (oldFront - line) < 0f &&
                Mathf.Max(side * moving.velocity.x, side * previousVelocity.x) > .05f;
            if (crossingNow) enteringGoals[i] = true;
            // Initial trigger overlap can be too high while a shot grazes the
            // upper corner. Keep that genuine entry alive until it retreats
            // onto the pitch or is declared out, and recheck its later descent.
            if (!enteringGoals[i] || side * (newFront - oldFront) < -.02f) continue;
            float crossing = crossingNow ? Mathf.Clamp01((line - oldFront) / (newFront - oldFront)) : 1f;
            float bottom = Mathf.Lerp(previousBall.min.y, current.min.y, crossing);
            float top = Mathf.Lerp(previousBall.max.y, current.max.y, crossing);
            if (bottom < goalFloors[i] - .06f || top > FindGoalCeiling(goals[i]) + .02f) continue;
            // Low rollers may pass below the native trigger rectangle. The
            // physical pitch floor and bar define the real goal opening.
            manager.StartCoroutine((IEnumerator)manager.GetType().GetMethod("Goal").Invoke(manager, new object[] { playerOneGoals[i] }));
            return true;
        }
        return false;
    }

    public static float FindGoalCeiling(Collider2D mouth)
    {
        Bounds bounds = mouth.bounds;
        float side = mouth.transform.position.x > 0f ? 1f : -1f;
        float line = side > 0f ? bounds.min.x : bounds.max.x;
        float floor = FindGoalFloor(mouth);
        // Measure the underside of the actual bar, in both editions; trigger
        // geometry alone cannot tell a bar rebound from a valid corner entry.
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(new Vector2(line + side * .02f, floor + .1f), Vector2.up, 20f))
        {
            if (hit.collider == null || hit.collider.isTrigger || hit.normal.y > -.5f ||
                mouth.transform.parent == null || !hit.collider.transform.IsChildOf(mouth.transform.parent)) continue;
            return hit.point.y;
        }
        return bounds.max.y;
    }

    private void FixedUpdate()
    {
        if (Time.timeScale <= 0f || ballCollider == null) return;
        if (Time.time < enableAfter) { previousBall = ballCollider.bounds; previousVelocity = ballCollider.attachedRigidbody.velocity; return; }
        if (TryScoreCrossing()) { previousBall = ballCollider.bounds; previousVelocity = ballCollider.attachedRigidbody.velocity; return; }
        if (manager == null ||
            stopping == null || goaling == null || restartOut == null ||
            GameAIMod.BallOutThisRally || (bool)stopping.GetValue(manager) || (bool)goaling.GetValue(manager)) return;
        Bounds ball = ballCollider.bounds;
        for (int i = 0; i < goals.Count; i++)
        {
            Collider2D mouth = goals[i];
            if (mouth == null) continue;
            Bounds goal = mouth.bounds;
            float side = mouth.transform.position.x > 0f ? 1f : -1f;
            float line = side > 0f ? goal.min.x : goal.max.x;
            float oldEdge = side > 0f ? previousBall.min.x : previousBall.max.x;
            float newEdge = side > 0f ? ball.min.x : ball.max.x;
            bool fullCrossing = side * (oldEdge - line) < 0f && side * (newEdge - line) >= 0f;
            float crossing = fullCrossing ? Mathf.Clamp01((line - oldEdge) / (newEdge - oldEdge)) : 1f;
            bool aboveBar = Mathf.Lerp(previousBall.max.y, ball.max.y, crossing) > FindGoalCeiling(mouth) + .02f;
            bool belowFloor = Mathf.Lerp(previousBall.min.y, ball.min.y, crossing) < goalFloors[i] - .06f;
            bool invalidCrossing = fullCrossing && (!enteringGoals[i] || aboveBar || belowFloor);
            bool behind = mouth.transform.position.x > 0f ?
                ball.min.x > goal.max.x + .02f : ball.max.x < goal.min.x - .02f;
            if (!invalidCrossing && !behind) continue;
            // Latch before starting the native whistle/restart coroutine, so a
            // rebound from the back of the net cannot subsequently score.
            GameAIMod.OnBallOut(ballCollider);
            PlayerSkills.ResetAll();
            manager.StartCoroutine((IEnumerator)restartOut.Invoke(manager, new object[] { playerOneGoals[i] }));
            return;
        }
        previousBall = ballCollider.bounds;
        previousVelocity = ballCollider.attachedRigidbody.velocity;
    }
}

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
    private static bool ballOutThisRally;

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
        public bool power;
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
        public float ownGoalLine = 6f;
        public float nativeSpeed = 5f;
        public float jumpSpeed = 4f;
        public bool volleyDefense;
        public bool contesting, highBallPlan, interceptJump;
        public float contestUntil = -1f, interceptTime, interceptX;
        public float leftGoalLine = -6f, rightGoalLine = 6f;
        public Collider2D ballCollider;
        public Collider2D[] playerColliders;
        public HingeJoint2D[] joints;
        public readonly ContactPoint2D[] contacts = new ContactPoint2D[32];
        public Component manager;
        public FieldInfo stoppingField;
    }

    public static void SetupMenu(Component panel)
    {
        states.Clear();
        ballBody = null;
        ballOutThisRally = false;
        BurstRangeTests.Boot();
        menuPanel = panel;
        ControlBindings.EnsureLoaded();
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
            text.text = "玩家1（范）：\n移动跳跃：" +
                ControlBindings.KeyName(ControlBindings.Get(true, GameControlAction.Jump)) + ControlBindings.KeyName(ControlBindings.Get(true, GameControlAction.Left)) +
                ControlBindings.KeyName(ControlBindings.Get(true, GameControlAction.Down)) + ControlBindings.KeyName(ControlBindings.Get(true, GameControlAction.Right)) +
                "\n射门：" + ControlBindings.KeyName(ControlBindings.Get(true, GameControlAction.Kick)) +
                "\n大力射门：" + ControlBindings.KeyName(ControlBindings.Get(true, GameControlAction.PowerKick)) +
                "\n头球：" + ControlBindings.KeyName(ControlBindings.Get(true, GameControlAction.Head)) + "（经典模式也可使用）\n\n玩家2（赵）：\n移动跳跃：" +
                ControlBindings.KeyName(ControlBindings.Get(false, GameControlAction.Jump)) + ControlBindings.KeyName(ControlBindings.Get(false, GameControlAction.Down)) +
                ControlBindings.KeyName(ControlBindings.Get(false, GameControlAction.Left)) + ControlBindings.KeyName(ControlBindings.Get(false, GameControlAction.Right)) +
                "\n射门：" + ControlBindings.KeyName(ControlBindings.Get(false, GameControlAction.Kick)) +
                "\n大力射门：" + ControlBindings.KeyName(ControlBindings.Get(false, GameControlAction.PowerKick)) +
                "\n头球：" + ControlBindings.KeyName(ControlBindings.Get(false, GameControlAction.Head)) + "（经典模式也可使用）\n\n在特技模式下用赵鹏按大力射门键，有奇效！";
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 18;
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
            AddChoice(template, overlay.transform, "键位设置", -110f, delegate
            {
                ControlsSettingsPanel.Open(parent, template, UpdateRulesIntroduction);
            });
        }
        AddChoice(template, overlay.transform, "返回", chooseOpponent ? -160f : -200f, delegate
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
        states.Clear();
        ballBody = null;
        ballOutThisRally = false;
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
        ballOutThisRally = false;
        BallBoundaryGuard boundary = UnityEngine.Object.FindObjectOfType<BallBoundaryGuard>();
        if (boundary != null) boundary.ResetRally();
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
            state.contestUntil = -1f;
            state.contesting = state.highBallPlan = state.interceptJump = false;
        }
    }

    // Both native triggers can be entered during the two-second out-of-bounds
    // restart. Latch the first out event until Ball.Reset starts a new rally.
    public static void OnBallOut(Collider2D candidate)
    {
        if (candidate != null && candidate.CompareTag("Ball")) ballOutThisRally = true;
    }

    public static bool ShouldStartOut(Collider2D candidate)
    {
        BallBoundaryGuard boundary = candidate != null ? candidate.GetComponent<BallBoundaryGuard>() : null;
        if (boundary != null)
        {
            // Overlap with the high out trigger is not a completed exit. A
            // shot can clip the bar and still drop into the net. The guard
            // resolves valid/invalid full-ball crossings at the physics rate.
            boundary.TryScoreCrossing();
            return false;
        }
        if (candidate != null)
        {
            Component manager = null;
            Component ball = candidate.GetComponent(Type.GetType("Ball, Assembly-CSharp"));
            if (ball != null) manager = UnityEngine.Object.FindObjectOfType(ball.GetType().Assembly.GetType("GameManager")) as Component;
            if (manager != null && (bool)manager.GetType().GetField("isGoaling", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(manager)) return false;
        }
        OnBallOut(candidate);
        return true;
    }

    internal static bool BallOutThisRally { get { return ballOutThisRally; } }

    public static void AttachBallBoundaryGuard(Component ball)
    {
        if (ball == null || ball.GetComponent<BallBoundaryGuard>() != null) return;
        ball.gameObject.AddComponent<BallBoundaryGuard>().Initialize(ball);
    }

    public static bool CanScoreGoal(Component trigger, Collider2D candidate)
    {
        if (ballOutThisRally || trigger == null || candidate == null || !candidate.CompareTag("Ball")) return false;
        // The enter callback runs only once. If the ball grazes the upper bar
        // at first overlap, the continuous guard must still see its later entry.
        if (candidate.GetComponent<BallBoundaryGuard>() != null) return false;
        Component manager = UnityEngine.Object.FindObjectOfType(trigger.GetType().Assembly.GetType("GameManager")) as Component;
        if (manager != null && ((bool)manager.GetType().GetField("isGoaling", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(manager) ||
            (bool)manager.GetType().GetField("isStopping").GetValue(manager))) return false;
        Collider2D mouth = trigger.GetComponent<Collider2D>();
        Rigidbody2D movingBall = candidate.attachedRigidbody;
        if (mouth == null || movingBall == null) return false;
        float direction = trigger.transform.position.x > 0f ? 1f : -1f;
        if (movingBall.velocity.x * direction <= .05f) return false;
        // Count only an inward crossing with the whole ball under the bar.
        Bounds goal = mouth.bounds;
        Bounds ball = candidate.bounds;
        return ball.min.y >= BallBoundaryGuard.FindGoalFloor(mouth) - .06f && ball.max.y <= BallBoundaryGuard.FindGoalCeiling(mouth) + .02f;
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
        if (!IsAI(controller)) return PlayerSkills.RescueAxis(controller, ControlBindings.GetHorizontal(controller));
        AIState state = GetState(controller);
        UpdateDecision(controller, state);
        return PlayerSkills.RescueAxis(controller, state.axis);
    }

    // The original FixedUpdate has an extra horizontal-force branch. Give both
    // AI sides the same extra push only during a reachable, close contest.
    // Preserve the original check for human input.
    public static float GetExtraPushGate(string axisName, Component controller)
    {
        if (!IsAI(controller)) return PlayerSkills.RescueAxis(controller, ControlBindings.GetHorizontal(controller));
        AIState state = GetState(controller);
        UpdateDecision(controller, state);
        return state.contesting ? -1f : 0f;
    }

    public static bool GetButtonDown(KeyCode key, Component controller)
    {
        if ((key == KeyCode.W || key == KeyCode.UpArrow) && PlayerSkills.RescueButton(controller, true)) return true;
        if ((key == KeyCode.None || key == KeyCode.Keypad0) && controller != null && controller.name == "Zhao" && PlayerSkills.RescueButton(controller, false)) return true;
        if (!IsAI(controller)) return ControlBindings.GetNativeButtonDown(key, controller);
        AIState state = GetState(controller);
        UpdateDecision(controller, state);
        // The game's PlayerInput maps each side to these keys.
        if (key == KeyCode.W || key == KeyCode.UpArrow) return state.jump;
        if (key == KeyCode.K || key == KeyCode.KeypadEnter)
        {
            if (state.kick && state.power) PowerShot.Request(controller);
            return state.kick;
        }
        if (key == KeyCode.J) return state.head;
        // The original Zhao input has no head key assigned, but its AI can use the move.
        if ((key == KeyCode.None || key == KeyCode.Keypad0) && controller.gameObject.name == "Zhao") return state.head;
        return false;
    }

    public static bool GetDownCommand(Component controller)
    {
        if (!IsAI(controller)) return ControlBindings.GetActionDown(GameControlAction.Down, controller);
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
            {
                FieldInfo speed = type.GetField("maxVelocity");
                FieldInfo jump = type.GetField("jumpForce");
                if (speed != null) state.nativeSpeed = (float)speed.GetValue(controller);
                float rigMass = 0f;
                foreach (Rigidbody2D limb in controller.GetComponentsInChildren<Rigidbody2D>()) rigMass += limb.mass;
                if (jump != null && rigMass > 0f)
                    state.jumpSpeed = Mathf.Clamp(PlayerSkills.GetJumpForce((float)jump.GetValue(controller), controller) *
                        Time.fixedDeltaTime * Time.fixedDeltaTime / rigMass, 3f, 8f);
                Type goals = type.Assembly.GetType("GoalTrigger");
                if (goals != null)
                    foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(goals))
                    {
                        Component goal = item as Component;
                        Collider2D mouth = goal != null ? goal.GetComponent<Collider2D>() : null;
                        if (mouth == null) continue;
                        if (goal.transform.position.x > 0f) state.rightGoalLine = mouth.bounds.min.x;
                        else state.leftGoalLine = mouth.bounds.max.x;
                    }
                state.ownGoalLine = controller.name == "Fan" ? state.leftGoalLine : state.rightGoalLine;
            }
            state.playerColliders = controller.GetComponentsInChildren<Collider2D>();
            state.joints = controller.GetComponentsInChildren<HingeJoint2D>();
            Rigidbody2D football = FindBall();
            state.ballCollider = football != null ? football.GetComponent<Collider2D>() : null;
            Type managerType = type.Assembly.GetType("GameManager");
            state.manager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
            state.stoppingField = managerType.GetField("isStopping");
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
        state.jump = state.kick = state.head = state.power = false;
        state.down = false;
        state.volleyDefense = false;
        state.contesting = state.highBallPlan = state.interceptJump = false;

        Rigidbody2D ball = FindBall();
        Rigidbody2D player = state.body;
        if (ball == null || player == null || Time.timeScale <= 0f || ballOutThisRally ||
            (state.manager != null && state.stoppingField != null && (bool)state.stoppingField.GetValue(state.manager))) return;

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
        state.contesting = InBallContest(controller, state, ball);

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
        if (state.contesting)
        {
            // A squeezed ball needs a planted gait, not a kick animation that
            // takes away the support foot. Use the same direction input as a
            // human, towards the opponent, even after the players swap sides.
            state.serveApproach = false;
            state.pendingKickTap = -10f;
            float opponentDirection = state.opponentBody.position.x - playerPosition.x;
            float pressure = .85f;
            foreach (HingeJoint2D joint in state.joints)
                if (joint != null && joint.connectedBody != null && Vector2.Distance(joint.transform.TransformPoint(joint.anchor),
                    joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)) > .075f * Mathf.Abs(controller.transform.lossyScale.x) / .8f)
                { pressure = .45f; break; }
            if (Mathf.Abs(player.angularVelocity) > 120f) pressure = .45f;
            state.axis = (Mathf.Abs(opponentDirection) > .05f ? Mathf.Sign(opponentDirection) : attackDirection) * pressure;
            return;
        }
        if (!serving && UpdateZhaoVolleyDefense(controller, state, ball)) return;
        if (!serving && UpdateHighBallIntercept(controller, state, ball)) return;
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
        state.pendingKickTap = -10f;
        if (distance < kickReach && forwardBall > -0.65f &&
            footGap > -0.3f && footGap < 2.15f &&
            now - state.lastKick > kickCooldown)
        {
            state.kick = true;
            state.lastKick = now;
            state.power = !duel && (controller.name == "Zhao" || !PlayerSkills.Enabled);
        }
        float headCooldown = duel ? 0.3f : 0.45f;
        if ((!PlayerSkills.Enabled || !state.kick) && Mathf.Abs(forwardHead) < 1.45f && forwardHead > -0.55f &&
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

    private static bool InBallContest(Component controller, AIState state, Rigidbody2D ball)
    {
        if (state.opponentBody == null || state.ballCollider == null) return false;
        float scale = Mathf.Abs(controller.transform.lossyScale.x) / .8f;
        float ours = Mathf.Abs(ball.position.x - state.body.position.x);
        float theirs = Mathf.Abs(ball.position.x - state.opponentBody.position.x);
        float separation = Mathf.Abs(state.body.position.x - state.opponentBody.position.x);
        float headY = state.headTransform != null ? state.headTransform.position.y : state.body.position.y + .9f * scale;
        bool nearby = ours < 1.8f * scale && theirs < 1.8f * scale && separation < 2.5f * scale &&
            ball.position.y < headY + .6f * scale && Mathf.Abs(ball.position.y - state.body.position.y) < 2.4f * scale;
        if (!nearby) { state.contestUntil = -1f; return false; }
        bool ownTouch = false, opponentTouch = false, bodyTouch = false;
        int count = state.ballCollider.GetContacts(state.contacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = state.contacts[i];
            Collider2D other = contact.collider == state.ballCollider ? contact.otherCollider : contact.collider;
            if (other == null || other.isTrigger) continue;
            ownTouch |= other.transform.IsChildOf(controller.transform);
            opponentTouch |= other.transform.IsChildOf(state.opponentBody.transform.parent);
        }
        foreach (Collider2D limb in state.playerColliders)
        {
            if (limb == null || limb.isTrigger) continue;
            count = limb.GetContacts(state.contacts);
            for (int i = 0; i < count; i++)
            {
                ContactPoint2D contact = state.contacts[i];
                Collider2D other = contact.collider == limb ? contact.otherCollider : contact.collider;
                if (other != null && !other.isTrigger && other.transform.IsChildOf(state.opponentBody.transform.parent) &&
                    Mathf.Abs(contact.normal.x) > .3f) { bodyTouch = true; break; }
            }
            if (bodyTouch) break;
        }
        bool between = ball.position.x >= Mathf.Min(state.body.position.x, state.opponentBody.position.x) - .15f * scale &&
            ball.position.x <= Mathf.Max(state.body.position.x, state.opponentBody.position.x) + .15f * scale;
        bool closing = between && separation < 1.15f * scale &&
            Mathf.Abs(state.body.position.y - state.opponentBody.position.y) < 1.6f * scale;
        if ((ownTouch && opponentTouch) || bodyTouch || closing) state.contestUntil = Time.time + .12f;
        // Contact manifolds can change for a frame without ending a squeeze.
        return Time.time <= state.contestUntil;
    }

    private static void ForecastBall(Rigidbody2D ball, float ahead, out Vector2 position, out Vector2 velocity)
    {
        if (PlayerSkills.TryPredictFanVolley(ahead, out position, out velocity)) return;
        position = ball.position;
        velocity = ball.velocity;
        float gravity = Physics2D.gravity.y * ball.gravityScale;
        for (float elapsed = 0f; elapsed < ahead - .0001f; )
        {
            float step = Mathf.Min(Time.fixedDeltaTime, ahead - elapsed);
            velocity.y += gravity * step;
            velocity *= 1f / (1f + ball.drag * step);
            position += velocity * step;
            elapsed += step;
        }
    }

    private static float InterceptRunSpeed(Component controller, AIState state, float movement)
    {
        bool fan = controller.name == "Fan";
        float direction = fan ? 1f : -1f;
        float multiplier = movement * direction < 0f ? PlayerMovement.BackwardMultiplier * (fan ? PlayerMovement.FanRetreatReduction : 1f) :
            (fan ? 1f : PlayerMovement.ZhaoForwardMultiplier);
        return state.nativeSpeed * multiplier * PlayerSkills.GetMovementSkillMultiplier(controller, Mathf.Sign(movement));
    }

    private static bool UpdateHighBallIntercept(Component controller, AIState state, Rigidbody2D ball)
    {
        float scale = Mathf.Abs(controller.transform.lossyScale.y) / .8f;
        float direction = controller.name == "Fan" ? 1f : -1f;
        float headY = state.headTransform != null ? state.headTransform.position.y : state.body.position.y + .9f * scale;
        float headX = state.headTransform != null ? state.headTransform.position.x : state.body.position.x;
        if (ball.position.y < headY + .3f * scale && !(ball.velocity.y > 3f && ball.position.y > headY - .15f * scale)) return false;
        state.highBallPlan = true;
        bool grounded = state.groundField != null && (bool)state.groundField.GetValue(controller);
        float gravity = Mathf.Max(.1f, -Physics2D.gravity.y * state.body.gravityScale);
        float apexTime = Mathf.Clamp(state.jumpSpeed / gravity, .12f, .65f);
        float radius = state.ballCollider != null ? state.ballCollider.bounds.extents.x : .2f * scale;
        Collider2D headCollider = state.headTransform != null ? state.headTransform.GetComponent<Collider2D>() : null;
        float headRadius = headCollider != null ? headCollider.bounds.extents.y : .3f * scale;
        float verticalReach = radius + headRadius + .12f * scale;
        float target = Mathf.Clamp(ball.position.x, state.leftGoalLine + .4f, state.rightGoalLine - .4f);
        float arrival = 1.6f, launchDelay = 10f;
        bool found = false, needJump = false;
        Vector2 predicted, predictedVelocity;
        // Look for the first descending, reachable contact, not the ball's
        // current overhead position. Running and jumping use each rig's real
        // speed, jump force, gravity and size in both gameplay modes.
        for (float ahead = .06f; ahead <= 1.6f; ahead += .04f)
        {
            ForecastBall(ball, ahead, out predicted, out predictedVelocity);
            target = Mathf.Clamp(predicted.x - (headX - state.body.position.x) - direction * .12f * scale,
                state.leftGoalLine + .4f, state.rightGoalLine - .4f);
            if (predicted.x < state.leftGoalLine + radius || predicted.x > state.rightGoalLine - radius) break;
            if (predictedVelocity.y > .5f) continue;
            if (grounded && predicted.y < headY - verticalReach) break;
            float difference = target - state.body.position.x;
            float reachable = InterceptRunSpeed(controller, state, difference) * ahead * .75f + .45f * scale +
                Mathf.Max(0f, state.body.velocity.x * Mathf.Sign(difference)) * .08f;
            if (Mathf.Abs(difference) > reachable) continue;
            float standingHead = grounded ? headY : headY + state.body.velocity.y * ahead - .5f * gravity * ahead * ahead;
            bool candidateJump = grounded && predicted.y > standingHead + verticalReach;
            float jumpingFor = Mathf.Min(ahead, apexTime);
            float contactHead = candidateJump ? headY + state.jumpSpeed * jumpingFor - .5f * gravity * jumpingFor * jumpingFor : standingHead;
            if (Mathf.Abs(predicted.y - contactHead) > verticalReach) continue;
            arrival = ahead;
            launchDelay = candidateJump ? Mathf.Max(0f, ahead - apexTime) : 10f;
            needJump = candidateJump;
            found = true;
            break;
        }
        state.interceptX = target;
        state.interceptTime = arrival;
        state.interceptJump = found && needJump;
        float movement = target - (state.body.position.x + Mathf.Clamp(state.body.velocity.x, -8f, 8f) * .12f);
        state.axis = Mathf.Abs(movement) < .10f * scale ? 0f : Mathf.Clamp(movement * 2f, -1f, 1f);
        if (!grounded) state.axis = Mathf.Clamp(state.axis, -.55f, .55f);
        if (found && needJump && launchDelay <= .06f && grounded && Time.time - state.lastJump > .72f)
        {
            float predictedSelf = state.body.position.x + state.body.velocity.x * Mathf.Min(arrival, .15f);
            float horizontalReach = InterceptRunSpeed(controller, state, target - predictedSelf) * arrival * .7f + .35f * scale;
            if (Mathf.Abs(target - predictedSelf) <= horizontalReach)
            {
                state.jump = true;
                state.lastJump = Time.time;
            }
        }
        ForecastBall(ball, .08f, out predicted, out predictedVelocity);
        float predictedHead = headY + (grounded ? 0f : state.body.velocity.y * .08f - .5f * gravity * .08f * .08f);
        if (Mathf.Abs(predicted.x - headX - state.body.velocity.x * .08f) < radius + headRadius + .15f * scale &&
            Mathf.Abs(predicted.y - predictedHead) < verticalReach && Time.time - state.lastHead > .5f &&
            Mathf.Abs(state.body.angularVelocity) < 120f && (grounded || state.body.velocity.y < 2.5f))
        {
            state.head = true;
            state.lastHead = Time.time;
        }
        return true;
    }

    private static bool UpdateZhaoVolleyDefense(Component controller, AIState state, Rigidbody2D ball)
    {
        Vector2 predicted, predictedVelocity;
        if (controller.name != "Zhao" || ball.velocity.x < 1f ||
            !PlayerSkills.TryPredictFanVolley(0f, out predicted, out predictedVelocity)) return false;
        state.volleyDefense = true;
        Rigidbody2D body = state.body;
        float headY = state.headTransform != null ? state.headTransform.position.y : body.position.y + .9f;
        float headX = state.headTransform != null ? state.headTransform.position.x : body.position.x;
        float footY = state.foot != null ? state.foot.position.y : body.position.y - 2.2f;
        bool grounded = state.groundField != null && (bool)state.groundField.GetValue(controller);
        float gravity = Physics2D.gravity.y * body.gravityScale;
        float targetX = state.ownGoalLine - .65f;
        float arrival = 1f;
        bool jump = false;
        // Choose the first reachable point before the goal, including the sharp
        // late descent. Stay on the goal side of it instead of chasing the ball.
        for (float ahead = .08f; ahead <= .76f; ahead += .04f)
        {
            if (!PlayerSkills.TryPredictFanVolley(ahead, out predicted, out predictedVelocity)) break;
            if (predicted.x > state.ownGoalLine - .25f) break;
            float candidateX = Mathf.Min(predicted.x + .18f, state.ownGoalLine - .4f);
            float difference = candidateX - body.position.x;
            float speed = state.nativeSpeed * (difference > 0f ? PlayerMovement.BackwardMultiplier : PlayerMovement.ZhaoForwardMultiplier);
            float reachable = speed * ahead * .7f + .55f + Mathf.Max(0f, body.velocity.x * Mathf.Sign(difference)) * .06f;
            if (Mathf.Abs(difference) > reachable) continue;
            float standingHead = grounded ? headY : headY + body.velocity.y * ahead + .5f * gravity * ahead * ahead;
            bool needJump = predicted.y > standingHead + .25f;
            if (needJump)
            {
                float jumpTime = Mathf.Max(0f, ahead - .05f);
                float jumpingHead = headY + state.jumpSpeed * jumpTime + .5f * gravity * jumpTime * jumpTime;
                if (!grounded || predicted.y > jumpingHead + .65f || predicted.y < jumpingHead - .8f) continue;
            }
            else if (predicted.y < footY - .15f) continue;
            targetX = candidateX;
            arrival = ahead;
            jump = needJump;
            break;
        }
        float movement = targetX - (body.position.x + body.velocity.x * .1f);
        state.axis = Mathf.Abs(movement) < .12f ? 0f : Mathf.Clamp(movement * 2.6f, -1f, 1f);
        if (!grounded) state.axis = Mathf.Clamp(state.axis, -.55f, .55f);
        float now = Time.time;
        if (jump && arrival <= .38f && grounded && now - state.lastJump > .55f)
        {
            state.jump = true;
            state.lastJump = now;
        }
        PlayerSkills.TryPredictFanVolley(.08f, out predicted, out predictedVelocity);
        float headGap = predicted.y - (headY + (grounded ? 0f : body.velocity.y * .08f));
        float headDistance = Mathf.Abs(predicted.x - (headX + body.velocity.x * .08f));
        // Heading takes priority over a queued double kick when the descending
        // shot is about to meet the head. All contact remains native physics.
        if (headDistance < 1f && Mathf.Abs(headGap) < .85f && ball.position.y > headY - .65f &&
            now - state.lastHead > .5f && Mathf.Abs(body.angularVelocity) < 120f && (grounded || body.velocity.y < 2.5f))
        {
            state.head = true;
            state.lastHead = now;
            state.pendingKickTap = -10f;
        }
        else
        {
            float footGap = ball.position.y - footY;
            if (Mathf.Abs(ball.position.x - body.position.x) < 1.55f &&
                body.position.x - ball.position.x > -.65f && footGap > -.3f && footGap < 1.55f &&
                now - state.lastKick > .4f)
            {
                state.kick = true;
                state.lastKick = now;
                state.power = true;
            }
        }
        return true;
    }
}

