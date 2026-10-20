using System.Collections.Generic;
using System.Text;
using UnityEngine.XR;

namespace PeakXR.XR;

/// <summary>
/// Logs each XR input device with every input usage it reports (stack.md section 5 item 4), so a
/// user's log shows whether e.g. <c>TriggerTouch</c> or <c>GripForce</c> exist on their runtime and
/// controller. Runs on connect only, never per frame.
/// </summary>
static class XRDeviceDump
{
    static readonly List<InputDevice> devices = new();
    static readonly List<InputFeatureUsage> usages = new();

    public static void DumpAll()
    {
        InputDevices.GetDevices(devices);
        Plugin.Log.LogInfo($"[xr.input-dump] {devices.Count} XR input device(s)");
        foreach (var d in devices)
            Dump(d);
    }

    public static void Dump(InputDevice device)
    {
        if (!device.isValid)
            return;
        var sb = new StringBuilder();
        sb.Append("[xr.input-dump] '")
            .Append(device.name)
            .Append("' by '")
            .Append(device.manufacturer)
            .Append("' (")
            .Append(device.characteristics)
            .Append("):");
        if (device.TryGetFeatureUsages(usages))
        {
            foreach (var u in usages)
                sb.Append(' ').Append(u.name).Append('(').Append(TypeName(u)).Append(')');
        }
        else
        {
            sb.Append(" (usages unavailable)");
        }
        Plugin.Log.LogInfo(sb.ToString());
    }

    static string TypeName(InputFeatureUsage u)
    {
        var t = u.type;
        if (t == typeof(bool))
            return "bool";
        if (t == typeof(float))
            return "float";
        if (t == typeof(UnityEngine.Vector2))
            return "vec2";
        if (t == typeof(UnityEngine.Vector3))
            return "vec3";
        if (t == typeof(UnityEngine.Quaternion))
            return "quat";
        return t?.Name ?? "?";
    }
}
