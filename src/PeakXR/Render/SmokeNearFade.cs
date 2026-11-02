using System.Collections.Generic;
using System.Diagnostics;
using Peak;
using PeakXR.Core;
using PeakXR.UI.Common;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakXR.Render;

/// <summary>
/// Fades big view-aligned smoke sheets near the eyes and shows a haze for what that took away (<c>Render.NearSmokeFade</c>,
/// issue #5). A flat sheet of 2 m or more half a metre away shows each eye a very different part of itself. Only smoke
/// that warns of a hazard takes part, a system under an AOE, StatusFieldBase or StatusEmitter: poison, spore bursts, gas,
/// spore trees and gloom Haze. Other smoke stays exactly vanilla: <see cref="SheetFade.Lost"/> adds up opacity at full
/// texture, so the hundreds of thin sheets of a campfire or a held flare would make a near-black haze. Geysers are out
/// as well, their AOE and StatusEmitter being siblings of the particles. It only writes this client's particle alpha:
/// AOE, StatusField and RPCs stay untouched and other players see no change.
/// What a sheet draws comes from the shaders' last alpha step, on <c>_Opacity</c> and <c>_FinalAlphaRemap</c> (in the
/// shaders' constant table: cb3[7].y and cb3[4].xy in SmokeParticleSimple, cb3[7].x and cb3[5].zw in SmokeParticle). The
/// 0.361 in SporeSmoke's <c>_AlphaRemap</c> cuts the texture before vertex alpha, so a poison sheet draws until its alpha
/// runs out.
/// </summary>
internal sealed class SmokeNearFade : MonoBehaviour
{
    private const string Tag = "[render.fixes]";
    private const string SmokeShaderPrefix = "SmokeParticle";
    private const float ScanSeconds = 2f;
    private const float ScanRadius = 25f;
    private const float MinSheetSize = 2f;
    private const int MaxSystems = 32;
    private const int MaxRememberedSystems = 2048;
    private const float HazeEaseSeconds = 0.3f;
    private const float HazeDiameter = 1.2f;
    private const float HazeHidden = 0.002f;
    private const float MinSkyLight = 0.3f;
    private const float FadeEndSquared = SheetFade.FadeEnd * SheetFade.FadeEnd;

    private static readonly int FinalAlphaRemapId = Shader.PropertyToID("_FinalAlphaRemap");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>Set by DayNightManager.cs:406; black on a level without one.</summary>
    private static readonly int SkyMidColorId = Shader.PropertyToID("SkyMidColor");

    /// <summary>A verdict per system, so a scan only looks at each one once. Null: not a big sheet of hazard smoke.</summary>
    private readonly Dictionary<ParticleSystem, Sheet> known = new();
    private readonly HashSet<string> loggedSkips = new(System.StringComparer.Ordinal);
    private List<Sheet> kept = new(MaxSystems);
    private List<Sheet> previous = new(MaxSystems);
    private ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[1000];
    private float nextScan;
    private int scanStamp;
    private bool running;
    private static bool scanWanted;

    private GameObject haze;
    private Material hazeMaterial;
    private MeshRenderer hazeRenderer;
    private Color hazeColor = Color.gray;

    /// <summary>The haze's alpha this frame, for self-test vr.482.</summary>
    internal static float HazeAlpha { get; private set; }

    /// <summary>What the last scan's <c>FindObjectsByType</c> took, for self-test vr.482: a level holds about 184,000
    /// GameObjects.</summary>
    internal static float LastFindMilliseconds { get; private set; }

    /// <summary>The sky's brightest channel at the last scan, before its clamp, for self-test vr.482.</summary>
    internal static float SkyLevel { get; private set; }

    private sealed class Sheet
    {
        public ParticleSystem System;
        public ParticleSystemRenderer Renderer;
        public float MaxSize;
        public bool AnyFaded;

        /// <summary>What particle positions are relative to; null for world space.</summary>
        public Transform Space;
        public float RemapX;
        public float RemapY;
        public float Opacity;
        public Color MaterialTint;
        public byte StartAlpha;
        public Color Tint;
        public int Stamp;
    }

    /// <summary>A hazard effect wakes a scan for its first frame instead of waiting for the next slow one.</summary>
    internal static void RequestScan() => scanWanted = true;

