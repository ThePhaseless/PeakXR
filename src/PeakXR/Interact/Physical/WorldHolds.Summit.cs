using System.Reflection;
using HarmonyLib;
using Peak;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.Framework;
using PeakXR.UI.Affordances;
using PeakXR.VR;
using UnityEngine;
using Zorro.Core;

namespace PeakXR.Interact.Physical;

/// <summary>
/// Summit props driven by the hands instead of a grip anywhere on them, each ending in the prop's own interact hold:
/// <list type="bullet">
/// <item>Altar socket (WS-W14): the held amulet brought to the socket it would fill (<see cref="AltarSocket"/>)
/// holds interact on the statue; near a full altar it is refused (Occupied, statue red). The Peak altar's sockets sit
/// 2.7 m up, past a hand's reach: as in vanilla, where the camera ray takes the statue within
/// <c>Interaction.distance</c> (field default 2 m, Interaction.cs:7, used at :203; the scene sets 2.5 m, read at
/// runtime, vr50), a grip pointed at the statue within that reach runs the
/// same hold (interact.physical's hand-aimed interact). Pointing at it with an amulet shows the socket it fills
/// (teal ring); a full altar is refused as the hold starts; a pointed grip without an amulet gets CantUse.</item>
/// <item>Soul pillar (WS-W15): both open palms laid on it hold interact (2 s, ScoutmasterSoulPillar.cs:94).</item>
/// <item>Summit portal (WS-W16): a hand pushed into it holds interact (2 s, Peak/PeakGatePortal.cs:71); pulling it
/// out is the vanilla cancel, and run flow's fade follows the hold.</item>
/// </list>
/// The holding hands feel the progress ramp.
/// </summary>
internal static partial class WorldHolds
{
    /// <summary>Amulet hand to its socket (<see cref="AltarSocket.Reach"/>) for the insert to run. The tracked grip,
    /// not the amulet: the held item hangs off the ragdoll arm and lags a raised hand (vr36: 0.89 m short).</summary>
    private const float SocketReach = 0.15f;

    /// <summary>How far the amulet hand is from offering it to <paramref name="socket"/> (0 when there): a reachable
    /// socket directly, the Peak altar's 2.7 m high ones held up over the head under them, as vanilla inserts from
    /// below by interact.</summary>
    internal static float SocketDistance(Vector3 hand, Vector3 socket)
    {
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        float headY = head != null ? head.position.y : hand.y;
        return AltarSocket.Reach(hand.N(), socket.N(), headY);
    }

    /// <summary>Amulet this near any socket of a full altar is refused.</summary>
    private const float AltarNearReach = 0.35f;

    /// <summary>Palm to pillar surface.</summary>
    private const float PalmReach = 0.05f;

    /// <summary>Hand to portal surface (inside counts as 0).</summary>
    private const float PortalReach = 0.05f;

    /// <summary>The soul pillar is looked up at most this often, and only in the Nadir (<c>MapHandler.inNadir</c>).</summary>
    private const float PillarLookSeconds = 2f;

    private static readonly ContactDwell Socket = new()
    {
        Enter = SocketReach,
        Exit = SocketReach + 0.05f,
        Dwell = 0.2f,
    };
    private static readonly ContactDwell AltarNear = new()
    {
        Enter = AltarNearReach,
        Exit = AltarNearReach + 0.1f,
        Dwell = 0.3f,
    };
    private static readonly ContactDwell Palms = new()
    {
        Enter = 0f,
        Exit = 0.5f,
        Dwell = 0.2f,
    };
    private static readonly ContactDwell PortalHand = new()
    {
        Enter = PortalReach,
        Exit = PortalReach + 0.05f,
        Dwell = 0.1f,
    };

    private static ScoutStatue statue;
    private static float nextStatueLook;
    private static PeakGatePortal portal;
    private static int portalFrame = -10;
    private static PeakGatePortal portalColliderOf;
    private static Collider portalCollider;
    private static ScoutmasterSoulPillar pillar;
    private static Collider pillarCollider;
    private static float nextPillarLook;

    /// <summary>The socket the held amulet is headed for, -1 when none; for the self-test (vr.89b).</summary>
    internal static int SocketTarget { get; private set; } = -1;

    private static bool Seen(int frame) => Time.frameCount - frame <= 2;

