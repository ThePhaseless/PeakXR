using PeakXR.Core;
using UnityEngine;

namespace PeakXR.UI.Affordances;

/// <summary>
/// The live <see cref="CueCapture"/>: ui.affordance writes every refusal or notice cue into it as it emits it
/// (<see cref="Affordance"/> the reason, <see cref="AffordanceRuntime"/> the drawn ring and the cue pulses,
/// <see cref="CueAudio"/> the sounds). Only the self-test reads it. Writing overwrites a few struct fields, so normal
/// play pays nothing. Its clock is <see cref="Now"/>.
/// </summary>
internal static class CueLog
{
    public static readonly CueCapture Capture = new();

    /// <summary>The capture's clock (<c>Time.unscaledTime</c>).</summary>
    public static float Now => Time.unscaledTime;
}