    /// <summary>Whether this system's sheets fade: <see cref="IsSheet"/>, on an effect that warns of a hazard.</summary>
    internal static bool Fades(ParticleSystem system) => IsSheet(system) && Warns(system);

    /// <summary>Big billboards of the smoke shaders whose start alpha is one value, hazard or not.</summary>
    internal static bool IsSheet(ParticleSystem system) =>
        system.TryGetComponent<ParticleSystemRenderer>(out var renderer)
        && IsBigSmokeSheet(renderer, system.main)
        && OneStartAlpha(system.main.startColor);

    /// <summary>Includes what is switched off: a burst's AOE stays on it after it has gone off.</summary>
    private static bool Warns(ParticleSystem system) =>
        system.GetComponentInParent<AOE>(true) != null
        || system.GetComponentInParent<StatusFieldBase>(true) != null
        || system.GetComponentInParent<StatusEmitter>(true) != null;

    private static bool IsBigSmokeSheet(
        ParticleSystemRenderer renderer,
        ParticleSystem.MainModule main
    )
    {
        var material = renderer.sharedMaterial;
        return renderer.renderMode == ParticleSystemRenderMode.Billboard
            && material != null
            && material.shader != null
            && material.shader.name.StartsWith(SmokeShaderPrefix, System.StringComparison.Ordinal)
            && BiggestStart(main.startSize) >= MinSheetSize;
    }

    /// <summary>TwoConstants can hold its range either way round: <c>VFX_Petrify</c> has max 2, min 4.</summary>
    private static float BiggestStart(ParticleSystem.MinMaxCurve size) =>
        size.mode switch
        {
            ParticleSystemCurveMode.Constant => size.constant,
            ParticleSystemCurveMode.TwoConstants => Mathf.Max(size.constantMin, size.constantMax),
            _ => size.curveMultiplier,
        };

    /// <summary>A particle holds only its faded alpha, so it must be possible to tell what it started from. TwoColors with
    /// one alpha qualifies: only alpha is written, and RGB stays per particle.</summary>
    private static bool OneStartAlpha(ParticleSystem.MinMaxGradient color) =>
        color.mode == ParticleSystemGradientMode.Color
        || (
            color.mode == ParticleSystemGradientMode.TwoColors
            && Mathf.Approximately(color.colorMin.a, color.colorMax.a)
        );

