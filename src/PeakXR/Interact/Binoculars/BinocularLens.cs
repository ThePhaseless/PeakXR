using System;
using HarmonyLib;
using PeakXR.Avatar;
using PeakXR.Core;
using PeakXR.VR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeakXR.Interact.Binoculars;

/// <summary>
/// The VR binocular view. Vanilla zooms by taking over the main camera, which in a headset is forced motion;
/// here a separate camera at the holding hand renders the zoom into a render texture, shown on an unlit quad at
/// the eyepiece, in front of the eyes once the binoculars are raised to the face, or on a world panel. The
/// image always comes from the hand-stable camera, never from moving the headset view. Resources exist only
/// while the local player holds active binoculars in VR. One instance, on a hidden DontDestroyOnLoad host.
/// </summary>
[DefaultExecutionOrder(10000)]
internal sealed class BinocularLens : MonoBehaviour
{
    internal static AccessTools.FieldRef<ItemActionBase, Item> ItemOf;
    internal static AccessTools.FieldRef<MainCamera, CameraOverride> CamOverride;

    // Eyepiece lens: distance (m) from the grip toward the head, and quad size.
    private const float EyepieceOffset = 0.07f;
    private const float EyepieceSize = 0.05f;

    // Near-eye lens: 0.35 m ahead, 0.255 m wide, about 40 degrees of view. Leaves at 1.5x EyeDistance.
    private const float NearEyeDistance = 0.35f;
    private const float NearEyeSize = 0.255f;
    private const float NearEyeExit = 1.5f;

    // Panel fallback: 0.5 m wide, 1 m ahead and slightly low; follows the head's yaw lazily.
    private const float PanelDistance = 1f;
    private const float PanelSize = 0.5f;
    private const float PanelDrop = 0.1f;
    private const float PanelFollowAngle = 30f;
    private const float PanelSettleAngle = 2f;
    private const float PanelFollowRate = 4f;

    // Keeps the binoculars' own model (held at the hand) out of the zoomed image.
    private const float LensNearClip = 0.3f;

    // Wrist-twist steps across the binoculars' whole zoom range.
    private const float TwistZoomSteps = 8f;

    private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
    private static readonly Action<ScriptableRenderContext, Camera> BeginRender = OnBeginCamera;
    private static readonly Action<ScriptableRenderContext, Camera> EndRender = OnEndCamera;

    private static BinocularLens instance;
    private static bool shaderWarned;

    private enum Placement
    {
        None,
        Eyepiece,
        NearEye,
        Panel,
    }

    private readonly TwistKnob knob = new();

    private Action_ShowBinocularOverlay overlay;
    private Item item;

    private Camera lensCam;
    private RenderTexture texture;
    private Material material;
    private GameObject quad;
    private Transform quadTransform;
    private Renderer quadRenderer;
    private Placement placement;

    private Vector3 panelDir;
    private bool panelFollowing;

    private bool twistValid;
    private bool twistLeft;
    private Quaternion lastHandRotation;

    /// <summary>Binoculars were raised in VR: hand the view to the lens and keep vanilla off the headset.</summary>
    public static void Begin(Action_ShowBinocularOverlay binoculars)
    {
        Suppress(binoculars);
        if (instance == null)
        {
            var host = new GameObject("PeakXR.Binoculars")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            DontDestroyOnLoad(host);
            instance = host.AddComponent<BinocularLens>();
        }
        instance.Track(binoculars);
    }

    /// <summary>Binoculars were lowered: drop the lens view and its render resources.</summary>
    public static void End(Action_ShowBinocularOverlay binoculars)
    {
        if (instance != null && instance.overlay == binoculars)
            instance.Stop();
    }

    /// <summary>VR started while the local player already looks through binoculars (flat override active).</summary>
    public static void OnVRStarted()
    {
        var c = Character.localCharacter;
        if (c == null || c.data.currentItem == null)
            return;
        var binoculars = c.data.currentItem.GetComponentInChildren<Action_ShowBinocularOverlay>();
        if (binoculars != null && binoculars.binocularsActive && !binoculars.isProp)
            Begin(binoculars);
    }

