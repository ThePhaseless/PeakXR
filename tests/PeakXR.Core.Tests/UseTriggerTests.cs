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
        Assert.False(t.Refused);
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
    public void PressWithTheGrip_NotRefused()
    {
        var t = new UseTrigger();
        t.Update(false, true, false, Dt);
        t.Update(true, true, false, Dt);
        Assert.False(t.Refused);
    }

    [Fact]
    public void PressAfterTheHandSettledOnTheWall_Refused()
    {
        var t = new UseTrigger();
        for (float s = 0f; s < UseTrigger.SettleSeconds + Dt; s += Dt)
            t.Update(false, true, false, Dt);
        t.Update(true, true, false, Dt);
        Assert.True(t.Refused);
        Assert.False(t.Down);
        t.Update(true, true, false, Dt);
        Assert.False(t.Refused);
    }
}
