using System.Collections;
using PeakXR.Interact.Climb;
using PeakXR.VR;
using Photon.Pun;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>vr.43d: from a rope to a chain or vine and back, as the game allows (RopeSegment.cs:49-56, JungleVine.cs:96-108).</summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>
    /// A test rope hanging ahead and a shooter's chain (a <see cref="JungleVine"/>, VineShooter.cs:61) running off at the
    /// free hand's side, both in reach. On the rope, the free hand closes on the chain: the game swaps the line
    /// (<c>GrabVineRpc</c>, CharacterVineClimbing.cs:155) with no moment off either and no fall, only that hand holds on
    /// (<c>ClimbHands.Adopt</c> holds on with the other only if it is at the new line too), and the other hand opening
    /// frees it and changes nothing else.
    /// Then hand over hand along the chain and back, the other hand closes on the rope, and hand over hand up that.
    /// </summary>
    IEnumerator RopeToVineStep()
    {
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        if (!ClimbHands.RopeHands)
        {
            stepSkip = "Interaction.RopeHandOverHand off";
            yield break;
        }
        var shooter = FindItemComponent<VineShooter>();
        if (shooter == null || shooter.vinePrefab == null)
        {
            Fail("no vine shooter prefab to build a chain from");
            yield break;
        }
        yield return HangRope();
        if (testRope == null)
            yield break;
        var rope = testRope;
        bool first = VRInput.LeftHanded;
        bool second = !first;
        yield return GrabRope(first);
        if (!stepOk)
        {
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(0.8f);

        // Off the rope's side, over the eye so the body hangs about as high as on the rope (a vine holds the torso 1 m
        // under it, CharacterVineClimbing.cs:150), running away from the rope so only the near end is in reach of both.
        HeadFrame(out _, out _, out var right);
        var side = rig.Origin.TransformDirection(second ? -right : right);
        side.y = 0f;
        side.Normalize();
        var ahead = Flat(rig.Head.forward);
        var near = rig.Head.position + ahead * 0.25f + side * 0.3f + Vector3.up * 0.3f;
        JungleVine vine = null;
        foreach (float turn in new[] { 0f, 30f, -30f, 60f })
        {
            var to = near + Quaternion.Euler(0f, turn, 0f) * side * 6f;
            if (!JungleVine.CheckVinePath(near, to, 0.05f, out var mid))
                continue;
            var go = PhotonNetwork.Instantiate(
                shooter.vinePrefab.name,
                near,
                Quaternion.identity,
                0
            );
            spawned.Add(go);
            vine = go.GetComponent<JungleVine>();
            vine.photonView.RPC("ForceBuildVine_RPC", RpcTarget.All, near, to, 0.05f, mid);
            break;
        }
        if (vine == null)
        {
            stepSkip = "no clear line for a test chain beside the rope";
            yield return LetGoAll(c);
            yield break;
        }
        yield return Wait(0.2f);

        // From here on there must be no frame off both lines, and no fall.
        float offLines = 0f;
        float fell = 0f;
        void Watch()
        {
            if (!c.data.isRopeClimbing && !c.data.isVineClimbing)
                offLines += Time.unscaledDeltaTime;
            fell = Mathf.Max(fell, c.data.fallSeconds);
        }
        string Lines() =>
            F(
                $"{(c.data.isRopeClimbing ? "rope" : "")}{(c.data.isVineClimbing ? "chain" : "")}, off both {offLines:0.00} s, fell {fell:0.00} s; hands anchored first {ClimbHands.HandAnchored(first)} second {ClimbHands.HandAnchored(second)}; {TraversalGrips.LastNote}; {Interact.PhysicalInteraction.LastGripNote}"
            );

        // The free hand closes on the chain.
        var spot = NearestVinePoint(vine, near);
        yield return Sampled(MoveTo(second, () => spot, 0.5f), Watch);
        if (OverReach(second) is { } far)
        {
            Fail(F($"{far}: the chain's near end is out of the free hand's reach"));
            yield return LetGoAll(c);
            yield break;
        }
        yield return Sampled(GripTo(second, 1f, 0.1f), Watch);
        yield return Sampled(
            Until(
                () => c.data.isVineClimbing && c.data.heldVine == vine,
                2f,
                "the free hand's grip on the chain did not take it from the rope: "
            ),
            Watch
        );
        if (!stepOk)
        {
            failNote += Lines();
            yield return LetGoAll(c);
            yield break;
        }
        yield return Sampled(WaitFor(() => !c.refs.vineClimbing.Sliding(), 4f), Watch);
        yield return Sampled(Wait(0.5f), Watch);
        // The game leaves heldRope and the rope's climber list set (GrabVineRpc clears only isRopeClimbing); nothing of
        // the mod's may read that as being on the rope.
        bool staleRope = c.data.heldRope == rope;
        bool staleClimber = rope.charactersClimbing.Contains(c);
        string toChain = F(
            $"rope to chain: heldRope still set {staleRope}, still on the rope's climber list {staleClimber}, the hand on the chain anchored {ClimbHands.HandAnchored(second)}"
        );
        // The other hand holds on too if it happened to be at the new line then (ClimbHands.Adopt: twice the grip radius).
        if (!ClimbHands.HandAnchored(second))
        {
            Fail("the hand that took the chain does not hold it: " + Lines());
            yield return LetGoAll(c);
            yield break;
        }
        // The hand left on the rope opens: the chain stays held.
        yield return Sampled(GripTo(first, 0f, 0.1f), Watch);
        yield return Sampled(Wait(0.3f), Watch);
        if (ClimbHands.HandAnchored(first) || !c.data.isVineClimbing)
        {
            Fail("the first hand's opening should free it and keep the chain: " + Lines());
            yield return LetGoAll(c);
            yield break;
        }

        // Hand over hand along the chain, away from the rope and back.
        var along = rig.Origin.InverseTransformVector(
            vine.GetPosition(0.6f, 1, 1) - vine.GetPosition(0.4f, 1, 1)
        );
        along.Normalize();
        var worldAlong = rig.Origin.TransformVector(along).normalized;
        float At() => Vector3.Dot(vine.GetPosition(c.data.vinePercent, 1, 1), worldAlong);
        IEnumerator Pull(bool hand, float sign)
        {
            // Pulling the hand back along the chain moves the body on along it.
            yield return Sampled(
                MoveTrack(hand, InReach(Script(hand).Position - along * sign * 0.4f), 0.6f),
                Watch
            );
            yield return Sampled(Wait(0.3f), Watch);
        }
        IEnumerator Regrip(bool hand, bool off, float sign)
        {
            var reach = NearestVinePoint(
                vine,
                rig.Head.position + worldAlong * sign * 0.2f + Vector3.up * 0.3f
            );
            yield return Sampled(MoveTo(hand, () => reach, 0.5f), Watch);
            yield return Sampled(GripTo(hand, 1f, 0.1f), Watch);
            yield return Sampled(Wait(0.1f), Watch);
            yield return Sampled(GripTo(off, 0f, 0.1f), Watch);
        }
        float a0 = At();
        yield return Pull(second, 1f);
        float pull1 = At() - a0;
        yield return Regrip(first, second, 1f);
        float a1 = At();
        yield return Pull(first, 1f);
        float pull2 = At() - a1;
        yield return Regrip(second, first, -1f);
        float a2 = At();
        yield return Pull(second, -1f);
        float pull3 = a2 - At();
        yield return Regrip(first, second, -1f);
        float a3 = At();
        yield return Pull(first, -1f);
        float pull4 = a3 - At();
        string along3 = F(
            $"along the chain {pull1:0.00} m, the other hand {pull2:0.00} m, and back {pull3:0.00} m and {pull4:0.00} m for 0.40 m pulls"
        );
        if (!c.data.isVineClimbing)
        {
            Fail($"fell off the chain hand over hand ({along3}): " + Lines());
            yield return LetGoAll(c);
            yield break;
        }
        if (
            pull1 < MinPullRise
            || pull2 < MinPullRise
            || pull3 < MinPullRise
            || pull4 < MinPullRise
        )
        {
            Fail($"hand over hand on the chain did not move: {along3}");
            yield return LetGoAll(c);
            yield break;
        }

        // Back: the hand that let go of the chain last closes on the rope.
        yield return Sampled(GrabRope(second), Watch);
        if (!stepOk || !c.data.isRopeClimbing || c.data.heldRope != rope)
        {
            if (stepOk)
                Fail("the grip on the rope did not take it from the chain: " + Lines());
            else
                failNote += " " + Lines();
            yield return LetGoAll(c);
            yield break;
        }
        yield return Sampled(Wait(0.8f), Watch);
        if (!ClimbHands.HandAnchored(second))
        {
            Fail("the hand that took the rope does not hold it: " + Lines());
            yield return LetGoAll(c);
            yield break;
        }
        yield return Sampled(GripTo(first, 0f, 0.1f), Watch);
        yield return Sampled(Wait(0.3f), Watch);

        // Hand over hand up the rope straight after.
        var api = rope.climbingAPI;
        float y0 = api.GetPosition(c.data.ropePercent).y;
        yield return Sampled(PullDown(second, 0.4f, 0.6f), Watch);
        yield return Sampled(Wait(0.3f), Watch);
        float rise1 = api.GetPosition(c.data.ropePercent).y - y0;
        var nextSegment = RopeSegmentNear(first, out _, out _);
        yield return Sampled(MoveTo(first, () => nextSegment.position, 0.5f), Watch);
        yield return Sampled(GripTo(first, 1f, 0.1f), Watch);
        yield return Sampled(GripTo(second, 0f, 0.1f), Watch);
        float y1 = api.GetPosition(c.data.ropePercent).y;
        yield return Sampled(PullDown(first, 0.4f, 0.6f), Watch);
        yield return Sampled(Wait(0.3f), Watch);
        float rise2 = api.GetPosition(c.data.ropePercent).y - y1;
        bool onRope = c.data.isRopeClimbing;
        var note = F(
            $"{toChain}; {along3}; chain to rope: then {rise1:0.00} m and {rise2:0.00} m up the rope for 0.40 m pulls, on the rope {onRope}, off both lines {offLines:0.00} s, fell {fell:0.00} s, rope's climber list holds the player {rope.charactersClimbing.FindAll(x => x == c).Count} time(s)"
        );
        if (offLines > 0f || fell > 0f)
            Fail("the swap dropped the player: " + note);
        else if (!onRope || rise1 < MinPullRise || rise2 < MinPullRise)
            Fail("hand over hand on the rope after the chain: " + note);
        else
            stepNote = note;
        yield return LetGoAll(c);
    }

    /// <summary>The drawn chain's point nearest <paramref name="world"/>: its collider joints, as
    /// <see cref="ClimbHands.VineDistance"/> measures the grip against them.</summary>
    static Vector3 NearestVinePoint(JungleVine vine, Vector3 world)
    {
        var root = vine.colliderRoot;
        var down = Vector3.down * vine.colliderOffset;
        var best = world;
        float bestSq = float.MaxValue;
        for (int i = 0; root != null && i < root.childCount; i++)
        {
            var at = root.GetChild(i).position + down;
            float sq = (at - world).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = at;
            }
        }
        return best;
    }
}
