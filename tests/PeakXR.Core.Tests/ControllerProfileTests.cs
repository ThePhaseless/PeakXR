using System.Numerics;

namespace PeakXR.Core.Tests;

/// <summary>
/// What each OpenXR interaction profile enabled in OpenXRBackend gives a hand. Usage lists are the legacy
/// InputDevice feature usages Unity OpenXR 1.16.1 declares per profile (Runtime/Features/Interactions/*Profile.cs);
/// the Index list matches a real headset log. Pose usages are left out: they do not affect the mapping.
/// </summary>
public class ControllerProfileTests
{
    static readonly string[] Index =
    {
        "MenuButton",
        "MenuTouch",
        "PrimaryButton",
        "PrimaryTouch",
        "SecondaryButton",
        "SecondaryTouch",
        "Grip",
        "GripButton",
        "GripForce",
        "Trigger",
        "TriggerButton",
        "TriggerTouch",
        "Primary2DAxis",
        "Primary2DAxisClick",
        "Primary2DAxisTouch",
        "Secondary2DAxis",
        "Secondary2DAxisForce",
        "Secondary2DAxisTouch",
    };

    static readonly string[] Touch =
    {
        "Primary2DAxis",
        "Grip",
        "GripButton",
        "MenuButton",
        "PrimaryButton",
        "PrimaryTouch",
        "SecondaryButton",
        "SecondaryTouch",
        "Trigger",
        "TriggerButton",
        "TriggerTouch",
        "Primary2DAxisClick",
        "Primary2DAxisTouch",
        "ThumbrestTouch",
    };

    // Touch Plus and Touch Pro add these to the Touch list.
    static readonly string[] TouchPlusExtra =
    {
        "TriggerForce",
        "TriggerCurl",
        "TriggerSlide",
        "TriggerProximity",
        "ThumbProximity",
    };
    static readonly string[] TouchProExtra =
    {
        "ThumbrestForce",
        "StylusForce",
        "TriggerCurl",
        "TriggerSlide",
        "TriggerProximity",
        "ThumbProximity",
        "HapticTrigger",
        "HapticThumb",
    };

    static readonly string[] ViveWand =
    {
        "Grip",
        "GripButton",
        "MenuButton",
        "SystemButton",
        "Trigger",
        "TriggerButton",
        "Primary2DAxis",
        "Primary2DAxisTouch",
        "Primary2DAxisClick",
    };

    static readonly string[] ReverbG2 =
    {
        "PrimaryButton",
        "SecondaryButton",
        "MenuButton",
        "Grip",
        "GripButton",
        "Trigger",
        "TriggerButton",
        "Primary2DAxis",
        "Primary2DAxisClick",
    };

    static readonly string[] WindowsMotion =
    {
        "Primary2DAxis",
        "Secondary2DAxis",
        "Grip",
        "GripButton",
        "MenuButton",
        "Trigger",
        "TriggerButton",
        "Primary2DAxisClick",
        "Secondary2DAxisClick",
        "Secondary2DAxisTouch",
    };

    // The Khronos simple profile's select click arrives as PrimaryButton.
    static readonly string[] Simple = { "PrimaryButton", "MenuButton" };

    static string[] Concat(string[] a, string[] b)
    {
        var all = new string[a.Length + b.Length];
        a.CopyTo(all, 0);
        b.CopyTo(all, a.Length);
        return all;
    }

    static ControllerButtons Map(string[] usages, ControllerSample s, bool moveHand = false) =>
        ControllerMap.Map(ControllerLayout.From(usages), s, moveHand);

    [Fact]
    public void Index_FaceButtonsAndAnalogsPassThrough()
    {
        var b = Map(
            Index,
            new ControllerSample
            {
                Trigger = 0.4f,
                Grip = 0.7f,
                Primary = true,
                SecondaryTouch = true,
                Stick = new Vector2(0.2f, -0.9f),
                StickTouch = true,
                StickClick = true,
            }
        );
        Assert.Equal(0.4f, b.Trigger);
        Assert.Equal(0.7f, b.Grip);
        Assert.True(b.TriggerTouch);
        Assert.True(b.Primary);
        Assert.False(b.Secondary);
        Assert.True(b.SecondaryTouch);
        Assert.Equal(new Vector2(0.2f, -0.9f), b.Stick);
        Assert.True(b.StickTouch);
        Assert.True(b.StickClick);
        Assert.False(b.Menu);
    }

