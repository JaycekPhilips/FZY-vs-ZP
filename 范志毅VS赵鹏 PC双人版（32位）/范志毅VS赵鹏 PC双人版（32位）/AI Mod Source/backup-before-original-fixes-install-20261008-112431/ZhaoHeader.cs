using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Zhao's original Head clip is a placeholder: it never swings the neck or
// torso. Reuse Fan's real heading curves and face them toward Zhao's goal.
// Only native muscle angle targets change; all joints and colliders remain.
public sealed class ZhaoHeader : MonoBehaviour
{
    private Animator animator;
    private RuntimeAnimatorController originalController;
    private AnimatorOverrideController headingController;
    private AnimationClip headingClip;
    private Component stick;
    private FieldInfo headAngle, bodyAngle;
    private int mirroredFrame = -1;

    public static void Attach(Component player)
    {
        if (player == null || player.name != "Zhao" || player.GetComponent<ZhaoHeader>() != null) return;
        ZhaoHeader header = player.gameObject.AddComponent<ZhaoHeader>();
        header.animator = player.GetType().GetField("anim").GetValue(player) as Animator;
        Type stickType = player.GetType().Assembly.GetType("StickManController");
        header.stick = player.GetComponent(stickType);
        header.headAngle = stickType.GetField("head");
        header.bodyAngle = stickType.GetField("body");
        header.PrepareAnimation();
    }

    private bool PrepareAnimation()
    {
        if (headingController != null) return true;
        GameObject fan = GameObject.Find("Fan");
        Animator reference = fan != null ? fan.GetComponent<Animator>() : null;
        if (animator == null || reference == null || reference.runtimeAnimatorController == null) return false;
        foreach (AnimationClip clip in reference.runtimeAnimatorController.animationClips)
            if (clip.name == "Head") { headingClip = clip; break; }
        if (headingClip == null) return false;
        originalController = animator.runtimeAnimatorController;
        headingController = new AnimatorOverrideController(originalController);
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        headingController.GetOverrides(overrides);
        bool replaced = false;
        for (int i = 0; i < overrides.Count; i++)
            if (overrides[i].Key.name == "Head")
            {
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, headingClip);
                replaced = true;
            }
        if (!replaced) { Destroy(headingController); headingController = null; return false; }
        headingController.ApplyOverrides(overrides);
        animator.runtimeAnimatorController = headingController;
        return true;
    }

    public static void BeforeMuscles(Component muscles)
    {
        ZhaoHeader header = muscles != null ? muscles.GetComponent<ZhaoHeader>() : null;
        if (header == null || Time.timeScale <= 0f || header.mirroredFrame == Time.frameCount || !header.PrepareAnimation()) return;
        bool heading = false;
        foreach (AnimatorClipInfo clip in header.animator.GetCurrentAnimatorClipInfo(0))
            if (clip.clip == header.headingClip && clip.weight > .1f) { heading = true; break; }
        if (!heading) return;
        // Fan attacks right; Zhao attacks left. Reflect the actual animation
        // targets before the original joint-driven muscle update consumes them.
        header.headAngle.SetValue(header.stick, -(float)header.headAngle.GetValue(header.stick));
        header.bodyAngle.SetValue(header.stick, -(float)header.bodyAngle.GetValue(header.stick));
        header.mirroredFrame = Time.frameCount;
    }

    private void OnDestroy()
    {
        if (animator != null && animator.runtimeAnimatorController == headingController)
            animator.runtimeAnimatorController = originalController;
        if (headingController != null) Destroy(headingController);
    }
}
