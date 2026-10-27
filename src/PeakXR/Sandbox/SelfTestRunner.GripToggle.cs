using System.Collections;
using System.Collections.Generic;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.VR;

namespace PeakXR.Sandbox;

/// <summary>
/// <c>Interaction.GripMode</c> Toggle (<see cref="GripToggle"/>): a grip latches on a wall hold and on a held item
/// with a contact tick, the hand then hangs or holds open, and a second squeeze and release lets go with a cancel
/// tick, exactly as opening the grip does in Hold.
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>Turns Toggle on for the step and back after it; false (with the step SKIP) without the setting.</summary>
    bool ToggleGrips()
    {
        var entry = InteractConfig.Instance.GripModeEntry;
        if (entry == null)
        {
            stepSkip = "no Interaction.GripMode setting";
            return false;
        }
        var saved = entry.Value;
        entry.Value = GripMode.Toggle;
        OnRestore(() => entry.Value = saved);
        return true;
    }

    /// <summary>
    /// vr.237: in Toggle the hand grips the wall and opens: the grip latches with a contact tick and the body keeps
    /// climbing for 1.5 s with the grip open. A squeeze and release unlatches with the cancel tick, and with no other
    /// hand on the wall the climb ends after the regrip grace, as letting go does in Hold.
    /// </summary>
    IEnumerator GripToggleClimbStep()
    {
        var c = Character.localCharacter;
        if (!ToggleGrips())
            yield break;
        yield return ToWall();
        if (!wallReady)
            yield break;
        bool left = VRInput.LeftHanded;
        yield return GripWall(left);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(0.3f);
        float mark = CueMark();
        yield return GripTo(left, 0f, 0.3f);
        yield return Feel(left, CueFeel.Contact, mark, 1f);
        bool latched = GripToggle.Latched(left);
        yield return Wait(1.5f);
        bool hung = c.data.isClimbing;
        // A full squeeze held past GripLatch.SqueezeSeconds, then a slow opening (no throw).
        yield return GripTo(left, 1f, 0.1f);
        yield return Wait(0.25f);
        float open = CueMark();
        yield return GripTo(left, 0f, 0.4f);
        yield return Feel(left, CueFeel.Cancel, open, 1f);
        bool unlatched = !GripToggle.Latched(left);
        float grace = InteractConfig.Instance.ClimbRegripGrace;
        yield return WaitFor(() => !c.data.isClimbing, grace + 1.5f);
        bool fell = !c.data.isClimbing;
        var note = F(
            $"latched {latched}, still climbing after 1.5 s open {hung}; unlatched {unlatched}, climb ended {fell}"
        );
        if (!latched || !hung)
            Fail("the open hand did not keep its latched hold: " + note);
        else if (!unlatched || !fell)
            Fail("squeeze and release did not let go: " + note);
        else
            Note(note);
        yield return LetGoAll(c);
    }

    /// <summary>
    /// vr.238: in Toggle with <c>Interaction.ReleaseToDrop</c> on, the right hand closes on a given Apple and opens:
    /// the grip latches with a contact tick and the Apple stays in hand for 1 s, so the release that latched it never
    /// drops it. A squeeze and slow release unlatches with the cancel tick and drops it.
    /// </summary>
    IEnumerator GripToggleItemStep()
    {
        if (!InteractConfig.Instance.ReleaseToDrop)
        {
            stepSkip = "Interaction.ReleaseToDrop off";
            yield break;
        }
        var apple = FindItem(SandboxActions.Items, AppleFragment);
        if (apple == null)
        {
            stepSkip = "no Apple item";
            yield break;
        }
        if (!ToggleGrips())
            yield break;
        const bool left = false;
        if (!SandboxActions.GiveItem(apple, left))
        {
            stepSkip = SandboxActions.LastMessage;
            yield break;
        }
        yield return Until(() => Holding(apple), 5f, "given Apple not in hand");
        if (!stepOk)
            yield break;
        spawned.Add(HeldItem().gameObject);
        yield return null;
        yield return GripTo(left, 1f, 0.1f);
        yield return Wait(0.3f);
        float mark = CueMark();
        yield return GripTo(left, 0f, 0.4f);
        yield return Feel(left, CueFeel.Contact, mark, 1f);
        bool latched = GripToggle.Latched(left);
        yield return Wait(1f);
        bool kept = Holding(apple);
        // A full squeeze held past GripLatch.SqueezeSeconds, then a slow opening (no throw).
        yield return GripTo(left, 1f, 0.1f);
        yield return Wait(0.25f);
        float open = CueMark();
        yield return GripTo(left, 0f, 0.4f);
        yield return Feel(left, CueFeel.Cancel, open, 1f);
        bool unlatched = !GripToggle.Latched(left);
        yield return WaitFor(() => !Holding(apple), 1.5f);
        bool dropped = !Holding(apple);
        var note = F(
            $"latched {latched}, held 1 s with the hand open {kept}; unlatched {unlatched}, dropped {dropped}"
        );
        if (!latched || !kept)
            Fail("the latching release let the Apple go: " + note);
        else if (!unlatched || !dropped)
            Fail("squeeze and slow release did not drop it: " + note);
        else
            Note(note);
    }
}