    /// <summary>VR stopped mid-use: restore the vanilla flat zoom that the lens replaced.</summary>
    public static void OnVRStopped()
    {
        if (instance == null)
            return;
        var binoculars = instance.overlay;
        instance.Stop();
        if (
            binoculars == null
            || !binoculars.binocularsActive
            || !HandAim.IsLocalHeld(ItemOf(binoculars))
        )
            return;
        if (binoculars.featureManager != null)
            binoculars.featureManager.setFeatureActive(true);
        if (MainCamera.instance != null)
            MainCamera.instance.SetCameraOverride(binoculars.cameraOverride);
    }

    /// <summary>Turns off the screen-space overlay feature and clears this binoculars' camera override only.</summary>
    private static void Suppress(Action_ShowBinocularOverlay binoculars)
    {
        if (binoculars.featureManager != null)
            binoculars.featureManager.setFeatureActive(false);
        ClearOverride(binoculars);
    }

    private static void ClearOverride(Action_ShowBinocularOverlay binoculars)
    {
        var main = MainCamera.instance;
        var own = binoculars.cameraOverride;
        if (main != null && own != null && CamOverride(main) == own)
            main.SetCameraOverride(null);
    }

    private void Track(Action_ShowBinocularOverlay binoculars)
    {
        overlay = binoculars;
        item = ItemOf(binoculars);
        placement = Placement.None;
        twistValid = false;
        knob.Reset();
        enabled = true;
    }

    private void Stop()
    {
        Release();
        overlay = null;
        item = null;
        twistValid = false;
        knob.Reset();
        enabled = false;
    }

    private void LateUpdate()
    {
        var binoculars = overlay;
        if (binoculars == null || !VRSession.Active || !binoculars.binocularsActive)
        {
            Stop();
            return;
        }
        // Not (or no longer) in the local player's hand: free the GPU resources, keep watching in case the same
        // binoculars come back to hand still raised.
        if (item == null || !HandAim.IsLocalHeld(item))
        {
            Release();
            twistValid = false;
            return;
        }
        ClearOverride(binoculars);
        var rig = VRRig.Instance;
        if (rig == null || !EnsureResources())
            return;

        bool left = LocalHold.ItemInLeftHand;
        AimLens(binoculars);
        TwistZoom(binoculars, left);
        Place(rig, left);
    }

