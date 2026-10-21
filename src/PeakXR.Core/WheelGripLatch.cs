namespace PeakXR.Core
{
    /// <summary>
    /// Which grips keep an open backpack wheel open. The wheel blocks the game's input, so a grip that opened it
    /// through the game's interact (a grab on a ground backpack) must hold interact itself: the wheel commits and
    /// closes the moment interact is up, and the still-closed grip would reopen it next frame. Only the hands that were
    /// pressing interact when it opened are latched, and each only until its grip opens. A wheel an extension opened
    /// (the shoulder reach, which holds interact itself) latches nobody, so a hand still closed on its held item or an
    /// interact fist on the other hand cannot keep that wheel from committing.
    /// </summary>
    public sealed class WheelGripLatch
    {
        private bool wasOpen;

        public bool Left { get; private set; }
        public bool Right { get; private set; }
        public bool Holds => Left || Right;

        /// <param name="open">The wheel is open now.</param>
        /// <param name="openedByExtension">It opened during this frame's grip handling, not through the game's interact.</param>
        /// <param name="leftInjected">The left grip was pressing the game's interact on the last frame input ran.</param>
        /// <param name="rightInjected">The same for the right grip.</param>
        /// <param name="leftClosed">The left grip is still closed in the same press.</param>
        /// <param name="rightClosed">The same for the right grip.</param>
        public void Update(
            bool open,
            bool openedByExtension,
            bool leftInjected,
            bool rightInjected,
            bool leftClosed,
            bool rightClosed
        )
        {
            if (open && !wasOpen)
            {
                Left = !openedByExtension && leftInjected;
                Right = !openedByExtension && rightInjected;
            }
            wasOpen = open;
            Left = open && Left && leftClosed;
            Right = open && Right && rightClosed;
        }
    }
}
