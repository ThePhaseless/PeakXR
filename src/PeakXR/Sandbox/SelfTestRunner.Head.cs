using System;
using System.Collections;
using System.Collections.Generic;
using PeakXR.Avatar;
using PeakXR.Interact;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>vr.247-own-head-mirror</c>: the own head is hidden only from the eyes (<see cref="OwnHead"/>). A probe camera
/// 1.5 m in front of the face, rendered once the way a mirror or the photobooth renders (Mirror.cs:7,
/// AirportPhotoboothKiosk.cs:111), must draw every hidden head renderer during its render, the eye camera must draw
/// none of them, and none may be drawn once the probe's render ends.
/// <c>vr.274-own-head-hidden</c>: no own head, face or hat renderer drawn by the eye camera in any frame of a sprint,
/// jumps, a wall climb and a ragdoll fall; in VR and in flat play.
/// </summary>
internal sealed partial class SelfTestRunner
{
    IEnumerator OwnHeadCheck()
    {
        var fails = new List<string>();
        var notes = new List<string>();
        var rig = VRRig.Instance;
        if (OwnHead.Hidden.Count == 0)
        {
            bool expected =
                string.Equals(
                    AvatarConfig.Instance.OwnBody,
                    AvatarConfig.ShowArms,
                    StringComparison.Ordinal
                ) && Rig.RigDriver.ViewOnBody;
            if (expected)
                Fail("own head not hidden from the eyes in OwnBody Arms with the view on the body");
            else
                stepNote = "own head not hidden (OwnBody or view): nothing to judge";
            yield break;
        }
        var head = OwnHead.Hidden[0];
        if (rig == null || rig.HeadCamera == null || head == null)
        {
            Fail("no head camera or head renderer");
            yield break;
        }

        int probeDrawn = -1;
        int eyeDrawn = -1;
        var go = new GameObject("PeakXR.SelfTest.HeadProbe");
        var probe = go.AddComponent<Camera>();
        probe.enabled = false;
        probe.stereoTargetEye = StereoTargetEyeMask.None;
        var texture = new RenderTexture(64, 64, 24);
        probe.targetTexture = texture;
        var face = head.bounds.center;
        var forward = Vector3.ProjectOnPlane(rig.HeadCamera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = Vector3.forward;
        go.transform.position = face + forward.normalized * 1.5f * rig.Scale;
        go.transform.LookAt(face);

        Action<ScriptableRenderContext, Camera> record = (_, cam) =>
        {
            if (cam == probe)
                probeDrawn = DrawnHeads();
            else if (cam == rig.HeadCamera)
                eyeDrawn = DrawnHeads();
        };
        // Added after OwnHead's handler, so it sees the state OwnHead set for that camera.
        RenderPipelineManager.beginCameraRendering += record;
        int count = OwnHead.Hidden.Count;
        try
        {
            probe.Render();
            int after = DrawnHeads();
            notes.Add(F($"probe drew {probeDrawn} of {count}, {after} after its render"));
            if (probeDrawn != count)
                fails.Add(
                    F(
                        $"a mirror-like camera drew {probeDrawn} of the {count} head renderers, want all"
                    )
                );
            if (after != 0)
                fails.Add(
                    F($"{after} head renderers left drawn after the probe render, want none")
                );
        }
        finally
        {
            UnityEngine.Object.Destroy(go);
            texture.Release();
            UnityEngine.Object.Destroy(texture);
        }
        yield return WaitFor(() => eyeDrawn >= 0, 2f);
        RenderPipelineManager.beginCameraRendering -= record;
        notes.Add(F($"eye drew {eyeDrawn} of {count} head renderers ({head.name} first)"));
        if (eyeDrawn != 0)
            fails.Add(F($"the eye camera drew {eyeDrawn} head renderers, want none"));
        Conclude(fails, notes);
    }

    static int DrawnHeads()
    {
        int n = 0;
        foreach (var r in OwnHead.Hidden)
            if (r != null && OwnHead.Drawn(r))
                n++;
        return n;
    }

    static readonly int VertexGhost = Shader.PropertyToID("_VertexGhost");

    /// <summary>Seconds each vr.274 motion runs.</summary>
    const float HeadPhaseSeconds = 1.5f;

    /// <summary>
    /// <c>vr.274-own-head-hidden</c>: the own head (HideTheBody.headRend, HideTheBody.cs:10), face (:14) and hats
    /// (CustomizationRefs.playerHats) must never be drawn by the eye camera while the view is on the own head, through
    /// a sprint, two jumps, a wall climb (the sandbox's test wall offline) and a 1.5 s ragdoll fall (RPCA_Fall,
    /// Character.cs:1749). Each eye render is judged after <see cref="OwnHead"/> set its state: a renderer counts as
    /// drawn when it is on, not shadow-only or switched off, in the view, and neither hidden by the W/Character
    /// shader's ghost (<c>_VertexGhost</c> 1, every part within <see cref="Core.BodyGhost.Radius"/> flat) nor, for a
    /// face card the ghost does not reach (GD/Face Cards), seen from behind. In flat play (-peakxr-novr) the head must
    /// be the game's own: PeakXR holds none of it hidden. The motions are the game's input (stick, sprint, jump), the
    /// climb its own start (CharacterClimbing.TryToStartWallClimb, CharacterClimbing.cs:534).
    /// </summary>
    IEnumerator OwnHeadHiddenStep()
    {
        var c = Character.localCharacter;
        var hide = c != null ? c.refs.hideTheBody : null;
        if (hide == null)
        {
            Fail("no HideTheBody on the local character");
            yield break;
        }
        var heads = new List<Renderer> { hide.headRend };
        if (hide.face != null)
            heads.AddRange(hide.face.GetComponentsInChildren<Renderer>(true));
        if (hide.refs != null && hide.refs.playerHats != null)
            heads.AddRange(hide.refs.playerHats);
        var home = c.Center;
        var drawn = new Dictionary<string, int>(StringComparer.Ordinal);
        var phaseFrames = new Dictionary<string, int>(StringComparer.Ordinal);
        string phase = "stand";
        var planes = new Plane[6];
        var materials = new List<Material>();
        float until = Now + 40f;
        Action<ScriptableRenderContext, Camera> check = null;
        check = (_, cam) =>
        {
            // Removes itself: a dropped step never runs a finally block.
            if (Now > until)
            {
                RenderPipelineManager.beginCameraRendering -= check;
                return;
            }
            if (!EyeCamera(cam) || !OnOwnHead())
                return;
            phaseFrames[phase] = phaseFrames.TryGetValue(phase, out int f) ? f + 1 : 1;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            var at = cam.transform.position;
            var centre = hide.headRend != null ? hide.headRend.bounds.center : c.Head;
            foreach (var r in heads)
            {
                if (!DrawnToEye(r, cam, at, centre, planes, materials))
                    continue;
                string key = phase + ": " + r.name;
                drawn[key] = drawn.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        };
        RenderPipelineManager.beginCameraRendering += check;
        try
        {
            yield return Wait(0.5f);
            phase = "sprint";
            SelfTestInputPatch.SprintUntil = Now + HeadPhaseSeconds;
            yield return Wait(HeadPhaseSeconds + 0.3f);
            phase = "jump";
            for (int i = 0; i < 2; i++)
            {
                SelfTestInputPatch.JumpOnce = true;
                yield return Wait(0.9f);
            }
            phase = "climb";
            yield return HeadClimb(c);
            phase = "ragdoll";
            c.photonView.RPC("RPCA_Fall", RpcTarget.All, HeadPhaseSeconds, 0f);
            yield return Wait(HeadPhaseSeconds);
            yield return WaitFor(() => c.data.fullyConscious && c.data.isGrounded, 5f);
            phase = "after";
            yield return Wait(0.3f);
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering -= check;
            SelfTestInputPatch.SprintUntil = 0f;
        }
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home, false);
        yield return WaitFor(() => !c.warping, 3f);

        var fails = new List<string>();
        foreach (var kv in drawn)
            fails.Add(F($"{kv.Key} drawn by the eye camera in {kv.Value} frames"));
        if (!VRSession.Active && OwnHead.Hidden.Count > 0)
            fails.Add(
                F(
                    $"flat play with {OwnHead.Hidden.Count} head renderers hidden by PeakXR, want the game's own"
                )
            );
        // A ragdoll may take the view off the head (Camera.DetachOnRagdoll), so only the motions on foot must show.
        foreach (var p in new[] { "sprint", "jump" })
            if (!phaseFrames.ContainsKey(p))
                fails.Add(p + ": no eye frame judged");
        var note = new List<string>
        {
            (VRSession.Active ? "VR" : "flat") + F($", {heads.Count} head renderers"),
        };
        foreach (var kv in phaseFrames)
            note.Add(F($"{kv.Key} {kv.Value} frames"));
        if (climbNote != null)
            note.Add(climbNote);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails) + " | " + string.Join(", ", note));
        else
            stepNote = string.Join(", ", note);
    }

    string climbNote;

    /// <summary>vr.274's climb: the test wall (or a map wall) faced from 0.6 m, the game's own climb start toward it,
    /// the stick up for <see cref="HeadPhaseSeconds"/>, then let go. A missing wall leaves a note, not a failure.</summary>
    IEnumerator HeadClimb(Character c)
    {
        climbNote = null;
        if (PhysicalInteraction.StartWallClimb == null || !FindWall(c, out var wall, out var stand))
        {
            climbNote = "climb skipped: no wall or no climb start";
            yield break;
        }
        c.photonView.RPC("WarpPlayerRPC", RpcTarget.All, CentreOver(c, stand), false);
        yield return Wait(0.5f);
        yield return WaitFor(() => !c.warping, 3f);
        yield return Wait(1f);
        var toWall = -Flat(wall.normal);
        float yaw = Mathf.Atan2(toWall.x, toWall.z) * Mathf.Rad2Deg;
        var rig = VRRig.Instance;
        if (VRSession.Active && rig != null)
            rig.RotateYaw(Mathf.DeltaAngle(rig.Head.eulerAngles.y, yaw));
        else
            c.data.lookValues = new Vector2(yaw, 0f);
        yield return Wait(0.6f);
        PhysicalInteraction.StartWallClimb(c.refs.climbing, true, toWall, false, 1.25f);
        yield return WaitFor(() => c.data.isClimbing, 1f);
        if (!c.data.isClimbing)
        {
            climbNote = "climb skipped: the climb did not start";
            yield break;
        }
        SelfTestInputPatch.SprintUntil = Now + HeadPhaseSeconds;
        yield return Wait(HeadPhaseSeconds);
        SelfTestInputPatch.SprintUntil = 0f;
        c.refs.climbing.StopAnyClimbing();
        yield return WaitFor(() => c.data.isGrounded, 3f);
        climbNote = "climbed";
    }

    /// <summary>The camera that shows the player the world: the rig's eye in VR, the game's main camera flat.</summary>
    static bool EyeCamera(Camera cam)
    {
        if (cam == null || cam.cameraType != CameraType.Game)
            return false;
        if (VRSession.Active)
            return OwnHead.IsEye(cam);
        return MainCamera.instance != null && cam.gameObject == MainCamera.instance.gameObject;
    }

    /// <summary>The view is in the own head and not black: a chase or spectate view shows the head on purpose.</summary>
    static bool OnOwnHead() =>
        !VRSession.Active
        || (
            Rig.RigDriver.ViewOnBody
            && !Rig.RigDriver.ViewDetached
            && UI.Common.FadeView.Alpha < 0.99f
        );

    /// <summary>Whether <paramref name="r"/> shows in <paramref name="cam"/>'s image (see vr.274).</summary>
    static bool DrawnToEye(
        Renderer r,
        Camera cam,
        Vector3 at,
        Vector3 headCentre,
        Plane[] planes,
        List<Material> materials
    )
    {
        if (
            r == null
            || !r.enabled
            || !r.gameObject.activeInHierarchy
            || !OwnHead.Drawn(r)
            || (cam.cullingMask & (1 << r.gameObject.layer)) == 0
        )
            return false;
        var b = r.bounds;
        if (!GeometryUtility.TestPlanesAABB(planes, b))
            return false;
        r.GetSharedMaterials(materials);
        bool ghosted = materials.Count > 0;
        foreach (var m in materials)
            ghosted &= m != null && m.HasProperty(VertexGhost) && m.GetFloat(VertexGhost) >= 0.99f;
        if (ghosted)
        {
            // Every corner within the ghost's flat radius: the shader dithers it all out.
            float far = 0f;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    at.y,
                    (i & 2) == 0 ? b.min.z : b.max.z
                );
                far = Mathf.Max(far, Vector3.Distance(corner, at));
            }
            return far >= Core.BodyGhost.Radius;
        }
        // A face card faces out of the head; from inside or behind the head only its culled back shows.
        return Vector3.Dot(b.center - headCentre, at - b.center) > 0f;
    }
}
