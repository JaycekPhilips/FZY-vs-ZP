using System;
using System.Reflection;
using UnityEngine;

// Compiled only for the separate experimental edition.
public static class ExperimentScale
{
    public static bool Enabled = true;
    public const float Size = .75f;
    public const float GoalHeight = .8f;
    public static void ScaleGoals(Component context)
    {
        if (!Enabled || context == null) return;
        Type goalType = context.GetType().Assembly.GetType("GoalTrigger");
        if (goalType == null) return;
        foreach (UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(goalType))
        {
            Component goal = item as Component;
            Collider2D mouth = goal != null ? goal.GetComponent<Collider2D>() : null;
            Transform goalRoot = goal != null ? goal.transform.parent : null;
            if (mouth == null || goalRoot == null || goalRoot.GetComponent<ExperimentGoal>() != null) continue;
            // Resize the goal's own hierarchy only, anchored at the pitch floor.
            // Meshes, bars and both triggers follow the same vertical transform.
            float floor = BallBoundaryGuard.FindGoalFloor(mouth);
            goalRoot.gameObject.AddComponent<ExperimentGoal>();
            Vector3 scale = goalRoot.localScale;
            goalRoot.localScale = new Vector3(scale.x, scale.y * GoalHeight, scale.z);
            Vector3 position = goalRoot.position;
            position.y = floor + (position.y - floor) * GoalHeight;
            goalRoot.position = position;
            Physics2D.SyncTransforms();
        }
    }
    public static void ScalePlayer(Component stick)
    {
        if (!Enabled || stick == null || stick.GetComponent<ExperimentActor>() != null) return;
        ExperimentActor actor = stick.gameObject.AddComponent<ExperimentActor>();
        actor.Initialize(stick, true);
        Collider2D[] shapes = stick.GetComponentsInChildren<Collider2D>();
        float bottom = Bottom(shapes);
        stick.transform.localScale *= Size; PlayerSkills.RescaleStanding(stick, Size);
        Physics2D.SyncTransforms();
        stick.transform.position += Vector3.up * (bottom - Bottom(shapes));
        Type playerType = stick.GetType().Assembly.GetType("PlayerController");
        Component player = stick.GetComponent(playerType);
        FieldInfo offset = playerType.GetField("footPosY_OffSet", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        offset.SetValue(player, (float)offset.GetValue(player) * Size);
        FieldInfo radius = playerType.GetField("positionRadius");
        radius.SetValue(player, (float)radius.GetValue(player) * Size);
        Physics2D.SyncTransforms();
    }
    private static float Bottom(Collider2D[] shapes)
    {
        float bottom = float.PositiveInfinity;
        foreach (Collider2D shape in shapes) if (shape.enabled && !shape.isTrigger) bottom = Mathf.Min(bottom, shape.bounds.min.y);
        return float.IsInfinity(bottom) ? 0f : bottom;
    }
    public static void ScaleBall(Component ball)
    {
        if (!Enabled || ball == null || ball.GetComponent<ExperimentActor>() != null) return;
        ExperimentActor actor = ball.gameObject.AddComponent<ExperimentActor>();
        actor.Initialize(ball, false);
        ball.transform.localScale *= Size;
        Physics2D.SyncTransforms();
    }
    public static void OnAction(Component player, string action)
    {
        ExperimentActor actor = player != null ? player.GetComponent<ExperimentActor>() : null;
        if (actor != null) actor.OnAction(action);
    }
    public static void OnBallCollision(Collision2D collision)
    {
        if (!Enabled || collision == null || collision.collider == null) return;
        ExperimentActor striker = collision.collider.GetComponentInParent<ExperimentActor>();
        if (striker == null || !striker.CanCorrect(collision.collider.name)) return;
        Rigidbody2D ball = collision.otherCollider != null ? collision.otherCollider.attachedRigidbody : null;
        ExperimentActor actor = ball != null ? ball.GetComponent<ExperimentActor>() : null;
        if (actor == null) return;
        bool grounded = false;
        ContactPoint2D[] contacts = new ContactPoint2D[16];
        int count = ball.GetContacts(contacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D point = contacts[i];
            Collider2D other = point.collider == collision.otherCollider ? point.otherCollider : point.collider;
            if (other == null || other.isTrigger || other.transform.root.name == "Fan" || other.transform.root.name == "Zhao") continue;
            Vector2 normal = point.normal; if (Vector2.Dot(normal, ball.position - point.point) < 0f) normal = -normal;
            if (normal.y > .5f) grounded = true;
        }
        Vector2 impulse = Vector2.zero;
        foreach (ContactPoint2D point in collision.contacts)
        {
            Vector2 normal = point.normal;
            if (Vector2.Dot(normal, ball.position - point.point) < 0f) normal = -normal;
            impulse += normal * point.normalImpulse;
            if (!grounded) impulse += new Vector2(normal.y, -normal.x) * point.tangentImpulse;
        }
        // Compensate only the real strike's collision impulse, leaving the
        // incoming/free-flight velocity, gravity and ordinary arc law intact.
        ball.AddForce(impulse * (1f / Size - 1f), ForceMode2D.Impulse);
        striker.MarkCorrected();
    }
}

[DefaultExecutionOrder(10000)]
public sealed class ExperimentActor : MonoBehaviour
{
    public Vector3 OriginalScale;
    public float OriginalMass;
    public float OriginalGravity;
    private Animator animator;
    private bool playerActor;
    private float requestedUntil = -1f;
    private string strikeAction;
    private float correctedAt = -1f;
    private Component manager;
    private FieldInfo stopping;
    private Font labelFont;
    public void Initialize(Component source, bool player)
    {
        OriginalScale = source.transform.localScale;
        Rigidbody2D body = source.GetComponent<Rigidbody2D>();
        if (body == null && player) body = source.transform.Find("Body").GetComponent<Rigidbody2D>();
        OriginalMass = body.mass; OriginalGravity = body.gravityScale;
        playerActor = player;
        if (!player) return;
        animator = source.GetComponent<Animator>();
        Type managerType = source.GetType().Assembly.GetType("GameManager");
        manager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
        stopping = managerType.GetField("isStopping");
    }
    public void OnAction(string action)
    {
        if (action == "Kick" || action == "Head") { requestedUntil = Time.time + .75f; strikeAction = action; }
        else requestedUntil = -1f;
    }
    public bool CanCorrect(string part)
    {
        if (!playerActor || correctedAt == Time.fixedTime || Time.time >= requestedUntil || Time.timeScale <= 0f || (manager != null && (bool)stopping.GetValue(manager))) return false;
        bool animated = false;
        foreach (AnimatorClipInfo clip in animator.GetCurrentAnimatorClipInfo(0)) if (clip.clip.name == strikeAction && clip.weight > .1f) animated = true;
        if (!animated) return false;
        return strikeAction == "Head" ? part == "Head" : part == "L_LowLeg" || part == "R_LowLeg" || part.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    public void MarkCorrected() { correctedAt = Time.fixedTime; }
    private void OnGUI()
    {
        if (name != "Fan") return;
        if (labelFont == null) labelFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
        GUIStyle style = new GUIStyle(GUI.skin.label); style.font = labelFont; style.fontSize = 16;
        GUI.Label(new Rect(12, Screen.height - 56, 420, 24), "实验版 · 球员/足球 75% · 球门高度 80%", style);
    }
}

public sealed class ExperimentGoal : MonoBehaviour { }
