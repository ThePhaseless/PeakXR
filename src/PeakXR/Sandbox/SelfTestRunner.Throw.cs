using System.Collections;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Interact;
using PeakXR.VR;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// vr.13 made deterministic and self-explaining: one fixed item, and a note that gives the launch (pitch, heading
/// against the body, the hand and hip parts of interact.physical's release velocity), where the item appeared
/// against the controller, its first contact and its rest point, so a short throw says whether it was aimed down,
/// hit the thrower or something on the way, or fell short on open ground (<see cref="SelfTestChecks.JudgeThrow"/>).
/// </summary>
internal sealed partial class SelfTestRunner
{
    /// <summary>The item vr.13 always throws (an exact name, never a fragment match).</summary>
    const string ThrowItemName = "Apple Berry Green";

    /// <summary>Shortest landing that counts as a throw (m).</summary>
    const float MinThrowDistance = 2f;

    /// <summary>vr.13: the fixed item, thrown along the head's level forward, judged with <see cref="ThrowReport"/>.</summary>
    IEnumerator ThrowCheck()
    {
        Item item = null;
        foreach (var it in SandboxActions.Items)
        {
            if (it != null && it.name == ThrowItemName)
                item = it;
        }
        if (item == null)
        {
            Fail($"no '{ThrowItemName}' item to throw");
            yield break;
        }
        yield return GiveAndHold(item);
        if (!stepOk)
            yield break;
        var c = Character.localCharacter;
        var rig = VRRig.Instance;
        var held = HeldItem();
        ushort heldId = held.itemID;
        bool left = LocalHold.ItemInLeftHand;
        float multiplier = held.throwForceMultiplier;
        yield return GripTo(left, 1f, 0.1f);

        HeadFrame(out var head, out var forward, out var right);
        var side = left ? -right : right;
        var windup = head + side * 0.25f + Vector3.down * 0.15f;
        var release = windup + forward * 0.9f + Vector3.up * 0.15f;
        yield return MoveTrack(left, windup, 0.4f);
        yield return Wait(0.3f);
        SnapshotItems();
        int throwsBefore = PhysicalInteraction.ThrowCount;
        float consumedBefore = c.data.lastConsumedItem;
        int slotsBefore = SlotsHolding(c, heldId);
        float clamp =
            (c.refs.hip != null ? c.refs.hip.Rig.linearVelocity.magnitude : 0f)
            + 0.5f * c.refs.items.maxThrowForce * multiplier;
        var from = c.Center;
        var bodyForward = c.data.lookDirection_Flat;

        // The swing, watching every frame for the thrown instance so the contact probe is on before it lands.
        Item thrown = null;
        ThrowProbe probe = null;
        float releasedAt = 0f;
        var spawnAt = Vector3.zero;
        var controllerAt = Vector3.zero;
        var firstVelocity = Vector3.zero;
        var swing = SwingRelease(left, windup, release);
        float end = Now + 2f;
        bool swinging = true;
        while (Now < end && (swinging || thrown == null))
        {
            if (swinging)
                swinging = swing.MoveNext();
            if (thrown == null && (thrown = NewItem(heldId)) != null)
            {
                releasedAt = Now;
                spawnAt = thrown.Center();
                controllerAt = rig.Hand(left).position;
                firstVelocity = thrown.rig != null ? thrown.rig.linearVelocity : Vector3.zero;
                probe = (
                    thrown.rig != null ? thrown.rig.gameObject : thrown.gameObject
                ).AddComponent<ThrowProbe>();
                probe.Thrower = c;
                probe.Since = Now;
            }
            yield return null;
        }
        if (thrown == null)
        {
            // Where it went instead: still in hand (the release was not a throw), pocketed into a slot, eaten.
            var now = HeldItem();
            Fail(
                F(
                    $"no thrown {held.name} in the world: in hand {(now != null ? now.name : "nothing")}, throws {PhysicalInteraction.ThrowCount - throwsBefore}, slots holding it {slotsBefore} -> {SlotsHolding(c, heldId)}, eaten {c.data.lastConsumedItem != consumedBefore}, last grip {PhysicalInteraction.LastGripNote}"
                )
            );
            yield break;
        }
        float launch = firstVelocity.magnitude;
        for (int i = 0; i < 5 && thrown != null; i++)
        {
            if (thrown.rig != null)
                launch = Mathf.Max(launch, thrown.rig.linearVelocity.magnitude);
            yield return null;
        }
        yield return Wait(0.25f);
        yield return ShotAt("vr-13-throw", thrown != null ? thrown.Center() : HandsMid());
        float still = 0f;
        end = Now + 5f;
        while (thrown != null && Now < end && still < 0.3f)
        {
            float speed = thrown.rig != null ? thrown.rig.linearVelocity.magnitude : 0f;
            still = speed < 0.2f ? still + Time.unscaledDeltaTime : 0f;
            yield return null;
        }
        if (thrown == null)
        {
            Fail($"thrown {ThrowItemName} vanished (launch {launch:0.0} m/s)");
            yield break;
        }
        var landed = thrown.Center();
        ThrowReport(
            c,
            from,
            bodyForward,
            spawnAt,
            controllerAt,
            firstVelocity,
            launch,
            clamp,
            probe,
            releasedAt,
            landed
        );
        if (probe != null)
            Destroy(probe);
    }