    [Theory]
    [InlineData(0.3f, false)]
    [InlineData(0.7f, true)]
    public void Index_FirmTrackpadSqueezeIsMenu(float force, bool menu)
    {
        // SteamVR keeps the Index system button, so the trackpad squeeze stands in; the pad has no click usage.
        var layout = ControllerLayout.From(Index);
        Assert.False(layout.EmulateFaceButtons);
        Assert.False(layout.PadMenuClick);
        var b = Map(Index, new ControllerSample { PadForce = force, Pad = new Vector2(0f, 0.9f) });
        Assert.Equal(menu, b.Menu);
        Assert.False(b.Secondary);
        Assert.Equal(Vector2.Zero, b.Stick);
    }

    [Fact]
    public void Touch_ButtonsMenuAndTouches()
    {
        var b = Map(
            Touch,
            new ControllerSample
            {
                Secondary = true,
                PrimaryTouch = true,
                Menu = true,
                TriggerTouch = true,
                Stick = new Vector2(-1f, 0f),
            }
        );
        Assert.True(b.Secondary);
        Assert.False(b.Primary);
        Assert.True(b.PrimaryTouch);
        Assert.True(b.Menu);
        Assert.True(b.TriggerTouch);
        Assert.Equal(0f, b.Trigger);
        Assert.Equal(new Vector2(-1f, 0f), b.Stick);
    }

    [Fact]
    public void TouchPlusAndPro_ExtraUsagesChangeNothing()
    {
        var s = new ControllerSample
        {
            Trigger = 0.6f,
            Grip = 1f,
            Primary = true,
            Stick = new Vector2(0f, 0.5f),
        };
        var touch = Map(Touch, s);
        Assert.Equal(touch, Map(Concat(Touch, TouchPlusExtra), s));
        Assert.Equal(touch, Map(Concat(Touch, TouchProExtra), s));
        Assert.Equal(
            ControllerLayout.From(Touch),
            ControllerLayout.From(Concat(Touch, TouchProExtra))
        );
        // TriggerCurl is a trigger value, not a finger curl.
        foreach (var name in Concat(TouchPlusExtra, TouchProExtra))
            Assert.Equal(-1, XRUsageNames.FingerIndex(name));
    }

    [Fact]
    public void ViveWand_PadIsTheStickAndNeedsAClick()
    {
        var layout = ControllerLayout.From(ViveWand);
        Assert.True(layout.EmulateFaceButtons);
        Assert.True(layout.PadIsStick);

        // Resting thumb: nothing.
        var rest = new ControllerSample { Stick = new Vector2(0f, 0.9f), StickTouch = true };
        var b = Map(ViveWand, rest, moveHand: true);
        Assert.Equal(Vector2.Zero, b.Stick);
        Assert.True(b.StickTouch);
        Assert.False(b.Secondary);

        // Move hand walks toward the clicked thumb.
        var click = new ControllerSample { Stick = new Vector2(0f, 0.5f), StickClick = true };
        b = Map(ViveWand, click, moveHand: true);
        Assert.Equal(new Vector2(0f, 0.5f), b.Stick);
        Assert.False(b.StickClick);

        // Turn hand: a top click is B/Y, a centre click the stick click.
        b = Map(ViveWand, click, moveHand: false);
        Assert.True(b.Secondary);
        Assert.Equal(Vector2.Zero, b.Stick);
        b = Map(ViveWand, new ControllerSample { StickClick = true }, moveHand: false);
        Assert.True(b.StickClick);
        Assert.False(b.Primary);
    }

