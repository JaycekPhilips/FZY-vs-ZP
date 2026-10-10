using System;
using System.Reflection;
using UnityEngine;

// Ordinary powered shooting exists in classic mode too. It uses the native
// Kick motion and can launch only from a real, eligible foot-ball collision.
public sealed class PowerShot : MonoBehaviour
{
    public const float HeaderSpeedMultiplier = .9f;
    private float headerUntil = -1f;
    public const float SpeedMultiplier = 1.2f;
    public const float SkillBurstMultiplier = 1.1f;
    private Component controller, manager;
    private Rigidbody2D body, ball;
    private Collider2D ballShape;
    private FieldInfo stopping;
    private int groundMask;
    private bool fan;
    private int requestedFrame = -1, commandFrame = -1;
    private float armedUntil = -1f, flatUntil = -1f, floorHeight;
    public static void Attach(Component player)
    {
        if (player == null || (player.name != "Fan" && player.name != "Zhao") || player.GetComponent<PowerShot>() != null) return;
        PowerShot shot = player.gameObject.AddComponent<PowerShot>();
        shot.controller = player; shot.fan = player.name == "Fan";
        shot.body = player.GetType().GetField("rb").GetValue(player) as Rigidbody2D;
        shot.groundMask = (int)(LayerMask)player.GetType().GetField("ground").GetValue(player);
        Component nativeBall = UnityEngine.Object.FindObjectOfType(player.GetType().Assembly.GetType("Ball")) as Component;
        if (nativeBall != null) { shot.ball = nativeBall.GetComponent<Rigidbody2D>(); shot.ballShape = nativeBall.GetComponent<Collider2D>(); if (nativeBall.GetComponent<HeaderContactSensor>() == null) nativeBall.gameObject.AddComponent<HeaderContactSensor>(); }
        Type managerType = player.GetType().Assembly.GetType("GameManager");
        shot.manager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
        shot.stopping = managerType.GetField("isStopping");
    }
    public static void Request(Component player) { Attach(player); PowerShot shot = player.GetComponent<PowerShot>(); if (shot != null && !shot.Stopped()) shot.requestedFrame = Time.frameCount; }
    public static bool IsPowerCommand(Component player) { PowerShot shot = player.GetComponent<PowerShot>(); return shot != null && (shot.requestedFrame == Time.frameCount || shot.commandFrame == Time.frameCount); }
    public static void OnAction(Component player, string action)
    {
        PowerShot shot = player.GetComponent<PowerShot>(); if (shot == null) return;
        shot.commandFrame = action == "Kick" && shot.requestedFrame == Time.frameCount ? Time.frameCount : -1;
        shot.armedUntil = shot.commandFrame >= 0 ? Time.time + .75f : -1f;
        shot.headerUntil = action == "Head" && !shot.Stopped() ? Time.time + .55f : -1f;
        shot.requestedFrame = -1;
    }
    public static void CompleteHeader(Component player) { PowerShot s = player != null ? player.GetComponent<PowerShot>() : null; if (s != null) s.headerUntil = -1f; }
    private bool SlowHeader(Collider2D limb, bool exiting)
    {
        if (Stopped() || Time.time > headerUntil || limb.name != "Head") return false;
        if (PlayerSkills.RescueHeader(controller)) return true;
        if (!exiting) return false;
        // Fan's assisted cannon header supplies its own reduced target once.
        PlayerSkills skills = controller.GetComponent<PlayerSkills>();
        if (skills != null && skills.AwaitingFanHeader) return false;
        headerUntil = -1f;
        ball.AddForce(ball.velocity * ((HeaderSpeedMultiplier - 1f) * ball.mass), ForceMode2D.Impulse);
        return true;
    }
    private bool Stopped() { return controller == null || ball == null || Time.timeScale <= 0f || GameAIMod.BallOutThisRally || (manager != null && (bool)stopping.GetValue(manager)); }
    private bool InFront() { return ball != null && body != null && (fan ? 1f : -1f) * (ball.position.x - body.position.x) >= -.1f; }
    private bool Contest()
    {
        GameObject opponent = GameObject.Find(fan ? "Zhao" : "Fan");
        if (opponent == null) return false;
        Rigidbody2D other = opponent.transform.Find("Body").GetComponent<Rigidbody2D>();
        return Mathf.Abs(body.position.x - other.position.x) <= 2f && Mathf.Abs(ball.position.x - body.position.x) < 1.9f && Mathf.Abs(ball.position.x - other.position.x) < 1.9f && ball.position.y < Mathf.Max(body.position.y, other.position.y) + 2.5f;
    }
    private bool Strike(Collider2D limb, Vector2 contact, Vector2 normal)
    {
        if (Stopped() || Time.time > armedUntil || !InFront() || (!fan && Contest()) || !PlayerSkills.IsStrikeFoot(controller, limb, contact, normal, !fan && PlayerSkills.Enabled)) return false;
        ball.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        foreach (PowerShot player in UnityEngine.Object.FindObjectsOfType<PowerShot>())
            foreach (Rigidbody2D limbBody in player.GetComponentsInChildren<Rigidbody2D>()) limbBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        Vector2 target;
        if (fan) { FanStrategyTests.PowerStrike(); target = ball.velocity * SpeedMultiplier; }
        else
        {
            float speed = Mathf.Clamp(Mathf.Max(17f, ball.velocity.magnitude * 1.65f), 17f, 23f) * SpeedMultiplier * (PlayerSkills.Enabled ? SkillBurstMultiplier : 1f);
            floorHeight = FindFloor();
            target = new Vector2(-speed, FlatVertical()); flatUntil = Time.time + .6f;
        }
        armedUntil = -1f;
        PlayerSkills.CancelShotFlights();
        ball.AddForce((target - ball.velocity) * ball.mass, ForceMode2D.Impulse);
        if (!fan && PlayerSkills.Enabled) PlayerSkills.ShowPowerBurst(controller);
        return true;
    }
    private float FindFloor()
    {
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(ball.position + Vector2.up * .1f, Vector2.down, 30f, groundMask))
        {
            if (hit.collider == null || hit.collider == ballShape || hit.collider.isTrigger || hit.normal.y < .5f || hit.collider.GetComponentInParent<PowerShot>() != null) continue;
            return hit.point.y + ballShape.bounds.extents.y + .035f;
        }
        return ball.position.y;
    }
    private float FlatVertical() { float clearance = ball.position.y - floorHeight; return clearance > .1f ? -Mathf.Clamp(clearance * 12f, 2f, 16f) : .2f; }
    private void FixedUpdate()
    {
        if (Stopped()) { headerUntil = armedUntil = flatUntil = -1f; return; }
        if (Time.time <= flatUntil) ball.AddForce(Vector2.up * (FlatVertical() - ball.velocity.y) * ball.mass, ForceMode2D.Impulse);
    }
    public static bool OnBallCollision(Collision2D collision)
    {
        if (collision == null || collision.collider == null) return false;
        PowerShot striker = collision.collider.GetComponentInParent<PowerShot>();
        bool launched = false;
        if (striker != null && striker.SlowHeader(collision.collider, false)) return false;
        if (striker != null)
            for (int i = 0; i < collision.contactCount && !launched; i++)
            {
                ContactPoint2D point = collision.GetContact(i); Vector2 normal = point.normal;
                if (Vector2.Dot(normal, striker.ball.position - point.point) < 0) normal = -normal;
                launched = striker.Strike(collision.collider, point.point, normal);
            }
        foreach (PowerShot owner in UnityEngine.Object.FindObjectsOfType<PowerShot>())
            if (owner != striker && striker != null) owner.flatUntil = -1f;
        return launched;
    }
    public static void HeaderContact(Collision2D collision, bool exiting)
    {
        if (collision == null || collision.collider == null || collision.collider.name != "Head") return;
        PowerShot owner = collision.collider.GetComponentInParent<PowerShot>();
        if (owner != null) owner.SlowHeader(collision.collider, exiting);
    }
    public static void ResetAll() { foreach (PowerShot shot in UnityEngine.Object.FindObjectsOfType<PowerShot>()) { shot.headerUntil = shot.armedUntil = shot.flatUntil = -1f; shot.requestedFrame = shot.commandFrame = -1; } }
}

