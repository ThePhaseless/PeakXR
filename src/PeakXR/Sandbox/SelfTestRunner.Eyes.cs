using System;
using System.Collections;
using System.Collections.Generic;
using Peak;
using PeakXR.Core;
using PeakXR.Render;
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
/// <c>vr.482</c> logs stereo metrics per effect.
/// </summary>
internal sealed partial class SelfTestRunner
{
    private const int EyeLumaStep = 4;
    private const int MonoDiffReads = 10;

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
    private static readonly string[] SceneEffects = { "Broad Wind", "spores" };

    /// <summary>Metres to the side of the spot, so one does not hide another.</summary>
    private static readonly float[] MonoDiffSides = { 0f, -2f, 2f };

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
        FreezeWorld();
        yield return Frames(3);

        var first = new EyeImages(2);
        var second = new EyeImages(2);
        yield return ReadEyes(cam, display, first);
        yield return ReadEyes(cam, display, second);
        string problem = first.Problem ?? second.Problem;
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
        var notes = new List<string> { F($"floor L {floor[0]:0.00} R {floor[1]:0.00} of 255") };
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
            if (!HoleVerdict(cam, eyeLocked, floor, notes, fails))
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
        string problem = flashOff.Problem ?? flashOn.Problem;
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

    /// <summary>P4: the hole's centre, in head-frame tangents, per eye. Off it differs by about the eyes' stereo on a quad
    /// at D, and on it must not differ. False on a red.</summary>
    bool HoleVerdict(
        Camera cam,
        bool eyeLocked,
        float[] floor,
        List<string> notes,
        List<string> fails
    )
    {
        string phase = eyeLocked ? "P4 on" : "P4 off";
        string problem = flashOff.Problem ?? flashOn.Problem;
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
            // The head's axis in this eye. The hole's chord is centred on any row through it, so a row a few pixels off
            // or a flipped readback changes nothing.
            Vector3 axis = view.MultiplyVector(cam.transform.forward);
            float axisX = axis.x / -axis.z;
            float axisY = axis.y / -axis.z;
            int column = Mathf.Clamp(
                Mathf.RoundToInt(viewport.x + (axisX - left) / (right - left) * viewport.width),
                0,
                flashOn.Width - 1
            );
            int row = Mathf.Clamp(
                Mathf.RoundToInt(viewport.y + (axisY - bottom) / (top - bottom) * viewport.height),
                0,
                flashOn.Height - 1
            );
            var (leftEdge, rightEdge) = EyeImageMatch.HoleEdges(
                flashOff.Pixels[eye],
                flashOn.Pixels[eye],
                flashOn.Width,
                row,
                column,
                Mathf.CeilToInt(Mathf.Max(8f, 3f * floor[eye]))
            );
            if (leftEdge < 0 || rightEdge < 0)
            {
                fails.Add(
                    $"{phase}: no hole in the {(eye == 0 ? "left" : "right")} eye (edges {leftEdge}, {rightEdge} from column {column}, row {row})"
                );
                return false;
            }
            float a = HeadTangent(cam, view, viewport, left, right, axisY, leftEdge);
            float b = HeadTangent(cam, view, viewport, left, right, axisY, rightEdge);
            centre[eye] = (a + b) * 0.5f;
            notes.Add(
                F(
                    $"{phase} {(eye == 0 ? "L" : "R")} hole columns {leftEdge}-{rightEdge}, centre {centre[eye]:0.000} tan"
                )
            );
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

    void SaveFlash(string shot) => SavePair(flashOn, shot);

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

    /// <summary>A copy of a game effect with what it does to players and what ends it on its own taken out, or null when
    /// it is not loaded. Returns its holder, to destroy early; <see cref="OnRestore"/> destroys it otherwise.</summary>
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
        Strip<AOE>(copy);
        Strip<AddScreenshake>(copy);
        Strip<TimeEvent>(copy);
        // VFX_PoisonExplo would end on its own while vr.482 holds it.
        Strip<RemoveAfterSeconds>(copy);
        // Gas. TrickLuggage goes first: its Update reaches for the field (Peak/TrickLuggage.cs:29).
        Strip<TrickLuggage>(copy);
        Strip<StatusFieldBase>(copy);
        // Flare smoke. A copy tracks nothing: it logs an error and stops, or destroys itself (TrackNetworkedObject.cs:43,55).
        Strip<TrackNetworkedObject>(copy);
        copy.SetActive(true);
        holder.SetActive(true);
        foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>())
            if (!ps.isPlaying)
                ps.Play(false);
        return holder;
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