    private void AimLens(Action_ShowBinocularOverlay binoculars)
    {
        if (HandAim.TryHeld(out var origin, out var rotation))
        {
            // Roll-free so a zoom twist does not spin the image.
            var forward = rotation * Vector3.forward;
            var up = Mathf.Abs(forward.y) > 0.98f ? rotation * Vector3.up : Vector3.up;
            lensCam.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward, up));
        }
        var zoom = binoculars.cameraOverride;
        lensCam.fieldOfView = Mathf.Clamp(zoom != null ? zoom.fov : 35f, 1f, 120f);
        if (!lensCam.enabled)
            lensCam.enabled = true;
    }

    private void TwistZoom(Action_ShowBinocularOverlay binoculars, bool left)
    {
        ref var hand = ref VRInput.Hand(left);
        if (!BinocularsConfig.TwistZoom || !hand.Tracked)
        {
            twistValid = false;
            return;
        }
        if (!twistValid || twistLeft != left)
        {
            lastHandRotation = hand.Rotation;
            twistLeft = left;
            twistValid = true;
            knob.Reset();
            return;
        }
        float delta = TwistKnob.TwistAngle(
            lastHandRotation.N(),
            hand.Rotation.N(),
            Vector3.forward.N()
        );
        lastHandRotation = hand.Rotation;
        int steps = knob.Update(delta, Time.deltaTime);
        var zoom = binoculars.cameraOverride as CameraOverride_Binoculars;
        if (steps == 0 || zoom == null)
            return;
        // Positive twist about the hand's forward is counter-clockwise seen from behind the hand: zoom out.
        // AdjustFOV clamps to the item's own range (CameraOverride_Binoculars.cs:27-30).
        float range = zoom.maxFov - zoom.minFov;
        zoom.AdjustFOV(steps * (range > 0f ? range / TwistZoomSteps : 5f));
        Haptics.Pulse(left, 0.3f, 0.02f);
    }

    private void Place(VRRig rig, bool left)
    {
        var head = rig.Head;
        if (head == null)
            return;
        if (BinocularsConfig.PanelMode)
        {
            PlacePanel(head);
            return;
        }
        var grip = rig.Hand(left);
        if (grip == null)
            return;

        var headPos = head.position;
        var gripPos = grip.position;
        var toHead = headPos - gripPos;
        float gripDistance = toHead.magnitude;
        if (gripDistance < 1e-3f)
            return;
        var eyepiece =
            gripPos + toHead * (Mathf.Min(EyepieceOffset, gripDistance * 0.5f) / gripDistance);
        float distance = Vector3.Distance(headPos, eyepiece);
        float enter = BinocularsConfig.EyeDistance;
        bool near =
            placement == Placement.NearEye ? distance < enter * NearEyeExit : distance < enter;

        if (near)
        {
            if (placement != Placement.NearEye || quadTransform.parent != head)
            {
                // Head-locked child: follows the HMD pose exactly, the image inside it comes from the hand.
                quadTransform.SetParent(head, false);
                quadTransform.SetLocalPositionAndRotation(
                    new Vector3(0f, 0f, NearEyeDistance),
                    Quaternion.identity
                );
                quadTransform.localScale = new Vector3(NearEyeSize, NearEyeSize, 1f);
                SetOnTop(true);
                placement = Placement.NearEye;
            }
            return;
        }

        if (placement != Placement.Eyepiece)
        {
            quadTransform.SetParent(transform, false);
            quadTransform.localScale = new Vector3(EyepieceSize, EyepieceSize, 1f);
            SetOnTop(false);
            placement = Placement.Eyepiece;
        }
        if (distance > 1e-3f)
            quadTransform.SetPositionAndRotation(
                eyepiece,
                Quaternion.LookRotation(eyepiece - headPos)
            );
    }

    private void PlacePanel(Transform head)
    {
        var headPos = head.position;
        var forward = head.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f)
        {
            forward = head.up;
            forward.y = 0f;
        }
        if (forward.sqrMagnitude < 1e-6f)
            return;
        forward.Normalize();

        if (placement != Placement.Panel)
        {
            quadTransform.SetParent(transform, false);
            quadTransform.localScale = new Vector3(PanelSize, PanelSize, 1f);
            SetOnTop(true);
            panelDir = forward;
            panelFollowing = false;
            placement = Placement.Panel;
        }

        float angle = Vector3.Angle(panelDir, forward);
        if (angle > PanelFollowAngle)
            panelFollowing = true;
        if (panelFollowing)
        {
            panelDir = Vector3
                .Slerp(panelDir, forward, 1f - Mathf.Exp(-PanelFollowRate * Time.deltaTime))
                .normalized;
            if (angle < PanelSettleAngle)
                panelFollowing = false;
        }
        var pos = headPos + panelDir * PanelDistance;
        pos.y -= PanelDrop;
        quadTransform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - headPos));
    }

    /// <summary>Near-eye and panel views draw over the world (the binoculars' own model sits between the eyes
    /// and the image); the eyepiece lens depth-tests normally.</summary>
    private void SetOnTop(bool onTop)
    {
        if (material.HasProperty(ZTestId))
            material.SetFloat(
                ZTestId,
                onTop ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual
            );
        material.renderQueue = onTop ? (int)RenderQueue.Overlay : -1;
    }

    private bool EnsureResources()
    {
        if (lensCam != null && texture != null && material != null && quad != null)
            return true;
        Release();

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Texture");
        if (shader == null)
        {
            if (!shaderWarned)
                Plugin.Log.LogWarning(
                    "[interact.binoculars] no unlit shader found; zoom view unavailable"
                );
            shaderWarned = true;
            return false;
        }

        int size = BinocularsConfig.LensResolution;
        texture = new RenderTexture(size, size, 24) { name = "PeakXR.BinocularLens" };
        texture.Create();

        Camera main = null;
        if (MainCamera.instance != null)
            MainCamera.instance.TryGetComponent(out main);

        var camObject = new GameObject("PeakXR.BinocularLens.Camera");
        camObject.transform.SetParent(transform, false);
        lensCam = camObject.AddComponent<Camera>();
        lensCam.enabled = false;
        lensCam.stereoTargetEye = StereoTargetEyeMask.None;
        lensCam.targetTexture = texture;
        if (main != null)
        {
            lensCam.cullingMask = main.cullingMask;
            lensCam.clearFlags = main.clearFlags;
            lensCam.backgroundColor = main.backgroundColor;
            lensCam.farClipPlane = main.farClipPlane;
            lensCam.nearClipPlane = Mathf.Max(main.nearClipPlane, LensNearClip);
            lensCam.depth = main.depth - 1f;
        }
        else
        {
            lensCam.nearClipPlane = LensNearClip;
        }
        // Renderer index stays the pipeline default, as on the game's main camera: its fog pass keeps the zoomed
        // view as hazy as the eyes see it. A third shadow-map render and AA are not worth it for a small lens
        // image, so this camera skips both. shortcut: renders every frame; render at half rate if profiling says so.
        var data = lensCam.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.allowXRRendering = false;
        data.volumeTrigger = camObject.transform;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;
        if (main != null && main.TryGetComponent(out UniversalAdditionalCameraData mainData))
        {
            data.renderPostProcessing = mainData.renderPostProcessing;
            data.volumeLayerMask = mainData.volumeLayerMask;
        }

        material = new Material(shader) { name = "PeakXR.BinocularLens", mainTexture = texture };

        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "PeakXR.BinocularLens.Quad";
        if (quad.TryGetComponent(out Collider collider))
            DestroyImmediate(collider);
        quadTransform = quad.transform;
        quadTransform.SetParent(transform, false);
        quad.TryGetComponent(out quadRenderer);
        quadRenderer.sharedMaterial = material;
        quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        placement = Placement.None;

        RenderPipelineManager.beginCameraRendering -= BeginRender;
        RenderPipelineManager.beginCameraRendering += BeginRender;
        RenderPipelineManager.endCameraRendering -= EndRender;
        RenderPipelineManager.endCameraRendering += EndRender;
        return true;
    }

    private void Release()
    {
        RenderPipelineManager.beginCameraRendering -= BeginRender;
        RenderPipelineManager.endCameraRendering -= EndRender;
        if (lensCam != null)
        {
            lensCam.targetTexture = null;
            Destroy(lensCam.gameObject);
        }
        if (quad != null)
            Destroy(quad);
        if (material != null)
            Destroy(material);
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }
        lensCam = null;
        texture = null;
        material = null;
        quad = null;
        quadTransform = null;
        quadRenderer = null;
        placement = Placement.None;
    }

    // The lens camera must not see its own output (the near-eye quad can sit inside its frustum).
    private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        var lens = instance;
        if (lens != null && lens.quadRenderer != null && camera == lens.lensCam)
            lens.quadRenderer.enabled = false;
    }

    private static void OnEndCamera(ScriptableRenderContext context, Camera camera)
    {
        var lens = instance;
        if (lens != null && lens.quadRenderer != null && camera == lens.lensCam)
            lens.quadRenderer.enabled = true;
    }

    private void OnDestroy()
    {
        Release();
        if (instance == this)
            instance = null;
    }
}