    /// <summary>The start colour of a system that <see cref="Fades"/>: for TwoColors, between the two.</summary>
    internal static Color StartColor(ParticleSystem system)
    {
        var start = system.main.startColor;
        return start.mode == ParticleSystemGradientMode.TwoColors
            ? Color.Lerp(start.colorMin, start.colorMax, 0.5f)
            : start.color;
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    /// <summary>The last scene's systems are gone, and would sit here as destroyed keys.</summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => known.Clear();

    private void LateUpdate()
    {
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        if (!VRSession.Active || !RenderFixesFeature.NearSmokeFade.Value || head == null)
        {
            if (running)
                Stop();
            return;
        }
        running = true;
        var position = head.position;
        var forward = head.forward;
        if (scanWanted || Time.unscaledTime >= nextScan)
        {
            scanWanted = false;
            nextScan = Time.unscaledTime + ScanSeconds;
            Scan(position);
        }

        float lost = 0f;
        var tint = default(Color);
        float weight = 0f;
        for (int i = 0; i < kept.Count; i++)
            if (kept[i].System != null && NeedsRead(kept[i], position))
                FadeSheets(kept[i], position, forward, ref lost, ref tint, ref weight);
        if (weight > 0f)
            hazeColor = tint / weight;
        ShowHaze(SheetFade.Haze(lost), head);
    }

    /// <summary>Nothing to read with no sheets, or with them all too far to fade and none faded last frame to give back.</summary>
    private static bool NeedsRead(Sheet sheet, Vector3 head)
    {
        if (sheet.System.particleCount == 0)
        {
            sheet.AnyFaded = false;
            return false;
        }
        return sheet.AnyFaded || SquaredDistance(sheet, head) <= FadeEndSquared;
    }

    /// <summary>To the sheets, sizes included, and not the emitter: <c>Haze</c> has an 18 m shape.</summary>
    private static float SquaredDistance(Sheet sheet, Vector3 head) =>
        sheet.Renderer.bounds.SqrDistance(head);

    private void FadeSheets(
        Sheet sheet,
        Vector3 head,
        Vector3 forward,
        ref float lost,
        ref Color tint,
        ref float weight
    )
    {
        int count = ReadParticles(sheet.System);
        bool changed = false;
        bool anyFaded = false;
        for (int i = 0; i < count; i++)
        {
            ref var particle = ref buffer[i];
            var offset =
                (
                    sheet.Space != null
                        ? sheet.Space.TransformPoint(particle.position)
                        : particle.position
                ) - head;
            float z = Vector3.Dot(offset, forward);
            float fade = SheetFade.Alpha(z, SheetFade.FadeStart, SheetFade.FadeEnd);
            var color = particle.startColor;
            byte had = color.a;
            if (z > 0f && fade < 1f)
            {
                // Full start alpha first: the over-life gradient lives outside startColor, and reading the faded value
                // would feed the fade back into what it lost.
                color.a = sheet.StartAlpha;
                particle.startColor = color;
                float a = particle.GetCurrentColor(sheet.System).a / 255f * sheet.Opacity;
                float removed =
                    SheetFade.Opacity(a, sheet.RemapX, sheet.RemapY)
                    - SheetFade.Opacity(a * fade, sheet.RemapX, sheet.RemapY);
                float lateral = (offset - (forward * z)).magnitude;
                float size = SheetFade.DrawnSize(
                    particle.GetCurrentSize(sheet.System),
                    sheet.MaxSize,
                    z
                );
                removed *= SheetFade.ViewShare(lateral, size, z);
                lost = SheetFade.Lost(lost, removed);
                tint += sheet.Tint * removed;
                weight += removed;
            }
            byte alpha = (byte)(sheet.StartAlpha * fade);
            changed |= had != alpha;
            anyFaded |= alpha != sheet.StartAlpha;
            color.a = alpha;
            particle.startColor = color;
        }
        sheet.AnyFaded = anyFaded;
        if (changed)
            sheet.System.SetParticles(buffer, count);
    }

    /// <summary>One whole pass back to full alpha, for a system leaving the list or the fade stopping.</summary>
    private void Restore(Sheet sheet)
    {
        if (sheet.System == null)
            return;
        int count = ReadParticles(sheet.System);
        bool changed = false;
        for (int i = 0; i < count; i++)
        {
            var color = buffer[i].startColor;
            if (color.a == sheet.StartAlpha)
                continue;
            color.a = sheet.StartAlpha;
            buffer[i].startColor = color;
            changed = true;
        }
        if (changed)
            sheet.System.SetParticles(buffer, count);
    }

    /// <summary>The system's particles in <see cref="buffer"/>. Grown first: SetParticles keeps only what it is given,
    /// so a short buffer would delete sheets.</summary>
    private int ReadParticles(ParticleSystem system)
    {
        int alive = system.particleCount;
        if (alive == 0)
            return 0;
        if (alive > buffer.Length)
            buffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(alive)];
        return system.GetParticles(buffer);
    }

