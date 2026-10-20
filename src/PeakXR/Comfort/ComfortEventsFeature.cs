using PeakXR.Framework;

namespace PeakXR.Comfort;

/// <summary>
/// <c>comfort.events</c>: game events that would move, shake or flash the view (knockbacks, launches, tumbles,
/// scripted looks, shakes, status flashes, post pulses, teleports, wind) go through one policy table
/// (<see cref="PeakXR.Core.ComfortPolicy"/>) to a VR treatment: vignette pulse, fade, rumble or the horizon-locked
/// third-person view. Also the cutscene view choice (still viewpoint, opt-in glide, or ui.runflow's screen),
/// cutscene camera jitter, mirrors and mirages, and the fade before a hard cut to Title. Gameplay stays vanilla:
/// hooks only observe, or swap a local presentation effect.
/// </summary>
public sealed class ComfortEventsFeature : Feature
{
    public override string Id => "comfort.events";

    /// <summary>After rig.camera, player.look and ui.runflow, whose state it drives.</summary>
    public override int Order => 60;

    protected internal override void Install()
    {
        ComfortEventsConfig.Bind(Plugin.Settings);
        ComfortEventsPatches.Resolve();
        ComfortEventsPatches.CinematicJitter.Smoothing = GameMember.Field<
            Peak.CinematicScreenShake,
            float
        >("smoothing");

        Patch(typeof(ComfortEventsPatches.Shake));
        Patch(typeof(ComfortEventsPatches.Fall));
        Patch(typeof(ComfortEventsPatches.ForceAtPosition));
        Patch(typeof(ComfortEventsPatches.ForceToBodyPart));
        Patch(typeof(ComfortEventsPatches.CannonLaunch));
        Patch(typeof(ComfortEventsPatches.ItemLaunch));
        Patch(typeof(ComfortEventsPatches.SuperJump));
        Patch(typeof(ComfortEventsPatches.TornadoCapture));
        Patch(typeof(ComfortEventsPatches.WindZone));
        Patch(typeof(ComfortEventsPatches.MoveZone));
        Patch(typeof(ComfortEventsPatches.TornadoPull));
        Patch(typeof(ComfortEventsPatches.LavaPush));
        Patch(typeof(ComfortEventsPatches.StatusFlash));
        Patch(typeof(ComfortEventsPatches.PostEffect));
        Patch(typeof(ComfortEventsPatches.CutsceneJitter));
        Patch(typeof(ComfortEventsPatches.CinematicJitter));
        Patch(typeof(ComfortEventsPatches.MirrorRender));
        Patch(typeof(ComfortEventsPatches.MirageFade));
        Patch(typeof(ComfortEventsPatches.LeaveRoom));
        Patch(typeof(ComfortEventsPatches.SceneCut));

        // Last, so a missing patch target above leaves no runtime object behind.
        ComfortEventsRuntime.Create();
    }
}
