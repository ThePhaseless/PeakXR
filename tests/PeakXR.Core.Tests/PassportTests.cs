namespace PeakXR.Core.Tests;

public class BookPoseTests
{
    static readonly Vector3 Head = Vector3.Zero;
    static readonly Vector3 HeadForward = new(0f, 0f, 1f);

    [Fact]
    public void LeftHand_BookSitsAboveAndToTheRight()
    {
        BookPose.Compute(
            new Vector3(0f, -0.4f, 0.4f),
            Head,
            HeadForward,
            leftHand: true,
            lift: 0.15f,
            side: 0.1f,
            out var pos,
            out _
        );
        TestUtil.Near(new Vector3(0.1f, -0.25f, 0.4f), pos);
    }

    [Fact]
    public void RightHand_BookSitsAboveAndToTheLeft()
    {
        BookPose.Compute(
            new Vector3(0f, -0.4f, 0.4f),
            Head,
            HeadForward,
            leftHand: false,
            lift: 0.15f,
            side: 0.1f,
            out var pos,
            out _
        );
        TestUtil.Near(new Vector3(-0.1f, -0.25f, 0.4f), pos);
    }

    [Fact]
    public void Side_FollowsDirectionFromHeadToHand()
    {
        // Hand off to the head's right (+X): the reader's right is then -Z.
        BookPose.Compute(
            new Vector3(0.4f, -0.4f, 0f),
            Head,
            HeadForward,
            leftHand: true,
            lift: 0f,
            side: 0.1f,
            out var pos,
            out _
        );
        TestUtil.Near(new Vector3(0.4f, -0.4f, -0.1f), pos);
    }

    [Fact]
    public void Rotation_FacesAwayFromHeadWithoutRoll()
    {
        BookPose.Compute(
            new Vector3(0.2f, -0.3f, 0.35f),
            Head,
            HeadForward,
            leftHand: true,
            lift: 0.15f,
            side: 0.1f,
            out var pos,
            out var rot
        );
        var forward = UnityQuat.Rotate(rot, new Vector3(0f, 0f, 1f));
        TestUtil.Near(Vector3.Normalize(pos - Head), forward);
        var right = UnityQuat.Rotate(rot, new Vector3(1f, 0f, 0f));
        TestUtil.Near(0f, right.Y);
    }

    [Fact]
    public void HandStraightBelowHead_UsesHeadForwardForSide()
    {
        BookPose.Compute(
            new Vector3(0f, -0.5f, 0f),
            Head,
            HeadForward,
            leftHand: true,
            lift: 0.1f,
            side: 0.1f,
            out var pos,
            out var rot
        );
        TestUtil.Near(new Vector3(0.1f, -0.4f, 0f), pos);
        Assert.False(float.IsNaN(rot.X) || float.IsNaN(rot.W));
    }

    [Fact]
    public void BookAtTheEyes_FallsBackToHeadForward()
    {
        BookPose.Compute(
            Vector3.Zero,
            Head,
            HeadForward,
            leftHand: true,
            lift: 0f,
            side: 0f,
            out _,
            out var rot
        );
        TestUtil.SameRotation(Quaternion.Identity, rot);
    }
}

public class PageSwipeTests
{
    const float Dt = 1f / 90f;

    static PageSwipe Make() =>
        new PageSwipe
        {
            HalfWidth = 0.2f,
            HalfHeight = 0.15f,
            MinTravel = 0.12f,
            Window = 0.35f,
            Cooldown = 0.5f,
        };

    /// <summary>Moves the hand from x0 to x1 at a constant speed in front of the page; returns every non-zero result.</summary>
    static int Sweep(PageSwipe s, float x0, float x1, float speed, float z = -0.08f, bool blocked = false)
    {
        int frames = (int)MathF.Ceiling(MathF.Abs(x1 - x0) / speed / Dt);
        int result = 0;
        for (int i = 0; i <= frames; i++)
        {
            float x = x0 + (x1 - x0) * i / frames;
            int r = s.Update(new Vector3(x, 0f, z), blocked, Dt);
            if (r != 0)
            {
                Assert.Equal(0, result);
                result = r;
            }
        }
        return result;
    }

    [Fact]
    public void FastRightToLeft_FlipsForward()
    {
        Assert.Equal(1, Sweep(Make(), 0.18f, -0.18f, 1.2f));
    }

    [Fact]
    public void FastLeftToRight_FlipsBack()
    {
        Assert.Equal(-1, Sweep(Make(), -0.18f, 0.18f, 1.2f));
    }

