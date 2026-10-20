namespace PeakXR.Core
{
    /// <summary>Who owns the view this frame, in the same priority order as the game's camera code.</summary>
    public enum CameraState
    {
        /// <summary>Normal play: the HMD sits on the player's body.</summary>
        Body,

        /// <summary>The body tumbles (fall, bonk, pass-out ramp): vanilla rolls the camera with the head.</summary>
        Ragdoll,

        /// <summary>Riding a rocket: vanilla switches to a third-person chase view.</summary>
        Rocket,

        /// <summary>Fully passed out or dead: vanilla orbits a spectated player.</summary>
        Spectate,

        /// <summary>A scripted camera override (not the binoculars).</summary>
        Override,

        /// <summary>The game disabled its camera for an end cutscene.</summary>
        Cutscene,
    }

    /// <summary>Player preference for views the game takes over.</summary>
    public enum TakeoverView
    {
        /// <summary>Detached chase view behind the target, slow follow, the player keeps their own rotation.</summary>
        ThirdPerson,

        /// <summary>Stay at the target's eyes with the horizon locked: only the HMD and the player's turns rotate.</summary>
        FirstPerson,
    }

    /// <summary>How the rig places its origin.</summary>
    public enum ViewKind
    {
        /// <summary>On the local body (the normal case).</summary>
        FirstPerson,

        /// <summary>At the eyes of the followed character (spectate, rocket), horizon locked.</summary>
        FollowFirstPerson,

        /// <summary>Behind and above the followed character, slow follow.</summary>
        ThirdPerson,

        /// <summary>A fixed point captured when the view switched (cutscenes, scripted shots).</summary>
        Static,
    }

    /// <summary>
    /// Comfort policy for camera takeovers: the game's camera states map onto a few rig placements that
    /// never rotate the player's view and never move it except to follow a body.
    /// </summary>
    public static class CameraPolicy
    {
        public static CameraState State(
            bool cutscene,
            bool scriptedOverride,
            bool spectating,
            bool rocketing,
            bool ragdoll
        )
        {
            if (cutscene)
                return CameraState.Cutscene;
            if (scriptedOverride)
                return CameraState.Override;
            if (spectating)
                return CameraState.Spectate;
            if (rocketing)
                return CameraState.Rocket;
            return ragdoll ? CameraState.Ragdoll : CameraState.Body;
        }

        /// <param name="eventDetach">comfort.events asked for the horizon-locked chase view (a launch): the own
        /// body's views go third person whatever the preference; game-owned views keep their placement.</param>
        public static ViewKind View(
            CameraState state,
            TakeoverView preference,
            bool detachOnRagdoll,
            bool eventDetach = false
        )
        {
            if (eventDetach && (state == CameraState.Body || state == CameraState.Ragdoll))
                return ViewKind.ThirdPerson;
            switch (state)
            {
                case CameraState.Ragdoll:
                    return detachOnRagdoll && preference == TakeoverView.ThirdPerson
                        ? ViewKind.ThirdPerson
                        : ViewKind.FirstPerson;
                case CameraState.Rocket:
                case CameraState.Spectate:
                    return preference == TakeoverView.ThirdPerson
                        ? ViewKind.ThirdPerson
                        : ViewKind.FollowFirstPerson;
                case CameraState.Override:
                case CameraState.Cutscene:
                    return ViewKind.Static;
                default:
                    return ViewKind.FirstPerson;
            }
        }

        public static bool IsDetached(ViewKind kind) => kind != ViewKind.FirstPerson;
    }

    /// <summary>
    /// Fires once per hold when a button stays down for <see cref="Seconds"/>. Time counts from the press frame
    /// (0 on it), the same clock as <see cref="ButtonEdge.HeldTime"/>, so a tap mapped by <see cref="ActionMapper"/>
    /// with the same threshold never overlaps the long press.
    /// </summary>
    public sealed class LongPress
    {
        public float Seconds = 1f;

        float _held;
        bool _down;

        /// <summary>True from the frame the hold fired until the button is released.</summary>
        public bool Fired { get; private set; }

        /// <summary>0..1 share of <see cref="Seconds"/> held so far; 0 while up.</summary>
        public float Progress =>
            !_down ? 0f
            : Seconds <= 0f ? 1f
            : System.Math.Min(1f, _held / Seconds);

        /// <summary>Seconds since the press frame while held, 0 while up.</summary>
        public float HeldSeconds => _down ? _held : 0f;

        /// <summary>Returns true on the frame the hold time reaches <see cref="Seconds"/>.</summary>
        public bool Update(bool down, float dt)
        {
            if (!down)
            {
                _down = false;
                _held = 0f;
                Fired = false;
                return false;
            }
            _held = _down ? _held + dt : 0f;
            _down = true;
            if (Fired || _held < Seconds)
                return false;
            Fired = true;
            return true;
        }
    }
}
