using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using PeakXR.Core;
using Photon.Pun;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace MpObserver;

/// <summary>
/// The modded-B mode (docs/testing.md, "Modded B"): when B runs PeakXR flat, the observer also records PeakXR's side
/// of the pose sync, read only. The handshake property both players carry, every event 151 from A with PeakXR's
/// verdict on it (<see cref="AcceptProbe"/>), A's avatar head and hand bones in A's body frame (<see cref="MpPoseFrame"/>,
/// the frame A's checkpoints use), what B raises itself (<see cref="SendProbe"/>), mod errors (<see cref="ModLog"/>), and
/// whether XR, a stereo camera or PeakXR's VR session ever ran on B. PeakXR is reached by name and reflection only.
/// </summary>
internal sealed partial class Observer
{
    const string PeakXRGuid = "com.thephaseless.peakxr";
    const string HandshakeKey = "PeakXR";

    static readonly object Unset = new();

    readonly RateWindow rate151 = new(1.0);
    bool modded;
    bool acceptProbed;
    string peakxrVersion;
    PropertyInfo vrActive;
    PropertyInfo driverFresh;
    bool vrEverActive;
    bool xrEverActive;
    bool stereoEver;
    object lastHsA = Unset;
    object lastHsB = Unset;

    void StartPeakXR()
    {
        modded = Chainloader.PluginInfos.TryGetValue(PeakXRGuid, out var info);
        if (!modded)
            return;
        peakxrVersion = info.Metadata.Version.ToString();
        var accept = AcceptProbe.Target();
        if (accept != null)
        {
            new Harmony(MyPluginInfo.PLUGIN_GUID).Patch(
                accept,
                postfix: new HarmonyMethod(typeof(AcceptProbe), nameof(AcceptProbe.Postfix))
            );
            acceptProbed = true;
        }
        vrActive = AccessTools
            .TypeByName("PeakXR.VR.VRSession")
            ?.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
        // Every plugin's Awake has run by now, so PeakXR's features are installed.
        Write(
            new JsonLine()
                .Str("type", "peakxr")
                .Num("t", Unix)
                .Str("version", peakxrVersion)
                .Str("runtime", Environment.GetEnvironmentVariable("PEAKXR_RUNTIME"))
                .Bool("acceptProbe", acceptProbed)
                .Raw("features", Features())
                .Raw("patches", PatchOwners())
        );
    }

    void OnPoseEvent(byte code)
    {
        if (code == 151)
            rate151.Add(Time.realtimeSinceStartupAsDouble);
    }

    /// <summary>Twice a second: XR and camera state on B, PeakXR's VR session, and the handshake values.</summary>
    void SampleFlat(Character remote)
    {
        xrEverActive |= UnityEngine.XR.XRSettings.isDeviceActive;
        var cam = Camera.main;
        stereoEver |= cam != null && cam.stereoEnabled;
        if (!modded)
            return;
        vrEverActive |= vrActive != null && vrActive.GetValue(null) is true;
        var owner = remote != null ? remote.refs.view.Owner : null;
        object a = owner?.CustomProperties?[HandshakeKey];
        object b = PhotonNetwork.LocalPlayer?.CustomProperties?[HandshakeKey];
        if (Equals(a, lastHsA) && Equals(b, lastHsB))
            return;
        lastHsA = a;
        lastHsB = b;
        Write(
            new JsonLine()
                .Str("type", "handshake")
                .Num("t", Unix)
                .Num("actor", owner != null ? owner.ActorNumber : -1)
                .Raw("a", Hs(a))
                .Raw("b", Hs(b))
        );
    }

    /// <summary>A's avatar on B: whether PeakXR drives it from A's poses, and its head and hand bones in A's frame
    /// (hip rigidbody, look yaw), the way A writes its own in a-checkpoints.jsonl.</summary>
    void PoseFields(JsonLine j, Character c)
    {
        if (!modded)
            return;
        var driver = c.GetComponent("AvatarPoseDriver");
        bool fresh = false;
        if (driver != null)
        {
            driverFresh ??= driver.GetType().GetProperty("HasFreshPose");
            fresh = driverFresh != null && driverFresh.GetValue(driver) is true;
        }
        j.Bool("vrDriven", fresh).Num("acc151", Result("Accepted"));
        var rig = c.refs.hip != null ? c.refs.hip.Rig : null;
        var parts = c.refs.ragdoll != null ? c.refs.ragdoll.partDict : null;
        if (rig == null || parts == null)
            return;
        var root = N(rig.position);
        float yaw = c.data.lookValues.x;
        void Put(string key, BodypartType type)
        {
            if (!parts.TryGetValue(type, out var part) || part.Rig == null)
                return;
            var v = MpPoseFrame.ToFrame(N(part.Rig.position), root, yaw);
            j.Vec(key, v.X, v.Y, v.Z);
        }
        Put("boneHead", BodypartType.Head);
        Put("boneL", BodypartType.Hand_L);
        Put("boneR", BodypartType.Hand_R);
    }

