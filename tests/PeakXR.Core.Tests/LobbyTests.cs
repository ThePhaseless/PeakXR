namespace PeakXR.Core.Tests;

public class LobbyDockTests
{
    static readonly Vector3 Kiosk = Vector3.Zero;
    const float Top = 1.5f;

    static Vector3 Dock(Vector3 head, out Vector3 facing) =>
        LobbyDock.Pose(Kiosk, Top, Vector3.UnitZ, head, new LobbyDockLimits(), out facing);

    [Fact]
    public void InFrontOfKiosk_FloatsOnPlayerSideAtScreenHeight()
    {
        var pos = Dock(new Vector3(0f, 1.7f, 1.2f), out _);
        TestUtil.Near(new Vector3(0f, 1.3f, 0.45f), pos);
    }

    [Fact]
    public void Facing_PointsFromEyesToPanel()
    {
        var head = new Vector3(0f, 1.7f, 1.2f);
        var pos = Dock(head, out var facing);
        TestUtil.Near(Vector3.Normalize(pos - head), facing);
        Assert.True(facing.Z < 0f && facing.Y < 0f);
        TestUtil.Near(1f, facing.Length());
    }

    [Fact]
    public void FarPlayer_PanelComesToMaxDistance()
    {
        var head = new Vector3(0f, 1.7f, 5f);
        var pos = Dock(head, out _);
        TestUtil.Near(1.3f, head.Z - pos.Z);
        TestUtil.Near(0f, pos.X);
    }

    [Fact]
    public void ClosePlayer_PanelKeepsMinDistance()
    {
        var head = new Vector3(0f, 1.7f, 0.3f);
        var pos = Dock(head, out _);
        TestUtil.Near(0.55f, head.Z - pos.Z);
    }

    [Fact]
    public void DiagonalApproach_StaysOnKioskToHeadLine()
    {
        var head = new Vector3(3f, 1.7f, 4f);
        var pos = Dock(head, out _);
        var flat = new Vector2(pos.X, pos.Z);
        TestUtil.Near(1.3f, Vector2.Distance(new Vector2(3f, 4f), flat));
        TestUtil.Near(0f, flat.X * 4f - flat.Y * 3f, 1e-3f);
    }

    [Fact]
    public void HeadAboveKioskCentre_UsesKioskForward()
    {
        var pos = LobbyDock.Pose(
            Kiosk,
            Top,
            Vector3.UnitX,
            new Vector3(0f, 1.7f, 0f),
            new LobbyDockLimits(),
            out var facing
        );
        TestUtil.Near(-0.55f, pos.X);
        TestUtil.Near(0f, pos.Z);
        Assert.True(facing.X < 0f);
    }

    [Fact]
    public void DegenerateForward_StillGivesUnitFacing()
    {
        LobbyDock.Pose(
            Kiosk,
            Top,
            Vector3.UnitY,
            new Vector3(0f, 1.3f, 0f),
            new LobbyDockLimits { MinDistance = 0f, StandOff = 0f },
            out var facing
        );
        TestUtil.Near(1f, facing.Length());
    }

    [Fact]
    public void TallKiosk_ClampsToJustAboveEyes()
    {
        var pos = LobbyDock.Pose(
            Kiosk,
            3f,
            Vector3.UnitZ,
            new Vector3(0f, 1.7f, 1.2f),
            new LobbyDockLimits(),
            out _
        );
        TestUtil.Near(1.75f, pos.Y);
    }

    [Fact]
    public void SeatedPlayer_PanelDropsWithEyes()
    {
        var pos = Dock(new Vector3(0f, 1.0f, 1.2f), out _);
        TestUtil.Near(1.05f, pos.Y);
    }

    [Fact]
    public void LowKiosk_ClampsToMaxBelowEyes()
    {
        var pos = LobbyDock.Pose(
            Kiosk,
            0.5f,
            Vector3.UnitZ,
            new Vector3(0f, 1.7f, 1.2f),
            new LobbyDockLimits(),
            out _
        );
        TestUtil.Near(1.1f, pos.Y);
    }

    [Fact]
    public void MinAboveMax_DoesNotThrow()
    {
        var pos = LobbyDock.Pose(
            Kiosk,
            Top,
            Vector3.UnitZ,
            new Vector3(0f, 1.7f, 3f),
            new LobbyDockLimits { MinDistance = 1f, MaxDistance = 0.5f },
            out _
        );
        TestUtil.Near(1f, 3f - pos.Z);
    }

    [Fact]
    public void NullLimits_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = LobbyDock.Pose(Kiosk, Top, Vector3.UnitZ, Vector3.One, null, out _);
        });

    [Fact]
    public void Anchor_UsesBoundsCentreAndTop()
    {
        LobbyDock.Anchor(
            new Vector3(10f, 0f, 10f),
            new Vector3(-0.3f, 0f, -0.2f),
            new Vector3(0.3f, 1.4f, 0.2f),
            out var center,
            out var top
        );
        TestUtil.Near(new Vector3(0f, 0.7f, 0f), center);
        TestUtil.Near(1.4f, top);
    }

    [Fact]
    public void Anchor_EmptyBounds_FallsBackToPivot()
    {
        var pivot = new Vector3(2f, 1f, 3f);
        LobbyDock.Anchor(pivot, pivot, pivot, out var center, out var top);
        TestUtil.Near(pivot, center);
        TestUtil.Near(1f + LobbyDock.FallbackHeight, top);
    }

    [Fact]
    public void Anchor_HugeBounds_FallsBackToPivot()
    {
        var pivot = new Vector3(2f, 1f, 3f);
        LobbyDock.Anchor(
            pivot,
            new Vector3(-20f, 0f, -20f),
            new Vector3(20f, 2f, 20f),
            out var center,
            out var top
        );
        TestUtil.Near(pivot, center);
        TestUtil.Near(1f + LobbyDock.FallbackHeight, top);
    }
}
