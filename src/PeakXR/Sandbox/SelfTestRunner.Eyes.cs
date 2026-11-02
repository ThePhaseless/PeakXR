using System;
using System.Collections;
using System.Collections.Generic;
using Peak;
using PeakXR.Core;
using PeakXR.Diagnostics;
using PeakXR.Render;
using PeakXR.Rig;
using PeakXR.UI.Common;
using PeakXR.UI.Status;
using PeakXR.UI.World;
using PeakXR.VR;
using PeakXR.XR;
using UnityEngine;
using UnityEngine.XR;

namespace PeakXR.Sandbox;

/// <summary>
/// Whether anything differs between the eyes that should not (issue #5), from each eye's own pixels (<see cref="ReadEyes"/>)
/// on the level. <c>vr.480</c> measures the head-locked overlays: the Peripheral hide, a control that shows it could fail,
/// and the stereo offset of a real quad. <c>vr.481</c> renders both eyes from one view and from swapped views.
/// <c>vr.482</c> logs stereo metrics per effect and checks the near smoke fade from particle data, and <c>vr.483</c> checks
/// what each eye renders from.
/// </summary>
internal sealed partial class SelfTestRunner
{
    private const int EyeLumaStep = 4;
    private const int MonoDiffReads = 10;

    /// <summary>What <see cref="EyesSettled"/> allows: a first eye sample that waits for a fade waits 3 s
    /// (<see cref="EyeCalibration.FadeWaitSeconds"/>) and its blink takes about 1 s, with the first still stand before it.</summary>
    private const float EyeSettleSeconds = 8f;

    /// <summary>Half the height of the rows P4 reads, in tangents: well inside the hole's radius of 0.5.</summary>
    private const float HoleBandTan = 0.15f;

    /// <summary>Share of pixels that may move past <see cref="DrawnPixels.ChannelStep"/> between two images of one view:
    /// a small leak an RMS over the whole eye dilutes.</summary>
    private const float MonoDiffShare = 0.002f;

    private static readonly string[] MonoDiffEffects =
    {
        "VFX_PoisonExplo",
        "VFX_Smoke",
        "vfx-fire",
    };

    private static readonly string[] EyeEffects =
    {
        "VFX_PoisonExplo",
        "VFX_SporeExplo",
        "VFX_SporeExploExplo",
        "Gas",
        "VFX_FlareSmoke",
        "VFX_DynamiteSmoke",
        "vfx-fire",
        "VFX_ExplosionGhost",
        "BEES",
        "AntiSphere",
        "Particle_Ghost",
        "Broad Wind",
        "Steam",
        "spores",
    };

    /// <summary>In <see cref="EyeEffects"/> with no prefab, only under a level's map. They carry nothing but particle
    /// systems, so a copy is safe.</summary>
    private static readonly string[] SceneEffects =
    {
        "Broad Wind",
        "spores",
        NearFadeVanillaEffect,
    };

    /// <summary>Metres to the side of the spot, so one does not hide another.</summary>
    private static readonly float[] MonoDiffSides = { 0f, -2f, 2f };

    /// <summary>The effects <c>vr.482</c>'s near-fade check prefers, in order: the first loaded is the named report's.</summary>
    private static readonly string[] NearFadeEffects = { "VFX_PoisonExplo", "VFX_SporeExplo" };

    /// <summary>A second shader and alpha numbers for the same check, so the class is proved and not one material.</summary>
    private const string NearFadeOtherEffect = "Gas";

    /// <summary>Campfire smoke, in the map and no prefab: a big sheet with no hazard, which the near fade must leave alone.</summary>
    private const string NearFadeVanillaEffect = "VFX_Smoke_Lit";

    /// <summary>The least a poison sheet taken away right ahead must raise the haze; predicted 0.55.</summary>
    private const float NearFadePoisonMinHaze = 0.3f;

    /// <summary>What the haze may drift from its reading before a spawn with this effect playing no part in it.</summary>
    private const float NearFadeHazeMargin = 0.02f;

    /// <summary>Ahead of the head, past any burst: from 6 m a sheet came to 0.70 m at the test rig's frame rate (issue #5).</summary>
    private const float NearFadeFarMetres = 15f;

    /// <summary>What <c>vr.482</c> places in the real system: one sheet under the fade, one past it.</summary>
    private const float NearFadeSheetZ = 0.25f;
    private const float NearFadeKeptSheetZ = 3f;
    private const float NearFadeSheetSize = 7f;
    private const float NearFadeSheetLife = 60f;

    /// <summary>Share of its life a placed sheet has had: Gas starts at alpha 0 and is at 1 from 9 % (Fog), 7 % (Spray),
    /// and poison runs from 1 to 0.</summary>
    private const float NearFadeSheetAge = 0.3f;

    /// <summary>Above this a sheet counts as drawn: at start alpha 255 the fade passes it at about 0.6 m.</summary>
    private const int NearFadeDrawnAlpha = 8;

    /// <summary>Longer than the haze's ease (0.3 s). No slow scan to wait for: <see cref="SpawnEffect"/> asks for one.</summary>
    private const float NearFadeSettleSeconds = 0.6f;

    private static readonly int PoisonIntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int PoisonEdgeId = Shader.PropertyToID("_Edge");
    private static readonly int PoisonEdgeAddId = Shader.PropertyToID("_EdgeAdd");

    private EyeImages flashOff;
    private EyeImages flashOn;

    /// <summary><see cref="VRStep"/> that runs <see cref="OnRestore"/>'s undos, which plain VRStep leaves alone; without
    /// the scripted hands <see cref="Restoring"/> also starts. A timeout reaches the finally through
    /// <see cref="Unwind"/>.</summary>
    IEnumerator EyeStep(string id, float timeout, IEnumerator body)
    {
        try
        {
            yield return VRStep(id, timeout, body);
        }
        finally
        {
            var restore = bodyRestore;
            bodyRestore = null;
            restore?.Invoke();
        }
    }

    /// <summary>Holds the world still for comparing reads. <see cref="Wait"/> and <see cref="ReadEyes"/> run on real time.</summary>
    void FreezeWorld()
    {
        float before = Time.timeScale;
        Time.timeScale = 0f;
        OnRestore(() => Time.timeScale = before);
    }