        FreezeWorld();
        foreach (var effect in effects)
        foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>())
            ps.Pause(false);
        yield return Frames(3);
        var before = head.position;
        var normal = new EyeImages(2);
        var again = new EyeImages(2);
        var swapped = new EyeImages(2);
        yield return ReadEyes(cam, display, normal);
        yield return ReadEyes(cam, display, again);
        EyeViewOverride.Install(EyeViewOverride.Mode.Swap, cam);
        yield return Frames(2);
        yield return ReadEyes(cam, display, swapped);
        if (!EyeViewOverride.Remove())
            fails.Add("something chained above the view override during the swap phase");
        string problem = normal.Problem ?? again.Problem ?? swapped.Problem;
        if (problem != null)
            fails.Add("swap read: " + problem);
        else
        {
            float still = Mathf.Max(
                EyeImageMatch.Rms(EyeLuma(normal, 0), EyeLuma(again, 0)),
                EyeImageMatch.Rms(EyeLuma(normal, 1), EyeLuma(again, 1))
            );
            float moved = Vector3.Distance(before, head.position);
            if (still > 1f || moved > 0.001f)
            {
                swapSkipped = true;
                notes.Add(
                    F(
                        $"swap phase SKIPPED: the frozen view was not still ({still:0.00} of 255, head moved {moved * 1000f:0.0} mm), so this run did not prove it"
                    )
                );
            }
            else
            {
                float toLeft = EyeImageMatch.Rms(EyeLuma(swapped, 1), EyeLuma(normal, 0));
                float toRight = EyeImageMatch.Rms(EyeLuma(swapped, 0), EyeLuma(normal, 1));
                float shareLeft = DrawnPixels.ChangedShare(swapped.Pixels[1], normal.Pixels[0]);
                float shareRight = DrawnPixels.ChangedShare(swapped.Pixels[0], normal.Pixels[1]);
                notes.Add(
                    F(
                        $"swap right-as-left {toLeft:0.00} share {shareLeft:0.0000}, left-as-right {toRight:0.00} share {shareRight:0.0000}, still {still:0.00}"
                    )
                );
                if (
                    toLeft > 1f
                    || toRight > 1f
                    || shareLeft > MonoDiffShare
                    || shareRight > MonoDiffShare
                )
                {
                    fails.Add(
                        "swapped eyes do not match the other eye's image: stale per-eye data"
                    );
                    SavePair(swapped, "vr-481-swapped");
                    SavePair(normal, "vr-481-normal");
                }
            }
        }

        stepNote = string.Join("; ", notes);
        if (fails.Count > 0)
            Fail(string.Join("; ", fails));
        else if (swapSkipped)
            stepSkip = stepNote;
    }

    /// <summary>Chosen alone, vr.482 holds the named effect for a compositor screenshot instead of measuring; a run that
    /// lists it with others must not stop there.</summary>
    bool EyeEffectsLive =>
        filter != null
        && filter.Patterns.Count == 1
        && string.Equals(filter.Patterns[0], "vr.482-eye-effects", StringComparison.Ordinal);

    /// <summary>vr.482: measure only, so nothing here fails the step. Logs stereo metrics per effect, or holds
    /// <c>VFX_PoisonExplo</c> still in <see cref="EyeEffectsLive"/>.</summary>
    IEnumerator EyeEffectsStep()
    {
        if (!EyesReady(out var cam, out var display))
            yield break;
        var prefabs = LoadedPrefabs();
        var head = cam.transform;
        var player = head.position;
        var ahead = player + Flat(head.forward) * 6f;
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
        stepNote = $"{EyeEffects.Length - absent.Count} effects measured (eye-effect log lines)";
        if (absent.Count > 0)
            stepNote += "; absent: " + string.Join(", ", absent);
        if (absent.Contains(MonoDiffEffects[0]))
            stepNote += $"; {MonoDiffEffects[0]} was not measured in this run";
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
    /// between the eyes, the shift that best explains the rest, and what that shift leaves.</summary>
    IEnumerator EyeEffectMetrics(
        Camera cam,
        XRDisplaySubsystem display,
        Dictionary<string, GameObject> prefabs,
        string effect,
        Vector3 spot,
        string where
    )
    {
        var before = new EyeImages(2);
        yield return ReadEyes(cam, display, before);
        var holder = SpawnEffect(prefabs, effect, spot);
        yield return Wait(1f);
        var after = new EyeImages(2);
        yield return ReadEyes(cam, display, after);
        string problem = before.Problem ?? after.Problem;
        if (problem != null)
            Plugin.Log.LogWarning($"[selftest] eye-effect {effect} {where}: {problem}");
        else
            Plugin.Log.LogInfo(EyeEffectLine(cam, before, after, holder, effect, where));
        Destroy(holder);
        yield return Frames(2);
    }

    static string EyeEffectLine(
        Camera cam,
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
        float axis = EyeImageMatch.AxisShift(w, EyeTangents(after, 0), EyeTangents(after, 1));
        // A near sheet can sit 370 px apart at full size.
        int from = Mathf.Clamp(Mathf.RoundToInt(axis) - 100, 1 - w, w - 1);
        int to = Mathf.Clamp(Mathf.RoundToInt(axis) + 100, 1 - w, w - 1);
        int shift = EyeImageMatch.BestShift(left, right, w, h, from, to);
        float residual = EyeImageMatch.Residual(left, right, w, h, shift);
        float mass = EyeImageMatch.MassRatio(left, right);
        float nearest = NearestSheet(holder, cam.transform);
        return F(
            $"[selftest] eye-effect {effect} {where}: mass {mass:0.000}, shift {shift} (axis {axis:0.0}), residual {residual:0.000}, nearest sheet {nearest:0.00} m"
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
}
