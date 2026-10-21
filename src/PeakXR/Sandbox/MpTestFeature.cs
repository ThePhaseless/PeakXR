using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using ExitGames.Client.Photon;
using HarmonyLib;
using PeakXR.Core;
using PeakXR.Framework;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace PeakXR.Sandbox;

/// <summary>
/// Client A of the unattended two-client multiplayer test (feature id <c>sandbox.mptest</c>), only with the launch
/// option <c>-peakxr-mptest</c>. It joins the lobby a vanilla peer running the MpObserver plugin hosts, then plays a
/// scripted VR sequence with checkpoints both sides log under the same names (docs/testing.md). Unlike the self-test
/// it never turns the sandbox on: everything it does online is something a VR player can do with the controllers.
/// </summary>
public sealed class MpTestFeature : Feature
{
    public const string LaunchFlag = "-peakxr-mptest";

    public override string Id => "sandbox.mptest";

    public override int Order => 1;

    public override bool VROnly => false;

    public override bool Requested =>
        Array.IndexOf(Environment.GetCommandLineArgs(), LaunchFlag) >= 0;

    protected internal override void Install()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), SelfTestFeature.LaunchFlag) >= 0)
            throw new InvalidOperationException(
                $"{LaunchFlag} and {SelfTestFeature.LaunchFlag} cannot run together"
            );
        Patch(typeof(MpSendProbe));
        Patch(typeof(MpPingProbe));
        Patch(typeof(InputEdgeCharacterPatch));
        InputEdgeLog.Install();

        var dir = MpDir.ToNative(
            MpDir.Resolve(
                Environment.GetCommandLineArgs(),
                MpDir.UnixHome(
                    Environment.GetEnvironmentVariable("HOME"),
                    Environment.GetEnvironmentVariable("WINEHOMEDIR")
                ),
                Path.Combine(Paths.BepInExRootPath, "peakxr-mp")
            ),
            Path.DirectorySeparatorChar == '\\'
        );
        SelfTestRunner.MpDirectory = dir;
        var go = new GameObject("PeakXR.MpTest") { hideFlags = HideFlags.HideAndDontSave };
        go.AddComponent<SelfTestRunner>();
        Plugin.Log.LogInfo($"[mptest] files in {dir}");
    }
}

/// <summary>
/// Counts what this client raises, per event code: <c>PhotonNetwork.RaiseEvent</c> (PhotonUnityNetworking.dll), the
/// path net.pose sends event 151 on (Net/NetPoseSync.cs:245). With a vanilla host it must stay at zero for 151.
/// </summary>
[HarmonyPatch]
static class MpSendProbe
{
    internal static readonly Dictionary<byte, int> Sent = new();

    static MethodBase TargetMethod() =>
        AccessTools.Method(
            typeof(PhotonNetwork),
            nameof(PhotonNetwork.RaiseEvent),
            new[] { typeof(byte), typeof(object), typeof(RaiseEventOptions), typeof(SendOptions) }
        );

    static void Prefix(byte eventCode)
    {
        Sent.TryGetValue(eventCode, out int n);
        Sent[eventCode] = n + 1;
    }
}

/// <summary>The point of this client's last ping as every client receives it: <c>PointPinger.ReceivePoint_Rpc</c>
/// (decomp Assembly-CSharp/PointPinger.cs:84, sent to All at :70). Read only.</summary>
[HarmonyPatch]
static class MpPingProbe
{
    internal static Vector3? Last;

    static MethodBase TargetMethod() => GameMember.Method(typeof(PointPinger), "ReceivePoint_Rpc");

    static void Postfix(PointPinger __instance, Vector3 point)
    {
        if (__instance.character != null && __instance.character.IsLocal)
            Last = point;
    }
}
