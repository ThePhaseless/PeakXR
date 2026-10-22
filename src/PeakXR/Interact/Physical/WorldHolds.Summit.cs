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
/// holds interact on the statue; near a full altar it is refused (Occupied, statue red).</item>
/// <item>Soul pillar (WS-W15): both open palms laid on it hold interact (2 s, ScoutmasterSoulPillar.cs:94).</item>
/// <item>Summit portal (WS-W16): a hand pushed into it holds interact (2 s, Peak/PeakGatePortal.cs:71); pulling it
/// out is the vanilla cancel, and run flow's fade follows the hold.</item>
/// </list>
/// The holding hands feel the progress ramp.
/// </summary>
internal static partial class WorldHolds
{
    /// <summary>Amulet centre to its socket for the insert to run.</summary>
    private const float SocketReach = 0.12f;

    /// <summary>Highest a raised hand offers the amulet, over the head.</summary>
    private const float RaisedReach = 0.4f;

    /// <summary>Where the amulet has to be held for <paramref name="socket"/>: the socket itself, or straight below it
    /// at a raised hand's height when it sits higher (holding it up to the altar).
    /// shortcut: the socket heights on the Peak altar are not measured yet (vr34 ended 2.16 m short); upgrade to the
    /// real socket reach once a run logs them.</summary>
    internal static Vector3 SocketSpot(Vector3 socket)
    {
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        if (head != null)
            socket.y = Mathf.Min(socket.y, head.position.y + RaisedReach);
        return socket;
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
            return;
        }
        var sockets = statue.amuletObjects;
        var centers = SocketCenters(statue);
        var point = item.Center();
        bool left = LocalHold.ItemInLeftHand;
        int socket = AltarSocket.For(amulet.amuletIndex, slots, sockets.Length);
        SocketTarget = socket;
        if (socket < 0 || sockets[socket] == null)
        {
            Socket.Reset();
            float near = NearestSocket(centers, point, out var at);
            bool was = AltarNear.Active;
            if (AltarNear.Update(near, Time.deltaTime) && !was)
                Affordance.Deny(left, at, DenyReason.Occupied, default, statue);
            return;
        }
        AltarNear.Reset();
        var spot = SocketSpot(centers[socket]);
        if (!Socket.Update(Vector3.Distance(point, spot), Time.deltaTime))
            return;
        GestureRequests.Interact(statue);
        HeldProgress(left, statue);
    }

    private static float NearestSocket(Vector3[] centers, Vector3 point, out Vector3 at)
    {
        float best = float.MaxValue;
        at = point;
        foreach (var p in centers)
        {
            float d = Vector3.Distance(p, point);
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
            && PalmOn(c, rig, true)
            && PalmOn(c, rig, false);
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
