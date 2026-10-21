using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakXR.Interact.Aim;

/// <summary>
/// Thin line from the holding hand to where an aimed tool would hit, shown while a finger rests on that hand's
/// trigger. The flat game shows its aim with a screen-centre crosshair, which means nothing once the tool aims from
/// the hand. Lives on its own DontDestroyOnLoad object; the line is built on the first VR frame.
/// </summary>
internal sealed class AimReticle : MonoBehaviour
{
    private const float DefaultRange = 40f;
    private const float TriggerAimThreshold = 0.05f;
    private const float LineWidth = 0.004f;
    private const float DotSize = 0.025f;

    private static readonly string[] ShaderNames =
    {
        "Universal Render Pipeline/Unlit",
        "Sprites/Default",
        "Unlit/Color",
        "Hidden/Internal-Colored",
    };

    private static readonly Color LineColor = new(1f, 1f, 1f, 0.6f);

    private LineRenderer line;
    private Transform dot;
    private bool built;
    private bool buildFailed;
    private bool shown;

    private Item cachedItem;
    private float range;
    private float rangeDown;
    private ClimbingSpikeComponent spike;
    private Material material;
    private bool red;

    /// <summary>The held tool's usability comes from its aim (the shooters write overrideUsability from
    /// WillAttach every frame, RopeShooter.cs:69, VineShooter.cs:33).</summary>
    private bool aimUsability;

    private void LateUpdate()
    {
        if (!VRSession.Active || !AimConfig.Instance.AimLine || !TryAim(out var start, out var end))
        {
            Show(false);
            return;
        }
        if (!built && !Build())
            return;
        // A piton aimed past its real range (SpikeCue): the line turns the ui.affordance "can't" colour.
        bool outOfRange =
            spike != null ? Tools.SpikeCue.OutOfRange : aimUsability && !cachedItem.CanUsePrimary();
        if (outOfRange && spike == null)
        {
            // Before the press: the reason and, for a vine aimed too steep, the ring an accepted aim would hit.
            var c = Character.localCharacter;
            bool left = LocalHold.ItemInLeftHand;
            var reason = UseDenial.Why(c, cachedItem, out var hint);
            UI.Affordances.Affordance.Hover(left, end, reason, hint);
        }
        if (outOfRange != red)
        {
            red = outOfRange;
            material.color = red ? UI.Affordances.Affordance.DenyColor : LineColor;
        }
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        dot.position = end;
        Show(true);
        // A tool held at the face would draw its line across the view; the dot still marks the aim.
        var head = VRRig.Instance != null ? VRRig.Instance.Head : null;
        bool beam = head == null || !LaserLook.CrossesFace(start.N(), end.N(), head.position.N());
        if (line.enabled != beam)
            line.enabled = beam;
    }

    private bool TryAim(out Vector3 start, out Vector3 end)
    {
        start = end = default;
        var c = Character.localCharacter;
        if (c == null)
            return false;
        var item = c.data.currentItem;
        if (item == null)
        {
            cachedItem = null;
            return false;
        }
        if (!ReferenceEquals(item, cachedItem))
        {
            cachedItem = item;
            Classify(item);
        }
        if (range <= 0f)
            return false;
        ref var hand = ref VRInput.Hand(LocalHold.ItemInLeftHand);
        if (!hand.TriggerTouch && hand.Trigger <= TriggerAimThreshold)
            return false;
        if (!HandAim.TryHeld(out start, out var rotation))
            return false;
        var dir = rotation * Vector3.forward;
        float max = dir.y < 0f ? rangeDown : range;
        // The piton's start range depends on climbing (CharacterItems.cs:1074).
        if (spike != null)
            max = Usable(
                c.data.isClimbingAnything
                    ? spike.climbingSpikeStartDistance
                    : spike.climbingSpikeStartDistanceGrounded
            );
        end = Physics.Raycast(
            start,
            dir,
            out var hit,
            max,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore
        )
            ? hit.point
            : start + dir * max;
        return true;
    }

    /// <summary>Aim range of an aimed tool, or 0 for anything else. Runs once per held-item change.</summary>
    private void Classify(Item item)
    {
        range = 0f;
        rangeDown = 0f;
        spike = null;
        aimUsability = false;
        if (item.TryGetComponent<RopeShooter>(out var ropeShooter))
        {
            range = ropeShooter.maxLength;
            aimUsability = true;
        }
        else if (item.TryGetComponent<VineShooter>(out var vineShooter))
        {
            range = vineShooter.maxLength;
            aimUsability = true;
        }
        else if (item.TryGetComponent<RescueHook>(out var hook))
        {
            range = Usable(hook.range);
            rangeDown = Usable(hook.rangeDownward);
            return;
        }
        else if (item.TryGetComponent<Action_RaycastDart>(out var dart))
            range = dart.maxDistance;
        else if (item.TryGetComponent<global::Peak.Action_RaycastSpawnSomething>(out var spawner))
            range = spawner.maxDistance;
        else if (item.TryGetComponent<global::Peak.Action_CloneSelectedItem>(out var clone))
            range = clone.range;
        else if (item.TryGetComponent<RopeTier>(out var tier))
            range = tier.maxAnchorGhostDistance;
        else if (item.TryGetComponent<Constructable>(out var constructable))
            range = constructable.maxConstructDistance;
        else if (item.TryGetComponent(out spike))
            range = Mathf.Max(
                spike.climbingSpikeStartDistance,
                spike.climbingSpikeStartDistanceGrounded
            );
        else
            return;
        range = Usable(range);
        rangeDown = range;
    }

    private static float Usable(float value) => value > 0f ? value : DefaultRange;

    private bool Build()
    {
        if (buildFailed)
            return false;
        Shader shader = null;
        for (int i = 0; i < ShaderNames.Length && shader == null; i++)
            shader = Shader.Find(ShaderNames[i]);
        if (shader == null)
        {
            buildFailed = true;
            Plugin.Log.LogWarning("interact.aim: no unlit shader found, aim line disabled.");
            return false;
        }
        material = new Material(shader) { color = LineColor };

        var lineObject = new GameObject("PeakXR.AimLine");
        lineObject.transform.SetParent(transform, false);
        line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = LineWidth;
        line.numCapVertices = 0;
        line.startColor = line.endColor = LineColor;
        line.sharedMaterial = material;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;

        var dotObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dotObject.name = "PeakXR.AimDot";
        Destroy(dotObject.GetComponent<Collider>());
        dotObject.transform.SetParent(transform, false);
        dotObject.transform.localScale = new Vector3(DotSize, DotSize, DotSize);
        var dotRenderer = dotObject.GetComponent<MeshRenderer>();
        dotRenderer.sharedMaterial = material;
        dotRenderer.shadowCastingMode = ShadowCastingMode.Off;
        dotRenderer.receiveShadows = false;
        dotObject.SetActive(false);
        dot = dotObject.transform;

        built = true;
        return true;
    }

    private void Show(bool visible)
    {
        if (visible == shown || !built)
            return;
        shown = visible;
        line.enabled = visible;
        dot.gameObject.SetActive(visible);
    }
}