    private static void TickSocket(Character c, bool on)
    {
        var item = c.data.currentItem;
        bool amuletHeld =
            on
            && WorldHoldsFeature.AmuletInSocket.Value
            && item != null
            && (item.itemTags & Item.ItemTags.ScoutAmulet) != 0;
        // Looked up only while an amulet is held, at most every 2 s, so finding it does not depend on the altar's
        // own Update running (vr33 found no socket through an Update postfix).
        if (amuletHeld && statue == null && Time.unscaledTime >= nextStatueLook)
        {
            nextStatueLook = Time.unscaledTime + PillarLookSeconds;
            statue = Object.FindFirstObjectByType<ScoutStatue>();
        }
        AmuletBase amulet = null;
        int[] slots = null;
        if (
            !amuletHeld
            || statue == null
            || !item.TryGetComponent(out amulet)
            || (slots = HasAmulets(statue)) == null
            || statue.amuletObjects == null
            || amulet.amuletIndex == 99
        )
        {
            Socket.Reset();
            AltarNear.Reset();
            SocketTarget = -1;
            statueHeld = false;
            return;
        }
        var sockets = statue.amuletObjects;
        var centers = SocketCenters(statue);
        bool left = LocalHold.ItemInLeftHand;
        var rig = VRRig.Instance;
        var grip = rig != null ? rig.Hand(left) : null;
        var point = grip != null ? grip.position : item.Center();
        int socket = AltarSocket.For(amulet.amuletIndex, slots, sockets.Length);
        SocketTarget = socket;
        var interaction = Interaction.instance;
        bool heldNow =
            interaction != null && ReferenceEquals(interaction.currentHeldInteractible, statue);
        bool holdStarted = heldNow && !statueHeld;
        if (holdStarted)
            StatueHolds++;
        statueHeld = heldNow;
        if (socket < 0 || sockets[socket] == null)
        {
            Socket.Reset();
            float near = NearestSocket(centers, point, out var at);
            bool was = AltarNear.Active;
            // Brought near a full altar, or its hold started some other way (pointed at it, vanilla's 2 m
            // interact ray): refused at once rather than after a hold that ends in nothing.
            // The ring where the player looks: at the amulet hand when it was carried up (vr50: a ring at the
            // 2.7 m socket sat 80 deg off the view), at the socket when the hold started by pointing at it.
            bool carried = AltarNear.Update(near, Time.deltaTime) && !was;
            if (carried || holdStarted)
                Affordance.Deny(left, carried ? point : at, DenyReason.Occupied, default, statue);
            return;
        }
        AltarNear.Reset();
        // Pointed at (or looked at) within vanilla's interact reach: the grip runs the game's own hold there, as a
        // flat player's look does. Show which socket it fills.
        if (
            !Socket.Active
            && Affordance.Hints
            && interaction != null
            && ReferenceEquals(interaction.currentHovered, statue)
        )
            Affordance.Mark(
                left,
                centers[socket],
                AboveReach(centers[socket]) ? DenyReason.AimHigher : DenyReason.Dropped,
                DenyHint.At(centers[socket])
            );
        // The pointed hold feels the same ramp, on the pointing hand (or the amulet hand for a head-aimed grip).
        if (heldNow && !Socket.Active)
            HeldProgress(HandOn(statue, InteractingHand(left)), statue);
        if (!Socket.Update(SocketDistance(point, centers[socket]), Time.deltaTime))
            return;
        GestureRequests.Interact(statue);
        HeldProgress(left, statue);
    }

    /// <summary>The hand whose grip presses the game's interact as the fallback (a pointed or head-aimed grip),
    /// else <paramref name="fallback"/>: the hand that feels a hold it did not grab by hand.</summary>
    private static bool InteractingHand(bool fallback)
    {
        if (PhysicalInteraction.HandAction(true) == GripPressAction.Interact)
            return true;
        if (PhysicalInteraction.HandAction(false) == GripPressAction.Interact)
            return false;
        return fallback;
    }

    private static bool statueHeld;

    /// <summary>Statue holds started with an amulet in hand, for the self-test: a short insert can start and
    /// finish between two of its samples (vr60).</summary>
    internal static int StatueHolds { get; private set; }
    private static readonly bool[] PointGrip = new bool[2];

    /// <summary>A socket higher than a raised hand reaches directly (<see cref="AltarSocket.RaisedReach"/> over
    /// the head).</summary>
    private static bool AboveReach(Vector3 socket)
    {
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        return head != null && socket.y > head.position.y + AltarSocket.RaisedReach;
    }

