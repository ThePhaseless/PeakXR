using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.08b-hand-scale</c>: the own hands drawn at <c>Avatar.HandScale</c>'s default and at the game's size (1), a
/// screenshot of each. The drawn palm width is measured on the skinned mesh as rendered (baked, palm and fingers
/// without the thumb, across the index-to-pinky knuckle line); it fails when the width ratio is off the setting or
/// when the drawn wrist parts from the drawn forearm. The setting is put back afterwards.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Ratio slack between the measured widths and the setting (the hands sway between the bakes).</summary>
    const float HandScaleSlack = 0.05f;

    IEnumerator HandScaleCheck()
    {
        var fails = new List<string>();
        var notes = new List<string>();
        var c = Character.localCharacter;
        var entry = AvatarConfig.Instance.HandScaleEntry;
        var driver = c != null ? AvatarPoseDriver.Of(c) : null;
        if (entry == null || driver == null || c.refs.mainRenderer == null)
        {
            stepSkip = "no hand scale setting, pose driver or body mesh";
            yield break;
        }
        float before = entry.Value;
        var widths = new float[2];
        var scales = new[] { HandScale.Default, 1f };
        try
        {
            for (int i = 0; i < scales.Length; i++)
            {
                entry.Value = scales[i];
                yield return Wait(0.5f);
                if (scales[i] < 1f && !DrawnHands.Drawn)
                    fails.Add(F($"hands not drawn at {scales[i]:0.00}"));
                widths[i] = DrawnPalmWidth(c, false);
                notes.Add(
                    F(
                        $"palm {widths[i]:0.000} wide at {scales[i]:0.00} ({Rig.RealScale.Real(widths[i]) * 100f:0.0} cm as seen)"
                    )
                );
                if (scales[i] < 1f)
                    CheckWrist(c, fails);
                yield return ShotAt(F($"vr.08b-hand-scale-{scales[i]:0.00}"), HandsMid());
            }
        }
        finally
        {
            entry.Value = before;
        }
        float ratio = widths[1] > 1e-4f ? widths[0] / widths[1] : 0f;
        notes.Add(F($"drawn/game width {ratio:0.00} (setting {HandScale.Default:0.00})"));
        if (Mathf.Abs(ratio - HandScale.Default) > HandScaleSlack)
            fails.Add(F($"drawn width ratio {ratio:0.00}, setting {HandScale.Default:0.00}"));
        Conclude(fails, notes);
    }

    /// <summary>The drawn forearm must end on the drawn hand (no gap at the wrist).</summary>
    static void CheckWrist(Character c, List<string> fails)
    {
        var parts = c.refs.ragdoll.partDict;
        foreach (
            var (fore, hand) in new[]
            {
                (BodypartType.Elbow_L, BodypartType.Hand_L),
                (BodypartType.Elbow_R, BodypartType.Hand_R),
            }
        )
        {
            if (!parts.TryGetValue(fore, out var f) || !parts.TryGetValue(hand, out var h))
                continue;
            var end = DrawnHands.DrawnBone(f.transform).TransformPoint(h.transform.localPosition);
            float gap = Vector3.Distance(end, DrawnHands.DrawnBone(h.transform).position);
            if (gap > 0.005f)
                fails.Add(F($"{hand} drawn {gap:0.000} m off its forearm"));
        }
    }

    /// <summary>Width of the <paramref name="left"/> palm and fingers as drawn this frame: the baked body mesh's
    /// vertices owned by the hand's bones (thumb left out) along the index-to-pinky knuckle line.</summary>
    static float DrawnPalmWidth(Character c, bool left)
    {
        var skin = c.refs.mainRenderer;
        var hand = c.refs
            .ragdoll
            .partDict[left ? BodypartType.Hand_L : BodypartType.Hand_R]
            .transform;
        Transform index = null,
            pinky = null;
        foreach (var t in hand.GetComponentsInChildren<Transform>())
        {
            if (t.name.StartsWith("Index_1", StringComparison.Ordinal))
                index = t;
            else if (t.name.StartsWith("Pinky_1", StringComparison.Ordinal))
                pinky = t;
        }
        if (index == null || pinky == null)
            return 0f;
        // The knuckles share their position in the prefab: the line runs through the second joints.
        var axis = (index.GetChild(0).position - pinky.GetChild(0).position).normalized;
        var bones = DrawnHands.Bones(skin);
        var owned = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            var b = bones[i];
            owned[i] =
                b != null
                && (b == hand || b.IsChildOf(hand))
                && !b.name.StartsWith("Thumb", StringComparison.Ordinal);
        }
        var baked = new Mesh();
        try
        {
            skin.BakeMesh(baked, false);
            var verts = baked.vertices;
            var weights = skin.sharedMesh.boneWeights;
            var toWorld = skin.transform.localToWorldMatrix;
            float lo = float.PositiveInfinity,
                hi = float.NegativeInfinity;
            for (int v = 0; v < verts.Length && v < weights.Length; v++)
            {
                if (!owned[weights[v].boneIndex0])
                    continue;
                float d = Vector3.Dot(toWorld.MultiplyPoint3x4(verts[v]), axis);
                lo = Mathf.Min(lo, d);
                hi = Mathf.Max(hi, d);
            }
            return hi > lo ? hi - lo : 0f;
        }
        finally
        {
            UnityEngine.Object.Destroy(baked);
        }
    }
}
