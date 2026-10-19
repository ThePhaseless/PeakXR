namespace PeakXR.Core
{
    public enum PointerHandMode
    {
        LastUsed,
        Left,
        Right,
    }

    /// <summary>
    /// Picks which controller drives the UI laser and exposes its trigger as a click button.
    /// In <see cref="PointerHandMode.LastUsed"/> the laser moves to whichever hand last pulled its
    /// trigger, but never while the active hand is still holding (mid-drag).
    /// </summary>
    public sealed class PointerHands
    {
        public PointerHandMode Mode = PointerHandMode.LastUsed;

        public readonly AnalogButton Left = new AnalogButton(0.55f, 0.35f);
        public readonly AnalogButton Right = new AnalogButton(0.55f, 0.35f);

        bool _lastLeft;

        public bool ActiveLeft { get; private set; }

        AnalogButton Active => ActiveLeft ? Left : Right;

        public bool Pressed => Active.Pressed;
        public bool Released => Active.Released;
        public bool Held => Active.Held;

        public void Update(float leftTrigger, float rightTrigger, float dt)
        {
            bool wasHeld = Active.Held;
            Left.Update(leftTrigger, dt);
            Right.Update(rightTrigger, dt);

            if (!wasHeld)
            {
                if (Left.Pressed && !Right.Held)
                    _lastLeft = true;
                else if (Right.Pressed && !Left.Held)
                    _lastLeft = false;
            }

            ActiveLeft = Mode switch
            {
                PointerHandMode.Left => true,
                PointerHandMode.Right => false,
                _ => _lastLeft,
            };
        }
    }
}