    private void Scan(Vector3 head)
    {
        scanStamp++;
        (kept, previous) = (previous, kept);
        kept.Clear();
        if (known.Count > MaxRememberedSystems)
            known.Clear();
        SkyLevel = Shader.GetGlobalColor(SkyMidColorId).maxColorComponent;
        // A flat unlit colour would glow at night and in caves.
        float light = Mathf.Clamp(SkyLevel, MinSkyLight, 1f);
        float radiusSquared = ScanRadius * ScanRadius;
        long started = Stopwatch.GetTimestamp();
        var systems = FindObjectsByType<ParticleSystem>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );
        LastFindMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000f / Stopwatch.Frequency;
        // shortcut: the first 32 in range, not the nearest; a level with more big sheets than that in 25 m is not known.
        foreach (var system in systems)
        {
            if (kept.Count >= MaxSystems)
                break;
            if (!known.TryGetValue(system, out var sheet))
                known[system] = sheet = Describe(system);
            // The emitter as well: a system this scan catches on its first frame has no simulated bounds yet.
            if (
                sheet == null
                || (
                    SquaredDistance(sheet, head) > radiusSquared
                    && (system.transform.position - head).sqrMagnitude > radiusSquared
                )
            )
                continue;
            ReadStartColor(sheet, light);
            sheet.Stamp = scanStamp;
            kept.Add(sheet);
        }
        foreach (var old in previous)
            if (old.Stamp != scanStamp)
                Restore(old);
        previous.Clear();
    }

    private Sheet Describe(ParticleSystem system)
    {
        var main = system.main;
        if (
            !system.TryGetComponent<ParticleSystemRenderer>(out var renderer)
            || !IsBigSmokeSheet(renderer, main)
            || !Warns(system)
        )
            return null;
        if (!OneStartAlpha(main.startColor))
        {
            if (loggedSkips.Add(system.name))
                Plugin.Log.LogInfo(
                    $"{Tag} near smoke fade skips '{system.name}': its start alpha varies per particle ({main.startColor.mode})"
                );
            return null;
        }
        var material = renderer.sharedMaterial;
        var remap = material.HasProperty(FinalAlphaRemapId)
            ? material.GetVector(FinalAlphaRemapId)
            : new Vector4(0f, 1f);
        Transform space = main.simulationSpace switch
        {
            ParticleSystemSimulationSpace.World => null,
            ParticleSystemSimulationSpace.Custom => main.customSimulationSpace,
            _ => system.transform,
        };
        return new Sheet
        {
            System = system,
            Renderer = renderer,
            MaxSize = renderer.maxParticleSize,
            Space = space,
            RemapX = remap.x,
            RemapY = remap.y,
            Opacity = material.HasProperty(OpacityId) ? material.GetFloat(OpacityId) : 1f,
            // Clamped: some materials go HDR for their highlights (SporeSmokeExplo's _Color2 is 111). The shader
            // multiplies this into the vertex colour, so without it the haze of dark smoke would be white.
            MaterialTint = material.HasProperty(ColorId)
                ? ClampColor(material.GetColor(ColorId))
                : Color.white,
        };
    }

    /// <summary>Read at every scan: the sky moves on, and the game can set a start colour at runtime.</summary>
    private static void ReadStartColor(Sheet sheet, float light)
    {
        var color = StartColor(sheet.System);
        sheet.StartAlpha = ((Color32)color).a;
        sheet.Tint = color * sheet.MaterialTint * light;
    }

    private static Color ClampColor(Color color) =>
        new(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1f);

    private void ShowHaze(float target, Transform head)
    {
        HazeAlpha = Mathf.MoveTowards(HazeAlpha, target, Time.unscaledDeltaTime / HazeEaseSeconds);
        if (HazeAlpha <= HazeHidden)
        {
            if (hazeRenderer != null && hazeRenderer.enabled)
                hazeRenderer.enabled = false;
            return;
        }
        if (haze == null)
        {
            if (hazeMaterial == null)
                hazeMaterial = UIMaterials.Create(Color.clear, drawOnTop: false, renderQueue: 3100);
            haze = UIMaterials.MeshObject(
                "PeakXR_SmokeHaze",
                UIMaterials.SphereMesh(),
                hazeMaterial,
                head
            );
            haze.transform.localScale = Vector3.one * HazeDiameter;
            hazeRenderer = haze.GetComponent<MeshRenderer>();
        }
        else if (haze.transform.parent != head)
            haze.transform.SetParent(head, false);
        // One colour has no pattern, so it is the same in both eyes.
        hazeMaterial.color = new Color(hazeColor.r, hazeColor.g, hazeColor.b, HazeAlpha);
        if (!hazeRenderer.enabled)
            hazeRenderer.enabled = true;
    }

    /// <summary>VR stopped or the setting went off: nothing may stick, above all in a person's on/off comparison inside
    /// the cloud. One pass, and <see cref="LateUpdate"/> returns at once from then on.</summary>
    private void Stop()
    {
        running = false;
        foreach (var sheet in kept)
            Restore(sheet);
        kept.Clear();
        nextScan = 0f;
        HazeAlpha = 0f;
        if (haze != null)
            Destroy(haze);
        if (hazeMaterial != null)
            Destroy(hazeMaterial);
        haze = null;
        hazeMaterial = null;
        hazeRenderer = null;
    }
}
