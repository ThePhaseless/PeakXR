using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using UnityEngine;

namespace PeakXR.Diagnostics;

/// <summary>
/// Read-only ping probe (<c>Debug.PingProbe</c>, off by default, on in the test runs): what came of each ping this
/// client received. The RPC finishing says nothing about a marker, since its distance check can drop it
/// (PointPinger.cs:91), and a marker existing says nothing about it being drawn (#130). The sound and the marker's
/// looks are not measured; a screenshot within the marker's 2 s shows those.
/// </summary>
internal sealed class PingProbe : MonoBehaviour
{
    /// <summary>What one received ping became, as far as this client can see.</summary>
    internal readonly struct Report
    {
        internal int Actor { get; init; }
        internal bool Local { get; init; }
        internal float Distance { get; init; }
        internal bool Blocked { get; init; }
        internal bool Expected { get; init; }
        internal bool Marker { get; init; }
        internal bool RendererOn { get; init; }
        internal bool LayerSeen { get; init; }
        internal float Scale { get; init; }
        internal bool Visible { get; init; }
        internal bool InView { get; init; }
        internal bool ClearLine { get; init; }

        internal string Line =>
            FormattableString.Invariant(
                $"pinger actor {Actor} ({(Local ? "local" : "remote")}), heads {Distance:0.0} m apart, line blocked {Blocked}, game's distance check shows it {Expected}; marker {Marker}, renderer on {RendererOn}, layer in the camera's mask {LayerSeen}, scale {Scale:0.00}, renderer visible {Visible}, in the view {InView}, clear line from the camera {ClearLine}"
            );
    }

    /// <summary>The newest finished report; test steps clear it before they ping.</summary>
    internal static Report? Last;

    /// <summary>Frames after the RPC before <c>renderer.isVisible</c> counts: Unity sets it when a camera culls.</summary>
    private const int VisibleAfterFrames = 5;

    private static PingProbe instance;
    private static AccessTools.FieldRef<PointPinger, GameObject> pingInstance;

    /// <summary>Resolves the private marker field and starts the probe's runner; the caller patches <see cref="Receive"/>.</summary>
    internal static void Begin()
    {
        pingInstance = GameMember.Field<PointPinger, GameObject>("pingInstance");
        var go = new GameObject("PeakXR_PingProbe");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PingProbe>();
    }

    /// <summary>PointPinger.ReceivePoint_Rpc (PointPinger.cs:84, private), postfix: reads only.</summary>
    [HarmonyPatch]
    internal static class Receive
    {
        private static MethodBase TargetMethod() =>
            GameMember.Method(typeof(PointPinger), "ReceivePoint_Rpc");

        private static void Postfix(PointPinger __instance, Vector3 point)
        {
            try
            {
                if (instance != null)
                    instance.StartCoroutine(Read(__instance, point));
            }
            catch (Exception e)
            {
                PatchGuard.Fail<DebugFeature>(e);
            }
        }
    }

    private static IEnumerator Read(PointPinger pinger, Vector3 point)
    {
        var who = pinger.character;
        var viewer = Character.localCharacter;
        if (who == null || viewer == null || pinger.pointPrefab == null)
            yield break;
        var prefab = pinger.pointPrefab.GetComponent<PointPing>();
        if (prefab == null)
            yield break;
        // The same two tests as the RPC (PointPinger.cs:87-91), taken as it ran.
        bool blocked = Physics.Linecast(who.Head, viewer.Head, HelperFunctions.terrainMapMask);
        float distance = Vector3.Distance(who.Head, viewer.Head);
        bool expected = PingVisibility.Shown(
            distance,
            blocked,
            prefab.visibilityFullNoneNoLos.x,
            prefab.visibilityFullNoneNoLos.y,
            prefab.NoLosVisibilityMul
        );
        int actor = who.refs.view.OwnerActorNr;
        bool local = who.IsLocal;

        yield return null;
        var marker = pingInstance(pinger);
        // An older marker of this pinger stays when the check dropped the new one: only one at this point counts.
        bool fresh = marker != null && (marker.transform.position - point).sqrMagnitude < 0.01f;
        var ping = fresh ? marker.GetComponent<PointPing>() : null;
        var rend = ping != null ? ping.renderer : null;
        for (int i = 0; i < VisibleAfterFrames; i++)
            yield return null;
        var cam = Camera.main;
        bool alive = rend != null && cam != null;
        var at = alive ? rend.transform.position : point;
        var view = alive ? cam.WorldToViewportPoint(at) : default;
        var report = new Report
        {
            Actor = actor,
            Local = local,
            Distance = distance,
            Blocked = blocked,
            Expected = expected,
            Marker = fresh,
            RendererOn = alive && rend.enabled,
            LayerSeen = alive && (cam.cullingMask & (1 << rend.gameObject.layer)) != 0,
            Scale = alive ? rend.transform.lossyScale.x : 0f,
            Visible = alive && rend.isVisible,
            InView = alive && view.z > 0f && view.x is >= 0f and <= 1f && view.y is >= 0f and <= 1f,
            // Just over the ground it stands on: any body or rock between the camera and the marker.
            ClearLine =
                alive
                && !Physics.Linecast(
                    cam.transform.position,
                    at + Vector3.up * 0.3f,
                    HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical),
                    QueryTriggerInteraction.Ignore
                ),
        };
        Last = report;
        Plugin.Log.LogInfo("[debug.ping] " + report.Line);
    }
}