    static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    /// <summary>Keeps what still moves or ticks with time frozen out of the reads until the step ends: the local avatar's
    /// renderers and their shadows, whose outlines moved at timeScale 0 in the first in-game pairs (issue #5), and the
    /// wrist's frame readout, whose text updates every 0.5 s on unscaled time (DebugDriver.cs:141-145). The setting goes
    /// back through <see cref="OnRestore"/> and never reaches the disk: the run sets SaveOnConfigSet false.</summary>
    string HideAvatarFromReads()
    {
        var stats = DebugFeature.ShowFrameStats;
        if (stats != null && stats.Value)
        {
            stats.Value = false;
            OnRestore(() => stats.Value = true);
        }
        var character = Character.localCharacter;
        if (character == null)
            return "no local character to hide";
        var hidden = new List<Renderer>();
        void Hide(Component root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.enabled)
                {
                    r.enabled = false;
                    hidden.Add(r);
                }
        }
        Hide(character);
        if (character.data.currentItem != null)
            Hide(character.data.currentItem);
        OnRestore(() =>
        {
            foreach (var r in hidden)
                if (r != null)
                    r.enabled = true;
        });
        return $"{hidden.Count} avatar renderers hidden";
    }

    /// <summary>The first problem among reads meant to be compared. Adaptive quality can land between two reads, and
    /// images of two sizes would compare as equal.</summary>
    static string ReadsProblem(params EyeImages[] reads)
    {
        foreach (var read in reads)
        {
            if (read.Problem != null)
                return read.Problem;
            if (read.Width != reads[0].Width || read.Height != reads[0].Height)
                return $"the eye target went from {reads[0].Width}x{reads[0].Height} to {read.Width}x{read.Height} between reads";
        }
        return null;
    }

    /// <summary>The head camera and display when two eye passes render; otherwise the step is skipped or failed.</summary>
    bool EyesReady(out Camera cam, out XRDisplaySubsystem display)
    {
        display = XRRuntimeInfo.Display;
        var rig = VRRig.Instance;
        cam = rig != null ? rig.HeadCamera : null;
        if (display == null || display.GetRenderPassCount() != 2)
        {
            stepSkip =
                $"not MultiPass ({(display == null ? 0 : display.GetRenderPassCount())} render passes)";
            return false;
        }
        if (cam == null)
        {
            Fail("no head camera to read the eyes from");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Waits for no fade to be asked for or drawn and for the eye model's first still stand to have landed, a
    /// fail by name past <see cref="EyeSettleSeconds"/>. That landing blinks and moves the view by up to 0.25 m (#5:
    /// <c>applied inside a fade</c> 0.11 s after vr.470, in vr.480's first reads, the floor at 21 of 255). A later blink
    /// (adaptive quality) is <see cref="ReadEyes"/>'s to skip.
    /// </summary>
    IEnumerator EyesSettled()
    {
        float end = Now + EyeSettleSeconds;
        while (VRFade.Target > 0f || FadeView.Alpha > FadedAbove || !RigDriver.EyeCalibrated)
        {
            if (Now > end)
            {
                Fail(
                    F(
                        $"the view did not settle in {EyeSettleSeconds:0} s: fade target {VRFade.Target:0.00}, drawn {FadeView.Alpha:0.00}, eye model's first still stand {(RigDriver.EyeCalibrated ? "landed" : "not landed")}"
                    )
                );
                yield break;
            }
            yield return null;
        }
        // The landing snaps the view on the frame after it.
        yield return Frames(2);
    }

    /// <summary>Each eye's image boxed down for comparing.</summary>
    static float[] EyeLuma(EyeImages image, int eye) =>
        EyeImageMatch.Luma(image.Pixels[eye], image.Width, image.Height, EyeLumaStep);

    /// <summary>vr.480: see the class comment; the phases are in docs/testing.md.</summary>
    IEnumerator EyeOverlaysStep()
    {
        if (!EyesReady(out var cam, out var display))
            yield break;
        if (!UIWorldRuntime.Ready)
        {
            Fail("UIWorldRuntime.Ready is false: ui.world does not pose the overlays");
            yield break;
        }
        if (UIStatusFeature.Intensity == null)
        {
            Fail("ui.status is not running: no Peripheral hide to measure");
            yield break;
        }
        var fx = GUIManager.instance != null ? GUIManager.instance.poisonSVFX : null;
        if (fx == null || fx.renderer == null)
        {
            Fail("GUIManager.poisonSVFX has no renderer");
            yield break;
        }

        yield return EyesSettled();
        if (!stepOk)
            yield break;

        var material = fx.renderer.material;
        bool wasActive = fx.gameObject.activeSelf;
        float intensity = material.GetFloat(PoisonIntensityId);
        float edge = material.GetFloat(PoisonEdgeId);
        float edgeAdd = material.GetFloat(PoisonEdgeAddId);
        var overlays = UIWorldFeature.Settings.StatusOverlays;
        var mode = overlays.Value;
        float rim = UIStatusFeature.Intensity.Value;
        bool locked = RenderFixesFeature.EyeLockedOverlays.Value;
        OnRestore(() =>
        {
            flashOff = null;
            flashOn = null;
            overlays.Value = mode;
            UIStatusFeature.Intensity.Value = rim;
            RenderFixesFeature.EyeLockedOverlays.Value = locked;
            if (fx != null)
                fx.gameObject.SetActive(wasActive);
            if (material != null)
            {
                material.SetFloat(PoisonIntensityId, intensity);
                material.SetFloat(PoisonEdgeId, edge);
                material.SetFloat(PoisonEdgeAddId, edgeAdd);
            }
        });
        string avatar = HideAvatarFromReads();
        FreezeWorld();
        yield return Frames(3);

        // Half black: full black would land what waits for black (adaptive quality's resize).
        VRFade.Set(this, 0.5f);
        OnRestore(() => VRFade.Clear(this));
        yield return Until(() => FadeView.Alpha > FadedAbove, 1f, "the step's own fade never drew");
        if (!stepOk)
            yield break;
        var refused = new EyeImages(2);
        yield return ReadEyes(cam, display, refused, 0.3f);
        VRFade.Clear(this);
        if (refused.Problem == null || !refused.Problem.Contains("inside a fade"))
        {
            Fail($"a read did not refuse a frame under a fade: {refused.Problem ?? "it took one"}");
            yield break;
        }
        yield return EyesSettled();
        if (!stepOk)
            yield break;

        var first = new EyeImages(2);
        var second = new EyeImages(2);
        yield return ReadEyes(cam, display, first);
        yield return ReadEyes(cam, display, second);
        string problem = ReadsProblem(first, second);
        if (problem != null)
        {
            Fail("noise read: " + problem);
            yield break;
        }
        var floor = new float[2];
        for (int eye = 0; eye < 2; eye++)
            floor[eye] = Mathf.Max(
                0.5f,
                2f * EyeImageMatch.Rms(EyeLuma(first, eye), EyeLuma(second, eye))
            );
        var notes = new List<string>
        {
            avatar,
            F($"floor L {floor[0]:0.00} R {floor[1]:0.00} of 255"),
        };
        var fails = new List<string>();

        // A hide that works leaves nothing to see; the poison quad covers the view at alpha 1, or this proves nothing
        // (at its own _EdgeAdd it has alpha 0 everywhere in view). The rim would draw the same status at the view's edge.
        UIStatusFeature.Intensity.Value = 0f;
        material.SetFloat(PoisonIntensityId, 1f);
        material.SetFloat(PoisonEdgeAddId, 1f);
        var settings = new[] { false, true };
        foreach (bool eyeLocked in settings)
        {
            string label = eyeLocked ? "on" : "off";
            yield return PoisonOffAndOn(cam, display, fx, StatusOverlayMode.Peripheral, eyeLocked);
            if (!FlashVerdict($"P1 {label}", true, floor, notes, fails))
                SaveFlash($"vr-480-p1-{label}");
            yield return PoisonOffAndOn(cam, display, fx, StatusOverlayMode.HeadLocked, eyeLocked);
            if (!FlashVerdict($"P2 {label}", false, floor, notes, fails))
                SaveFlash($"vr-480-p2-{label}");
        }

        // Alpha 0 inside r = 1/12 of the quad (0.5 tan) and 1 outside: a hard-edged hole at the quad's centre. Its stereo
        // offset is the overlay's. The stock quad is the only kind that can answer: a shader with DOTS variants is what
        // the resident drawer can hold, which would keep the quad at one pose for both eyes.
        material.SetFloat(PoisonEdgeId, 200f);
        material.SetFloat(PoisonEdgeAddId, -200f / 12f);
        foreach (bool eyeLocked in settings)
        {
            yield return PoisonOffAndOn(cam, display, fx, StatusOverlayMode.HeadLocked, eyeLocked);
            string label = eyeLocked ? "on" : "off";
            if (!HoleVerdict(cam, eyeLocked, notes, fails))
                SaveFlash($"vr-480-p4-{label}");
        }

        stepNote = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails));
    }

    /// <summary>Reads both eyes with the poison quad off and then on, into <see cref="flashOff"/> and
    /// <see cref="flashOn"/>.</summary>
    IEnumerator PoisonOffAndOn(
        Camera cam,
        XRDisplaySubsystem display,
        ScreenVFX fx,
        StatusOverlayMode mode,
        bool eyeLocked
    )
    {
        UIWorldFeature.Settings.StatusOverlays.Value = mode;
        RenderFixesFeature.EyeLockedOverlays.Value = eyeLocked;
        fx.gameObject.SetActive(false);
        yield return Frames(3);
        flashOff = new EyeImages(2);
        yield return ReadEyes(cam, display, flashOff);
        fx.gameObject.SetActive(true);
        yield return Frames(3);
        flashOn = new EyeImages(2);
        yield return ReadEyes(cam, display, flashOn);
    }

    /// <summary>P1 (<paramref name="hidden"/>): each eye stays within the noise floor. P2: each eye shows the quad clearly.
    /// False on a red.</summary>
    bool FlashVerdict(
        string phase,
        bool hidden,
        float[] floor,
        List<string> notes,
        List<string> fails
    )
    {
        string problem = ReadsProblem(flashOff, flashOn);
        if (problem != null)
        {
            fails.Add($"{phase}: {problem}");
            return false;
        }
        float left = EyeImageMatch.Rms(EyeLuma(flashOff, 0), EyeLuma(flashOn, 0));
        float right = EyeImageMatch.Rms(EyeLuma(flashOff, 1), EyeLuma(flashOn, 1));
        notes.Add(F($"{phase} L {left:0.00} R {right:0.00}"));
        bool ok = hidden
            ? left <= floor[0] && right <= floor[1]
            : left >= Mathf.Max(10f, 3f * floor[0]) && right >= Mathf.Max(10f, 3f * floor[1]);
        if (!ok)
            fails.Add(
                hidden
                    ? F(
                        $"{phase}: the hidden flash still reached the eyes (L {left:0.00} R {right:0.00})"
                    )
                    : F(
                        $"{phase}: the HeadLocked flash barely shows (L {left:0.00} R {right:0.00}): phase P1 proves nothing"
                    )
            );
        return ok;
    }

    /// <summary>P4: the hole's centre, in head-frame tangents, per eye. Off it differs by the eyes' stereo on a quad at D,
    /// 0.063 / 0.06 = 1.05 tan on the default near plane, and on it must not differ. False on a red.</summary>
    bool HoleVerdict(Camera cam, bool eyeLocked, List<string> notes, List<string> fails)
    {
        string phase = eyeLocked ? "P4 on" : "P4 off";
        string problem = ReadsProblem(flashOff, flashOn);
        if (problem != null)
        {
            fails.Add($"{phase}: {problem}");
            return false;
        }
        var centre = new float[2];
        for (int eye = 0; eye < 2; eye++)
        {
            var view = flashOn.Views[eye];
            var viewport = flashOn.Viewports[eye];
            var (left, right, bottom, top) = StereoCull.Tangents(
                flashOn.Projections[eye].m00,
                flashOn.Projections[eye].m02,
                flashOn.Projections[eye].m11,
                flashOn.Projections[eye].m12
            );
            // The head's axis in this eye gives the row. The hole's chord is centred on any row through it, so a band
            // around that row survives a flipped readback, which this one is, and a median over it survives scene detail.
            // Pixels come from the measured viewport: adaptive quality rescales the eye, and a target can outgrow it.
            Vector3 axis = view.MultiplyVector(cam.transform.forward);
            float axisY = axis.y / -axis.z;
            int band = Mathf.RoundToInt(HoleBandTan / (top - bottom) * viewport.height);
            int firstRow = Mathf.RoundToInt(viewport.y);
            int lastRow = firstRow + Mathf.RoundToInt(viewport.height) - 1;
            int row = Mathf.RoundToInt(
                viewport.y + (axisY - bottom) / (top - bottom) * viewport.height
            );
            int firstColumn = Mathf.RoundToInt(viewport.x);
            var profile = EyeImageMatch.ChangeProfile(
                flashOff.Pixels[eye],
                flashOn.Pixels[eye],
                flashOn.Width,
                Mathf.Max(firstRow, row - band),
                Mathf.Min(lastRow, row + band),
                firstColumn,
                firstColumn + Mathf.RoundToInt(viewport.width) - 1
            );
            var (from, to) = EyeImageMatch.HoleInProfile(profile, EyeImageMatch.MinHoleContrast);
            if (from < 0)
            {
                fails.Add(
                    $"{phase}: no hole in the {(eye == 0 ? "left" : "right")} eye (rows {row - band}-{row + band})"
                );
                return false;
            }
            from += firstColumn;
            to += firstColumn;
            float a = HeadTangent(cam, view, viewport, left, right, axisY, from);
            float b = HeadTangent(cam, view, viewport, left, right, axisY, to);
            float width = b - a + (right - left) / viewport.width;
            centre[eye] = (a + b) * 0.5f;
            notes.Add(
                F(
                    $"{phase} {(eye == 0 ? "L" : "R")} hole columns {from}-{to}, centre {centre[eye]:0.000} tan, width {width:0.00}"
                )
            );
            if (width < EyeImageMatch.MinHoleTan || width > EyeImageMatch.MaxHoleTan)
            {
                fails.Add(
                    F(
                        $"{phase}: the {(eye == 0 ? "left" : "right")} eye's hole is {width:0.00} tan wide, not the quad's 1.0"
                    )
                );
                return false;
            }
        }
        float offset = Mathf.Abs(centre[0] - centre[1]);
        float separation = Vector3.Distance(
            flashOn.Views[0].inverse.GetColumn(3),
            flashOn.Views[1].inverse.GetColumn(3)
        );
        float bound = 0.6f * separation / (cam.nearClipPlane + 0.03f);
        notes.Add(
            F($"{phase} |cL - cR| {offset:0.000} tan, eyes {separation * 1000f:0.0} mm apart")
        );
        bool ok = eyeLocked ? offset <= 0.01f : offset >= bound;
        if (!ok)
            fails.Add(
                eyeLocked
                    ? F(
                        $"{phase}: the quad's pattern still sits {offset:0.000} tan apart between the eyes"
                    )
                    : F(
                        $"{phase}: {offset:0.000} tan apart, today's quad should show at least {bound:0.000}"
                    )
            );
        return ok;
    }

    /// <summary>A pixel column of one eye, as a tangent in the head's frame.</summary>
    private static float HeadTangent(
        Camera cam,
        Matrix4x4 view,
        Rect viewport,
        float left,
        float right,
        float eyeY,
        int column
    )
    {
        float eyeX = left + (column + 0.5f - viewport.x) / viewport.width * (right - left);
        Vector3 world = view.inverse.MultiplyVector(new Vector3(eyeX, eyeY, -1f));
        Vector3 head = cam.transform.InverseTransformDirection(world);
        return head.x / head.z;
    }

    /// <summary>Saves both images of a red, so a later look can replay the verdict on them.</summary>
    void SaveFlash(string shot)
    {
        SavePair(flashOn, shot);
        SavePair(flashOff, shot + "-base");
    }

    void SavePair(EyeImages image, string shot)
    {
        if (image.Problem != null)
            return;
        SaveEye(image, shot + "-left", 0);
        SaveEye(image, shot + "-right", 1);
    }

    /// <summary>Prefabs loaded right now, plus <see cref="SceneEffects"/>, by name; what a level has depends on what it
    /// refers to.</summary>
    static Dictionary<string, GameObject> LoadedPrefabs()
    {
        var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (!go.scene.IsValid() || Array.IndexOf(SceneEffects, go.name) >= 0)
                prefabs.TryAdd(go.name, go);
        return prefabs;
    }

    /// <summary>A copy of a game effect with what it does to players switched off and what ends it on its own taken out, or
    /// null when it is not loaded. Returns its holder, to destroy early; <see cref="OnRestore"/> destroys it otherwise.</summary>
    GameObject SpawnEffect(Dictionary<string, GameObject> prefabs, string name, Vector3 position)
    {
        if (!prefabs.TryGetValue(name, out var prefab))
            return null;
        // Instantiate runs Awake and OnEnable at once, and an AOE can explode in OnEnable (AOE.cs:80): the copy only
        // wakes under a holder once it is stripped.
        var holder = new GameObject("PeakXR.SelfTest.Effect");
        holder.SetActive(false);
        OnRestore(() =>
        {
            if (holder != null)
                Destroy(holder);
        });
        var copy = Instantiate(prefab, position, prefab.transform.rotation, holder.transform);
        // Off, not gone: the near smoke fade takes an effect by these, and a live AOE would ask for its scan itself.
        SwitchOff<AOE>(copy);
        Strip<AddScreenshake>(copy);
        Strip<TimeEvent>(copy);
        // VFX_PoisonExplo would end on its own while vr.482 holds it.
        Strip<RemoveAfterSeconds>(copy);
        // Gas: it would play the luggage's sounds and, after 10 s, reach for the field (Peak/TrickLuggage.cs:17,29).
        Strip<TrickLuggage>(copy);
        SwitchOff<StatusFieldBase>(copy);
        // Flare smoke. A copy tracks nothing: it logs an error and stops, or destroys itself (TrackNetworkedObject.cs:43,55).
        Strip<TrackNetworkedObject>(copy);
        copy.SetActive(true);
        holder.SetActive(true);
        foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>())
            if (!ps.isPlaying)
                ps.Play(false);
        SmokeNearFade.RequestScan();
        return holder;
    }

    static void SwitchOff<T>(GameObject root)
        where T : Behaviour
    {
        foreach (var part in root.GetComponentsInChildren<T>(true))
            part.enabled = false;
    }

    static void Strip<T>(GameObject root)
        where T : Component
    {
        foreach (var part in root.GetComponentsInChildren<T>(true))
            DestroyImmediate(part);
    }

    /// <summary>vr.481: looks for state that belongs to an eye, not to its view. On one view both eyes must match, and on
    /// swapped views each eye must match the other's normal image; the phases are in docs/testing.md. A red stops the
    /// near-fade work on #5: there is a stateful cause that reading the code missed.</summary>
    IEnumerator EyeMonoDiffStep()
    {
        if (!EyesReady(out var cam, out var display))
            yield break;
        yield return EyesSettled();
        if (!stepOk)
            yield break;
        string missing = EyeViewOverride.Resolve();
        if (missing != null)
        {
            stepSkip = missing;
            yield break;
        }
        // First, so the views are real again before anything else goes back.
        OnRestore(() => EyeViewOverride.Remove());

        var prefabs = LoadedPrefabs();
        var head = cam.transform;
        var ahead = head.position + Flat(head.forward) * 6f;
        var side = Flat(head.right);
        var effects = new List<GameObject>();
        var notes = new List<string>();
        for (int i = 0; i < MonoDiffEffects.Length; i++)
        {
            var effect = SpawnEffect(prefabs, MonoDiffEffects[i], ahead + side * MonoDiffSides[i]);
            if (effect != null)
                effects.Add(effect);
            notes.Add(MonoDiffEffects[i] + (effect != null ? " found" : " ABSENT"));
        }
        if (effects.Count == 0)
        {
            Fail("none of the effects is loaded on this level");
            yield break;
        }
        if (!prefabs.ContainsKey(MonoDiffEffects[0]))
            notes.Add($"{MonoDiffEffects[0]} was not measured in this run");
        yield return Wait(1f);

        var control = new EyeImages(2);
        yield return ReadEyes(cam, display, control);
        if (control.Problem != null)
        {
            Fail("control read: " + control.Problem);
            yield break;
        }
        float controlRms = EyeImageMatch.Rms(EyeLuma(control, 0), EyeLuma(control, 1));

        EyeViewOverride.Install(EyeViewOverride.Mode.Mono, cam);
        yield return Frames(2);
        float monoSum = 0f;
        float monoWorst = 0f;
        float monoShare = 0f;
        EyeImages worst = null;
        for (int i = 0; i < MonoDiffReads; i++)
        {
            var read = new EyeImages(2);
            yield return ReadEyes(cam, display, read);
            if (read.Problem != null)
            {
                EyeViewOverride.Remove();
                Fail("mono read: " + read.Problem);
                yield break;
            }
            float rms = EyeImageMatch.Rms(EyeLuma(read, 0), EyeLuma(read, 1));
            monoSum += rms;
            monoShare = Mathf.Max(
                monoShare,
                DrawnPixels.ChangedShare(read.Pixels[0], read.Pixels[1])
            );
            if (worst == null || rms > monoWorst)
            {
                monoWorst = rms;
                worst = read;
            }
        }
        if (!EyeViewOverride.Remove())
        {
            Fail("something chained above the view override during the mono phase");
            yield break;
        }
        float monoMean = monoSum / MonoDiffReads;
        notes.Add(
            F(
                $"control {controlRms:0.00}, mono mean {monoMean:0.00} worst {monoWorst:0.00} of 255, share {monoShare:0.0000}"
            )
        );
        var fails = new List<string>();
        bool swapSkipped = false;
        // Real eyes must differ by parallax, or the two images are one eye read twice. The noise floor catches 5 x 0.
        if (controlRms < Mathf.Max(5f * monoMean, 0.5f))
            fails.Add("capture is not per eye");
        if (monoMean > 1f || monoWorst > 2f || monoShare > MonoDiffShare)
        {
            fails.Add("the eyes differ on one view: state that belongs to an eye");
            SavePair(worst, "vr-481-mono");
        }

        // The mono phase keeps the avatar, whose eyes it compares within one frame. From here the avatar is out: it moves
        // while time is frozen, which reads as a difference no swap made.
        notes.Add(HideAvatarFromReads());
        FreezeWorld();
        foreach (var effect in effects)
        foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>())
            ps.Pause(false);
        yield return Frames(3);
        var before = head.position;
        var normal = new EyeImages(2);
        var swapped = new EyeImages(2);
        var later = new EyeImages(2);
        yield return ReadEyes(cam, display, normal);
        EyeViewOverride.Install(EyeViewOverride.Mode.Swap, cam);
        yield return Frames(2);
        yield return ReadEyes(cam, display, swapped);
        if (!EyeViewOverride.Remove())
            fails.Add("something chained above the view override during the swap phase");
        // After the swap, so this read's distance from the first spans at least the swapped read's.
        yield return Frames(2);
        yield return ReadEyes(cam, display, later);
        string problem = ReadsProblem(normal, swapped, later);
        if (problem != null)
            fails.Add("swap read: " + problem);
        else
        {
            var toLeft = SwapReading(swapped, 1, normal, later, 0);
            var toRight = SwapReading(swapped, 0, normal, later, 1);
            float still = Mathf.Max(toLeft.StillRms, toRight.StillRms);
            float moved = Vector3.Distance(before, head.position);
            notes.Add($"swap right-as-left {toLeft}; left-as-right {toRight}");
            if (still > EyeImageMatch.SwapStillLimit || moved > 0.001f)
            {
                swapSkipped = true;
                notes.Add(
                    F(
                        $"swap phase SKIPPED: the frozen view was not still ({still:0.00} of 255, head moved {moved * 1000f:0.0} mm), so this run did not prove it"
                    )
                );
            }
            else if (!toLeft.Matches || !toRight.Matches)
            {
                fails.Add("swapped eyes do not match the other eye's image: stale per-eye data");
                SavePair(swapped, "vr-481-swapped");
                SavePair(normal, "vr-481-normal");
                SavePair(later, "vr-481-later");
            }
        }

        stepNote = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails));
        else if (swapSkipped)
            stepSkip = stepNote;
    }

    /// <summary>Pass <paramref name="from"/> rendered on the other eye's view, against that eye's normal read.</summary>
    static EyeSwapReading SwapReading(
        EyeImages swapped,
        int from,
        EyeImages normal,
        EyeImages later,
        int eye
    ) =>
        EyeImageMatch.ReadSwap(
            EyeLuma(swapped, from),
            EyeLuma(normal, eye),
            EyeLuma(later, eye),
            normal.Width / EyeLumaStep,
            normal.Height / EyeLumaStep,
            DrawnPixels.ChangedShare(swapped.Pixels[from], normal.Pixels[eye]),
            DrawnPixels.ChangedShare(later.Pixels[eye], normal.Pixels[eye])
        );

    /// <summary>Chosen alone, vr.482 holds the named effect for a compositor screenshot instead of measuring; a run that
    /// lists it with others must not stop there.</summary>
    bool EyeEffectsLive =>
        filter != null
        && filter.Patterns.Count == 1
        && string.Equals(filter.Patterns[0], "vr.482-eye-effects", StringComparison.Ordinal);

    /// <summary>vr.482: logs stereo metrics per effect, which cannot fail, then checks the near smoke fade from particle
    /// data, which can (<see cref="NearFadeChecks"/>). Or holds <c>VFX_PoisonExplo</c> still in
    /// <see cref="EyeEffectsLive"/>.</summary>
    IEnumerator EyeEffectsStep()
    {
        if (!EyesReady(out var cam, out var display))
            yield break;
        yield return EyesSettled();
        if (!stepOk)
            yield break;
        var prefabs = LoadedPrefabs();
        var head = cam.transform;
        var player = head.position;
        var ahead = player + Flat(head.forward) * 6f;
        string avatar = HideAvatarFromReads();
        if (EyeEffectsLive)
        {
            if (!prefabs.ContainsKey(MonoDiffEffects[0]))
            {
                stepSkip = $"{MonoDiffEffects[0]} is not loaded on this level";
                yield break;
            }
            yield return EyeEffectHold(prefabs, ahead, "at 6 m");
            yield return EyeEffectHold(prefabs, player, "at the player");
            yield break;
        }
        var absent = new List<string>();
        foreach (var effect in EyeEffects)
        {
            if (!prefabs.ContainsKey(effect))
            {
                absent.Add(effect);
                continue;
            }
            yield return EyeEffectMetrics(cam, display, prefabs, effect, ahead, "at 6 m");
            yield return EyeEffectMetrics(cam, display, prefabs, effect, player, "at the player");
        }
        stepNote =
            $"{EyeEffects.Length - absent.Count} effects measured (eye-effect log lines); {avatar}";
        if (absent.Count > 0)
            stepNote += "; absent: " + string.Join(", ", absent);
        if (absent.Contains(MonoDiffEffects[0]))
            stepNote += $"; {MonoDiffEffects[0]} was not measured in this run";
        yield return NearFadeChecks(head, prefabs);
    }

    /// <summary>The near smoke fade, from particle data, with the setting on and then off. A burst lands at random, so a
    /// check on where it fell could pass with no sheet near: the sheets that decide are placed in the real system, with
    /// its real material. Off is the control: the same sheet must draw again, or the check proved nothing. Neither
    /// <see cref="NearFadeEffects"/> loaded is a SKIP, which does not count as passing: re-run on a level that has them.</summary>
    IEnumerator NearFadeChecks(Transform head, Dictionary<string, GameObject> prefabs)
    {
        string named = Array.Find(NearFadeEffects, prefabs.ContainsKey);
        if (named == null)
        {
            stepSkip =
                $"near fade not checked: {string.Join(" and ", NearFadeEffects)} are not loaded here; {stepNote}";
            yield break;
        }
        bool setting = RenderFixesFeature.NearSmokeFade.Value;
        OnRestore(() => RenderFixesFeature.NearSmokeFade.Value = setting);
        RenderFixesFeature.NearSmokeFade.Value = true;
        var notes = new List<string>();
        if (!string.Equals(named, NearFadeEffects[0], StringComparison.Ordinal))
            notes.Add($"{NearFadeEffects[0]} absent, near fade checked on {named}");

        // The last metrics line's haze easing out.
        yield return Wait(NearFadeSettleSeconds);
        float hazeBefore = SmokeNearFade.HazeAlpha;
        var position = head.position;
        var far = position + (Flat(head.forward) * NearFadeFarMetres);
        var checks = new List<(string Name, GameObject Holder)>
        {
            (named, SpawnEffect(prefabs, named, far)),
        };
        if (prefabs.ContainsKey(NearFadeOtherEffect))
        {
            var beside = far + (Flat(head.right) * 10f);
            checks.Add((NearFadeOtherEffect, SpawnEffect(prefabs, NearFadeOtherEffect, beside)));
        }
        else
            notes.Add($"{NearFadeOtherEffect} absent");
        yield return Wait(1f);
        foreach (var (name, holder) in checks)
            NearFadeFarCheck(name, holder, head, hazeBefore, notes);

        var burst = SpawnEffect(prefabs, named, position);
        yield return Wait(1f);
        var landed = ReadSheets(burst, head);
        notes.Add(F($"{named} burst at the player: nearest drawn {landed.NearestDrawn:0.00} m"));
        if (landed.NearestDrawn < SheetFade.FadeStart)
            Fail(F($"{named} burst at the player drew a sheet at {landed.NearestDrawn:0.00} m"));
        Destroy(burst);
        yield return Wait(NearFadeSettleSeconds);

        foreach (var (name, holder) in checks)
        {
            if (stepOk)
                yield return NearFadePlacedCheck(name, holder, head, notes);
            Destroy(holder);
            yield return Wait(NearFadeSettleSeconds);
        }
        if (stepOk)
            yield return NearFadeVanillaCheck(prefabs, head, notes);
        notes.Add(
            F(
                $"last scan's find {SmokeNearFade.LastFindMilliseconds:0.00} ms, sky level {SmokeNearFade.SkyLevel:0.00}"
            )
        );
        stepNote += "; " + string.Join("; ", notes);
    }

    /// <summary>Past the fade nothing changes: no sheet loses alpha and, with no sheet near, the haze does not rise. It is
    /// global and <c>read.Near</c> counts this effect only, so it is read against <paramref name="hazeBefore"/>.</summary>
    void NearFadeFarCheck(
        string name,
        GameObject holder,
        Transform head,
        float hazeBefore,
        List<string> notes
    )
    {
        var read = ReadSheets(holder, head);
        float rise = SmokeNearFade.HazeAlpha - hazeBefore;
        notes.Add(
            F(
                $"{name} {NearFadeFarMetres:0} m ahead: {read.Sheets} sheets, {read.FadedPastEnd} faded past {SheetFade.FadeEnd:0.0} m, {read.Near} near, haze {SmokeNearFade.HazeAlpha:0.00} from {hazeBefore:0.00}"
            )
        );
        if (read.Sheets == 0)
            Fail($"{name}: no sheets to read, so this proved nothing");
        if (read.FadedPastEnd > 0)
            Fail($"{name}: {read.FadedPastEnd} sheets past the fade lost alpha");
        if (read.Near == 0 && rise > NearFadeHazeMargin)
            Fail(F($"{name}: haze rose {rise:0.00} with no sheet near"));
    }

    /// <summary>A sheet under the fade and one past it: on, only the far one draws and a capped haze shows; off, one pass
    /// brings the near one back and takes the haze away.</summary>
    IEnumerator NearFadePlacedCheck(
        string name,
        GameObject holder,
        Transform head,
        List<string> notes
    )
    {
        var system = Array.Find(
            holder.GetComponentsInChildren<ParticleSystem>(),
            SmokeNearFade.Fades
        );
        if (system == null)
        {
            Fail($"{name}: none of its particle systems passes the near fade's filter");
            yield break;
        }
        float hazeBefore = SmokeNearFade.HazeAlpha;
        float timeScale = Time.timeScale;
        yield return PlaceSheetsAndFreeze(system, head);
        yield return Wait(NearFadeSettleSeconds);
        var on = ReadSheets(holder, head);
        float haze = SmokeNearFade.HazeAlpha;
        RenderFixesFeature.NearSmokeFade.Value = false;
        yield return Frames(3);
        var off = ReadSheets(holder, head);
        float hazeOff = SmokeNearFade.HazeAlpha;
        RenderFixesFeature.NearSmokeFade.Value = true;
        Time.timeScale = timeScale;
        float least = string.Equals(name, NearFadeEffects[0], StringComparison.Ordinal)
            ? NearFadePoisonMinHaze
            : NearFadeHazeMargin;
        notes.Add(
            F(
                $"{name} placed: on {on.Near} near, nearest drawn {on.NearestDrawn:0.00} m, haze {haze:0.00} from {hazeBefore:0.00}; off nearest drawn {off.NearestDrawn:0.00} m, {off.Faded} still faded, haze {hazeOff:0.00}"
            )
        );
        if (on.Near == 0)
            Fail($"{name}: the placed sheet is not there");
        else if (on.NearestDrawn < SheetFade.FadeStart)
            Fail(F($"{name}: a sheet draws at {on.NearestDrawn:0.00} m with the fade on"));
        else if (on.NearestDrawn > NearFadeKeptSheetZ + 0.1f)
            Fail(
                F($"{name}: the sheet at {NearFadeKeptSheetZ:0} m does not draw with the fade on")
            );
        else if (haze - hazeBefore < least || haze > SheetFade.MaxHaze + 0.01f)
            Fail(
                F(
                    $"{name}: haze {haze:0.00} from {hazeBefore:0.00} for a sheet taken away right ahead, wanted a rise of {least:0.00}"
                )
            );
        else if (off.NearestDrawn >= SheetFade.FadeStart)
            Fail($"{name}: the placed sheet does not draw with the fade off, so on proved nothing");
        else if (off.Faded > 0 || hazeOff > 0f)
            Fail(F($"{name}: off left {off.Faded} sheets faded and haze {hazeOff:0.00}"));
    }

    /// <summary>A big sheet with no hazard, placed like the others, stays exactly vanilla: the haze would go near black on
    /// the thin dense smoke of a campfire. A level without the effect only says so: a kept poison sheet proves the fade.</summary>
    IEnumerator NearFadeVanillaCheck(
        Dictionary<string, GameObject> prefabs,
        Transform head,
        List<string> notes
    )
    {
        var holder = SpawnEffect(
            prefabs,
            NearFadeVanillaEffect,
            head.position + (Flat(head.forward) * NearFadeFarMetres)
        );
        if (holder == null)
        {
            notes.Add($"{NearFadeVanillaEffect} absent");
            yield break;
        }
        var system = Array.Find(
            holder.GetComponentsInChildren<ParticleSystem>(),
            SmokeNearFade.IsSheet
        );
        if (system == null)
        {
            Fail($"{NearFadeVanillaEffect}: no big sheet in it, so this proved nothing");
            yield break;
        }
        float hazeBefore = SmokeNearFade.HazeAlpha;
        float timeScale = Time.timeScale;
        yield return PlaceSheetsAndFreeze(system, head);
        yield return Wait(NearFadeSettleSeconds);
        var read = ReadSheets(holder, head);
        float haze = SmokeNearFade.HazeAlpha;
        Time.timeScale = timeScale;
        notes.Add(
            F(
                $"{NearFadeVanillaEffect} placed: {read.Near} near, nearest drawn {read.NearestDrawn:0.00} m, {read.Faded} faded, haze {haze:0.00} from {hazeBefore:0.00}"
            )
        );
        if (SmokeNearFade.Fades(system))
            Fail($"{NearFadeVanillaEffect} passes the near fade's filter");
        else if (read.Near == 0)
            Fail($"{NearFadeVanillaEffect}: the placed sheet is not there");
        else if (read.Faded > 0)
            Fail($"{NearFadeVanillaEffect}: {read.Faded} sheets lost alpha");
        else if (haze - hazeBefore > NearFadeHazeMargin)
            Fail(F($"{NearFadeVanillaEffect}: haze rose to {haze:0.00} from {hazeBefore:0.00}"));
        Destroy(holder);
    }

    /// <summary>Two frames with time running, so the renderer's bounds, which decide whether a system is read, hold the
    /// new sheets; how they follow at timeScale 0 is not known. Then frozen, so what is placed stays where it is. The
    /// caller puts the time back.</summary>
    IEnumerator PlaceSheetsAndFreeze(ParticleSystem system, Transform head)
    {
        PlaceSheets(
            system,
            head.position + (head.forward * NearFadeSheetZ),
            head.position + (head.forward * NearFadeKeptSheetZ)
        );
        yield return Frames(2);
        FreezeWorld();
    }

    /// <summary>One still sheet at each spot, aged by <see cref="NearFadeSheetAge"/>. Emit has no age of its own.</summary>
    static void PlaceSheets(ParticleSystem system, params Vector3[] spots)
    {
        bool world = system.main.simulationSpace == ParticleSystemSimulationSpace.World;
        foreach (var spot in spots)
            system.Emit(
                new ParticleSystem.EmitParams
                {
                    position = world ? spot : system.transform.InverseTransformPoint(spot),
                    applyShapeToPosition = false,
                    velocity = Vector3.zero,
                    startSize = NearFadeSheetSize,
                    startLifetime = NearFadeSheetLife,
                },
                1
            );
        var buffer = new ParticleSystem.Particle[Mathf.Max(1000, system.particleCount)];
        int count = system.GetParticles(buffer);
        for (int i = 0; i < count; i++)
            if (Mathf.Approximately(buffer[i].startLifetime, NearFadeSheetLife))
                buffer[i].remainingLifetime = NearFadeSheetLife * (1f - NearFadeSheetAge);
        system.SetParticles(buffer, count);
    }

    private struct SheetRead
    {
        public int Sheets;
        public int Near;
        public int Faded;
        public int FadedPastEnd;
        public float NearestDrawn;
    }

    /// <summary>The sheets the near fade covers, ahead of the head along its forward: how many are under the fade, how
    /// many hold less than their start alpha, and the nearest still drawn.</summary>
    static SheetRead ReadSheets(GameObject holder, Transform head)
    {
        var read = new SheetRead { NearestDrawn = float.PositiveInfinity };
        foreach (var ps in holder.GetComponentsInChildren<ParticleSystem>())
        {
            if (!SmokeNearFade.IsSheet(ps))
                continue;
            var buffer = new ParticleSystem.Particle[Mathf.Max(1000, ps.particleCount)];
            byte full = ((Color32)SmokeNearFade.StartColor(ps)).a;
            bool world = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
            int count = ps.GetParticles(buffer);
            read.Sheets += count;
            for (int i = 0; i < count; i++)
            {
                var at = world
                    ? buffer[i].position
                    : ps.transform.TransformPoint(buffer[i].position);
                float z = Vector3.Dot(at - head.position, head.forward);
                if (buffer[i].startColor.a != full)
                {
                    read.Faded++;
                    if (Mathf.Abs(z) >= SheetFade.FadeEnd)
                        read.FadedPastEnd++;
                }
                if (z <= 0f)
                    continue;
                if (z < SheetFade.FadeStart)
                    read.Near++;
                if (buffer[i].GetCurrentColor(ps).a > NearFadeDrawnAlpha && z < read.NearestDrawn)
                    read.NearestDrawn = z;
            }
        }
        return read;
    }

    IEnumerator EyeEffectHold(Dictionary<string, GameObject> prefabs, Vector3 spot, string where)
    {
        var holder = SpawnEffect(prefabs, MonoDiffEffects[0], spot);
        yield return Wait(1f);
        foreach (var ps in holder.GetComponentsInChildren<ParticleSystem>())
            ps.Pause(false);
        Plugin.Log.LogInfo($"[selftest] eye-hold ready {MonoDiffEffects[0]} {where}");
        yield return Wait(30f);
        Destroy(holder);
        yield return Frames(2);
    }

    /// <summary>What the effect added to each eye, 1 s after it spawned, against a read before it: how much differs
    /// between the eyes, the shift that best explains the rest, and what that shift leaves. The world is not frozen here,
    /// so a read 1 s earlier gives what the same second does with no effect: a line is only readable when its signal is
    /// well over that noise.</summary>
    IEnumerator EyeEffectMetrics(
        Camera cam,
        XRDisplaySubsystem display,
        Dictionary<string, GameObject> prefabs,
        string effect,
        Vector3 spot,
        string where
    )
    {
        var quiet = new EyeImages(2);
        yield return ReadEyes(cam, display, quiet);
        yield return Wait(1f);
        var before = new EyeImages(2);
        yield return ReadEyes(cam, display, before);
        var holder = SpawnEffect(prefabs, effect, spot);
        yield return Wait(1f);
        var after = new EyeImages(2);
        yield return ReadEyes(cam, display, after);
        string problem = ReadsProblem(quiet, before, after);
        if (problem != null)
            Plugin.Log.LogWarning($"[selftest] eye-effect {effect} {where}: {problem}");
        else
            Plugin.Log.LogInfo(EyeEffectLine(cam, quiet, before, after, holder, effect, where));
        Destroy(holder);
        yield return Frames(2);
        // A line after this one must not read the haze easing out as noise.
        if (SmokeNearFade.HazeAlpha > 0f)
            yield return Wait(NearFadeSettleSeconds);
    }

    static string EyeEffectLine(
        Camera cam,
        EyeImages quiet,
        EyeImages before,
        EyeImages after,
        GameObject holder,
        string effect,
        string where
    )
    {
        var left = Added(EyeLuma(after, 0), EyeLuma(before, 0));
        var right = Added(EyeLuma(after, 1), EyeLuma(before, 1));
        int w = after.Width / EyeLumaStep;
        int h = after.Height / EyeLumaStep;
        // The axes sit in the viewport, which a target can outgrow, but both are read in target columns.
        float axis = EyeImageMatch.AxisShift(
            Mathf.RoundToInt(after.Viewports[0].width) / EyeLumaStep,
            EyeTangents(after, 0),
            EyeTangents(after, 1)
        );
        // A near sheet can sit 370 px apart at full size.
        int from = Mathf.Clamp(Mathf.RoundToInt(axis) - 100, 1 - w, w - 1);
        int to = Mathf.Clamp(Mathf.RoundToInt(axis) + 100, 1 - w, w - 1);
        int shift = EyeImageMatch.BestShift(left, right, w, h, from, to);
        float residual = EyeImageMatch.Residual(left, right, w, h, shift);
        float mass = EyeImageMatch.MassRatio(left, right);
        float nearest = NearestSheet(holder, cam.transform);
        float signal = Mathf.Max(
            EyeImageMatch.Rms(EyeLuma(after, 0), EyeLuma(before, 0)),
            EyeImageMatch.Rms(EyeLuma(after, 1), EyeLuma(before, 1))
        );
        float noise = Mathf.Max(
            EyeImageMatch.Rms(EyeLuma(before, 0), EyeLuma(quiet, 0)),
            EyeImageMatch.Rms(EyeLuma(before, 1), EyeLuma(quiet, 1))
        );
        return F(
            $"[selftest] eye-effect {effect} {where}: signal {signal:0.00}, noise {noise:0.00}, mass {mass:0.000}, shift {shift} (axis {axis:0.0}), residual {residual:0.000}, nearest sheet {nearest:0.00} m"
        );
    }

    static float[] Added(float[] after, float[] before)
    {
        var added = new float[after.Length];
        for (int i = 0; i < added.Length; i++)
            added[i] = after[i] - before[i];
        return added;
    }

    static EyeView EyeTangents(EyeImages image, int eye)
    {
        var p = image.Projections[eye];
        var (left, right, bottom, top) = StereoCull.Tangents(p.m00, p.m02, p.m11, p.m12);
        return new EyeView(
            System.Numerics.Vector3.Zero,
            System.Numerics.Quaternion.Identity,
            left,
            right,
            bottom,
            top
        );
    }

    /// <summary>The smallest distance ahead of the head, along its forward, of any live particle of the effect.</summary>
    static float NearestSheet(GameObject holder, Transform head)
    {
        var buffer = new ParticleSystem.Particle[1000];
        float nearest = float.PositiveInfinity;
        foreach (var ps in holder.GetComponentsInChildren<ParticleSystem>())
        {
            bool world = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
            int count = ps.GetParticles(buffer);
            for (int i = 0; i < count; i++)
            {
                var at = world
                    ? buffer[i].position
                    : ps.transform.TransformPoint(buffer[i].position);
                float z = Vector3.Dot(at - head.position, head.forward);
                if (z > 0f && z < nearest)
                    nearest = z;
            }
        }
        return nearest;
    }

    /// <summary>vr.483: over 30 frames each eye renders from its target's corner and with its own depth-normal prepass.
    /// Without that prepass, from a disabled fog or decal feature, transparents would read another eye's stale depth,
    /// because the renderer copies depth only after them. The note's cant and tangents are what a real headset's bug report
    /// answers.</summary>
    IEnumerator EyeStateStep()
    {
        if (!EyesReady(out var cam, out _))
            yield break;
        yield return EyesSettled();
        if (!stepOk)
            yield break;
        int frames = 0;
        int good = 0;
        string bad = null;
        for (int i = 0; i < 30; i++)
        {
            yield return null;
            var left = EyeMatrixFeature.Eyes[0];
            var right = EyeMatrixFeature.Eyes[1];
            int rendered = Time.frameCount - 1;
            if (left.Frame < rendered || right.Frame < rendered)
                continue;
            frames++;
            string wrong = EyeStateProblem(left, "left") ?? EyeStateProblem(right, "right");
            if (wrong == null)
                good++;
            else
                bad = wrong;
        }
        stepNote = $"{good} of {frames} frames; {EyeStateNote(cam, 0)}; {EyeStateNote(cam, 1)}";
        if (frames == 0)
            Fail("no frame recorded both eyes");
        else if (good < frames)
            Fail($"{frames - good} of {frames} frames: {bad}");
    }

    static string EyeStateProblem(in EyeMatrixFeature.EyeRecord eye, string label)
    {
        if (eye.Viewport.x >= 0.5f || eye.Viewport.x <= -0.5f)
            return F($"{label} eye viewport starts at x {eye.Viewport.x:0}");
        return eye.NormalsPrepass ? null : $"{label} eye had no depth-normal prepass";
    }

    static string EyeStateNote(Camera cam, int eye)
    {
        var record = EyeMatrixFeature.Eyes[eye];
        var (left, right, bottom, top) = StereoCull.Tangents(
            record.Proj.m00,
            record.Proj.m02,
            record.Proj.m11,
            record.Proj.m12
        );
        // View space looks down -Z.
        Vector3 forward = cam.transform.InverseTransformDirection(
            -(Vector3)record.View.inverse.GetColumn(2)
        );
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        return F(
            $"{(eye == 0 ? "left" : "right")} cant {yaw:0.00} deg, viewport {record.Viewport}, tangents {left:0.00}/{right:0.00}/{bottom:0.00}/{top:0.00}, prepass {record.NormalsPrepass}"
        );
    }
}