    /// <summary>
    /// A grip pointed at the scout statue within vanilla's interact reach (<c>Interaction.distance</c> from the
    /// camera, read at runtime: field default 2 m at Interaction.cs:7, 2.5 m in the scene; used at :203; the hand ray
    /// is shortened by the hand's offset, InteractPatches) while the
    /// hand holds no amulet: the statue takes only amulets (ScoutStatue.IsConstantlyInteractable,
    /// Peak/ScoutStatue.cs:288-295), so the game would ignore the grip. Refused with CantUse, the statue red.
    /// </summary>
    private static void TickStatuePoint(Character c, bool on)
    {
        for (int h = 0; h < 2; h++)
        {
            bool left = h == 0;
            bool now = on && PhysicalInteraction.HandAction(left) == GripPressAction.Interact;
            bool started = now && !PointGrip[h];
            PointGrip[h] = now;
            if (now && PressLogAt[h] > 0f && Time.unscaledTime >= PressLogAt[h])
            {
                PressLogAt[h] = 0f;
                LogPress(c, left, "0.3 s on");
            }
            if (!started)
                continue;
            var item = c.data.currentItem;
            if (item != null && (item.itemTags & Item.ItemTags.ScoutAmulet) != 0)
            {
                // What the game sees as the amulet's pointed grip starts, and once its interact has pressed.
                LogPress(c, left, "at the grip");
                PressLogAt[h] = Time.unscaledTime + 0.3f;
                continue;
            }
            if (PointedStatue(left, out var at, out var pointed))
                Affordance.Deny(left, at, DenyReason.CantUse, default, pointed);
        }
    }

    private static readonly float[] PressLogAt = new float[2];

    /// <summary>One line on an amulet's pointed grip: whether interact is pressed, what the game hovers and holds,
    /// which hand aims, and whether the statue takes the held item (ScoutStatue.IsConstantlyInteractable,
    /// Peak/ScoutStatue.cs:288-295).</summary>
    private static void LogPress(Character c, bool left, string when)
    {
        var interaction = Interaction.instance;
        var hovered = interaction != null ? interaction.currentHovered : null;
        var item = c.data.currentItem;
        Plugin.Log.LogInfo(
            $"[interact.world] amulet pointed grip ({(left ? "L" : "R")}) {when}: grip action {PhysicalInteraction.HandAction(left)}, "
                + $"hovered {Name(hovered)}, held {Name(interaction != null ? interaction.currentHeldInteractible : null)}, "
                + $"aim hand {PhysicalInteraction.AimHand}, statue takes {(item != null ? item.name : "nothing")} "
                + $"{(statue != null && statue.IsConstantlyInteractable(c))}"
        );
    }

    private static string Name(IInteractible it) =>
        it is Component comp && comp != null ? comp.name
        : it == null ? "nothing"
        : it.GetType().Name;

