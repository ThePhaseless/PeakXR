namespace PeakXR.Core.Tests;

public class UseTriggerTests
{
    const float Dt = 1f / 90f;

    [Fact]
    public void Holding_ItemHandIsPrimary()
    {
        Assert.True(UseHands.PrimaryLeft(true, true, false, false, true));
        Assert.False(UseHands.PrimaryLeft(true, false, true, true, false));
    }

    [Fact]
    public void EmptyHanded_DominantHandIsPrimary()
    {
        Assert.False(UseHands.PrimaryLeft(false, false, false, false, false));
        Assert.True(UseHands.PrimaryLeft(false, false, true, false, false));
        // Both on the wall: no free hand, the layout stands.
        Assert.False(UseHands.PrimaryLeft(false, false, false, true, true));
    }

    [Fact]
    public void EmptyHanded_OneHandClimbs_FreeHandIsSecondary()
    {
        // The left hand holds the wall: the free right hand's trigger is the secondary use, which sets a piton
        // while climbing (CharacterItems.cs:1019), aimed from that hand.
        Assert.True(UseHands.PrimaryLeft(false, false, false, true, false));
        Assert.False(UseHands.PrimaryLeft(false, false, true, false, true));
    }

    [Fact]
    public void FreeHand_PressIsUse()
    {
        var t = new UseTrigger();
        t.Update(true, false, true, Dt);
        Assert.True(t.Down);
        Assert.False(t.ClimbPress);
    }

    [Fact]
    public void ClimbingHand_TriggerIsNoUse()
    {
        // A fist closing on rock squeezes the trigger too: that set a piton from the pockets (tester: "when you
        // climb it sometimes places itself").
        var t = new UseTrigger();
        t.Update(false, true, false, Dt);
        t.Update(true, true, false, Dt);
        Assert.False(t.Down);
    }

    [Fact]
    public void TriggerHeldIntoAClimb_StaysMutedUntilReleased()
    {
        var t = new UseTrigger();
        t.Update(true, false, false, Dt);
        Assert.True(t.Down);
        t.Update(true, true, false, Dt);
        Assert.False(t.Down);
        // The grip opens with the trigger still squeezed: no fresh press out of it.
        t.Update(true, false, false, Dt);
        Assert.False(t.Down);
        t.Update(false, false, false, Dt);
        t.Update(true, false, false, Dt);
        Assert.True(t.Down);
    }

    [Fact]
    public void RoleChangedWhileHeld_MutedUntilReleased()
    {
        // The other hand took the wall: this held trigger turned from primary into the secondary use, which must
        // not start a piton.
        var t = new UseTrigger();
        t.Update(true, false, true, Dt);
        Assert.True(t.Down);
        t.Update(true, false, false, Dt);
        Assert.False(t.Down);
        t.Update(false, false, false, Dt);
        t.Update(true, false, false, Dt);
        Assert.True(t.Down);
    }

    [Fact]
    public void PressWithTheGrip_NoClimbPress()
    {
        var t = new UseTrigger();
        t.Update(false, true, false, Dt);
        t.Update(true, true, false, Dt);
        Assert.False(t.ClimbPress);
    }

    [Fact]
    public void PressAfterTheHandSettledOnTheWall_IsAClimbPress()
    {
        var t = Settled();
        t.Update(true, true, false, Dt);
        Assert.True(t.ClimbPress);
        Assert.False(t.Down);
        t.Update(true, true, false, Dt);
        Assert.False(t.ClimbPress);
    }

    [Fact]
    public void ClimbPressHeld_SetsAPitonAfterTheHold()
    {
        // While climbing, a hand on the wall that pulls its trigger and keeps it down sets a piton at that hand.
        var t = Settled();
        t.Update(true, true, false, Dt);
        Assert.True(t.ClimbHold);
        Assert.False(t.Piton);
        float held = 0f;
        while (held < UseTrigger.HoldSeconds - Dt)
        {
            t.Update(true, true, false, Dt);
            held += Dt;
            Assert.False(t.Piton);
        }
        t.Update(true, true, false, Dt);
        t.Update(true, true, false, Dt);
        Assert.True(t.Piton);
        Assert.False(t.Down);
    }

    [Fact]
    public void ClimbPressReleasedEarly_NoPiton()
    {
        var t = Settled();
        t.Update(true, true, false, Dt);
        t.Update(true, true, false, Dt);
        t.Update(false, true, false, Dt);
        Assert.False(t.ClimbHold);
        for (float s = 0f; s < UseTrigger.HoldSeconds * 2f; s += Dt)
        {
            t.Update(false, true, false, Dt);
            Assert.False(t.Piton);
        }
    }

    [Fact]
    public void TriggerSqueezedWithTheGrip_NeverSetsAPiton()
    {
        // The fist closing on rock squeezes trigger and grip together: no piton however long it stays closed.
        var t = new UseTrigger();
        t.Update(true, true, false, Dt);
        for (float s = 0f; s < 3f; s += Dt)
        {
            t.Update(true, true, false, Dt);
            Assert.False(t.ClimbHold);
            Assert.False(t.Piton);
        }
    }

