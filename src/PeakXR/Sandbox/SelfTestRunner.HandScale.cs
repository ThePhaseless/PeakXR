using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Core;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.08b-hand-scale</c>: the own hands drawn at <c>Avatar.HandScale</c>'s default and at the game's size (1), a
/// screenshot of each. The right hand is measured as drawn: the body mesh baked as it renders, its hand vertices
/// (thumb left out) found by their nearest drawn bone, for the width along the index-to-pinky line, the thickness
/// across the palm and the length from the drawn wrist. Run 1 read the bake through the Scout transform's x0.33
/// twice (7.7 cm at game size against 18-26 cm offline); the bake's frame is now checked against the bones. It fails when
/// the drawn index-to-pinky knuckle span (second joints, on the drawn bones) is off the setting, when the mesh width
/// ratio is far off it (<see cref="MeshRatioSlack"/>), when the game-size width is outside the measured prefab's range (a units
/// slip), when the drawn wrist parts from the drawn forearm, or when the physical hands sit differently at the two
/// settings (drawing must never move the ragdoll). The setting is put back afterwards.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Ratio slack between the drawn knuckle spans and the setting (the hands sway between the samples).</summary>
    const float HandScaleSlack = 0.05f;

    /// <summary>Ratio slack for the baked mesh's width. Its vertices go to their nearest drawn bone, and which ones the
    /// hand gets moves with the arm's pose: at the same 0.50 hand (length 0.110 m both times) run 20 read 0.141 m and
    /// run 21, its arms in another pose (vr.08 elbows 0.40 back against 0.26), 0.166 m. The ratio is judged on the bones; this only catches a mesh
    /// that stopped following them (a ratio near 1).</summary>
    const float MeshRatioSlack = 0.15f;

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
            Length,
            Knuckles;
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
        float ratio = sizes[1].Knuckles > 1e-4f ? sizes[0].Knuckles / sizes[1].Knuckles : 0f;
        float meshRatio = sizes[1].Width > 1e-4f ? sizes[0].Width / sizes[1].Width : 0f;
        notes.Add(
            F(
                $"drawn/game knuckle span {ratio:0.00}, mesh width {meshRatio:0.00} (setting {HandScale.Default:0.00})"
            )
        );
        if (Mathf.Abs(ratio - HandScale.Default) > HandScaleSlack)
            fails.Add(
                F($"drawn knuckle span ratio {ratio:0.00}, setting {HandScale.Default:0.00}")
            );
        if (Mathf.Abs(meshRatio - HandScale.Default) > MeshRatioSlack)
            fails.Add(
                F(
                    $"drawn mesh width ratio {meshRatio:0.00}, setting {HandScale.Default:0.00}: the mesh does not follow the drawn bones"
                )
            );
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
    /// The <paramref name="left"/> hand as drawn this frame: the body mesh baked as it renders (its source mesh is not
    /// readable, run 2), each vertex given to its nearest drawn bone, and the hand's own (under the hand bone, the
    /// thumb's outer joints left out; the thumb's root shares the palm's point) measured. Width along the
    /// index-to-pinky line (second joints: the knuckles share one point), thickness across the palm, length from the
    /// drawn wrist down the hand. Zero when the bake does not line up with the bones.
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
        if (index == null || middle == null || pinky == null)
            return default;
        var across = (index.GetChild(0).position - pinky.GetChild(0).position).normalized;
        var along = Vector3
            .ProjectOnPlane(middle.GetChild(0).position - hand.position, across)
            .normalized;
        var palm = Vector3.Cross(across, along);
        var wrist = DrawnHands.DrawnBone(hand).position;
        float knuckles = Vector3.Distance(
            DrawnPoint(index.GetChild(0)),
            DrawnPoint(pinky.GetChild(0))
        );

        var real = DrawnHands.Bones(skin);
        var drawn = skin.bones;
        var points = new Vector3[drawn.Length];
        var owned = new bool[drawn.Length];
        for (int i = 0; i < drawn.Length; i++)
        {
            var b = i < real.Length ? real[i] : null;
            points[i] = drawn[i] != null ? drawn[i].position : Vector3.positiveInfinity;
            owned[i] =
                b != null
                && b != hand
                && b.IsChildOf(hand)
                && !(
                    b.name.StartsWith("Thumb", StringComparison.Ordinal) && b.name.IndexOf('1') < 0
                );
        }
        var baked = new Mesh();
        try
        {
            skin.BakeMesh(baked, false);
            var verts = baked.vertices;
            // The bake's frame: the renderer's position and rotation, or its whole matrix (the Scout's x0.33 too).
            // Run 1 took the whole matrix and read the hand at a third of its size; the frame whose vertices meet
            // the drawn middle finger is the one the bake is in.
            var tr = skin.transform;
            var rigid = Matrix4x4.TRS(tr.position, tr.rotation, Vector3.one);
            var full = tr.localToWorldMatrix;
            var probe = DrawnHands.DrawnBone(middle.GetChild(0)).position;
            float dRigid = NearestVertex(verts, rigid, probe);
            float dFull = NearestVertex(verts, full, probe);
            var toWorld = dRigid <= dFull ? rigid : full;
            if (Mathf.Min(dRigid, dFull) > 0.1f)
                return default;
            Vector3 lo = Vector3.positiveInfinity,
                hi = Vector3.negativeInfinity;
            foreach (var v in verts)
            {
                var p = toWorld.MultiplyPoint3x4(v);
                if ((p - wrist).sqrMagnitude > 0.25f)
                    continue;
                int best = -1;
                float bestSq = float.PositiveInfinity;
                for (int i = 0; i < points.Length; i++)
                {
                    float sq = (points[i] - p).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        best = i;
                    }
                }
                if (best < 0 || !owned[best])
                    continue;
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
                Knuckles = knuckles,
            };
        }
        finally
        {
            UnityEngine.Object.Destroy(baked);
        }
    }

    /// <summary>Where <paramref name="bone"/> is drawn: carried by its nearest proxied ancestor. Run 22 read the second
    /// finger joints through <see cref="DrawnHands.DrawnBone"/> and got the real bones back (span ratio 1.00 against a
    /// 0.47 mesh): DrawnHands proxies only the bones the meshes list, and those joints are evidently not listed.</summary>
    static Vector3 DrawnPoint(Transform bone)
    {
        var a = bone;
        while (a != null && DrawnHands.DrawnBone(a) == a)
            a = a.parent;
        return a == null
            ? bone.position
            : DrawnHands.DrawnBone(a).TransformPoint(a.InverseTransformPoint(bone.position));
    }

    static float NearestVertex(Vector3[] verts, Matrix4x4 toWorld, Vector3 point)
    {
        float best = float.PositiveInfinity;
        foreach (var v in verts)
            best = Mathf.Min(best, (toWorld.MultiplyPoint3x4(v) - point).sqrMagnitude);
        return Mathf.Sqrt(best);
    }
}