    [Fact]
    public void SlowDrift_DoesNothing()
    {
        Assert.Equal(0, Sweep(Make(), 0.18f, -0.18f, 0.2f));
    }

    [Fact]
    public void FarInFrontOfThePage_DoesNothing()
    {
        Assert.Equal(0, Sweep(Make(), 0.18f, -0.18f, 1.2f, z: -0.5f));
    }

    [Fact]
    public void BehindTheBook_DoesNothing()
    {
        Assert.Equal(0, Sweep(Make(), 0.18f, -0.18f, 1.2f, z: 0.2f));
    }

    [Fact]
    public void Blocked_DoesNothing()
    {
        Assert.Equal(0, Sweep(Make(), 0.18f, -0.18f, 1.2f, blocked: true));
    }

    [Fact]
    public void ShortJiggle_DoesNothing()
    {
        var s = Make();
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(0, Sweep(s, 0.04f, -0.04f, 1.5f));
            Assert.Equal(0, Sweep(s, -0.04f, 0.04f, 1.5f));
        }
    }

    [Fact]
    public void Cooldown_BlocksAnImmediateSecondFlip()
    {
        var s = Make();
        Assert.Equal(1, Sweep(s, 0.18f, -0.18f, 1.2f));
        // Swinging straight back within the cooldown is the return stroke, not a flip.
        Assert.Equal(0, Sweep(s, -0.18f, 0.18f, 3f));
    }

    [Fact]
    public void AfterCooldown_FlipsAgain()
    {
        var s = Make();
        Assert.Equal(1, Sweep(s, 0.18f, -0.18f, 1.2f));
        for (int i = 0; i < 60; i++)
            s.Update(new Vector3(0.18f, 0f, -0.08f), false, Dt);
        Assert.Equal(1, Sweep(s, 0.18f, -0.18f, 1.2f));
    }

    [Fact]
    public void LeavingTheZone_ForgetsTheStroke()
    {
        var s = Make();
        Assert.Equal(0, Sweep(s, 0.18f, 0.08f, 1.2f));
        s.Update(new Vector3(0.08f, 0.6f, -0.08f), false, Dt);
        Assert.Equal(0, Sweep(s, 0.08f, 0.0f, 1.2f));
    }
}

public class PassportPagingTests
{
    [Fact]
    public void Forward_TurnsThePageWhenOneIsLeft()
    {
        Assert.Equal(
            PassportFlip.NextPage,
            PassportPaging.Decide(1, hasNextPage: true, hasPrevPage: false, 2, 7, out _)
        );
    }

    [Fact]
    public void Forward_OnLastPage_OpensNextTab()
    {
        Assert.Equal(
            PassportFlip.Tab,
            PassportPaging.Decide(1, hasNextPage: false, hasPrevPage: true, 2, 7, out var tab)
        );
        Assert.Equal(3, tab);
    }

    [Fact]
    public void Forward_OnLastTab_DoesNothing()
    {
        Assert.Equal(
            PassportFlip.None,
            PassportPaging.Decide(1, hasNextPage: false, hasPrevPage: false, 6, 7, out _)
        );
    }

    [Fact]
    public void Back_TurnsThePageWhenOneIsBefore()
    {
        Assert.Equal(
            PassportFlip.PrevPage,
            PassportPaging.Decide(-1, hasNextPage: true, hasPrevPage: true, 2, 7, out _)
        );
    }

    [Fact]
    public void Back_OnFirstPage_OpensPreviousTab()
    {
        Assert.Equal(
            PassportFlip.Tab,
            PassportPaging.Decide(-1, hasNextPage: true, hasPrevPage: false, 2, 7, out var tab)
        );
        Assert.Equal(1, tab);
    }

    [Fact]
    public void Back_OnFirstTab_DoesNothing()
    {
        Assert.Equal(
            PassportFlip.None,
            PassportPaging.Decide(-1, hasNextPage: false, hasPrevPage: false, 0, 7, out _)
        );
    }

    [Fact]
    public void UnknownTabOrZeroDirection_DoesNothing()
    {
        Assert.Equal(
            PassportFlip.None,
            PassportPaging.Decide(1, hasNextPage: false, hasPrevPage: false, -1, 7, out _)
        );
        Assert.Equal(
            PassportFlip.None,
            PassportPaging.Decide(0, hasNextPage: true, hasPrevPage: true, 2, 7, out _)
        );
    }
}