    static readonly byte[] PocketSlots = { 0, 1, 2, 250 };

    /// <summary>Hotbar slots and the overflow slot (<c>Player.GetItemSlot</c>, Player.cs:246-259) holding
    /// <paramref name="id"/>.</summary>
    static int SlotsHolding(Character c, ushort id)
    {
        int n = 0;
        foreach (byte i in PocketSlots)
        {
            var slot = c.player != null ? c.player.GetItemSlot(i) : null;
            if (slot != null && !slot.IsEmpty() && slot.prefab != null && slot.prefab.itemID == id)
                n++;
        }
        return n;
    }

    /// <summary>The vr.13 note and verdict.</summary>
    void ThrowReport(
        Character c,
        Vector3 from,
        Vector3 bodyForward,
        Vector3 spawnAt,
        Vector3 controllerAt,
        Vector3 velocity,
        float launch,
        float clamp,
        ThrowProbe probe,
        float releasedAt,
        Vector3 landed
    )
    {
        float g = Physics.gravity.magnitude;
        var level = new Vector3(velocity.x, 0f, velocity.z);
        float pitch =
            velocity.sqrMagnitude > 1e-6f
                ? Mathf.Asin(velocity.y / velocity.magnitude) * Mathf.Rad2Deg
                : 0f;
        float heading = Vector3.SignedAngle(bodyForward, level, Vector3.up);
        float height = GroundBelow(spawnAt, 20f);
        float flatRange = SelfTestChecks.FlatRange(
            level.magnitude,
            velocity.y,
            Mathf.Max(0f, height),
            g
        );
        float distance = Vector2.Distance(
            new Vector2(from.x, from.z),
            new Vector2(landed.x, landed.z)
        );
        bool contact = probe != null && probe.Hit;
        float contactAt = contact
            ? Vector2.Distance(
                new Vector2(spawnAt.x, spawnAt.z),
                new Vector2(probe.Point.x, probe.Point.z)
            )
            : 0f;
        float slope = contact ? Vector3.Angle(probe.Normal, Vector3.up) : 0f;
        float range = SelfTestChecks.MaxThrowRange(clamp, from.y - landed.y + 1f, g);
        var verdict = SelfTestChecks.JudgeThrow(
            distance,
            MinThrowDistance,
            pitch,
            contact && probe.OwnBody,
            contact,
            slope,
            contactAt,
            flatRange
        );
        string firstContact = contact
            ? F(
                $"first contact {probe.Collider}{(probe.OwnBody ? " (own body)" : "")} at {probe.Point}, {slope:0} deg from up, {contactAt:0.0} m out, {probe.Time - releasedAt:0.00} s after release"
            )
            : "no contact seen";
        string launchNote = F(
            $"landed {distance:0.0} m away at {landed}, launch {launch:0.0} m/s (clamp {clamp:0.0}), max range {range:0.0} m"
        );
        string releaseNote = F(
            $"release {velocity} m/s, pitch {pitch:0} deg, heading {heading:0} deg off the body forward, hand {PhysicalInteraction.LastThrowHand} + hip {PhysicalInteraction.LastThrowBody}"
        );
        string spawnNote = F(
            $"appeared at {spawnAt}, {Vector3.Distance(spawnAt, controllerAt):0.00} m from the controller, {height:0.00} m over the ground, flat range {flatRange:0.0} m"
        );
        var note = $"{ThrowItemName}: {launchNote}; {releaseNote}; {spawnNote}; {firstContact}";
        switch (verdict)
        {
            case ThrowVerdict.Ok:
                if (launch > clamp * 1.1f + 0.5f || distance > range + 1f)
                    Fail("throw beyond the vanilla clamp: " + note);
                else
                    stepNote = note;
                break;
            case ThrowVerdict.OwnBody:
                Fail("throw too short, hit the player's own arm or body: " + note);
                break;
            case ThrowVerdict.AimedDown:
                Fail("throw too short, aimed down: " + note);
                break;
            case ThrowVerdict.HitOnTheWay:
                Fail("throw too short, hit something on the way: " + note);
                break;
            default:
                Fail("throw too short on flat ground: " + note);
                break;
        }
    }

    /// <summary>Records a thrown item's first contact (point, normal, collider, time) and whether it was the
    /// thrower's own body.</summary>
    internal sealed class ThrowProbe : MonoBehaviour
    {
        public Character Thrower;
        public float Since;
        public bool Hit;
        public bool OwnBody;
        public Vector3 Point;
        public Vector3 Normal;
        public string Collider;
        public float Time;

        void OnCollisionEnter(Collision collision)
        {
            if (Hit)
                return;
            Hit = true;
            Time = Now;
            var contact = collision.GetContact(0);
            Point = contact.point;
            Normal = contact.normal;
            Collider = collision.collider.name;
            OwnBody =
                Thrower != null && collision.collider.GetComponentInParent<Character>() == Thrower;
        }
    }
}
