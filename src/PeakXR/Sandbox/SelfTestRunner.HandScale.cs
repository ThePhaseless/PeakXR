using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.08b-hand-scale</c>: the own hands drawn at <c>Avatar.HandScale</c>'s default and at the game's size (1), a
/// screenshot of each. The right hand is measured as drawn: the body mesh's hand vertices (thumb left out) skinned
/// here the way the GPU skins them, from the bones the mesh draws and its bind poses, for the width along the
/// index-to-pinky line, the thickness across the palm and the length from the drawn wrist. Run 1 baked the mesh
/// instead and read the Scout transform's x0.33 twice (7.7 cm at game size against 18-26 cm offline). It fails when
/// the width ratio is off the setting, when the game-size width is outside the measured prefab's range (a units
/// slip), when the drawn wrist parts from the drawn forearm, or when the physical hands sit differently at the two
/// settings (drawing must never move the ragdoll). The setting is put back afterwards.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Ratio slack between the measured widths and the setting (the hands sway between the samples).</summary>
    const float HandScaleSlack = 0.05f;

    /// <summary>Game-size width of the open or curled scout hand: 0.18 (a fist) to 0.26 (open, outer joints
    /// straight) from the prefab skinned offline in PeakXR's finger poses.</summary>
    const float MinGameWidth = 0.12f;
    const float MaxGameWidth = 0.32f;

    /// <summary>Most the physical hands' mean distance to their targets may differ between the two settings.</summary>
    const float MaxArmShift = 0.02f;

    struct HandSize
    {
        public float Width,
            Thickness,
            Length;
    }

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
        var sizes = new HandSize[2];
        var armError = new float[2];
        var scales = new[] { HandScale.Default, 1f };
        try
        {
            for (int i = 0; i < scales.Length; i++)
            {
                entry.Value = scales[i];
                yield return Wait(0.5f);
                if (scales[i] < 1f && !DrawnHands.Drawn)
                    fails.Add(F($"hands not drawn at {scales[i]:0.00}"));
                sizes[i] = DrawnHandSize(c, false);
                var s = sizes[i];
                notes.Add(
                    F(
                        $"at {scales[i]:0.00}: {s.Width:0.000} wide, {s.Thickness:0.000} thick, {s.Length:0.000} long (seen {Seen(s.Width)}/{Seen(s.Thickness)}/{Seen(s.Length)} cm)"
                    )
                );
                if (scales[i] < 1f)
                    CheckWrist(c, fails);
                float sum = 0f;
                int n = 0;
                for (int f = 0; f < 30; f++, n++)
                {
                    sum += ArmError(c, driver);
                    yield return null;
                }
                armError[i] = sum / n;
                yield return ShotAt(F($"vr.08b-hand-scale-{scales[i]:0.00}"), HandsMid());
            }
        }
        finally
        {
            entry.Value = before;
        }
        float ratio = sizes[1].Width > 1e-4f ? sizes[0].Width / sizes[1].Width : 0f;
        notes.Add(F($"drawn/game width {ratio:0.00} (setting {HandScale.Default:0.00})"));
        if (Mathf.Abs(ratio - HandScale.Default) > HandScaleSlack)
            fails.Add(F($"drawn width ratio {ratio:0.00}, setting {HandScale.Default:0.00}"));
        if (sizes[1].Width < MinGameWidth || sizes[1].Width > MaxGameWidth)
            fails.Add(
                F(
                    $"game-size width {sizes[1].Width:0.000} outside the prefab's {MinGameWidth:0.00}-{MaxGameWidth:0.00}"
                )
            );
        notes.Add(
            F(
                $"physical hands {armError[0]:0.000} m from their targets at {HandScale.Default:0.00}, {armError[1]:0.000} m at 1"
            )
        );
        if (Mathf.Abs(armError[0] - armError[1]) > MaxArmShift)
            fails.Add(
                F(
                    $"the drawn hands moved the physical ones: {armError[0]:0.000} vs {armError[1]:0.000} m"
                )
            );
        Conclude(fails, notes);
    }

    static string Seen(float units) => F($"{Rig.RealScale.Real(units) * 100f:0.0}");

    /// <summary>Mean distance of both physical hands (ragdoll rigidbodies) from their targets this frame.</summary>
    static float ArmError(Character c, AvatarPoseDriver driver)
    {
        var parts = c.refs.ragdoll.partDict;
        float sum = 0f;
        int n = 0;
        if (
            driver.HandTarget(true, out var tl)
            && parts.TryGetValue(BodypartType.Hand_L, out var hl)
        )
        {
            sum += Vector3.Distance(tl, hl.Rig.position);
            n++;
        }
        if (
            driver.HandTarget(false, out var tr)
            && parts.TryGetValue(BodypartType.Hand_R, out var hr)
        )
        {
            sum += Vector3.Distance(tr, hr.Rig.position);
            n++;
        }
        return n > 0 ? sum / n : 0f;
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

    /// <summary>
    /// The <paramref name="left"/> hand as drawn this frame: its body-mesh vertices (dominant bone the hand or
    /// under it, thumb left out) skinned from the bones the mesh draws now (the proxies while drawn smaller) and
    /// the bind poses, the GPU's own sum. Width along the index-to-pinky line (second joints: the knuckles share one
    /// point), thickness across the palm, length from the drawn wrist down the hand.
    /// </summary>
    static HandSize DrawnHandSize(Character c, bool left)
    {
        var skin = c.refs.mainRenderer;
        var hand = c.refs
            .ragdoll
            .partDict[left ? BodypartType.Hand_L : BodypartType.Hand_R]
            .transform;
        Transform index = null,
            middle = null,
            pinky = null;
        foreach (var t in hand.GetComponentsInChildren<Transform>())
        {
            if (t.name.StartsWith("Index_1", StringComparison.Ordinal))
                index = t;
            else if (t.name.StartsWith("Middle_1", StringComparison.Ordinal))
                middle = t;
            else if (t.name.StartsWith("Pinky_1", StringComparison.Ordinal))
                pinky = t;
        }
        var mesh = skin.sharedMesh;
        if (index == null || middle == null || pinky == null || mesh == null)
            return default;
        var across = (index.GetChild(0).position - pinky.GetChild(0).position).normalized;
        var along = Vector3
            .ProjectOnPlane(middle.GetChild(0).position - hand.position, across)
            .normalized;
        var palm = Vector3.Cross(across, along);
        var wrist = DrawnHands.DrawnBone(hand).position;

        var real = DrawnHands.Bones(skin);
        var drawn = skin.bones;
        var bind = mesh.bindposes;
        var owned = new bool[real.Length];
        var skinning = new Matrix4x4[drawn.Length];
        for (int i = 0; i < real.Length && i < drawn.Length && i < bind.Length; i++)
        {
            var b = real[i];
            owned[i] =
                b != null
                && (b == hand || b.IsChildOf(hand))
                && !b.name.StartsWith("Thumb", StringComparison.Ordinal);
            skinning[i] = drawn[i] != null ? drawn[i].localToWorldMatrix * bind[i] : Matrix4x4.zero;
        }
        var verts = mesh.vertices;
        var weights = mesh.boneWeights;
        Vector3 lo = Vector3.positiveInfinity,
            hi = Vector3.negativeInfinity;
        for (int v = 0; v < verts.Length && v < weights.Length; v++)
        {
            var w = weights[v];
            if (w.boneIndex0 >= owned.Length || !owned[w.boneIndex0])
                continue;
            var p =
                w.weight0 * skinning[w.boneIndex0].MultiplyPoint3x4(verts[v])
                + w.weight1 * skinning[w.boneIndex1].MultiplyPoint3x4(verts[v])
                + w.weight2 * skinning[w.boneIndex2].MultiplyPoint3x4(verts[v])
                + w.weight3 * skinning[w.boneIndex3].MultiplyPoint3x4(verts[v]);
            var d = new Vector3(
                Vector3.Dot(p, across),
                Vector3.Dot(p, palm),
                Vector3.Dot(p - wrist, along)
            );
            lo = Vector3.Min(lo, d);
            hi = Vector3.Max(hi, d);
        }
        if (hi.x < lo.x)
            return default;
        return new HandSize
        {
            Width = hi.x - lo.x,
            Thickness = hi.y - lo.y,
            Length = hi.z,
        };
    }
}