    [Fact]
    public void TriggerHeldIntoTheClimb_NeverSetsAPiton()
    {
        var t = new UseTrigger();
        t.Update(true, false, false, Dt);
        for (float s = 0f; s < 3f; s += Dt)
        {
            t.Update(true, true, false, Dt);
            Assert.False(t.Piton);
        }
    }

    [Fact]
    public void HandLeavesTheClimber_EndsThePitonHold()
    {
        var t = Settled();
        t.Update(true, true, false, Dt);
        for (float s = 0f; s < UseTrigger.HoldSeconds + 0.1f; s += Dt)
            t.Update(true, true, false, Dt);
        Assert.True(t.Piton);
        t.Update(true, false, false, Dt);
        Assert.False(t.ClimbHold);
        Assert.False(t.Piton);
        // Back on the wall with the trigger still down: no fresh hold out of it.
        for (float s = 0f; s < 1f; s += Dt)
            t.Update(true, true, false, Dt);
        Assert.False(t.Piton);
    }

    /// <summary>A trigger whose hand has held the wall past <see cref="UseTrigger.SettleSeconds"/>.</summary>
    private static UseTrigger Settled()
    {
        var t = new UseTrigger();
        for (float s = 0f; s < UseTrigger.SettleSeconds + Dt; s += Dt)
            t.Update(false, true, false, Dt);
        return t;
    }

    [Fact]
    public void FistClosingOnAPickup_TriggerIsNoUseUntilPressedAgain()
    {
        // Issue #48: a full fist squeezes the trigger a couple of frames after the grip; the Apple then in hand
        // started eating while the fist stayed closed.
        var t = new UseTrigger();
        t.Update(false, false, true, Dt, fist: true);
        t.Update(true, false, true, Dt, fist: true);
        Assert.False(t.Down);
        // The item lands and the grip has been closed a while: the same squeeze is still part of the fist.
        t.Update(true, false, true, Dt, fist: false);
        Assert.False(t.Down);
        t.Update(false, false, true, Dt);
        t.Update(true, false, true, Dt);
        Assert.True(t.Down);
    }

    [Fact]
    public void TriggerHeldWhenTheGripCloses_IsMuted()
    {
        var t = new UseTrigger();
        t.Update(true, false, true, Dt);
        Assert.True(t.Down);
        t.Update(true, false, true, Dt, fist: true);
        Assert.False(t.Down);
    }

    [Fact]
    public void FistWindow_HoldsForSettleSeconds()
    {
        Assert.True(UseTrigger.InFist(gripHeld: true, gripClosedFor: 0f));
        Assert.True(UseTrigger.InFist(true, UseTrigger.SettleSeconds - 0.01f));
        Assert.False(UseTrigger.InFist(true, UseTrigger.SettleSeconds));
        Assert.False(UseTrigger.InFist(false, 0f));
    }

    [Fact]
    public void Pressed_OnlyOnTheFrameAUsePressStarts()
    {
        var t = new UseTrigger();
        t.Update(true, false, true, Dt);
        Assert.True(t.Pressed);
        t.Update(true, false, true, Dt);
        Assert.False(t.Pressed);
        t.Update(false, false, true, Dt);
        t.Update(true, false, true, Dt);
        Assert.True(t.Pressed);
    }

    [Fact]
    public void FistMutedTrigger_IsNoPress_UntilAFreshSqueeze()
    {
        // Issue #74: a fist closing on an Apple squeezes the trigger; the refusal cues read this edge, so a muted
        // trigger must give none.
        var t = new UseTrigger();
        t.Update(true, false, true, Dt, fist: true);
        Assert.False(t.Pressed);
        t.Update(true, false, true, Dt);
        Assert.False(t.Pressed);
        t.Update(false, false, true, Dt);
        t.Update(true, false, true, Dt);
        Assert.True(t.Pressed);
    }

    [Fact]
    public void SettledClimbHand_FistPress_SetsNoPiton()
    {
        // Issue #75: a toggle-latched wall hand has held the climber for long; its unlatch squeeze closes the index on
        // the trigger, which must not be a deliberate piton press.
        var t = new UseTrigger();
        for (int i = 0; i < 60; i++)
            t.Update(false, true, false, Dt);
        t.Update(true, true, false, Dt, fist: true);
        Assert.False(t.ClimbPress);
        for (int i = 0; i < 60; i++)
            t.Update(true, true, false, Dt);
        Assert.False(t.Piton);
    }

    [Fact]
    public void FistPress_IsThePressTheFistMuted()
    {
        // Issue #48's gap: a press lost to the fist window gets a light cue, so it must be told apart.
        var t = new UseTrigger();
        t.Update(true, false, true, Dt, fist: true);
        Assert.True(t.FistPress);
        t.Update(true, false, true, Dt, fist: true);
        Assert.False(t.FistPress);
        t.Update(false, false, true, Dt);
        t.Update(true, false, true, Dt);
        Assert.False(t.FistPress);
    }

    [Fact]
    public void FistPress_NotForATriggerHeldBeforeTheGrip()
    {
        var t = new UseTrigger();
        t.Update(true, false, true, Dt);
        t.Update(true, false, true, Dt, fist: true);
        Assert.False(t.FistPress);
        Assert.False(t.Down);
    }
}
