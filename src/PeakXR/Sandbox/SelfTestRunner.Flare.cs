using System.Collections;
using PeakXR.Avatar;
using PeakXR.Interact.Lights;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>Scripted-hand step for lighting a flare by pulling its cap (vr.249).</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>
    /// vr.249 (tester request): a held unlit flare lit like a real one (<c>Interaction.Lights.PullToLight</c>, Core
    /// <c>CapPull</c>): the free hand closes on the flare's base end and pulls it 0.2 m away along the flare. The pull
    /// presses the flare's own primary use, so it lights through <c>Action_Flare</c> (Action_Flare.cs:5-8,
    /// Flare.LightFlare :48-58) as the trigger does.
    /// </summary>
    IEnumerator FlareCapPullStep(Item flarePrefab)
    {
        if (!Setting("Interaction.Lights", "PullToLight", true))
        {
            stepSkip = "Interaction.Lights.PullToLight is off";
            yield break;
        }
        yield return GiveAndHold(flarePrefab);
        if (!stepOk)
            yield break;
        var flare = HeldItem();
        if (HandLights.IsLit(flare))
        {
            Fail("the given flare was lit already");
            yield break;
        }
        bool left = LocalHold.ItemInLeftHand;
        bool free = !left;
        yield return GripTo(left, 1f, 0.1f);
        yield return MoveTo(free, () => HeldItemPoints.Base(flare), 0.6f);
        yield return Wait(0.2f);
        var tip = HeldItemPoints.Tip(flare);
        var bottom = HeldItemPoints.Base(flare);
        float atBase = Vector3.Distance(VR.VRRig.Instance.Hand(free).position, bottom);
        yield return GripTo(free, 1f, 0.06f);
        yield return Wait(0.1f);
        var pullTo = bottom + (bottom - tip).normalized * 0.2f;
        yield return MoveTo(free, () => pullTo, 0.3f);
        float budget = GameTiming.Budget(GameTiming.UseSeconds(flare), 0.5f);
        yield return WaitFor(() => HandLights.IsLit(flare), budget);
        bool lit = HandLights.IsLit(flare);
        yield return GripTo(free, 0f, 0.1f);
        var note = F(
            $"free hand {atBase:0.00} m from the base when it closed, pulled 0.2 m along the flare ({Vector3.Distance(tip, bottom):0.00} m long): lit {lit}"
        );
        if (!lit)
            Fail("the cap pull did not light the flare: " + note);
        else
            Note(note);
    }
}