    /// <summary>Summary fields for both modes; the PeakXR block only when it is loaded.</summary>
    void SummaryFlat(JsonLine j)
    {
        var sent = new JsonLine();
        foreach (var kv in SendProbe.Sent)
            sent.Num(kv.Key.ToString(CultureInfo.InvariantCulture), kv.Value);
        var log = ModLog.Instance;
        lock (log.FirstErrors)
        {
            j.Num("sent151", SendProbe.Sent.TryGetValue(151, out int n) ? n : 0)
                .Raw("sentEvents", sent.ToString())
                .Bool("xrEverActive", xrEverActive)
                .Bool("stereoEver", stereoEver)
                .Num("bepinexErrors", log.Errors)
                .Raw("firstBepinexErrors", Strings(log.FirstErrors))
                .Bool("peakxrLoaded", modded);
            if (!modded)
                return;
            var results = new JsonLine();
            foreach (var kv in AcceptProbe.Results)
                results.Num(kv.Key, kv.Value);
            j.Raw(
                "peakxr",
                new JsonLine()
                    .Str("version", peakxrVersion)
                    .Str("runtime", Environment.GetEnvironmentVariable("PEAKXR_RUNTIME"))
                    .Bool("vrEverActive", vrEverActive)
                    .Bool("acceptProbe", acceptProbed)
                    .Raw("features", Features())
                    .Raw("patches", PatchOwners())
                    .Num("warnings", log.PeakXRWarnings)
                    .Raw("firstWarnings", Strings(log.FirstWarnings))
                    .Raw(
                        "pose151",
                        new JsonLine()
                            .Num("received", rate151.Count)
                            .Num("peak1s", rate151.Peak)
                            .Num("busySeconds", rate151.BusySeconds)
                            .Num("meanBusy", rate151.MeanBusy)
                            .Raw("results", results.ToString())
                            .ToString()
                    )
                    .ToString()
            );
        }
    }

    static int Result(string key) => AcceptProbe.Results.TryGetValue(key, out int n) ? n : 0;

    /// <summary>PeakXR's feature report (<c>FeatureManager.Features</c>, src/PeakXR/Framework/FeatureManager.cs).</summary>
    static string Features()
    {
        var list =
            AccessTools
                .TypeByName("PeakXR.Framework.FeatureManager")
                ?.GetProperty("Features", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as IEnumerable;
        var sb = new StringBuilder("[");
        if (list != null)
        {
            foreach (var f in list)
            {
                var t = f.GetType();
                sb.Append(sb.Length > 1 ? "," : "")
                    .Append(
                        new JsonLine()
                            .Str("id", t.GetProperty("Id")?.GetValue(f) as string)
                            .Str("state", t.GetProperty("State")?.GetValue(f)?.ToString())
                            .Str("reason", t.GetProperty("FailureReason")?.GetValue(f) as string)
                    );
            }
        }
        return sb.Append(']').ToString();
    }

    /// <summary>Game methods PeakXR's features patch, counted per feature (Harmony id <c>com.thephaseless.peakxr.&lt;id&gt;</c>).</summary>
    static string PatchOwners()
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var method in Harmony.GetAllPatchedMethods())
        {
            var info = Harmony.GetPatchInfo(method);
            if (info == null)
                continue;
            foreach (var owner in info.Owners)
            {
                if (!owner.StartsWith(PeakXRGuid, StringComparison.Ordinal))
                    continue;
                var id = owner.Substring(PeakXRGuid.Length).TrimStart('.');
                counts.TryGetValue(id, out int n);
                counts[id] = n + 1;
            }
        }
        var j = new JsonLine();
        foreach (var kv in counts)
            j.Num(kv.Key, kv.Value);
        return j.ToString();
    }

    static string Strings(List<string> items)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < items.Count; i++)
            sb.Append(i > 0 ? "," : "").Append(new JsonLine().Str("e", items[i]));
        return sb.Append(']').ToString();
    }

    static string Hs(object value) =>
        value is int i ? i.ToString(CultureInfo.InvariantCulture) : "null";

    static NVector3 N(Vector3 v) => new(v.x, v.y, v.z);
}
