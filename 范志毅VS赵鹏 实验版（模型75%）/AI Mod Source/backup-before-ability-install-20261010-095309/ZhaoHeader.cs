using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Sample Fan's heading separately and copy only neck and torso targets.
// Zhao retains his native animator, leg angles and sprite orientation.
public sealed class ZhaoHeader : MonoBehaviour
{
    private Animator animator, sampler;
    private GameObject samplingObject;
    private Component stick, sampleStick;
    private FieldInfo headAngle, bodyAngle;
    private readonly List<FieldInfo> legAngles = new List<FieldInfo>();
    private readonly List<float> standingLegAngles = new List<float>();
    private readonly List<SpriteRenderer> legSprites = new List<SpriteRenderer>();
    private readonly List<Vector3> legScales = new List<Vector3>();
    private readonly List<bool> legFlipX = new List<bool>(), legFlipY = new List<bool>();
    private float started = -10f, duration = .4166667f;
    private int sampledFrame = -1;
    private bool heading, backwardHeading;
    private int locomotionState;
    public static void Attach(Component player)
    {
        if (player == null || player.name != "Zhao" || player.GetComponent<ZhaoHeader>() != null) return;
        ZhaoHeader header = player.gameObject.AddComponent<ZhaoHeader>();
        header.animator = player.GetType().GetField("anim").GetValue(player) as Animator;
        header.locomotionState = header.animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        Type stickType = player.GetType().Assembly.GetType("StickManController");
        header.stick = player.GetComponent(stickType);
        header.headAngle = stickType.GetField("head"); header.bodyAngle = stickType.GetField("body");
        foreach (string name in new[] { "L_Up_Leg", "L_Low_Leg", "R_Up_Leg", "R_Low_Leg" })
        { header.legAngles.Add(stickType.GetField(name)); header.standingLegAngles.Add(0f); }
        foreach (SpriteRenderer sprite in player.GetComponentsInChildren<SpriteRenderer>())
            if (sprite.name.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0 || sprite.name.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                header.legSprites.Add(sprite); header.legScales.Add(sprite.transform.localScale);
                header.legFlipX.Add(sprite.flipX); header.legFlipY.Add(sprite.flipY);
            }
    }
    private bool PrepareSampler()
    {
        if (sampler != null) return true;
        GameObject fan = GameObject.Find("Fan");
        Animator reference = fan != null ? fan.GetComponent<Animator>() : null;
        if (reference == null || reference.runtimeAnimatorController == null) return false;
        foreach (AnimationClip clip in reference.runtimeAnimatorController.animationClips) if (clip.name == "Head") duration = clip.length;
        samplingObject = new GameObject("Zhao heading angle sampler");
        samplingObject.hideFlags = HideFlags.HideAndDontSave;
        sampleStick = samplingObject.AddComponent(stick.GetType()); ((Behaviour)sampleStick).enabled = false;
        sampler = samplingObject.AddComponent<Animator>(); sampler.enabled = false;
        sampler.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        sampler.runtimeAnimatorController = reference.runtimeAnimatorController;
        return true;
    }
    public static void OnAction(Component player, string action)
    {
        ZhaoHeader header = player != null ? player.GetComponent<ZhaoHeader>() : null;
        if (header == null) return;
        if (action != "Head") { header.heading = false; return; }
        header.backwardHeading = PlayerSkills.BackwardHeaderPose(player);
        if (!header.PrepareSampler()) return;
        AnimatorStateInfo state = header.animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName("Head") && !state.IsName("Kick") && !state.IsName("Jump")) header.locomotionState = state.fullPathHash;
        for (int i = 0; i < header.legAngles.Count; i++) header.standingLegAngles[i] = (float)header.legAngles[i].GetValue(header.stick);
        header.started = Time.time; header.sampledFrame = -1; header.heading = true;
    }
    public static void BeforeMuscles(Component muscles)
    {
        ZhaoHeader header = muscles != null ? muscles.GetComponent<ZhaoHeader>() : null;
        if (header == null || !header.heading || Time.timeScale <= 0f || header.sampledFrame == Time.frameCount) return;
        float age = Time.time - header.started;
        if (header.backwardHeading && !PlayerSkills.BackwardHeaderPose(muscles)) age = header.duration;
        if (age >= header.duration)
        {
            header.heading = false;
            if (header.animator.GetCurrentAnimatorStateInfo(0).IsName("Head") && header.locomotionState != 0) header.animator.CrossFade(header.locomotionState, .05f, 0);
            header.RestoreLegSprites(); return;
        }
        header.sampler.Play("Head", 0, Mathf.Clamp01(age / header.duration)); header.sampler.Update(0f);
        if (header.backwardHeading)
        {
            // Clockwise torso/neck targets make Zhao arch toward the rear
            // ball. The intact joints and muscles supply the actual motion.
            float wave = Mathf.Sin(Mathf.Clamp01(age / header.duration) * Mathf.PI);
            header.bodyAngle.SetValue(header.stick, -48f * wave);
            header.headAngle.SetValue(header.stick, -65f * wave);
        }
        else header.headAngle.SetValue(header.stick, -(float)header.headAngle.GetValue(header.sampleStick));
        if (!header.backwardHeading) header.bodyAngle.SetValue(header.stick, -(float)header.bodyAngle.GetValue(header.sampleStick));
        for (int i = 0; i < header.legAngles.Count; i++) header.legAngles[i].SetValue(header.stick, header.standingLegAngles[i]);
        header.sampledFrame = Time.frameCount;
    }
    private void RestoreLegSprites()
    {
        for (int i = 0; i < legSprites.Count; i++) if (legSprites[i] != null)
        { legSprites[i].transform.localScale = legScales[i]; legSprites[i].flipX = legFlipX[i]; legSprites[i].flipY = legFlipY[i]; }
    }
    private void LateUpdate() { if (heading || animator.GetCurrentAnimatorStateInfo(0).IsName("Head")) RestoreLegSprites(); }
    private void OnDestroy() { if (samplingObject != null) Destroy(samplingObject); }
}
