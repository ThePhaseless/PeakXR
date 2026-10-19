namespace PeakXR.Core.Tests;

public class HeightCalibrationTests
{
    [Fact]
    public void Standing_MapsRealEyeToAvatarEye()
    {
        TestUtil.Near(-0.1f, HeightCalibration.YOffset(HeightMode.Standing, 1.7f, 1.6f), 1e-6f);
    }

    [Fact]
    public void Seated_MapsSeatedHeadToAvatarStandingEye()
    {
        TestUtil.Near(0.4f, HeightCalibration.YOffset(HeightMode.Seated, 1.2f, 1.6f), 1e-6f);
    }

    [Fact]
    public void ImplausibleMeasurement_UsesModeDefault()
    {
        TestUtil.Near(1.6f - HeightCalibration.DefaultStandingEyeHeight, HeightCalibration.YOffset(HeightMode.Standing, 0f, 1.6f), 1e-6f);
        TestUtil.Near(1.6f - HeightCalibration.DefaultSeatedEyeHeight, HeightCalibration.YOffset(HeightMode.Seated, float.NaN, 1.6f), 1e-6f);
        TestUtil.Near(1.6f - HeightCalibration.DefaultStandingEyeHeight, HeightCalibration.YOffset(HeightMode.Standing, 4f, 1.6f), 1e-6f);
    }
}