    private static bool PointedStatue(bool left, out Vector3 at, out ScoutStatue pointed)
    {
        at = default;
        pointed = null;
        var rig = VRRig.Instance;
        var aim = rig != null ? rig.Aim(left) : null;
        var cam = MainCamera.instance;
        var interaction = Interaction.instance;
        if (aim == null || cam == null || interaction == null)
            return false;
        float reach = interaction.distance - Vector3.Distance(cam.transform.position, aim.position);
        if (
            reach <= 0f
            || !Physics.Raycast(
                aim.position,
                aim.forward,
                out var hit,
                reach,
                HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical),
                QueryTriggerInteraction.Collide
            )
        )
            return false;
        pointed = hit.collider.GetComponentInParent<ScoutStatue>();
        at = hit.point;
        return pointed != null;
    }

    private static float NearestSocket(Vector3[] centers, Vector3 point, out Vector3 at)
    {
        float best = float.MaxValue;
        at = point;
        foreach (var p in centers)
        {
            float d = SocketDistance(point, p);
            if (d < best)
            {
                best = d;
                at = p;
            }
        }
        return best;
    }

    private static ScoutStatue centersOf;
    private static Vector3[] socketCenters = System.Array.Empty<Vector3>();

    /// <summary>Each socket's visible centre, cached per altar (it does not move).</summary>
    private static Vector3[] SocketCenters(ScoutStatue altar)
    {
        if (ReferenceEquals(centersOf, altar))
            return socketCenters;
        centersOf = altar;
        var sockets = altar.amuletObjects;
        socketCenters = new Vector3[sockets.Length];
        for (int i = 0; i < sockets.Length; i++)
            socketCenters[i] = sockets[i] != null ? SocketCenter(sockets[i]) : altar.Center();
        return socketCenters;
    }

    /// <summary>
    /// Where a socket's amulet shows: the centre of its mesh's local bounds, placed by its renderer's transform.
    /// The <c>amuletObjects</c> (Peak/ScoutStatue.cs:12, shown per filled slot by OnAmuletsChanged :99) are inactive
    /// while empty, so their world renderer bounds are not kept; and their own transforms are not at the slot (vr35
    /// found one 1.47 m under the feet of a player standing beside the altar).
    /// </summary>
    internal static Vector3 SocketCenter(GameObject socket)
    {
        var r = socket.GetComponentInChildren<Renderer>(true);
        return r != null
            ? r.transform.TransformPoint(r.localBounds.center)
            : socket.transform.position;
    }

    private static void TickPillar(Character c, bool on)
    {
        if (!on || !WorldHoldsFeature.PalmsOnPillar.Value)
        {
            Palms.Reset();
            return;
        }
        float now = Time.unscaledTime;
        if (pillar == null && now >= nextPillarLook)
        {
            nextPillarLook = now + PillarLookSeconds;
            var map = Singleton<MapHandler>.Instance;
            if (map != null && map.inNadir)
                pillar = Object.FindFirstObjectByType<ScoutmasterSoulPillar>();
            pillarCollider =
                pillar == null ? null
                : pillar.colliderToDisable != null ? pillar.colliderToDisable
                : pillar.GetComponentInChildren<Collider>();
        }
        var rig = VRRig.Instance;
        bool both =
            pillar != null
            && pillarCollider != null
            && pillarCollider.enabled
            && rig != null
            // One-handed play (Accessibility.OneHanded): the one palm is enough.
            && (
                VRInput.OneHanded
                    ? PalmOn(c, rig, VRInput.LeftHanded)
                    : PalmOn(c, rig, true) && PalmOn(c, rig, false)
            );
        if (!Palms.Update(both ? 0f : 1f, Time.deltaTime))
            return;
        GestureRequests.Interact(pillar);
        HeldProgress(true, pillar);
        HeldProgress(false, pillar);
    }

    /// <summary>An open, empty hand laid on the pillar, palm toward it (<see cref="PalmContact"/>).</summary>
    private static bool PalmOn(Character c, VRRig rig, bool left)
    {
        ref var state = ref VRInput.Hand(left);
        var t = rig.Hand(left);
        if (!state.Tracked || state.Grip > 0.4f || t == null)
            return false;
        if (c.data.currentItem != null && (LocalHold.TwoHand || LocalHold.ItemInLeftHand == left))
            return false;
        // Palm normal: +X of the left grip pose, -X of the right (HandRig.cs:62).
        var palm = left ? t.right : -t.right;
        return PalmContact.On(
            palm.N(),
            t.position.N(),
            Surface(pillarCollider, t.position).N(),
            PalmReach
        );
    }

    private static void TickPortal(bool on)
    {
        if (!on || !WorldHoldsFeature.HandIntoPortal.Value || portal == null || !Seen(portalFrame))
        {
            PortalHand.Reset();
            return;
        }
        if (!ReferenceEquals(portalColliderOf, portal))
        {
            portalColliderOf = portal;
            portalCollider = portal.GetComponentInChildren<Collider>();
        }
        var rig = VRRig.Instance;
        if (portalCollider == null || rig == null)
        {
            PortalHand.Reset();
            return;
        }
        float best = float.MaxValue;
        bool bestLeft = false;
        for (int h = 0; h < 2; h++)
        {
            bool left = h == 0;
            var t = rig.Hand(left);
            if (t == null || !VRInput.Hand(left).Tracked)
                continue;
            float d = Vector3.Distance(Surface(portalCollider, t.position), t.position);
            if (d < best)
            {
                best = d;
                bestLeft = left;
            }
        }
        if (!PortalHand.Update(best, Time.deltaTime))
            return;
        GestureRequests.Interact(portal);
        HeldProgress(bestLeft, portal);
    }

    /// <summary>Nearest point of <paramref name="col"/> to <paramref name="p"/> (itself when inside). A concave mesh
    /// has no exact query, so its bounds stand in.</summary>
    private static Vector3 Surface(Collider col, Vector3 p) =>
        col is MeshCollider { convex: false } ? col.bounds.ClosestPoint(p) : col.ClosestPoint(p);

    /// <summary>The progress ramp on <paramref name="left"/> while the game holds interact on
    /// <paramref name="target"/>.</summary>
    private static void HeldProgress(bool left, IInteractible target)
    {
        var interaction = Interaction.instance;
        if (interaction != null && ReferenceEquals(interaction.currentHeldInteractible, target))
            Affordance.Progress(left, Mathf.Clamp01(interaction.constantInteractableProgress));
    }

    /// <summary>PeakGatePortal.Update (Peak/PeakGatePortal.cs:95, private): the portal reports itself while
    /// loaded.</summary>
    [HarmonyPatch]
    internal static class PortalSeen
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(PeakGatePortal), "Update");

        private static void Postfix(PeakGatePortal __instance)
        {
            portal = __instance;
            portalFrame = Time.frameCount;
        }
    }
}