    [Fact]
    public void ViveWand_TriggerTouchFromPullAndMenuButton()
    {
        var b = Map(ViveWand, new ControllerSample { Trigger = 0.1f, Menu = true });
        Assert.True(b.TriggerTouch);
        Assert.True(b.Menu);
        Assert.False(Map(ViveWand, new ControllerSample { Trigger = 0.04f }).TriggerTouch);
    }

    [Fact]
    public void ReverbG2_ButtonsWithoutTouches()
    {
        var layout = ControllerLayout.From(ReverbG2);
        Assert.False(layout.EmulateFaceButtons);
        var b = Map(
            ReverbG2,
            new ControllerSample
            {
                Primary = true,
                Secondary = true,
                Menu = true,
                Stick = new Vector2(0.3f, 0.3f),
            }
        );
        Assert.True(b.Primary);
        Assert.True(b.Secondary);
        Assert.True(b.Menu);
        Assert.Equal(new Vector2(0.3f, 0.3f), b.Stick);
        Assert.False(b.PrimaryTouch);
        Assert.False(b.StickTouch);
    }

    [Theory]
    [InlineData(0.8f, false, true)]
    [InlineData(-0.8f, true, false)]
    [InlineData(0f, false, false)]
    public void WindowsMotion_TouchpadClickZonesAreFaceButtons(
        float padY,
        bool primary,
        bool secondary
    )
    {
        var layout = ControllerLayout.From(WindowsMotion);
        Assert.True(layout.EmulateFaceButtons);
        Assert.False(layout.PadIsStick);
        var b = Map(
            WindowsMotion,
            new ControllerSample { Pad = new Vector2(0f, padY), PadClick = true }
        );
        Assert.Equal(primary, b.Primary);
        Assert.Equal(secondary, b.Secondary);
        // The pad click is neither the stick click nor the menu.
        Assert.False(b.StickClick);
        Assert.False(b.Menu);
    }

    [Fact]
    public void WindowsMotion_JoystickStaysTheStick()
    {
        var b = Map(
            WindowsMotion,
            new ControllerSample
            {
                Stick = new Vector2(0.6f, 0f),
                StickClick = true,
                Pad = new Vector2(0f, 0.9f),
            }
        );
        Assert.Equal(new Vector2(0.6f, 0f), b.Stick);
        Assert.True(b.StickClick);
        Assert.False(b.Secondary);
    }

    [Fact]
    public void Simple_HeldSelectIsGripAndNotAlsoPrimary()
    {
        // Simple has no grip, so select stands in for it: without that a player could never climb or grab.
        var layout = ControllerLayout.From(Simple);
        Assert.False(layout.HasTrigger);
        Assert.False(layout.HasGrip);
        Assert.True(layout.SelectIsGrip);
        var b = Map(Simple, new ControllerSample { Primary = true, Menu = true });
        Assert.Equal(1f, b.Grip);
        Assert.False(b.Primary);
        Assert.True(b.Menu);
        Assert.Equal(0f, b.Trigger);
        Assert.False(b.TriggerTouch);
        b = Map(Simple, new ControllerSample { Menu = true });
        Assert.Equal(0f, b.Grip);
        Assert.True(b.Menu);
    }

    [Fact]
    public void SelectIsGrip_OnlyOnSimple()
    {
        foreach (
            var usages in new[]
            {
                Index,
                Touch,
                Concat(Touch, TouchPlusExtra),
                Concat(Touch, TouchProExtra),
                ViveWand,
                ReverbG2,
                WindowsMotion,
                new[] { "TriggerButton", "GripButton", "PrimaryButton" },
            }
        )
            Assert.False(ControllerLayout.From(usages).SelectIsGrip);
    }

    [Fact]
    public void AnalogMissing_FallsBackToButton()
    {
        var b = Map(
            new[] { "TriggerButton", "GripButton", "PrimaryButton" },
            new ControllerSample { TriggerButton = true, GripButton = true }
        );
        Assert.Equal(1f, b.Trigger);
        Assert.Equal(1f, b.Grip);
        Assert.True(b.TriggerTouch);
    }
}