// Apply the ordinary 10% reduction once at separation, so a ball already
// resting against a commanded head is reduced after its actual launch too.
public sealed class HeaderContactSensor : MonoBehaviour
{
    private void OnCollisionStay2D(Collision2D collision) { PowerShot.HeaderContact(collision, false); }
    private void OnCollisionExit2D(Collision2D collision) { PowerShot.HeaderContact(collision, true); }
}

public static class BallFlightSafety
{
    public static bool WillHit(Rigidbody2D ball, Vector2 velocity, Component shooter, float age)
    {
        if (velocity.sqrMagnitude < .001f) return false;
        ContactFilter2D filter = new ContactFilter2D(); filter.SetLayerMask(Physics2D.GetLayerCollisionMask(ball.gameObject.layer)); filter.useTriggers = false;
        RaycastHit2D[] hits = new RaycastHit2D[24];
        int count = ball.Cast(velocity.normalized, filter, hits, velocity.magnitude * Time.fixedDeltaTime + .015f);
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == ball) continue;
            if (hit.collider.GetComponentInParent<PowerShot>() == shooter.GetComponent<PowerShot>() && age <= Time.fixedDeltaTime * 1.5f) continue;
            if (hit.distance <= .002f && velocity.y > 0 && hit.normal.y > .5f) continue;
            if (Vector2.Dot(velocity, hit.normal) < -.05f) return true;
        }
        return false;
    }
}
