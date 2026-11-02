namespace PeakXR.Core.Tests;

public class SheetFadeTests
{
    private const float Start = SheetFade.FadeStart;
    private const float End = SheetFade.FadeEnd;

    /// <summary>A poison sheet well into its size curve, and the material's real alpha numbers: SporeSmoke has
    /// _Opacity 1 and _FinalAlphaRemap (0, 1), Gloom Clouds 0.22 and (0, 0.387).</summary>
    private const float PoisonSize = 7f;

    private static float Removed(float a, float fade, float x = 0f, float y = 1f) =>
        SheetFade.Opacity(a, x, y) - SheetFade.Opacity(a * fade, x, y);

    /// <summary>What <c>SmokeNearFade</c> adds up for sheets straight ahead of the head.</summary>
    private static float LostAhead(float a, params float[] depths)
    {
        float lost = 0f;
        foreach (float z in depths)
        {
            float share = SheetFade.ViewShare(0.5f, PoisonSize, z);
            lost = SheetFade.Lost(lost, Removed(a, SheetFade.Alpha(z, Start, End)) * share);
        }
        return lost;
    }

    [Fact]
    public void Alpha_is_zero_at_and_under_start()
    {
        Assert.Equal(0f, SheetFade.Alpha(0.5f, Start, End));
        Assert.Equal(0f, SheetFade.Alpha(0.1f, Start, End));
        Assert.Equal(0f, SheetFade.Alpha(0f, Start, End));
    }

    [Fact]
    public void Alpha_is_one_at_and_past_end()
    {
        Assert.Equal(1f, SheetFade.Alpha(1.5f, Start, End));
        Assert.Equal(1f, SheetFade.Alpha(5f, Start, End));
    }

    [Fact]
    public void Alpha_rises_monotonically_between_start_and_end()
    {
        float previous = 0f;
        for (float z = 0.5f; z <= 1.5f; z += 0.05f)
        {
            float alpha = SheetFade.Alpha(z, Start, End);
            Assert.True(alpha >= previous, $"at z {z}: {alpha} under {previous}");
            previous = alpha;
        }
        Assert.Equal(0.5f, SheetFade.Alpha(1f, Start, End), 3);
    }

    [Theory]
    [InlineData(0.2f)]
    [InlineData(0.8f)]
    [InlineData(1.2f)]
    [InlineData(4f)]
    public void Alpha_treats_a_sheet_behind_the_head_like_one_ahead(float z)
    {
        Assert.Equal(SheetFade.Alpha(z, Start, End), SheetFade.Alpha(-z, Start, End));
    }

    [Fact]
    public void Haze_is_zero_with_nothing_lost_and_at_the_cap_with_all_lost()
    {
        Assert.Equal(0f, SheetFade.Haze(0f));
        Assert.Equal(SheetFade.MaxHaze, SheetFade.Haze(1f), 4);
    }

    [Fact]
    public void Haze_never_exceeds_the_opacity_lost_or_the_cap()
    {
        for (float lost = 0f; lost <= 1f; lost += 0.01f)
        {
            float haze = SheetFade.Haze(lost);
            Assert.True(haze <= lost + 1e-5f, $"at lost {lost}: {haze}");
            Assert.True(haze <= SheetFade.MaxHaze + 1e-5f, $"at lost {lost}: {haze}");
        }
    }

    [Fact]
    public void Haze_rises_monotonically_with_what_is_lost()
    {
        float previous = 0f;
        for (float lost = 0f; lost <= 1f; lost += 0.01f)
        {
            float haze = SheetFade.Haze(lost);
            Assert.True(haze >= previous, $"at lost {lost}: {haze} under {previous}");
            previous = haze;
        }
    }

    [Fact]
    public void Opacity_follows_the_remap_after_vertex_alpha()
    {
        Assert.Equal(0.36f, SheetFade.Opacity(0.36f, 0f, 1f), 4);
        Assert.Equal(0f, SheetFade.Opacity(0.36f, 0.36f, 0.72f), 4);
        Assert.Equal(0.5f, SheetFade.Opacity(0.54f, 0.36f, 0.72f), 4);
        Assert.Equal(1f, SheetFade.Opacity(0.9f, 0.36f, 0.72f), 4);
        Assert.Equal(0f, SheetFade.Opacity(0.1f, 0.36f, 0.72f), 4);
    }

    [Fact]
    public void Opacity_of_a_gas_sheet_stays_visible_with_the_final_remap_and_not_the_texture_remap()
    {
        // Gloom Clouds at full alpha: _Opacity 0.22 over _FinalAlphaRemap (0, 0.387). Its _AlphaRemap (0.614, 1) cuts the
        // texture; taken here it would say the gas never draws.
        Assert.Equal(0.568f, SheetFade.Opacity(1f * 0.22f, 0f, 0.387f), 3);
        Assert.Equal(0f, SheetFade.Opacity(1f * 0.22f, 0.614f, 1f), 3);
    }

    [Fact]
    public void Lost_of_no_sheets_is_zero()
    {
        Assert.Equal(0f, LostAhead(1f));
    }

    [Fact]
    public void Lost_gathers_sheets_as_one_minus_the_product_of_what_is_left()
    {
        float lost = SheetFade.Lost(SheetFade.Lost(0f, 0.5f), 0.5f);

        Assert.Equal(0.75f, lost, 4);
    }

    [Fact]
    public void Lost_stays_in_range_whatever_a_sheet_reports()
    {
        Assert.Equal(1f, SheetFade.Lost(0.5f, 1.5f), 4);
        Assert.Equal(0.5f, SheetFade.Lost(0.5f, -1f), 4);
    }

    [Fact]
    public void A_sheet_under_the_remap_cut_adds_nothing_faded_or_not()
    {
        Assert.Equal(0f, SheetFade.Lost(0f, Removed(0.36f, 0f, 0.36f, 0.72f)), 4);
        Assert.Equal(0f, SheetFade.Lost(0f, Removed(0.36f, 1f, 0.36f, 0.72f)), 4);
    }

    [Fact]
    public void A_dying_sheet_loses_only_what_it_still_draws()
    {
        // Poison's remap is (0, 1): at alpha 0.1 a fully faded sheet took 0.1 away, not 1.
        Assert.Equal(0.1f, SheetFade.Lost(0f, Removed(0.1f, 0f)), 4);
    }

    [Fact]
    public void A_player_looking_out_of_the_cloud_gets_the_cap_where_the_fade_took_every_sheet_ahead()
    {
        // 2.7 m off the centre of a 3.2 m shell, looking outward: every sheet ahead is under 0.5 m.
        float haze = SheetFade.Haze(LostAhead(0.95f, 0.1f, 0.25f, 0.4f, 0.45f));

        Assert.Equal(SheetFade.MaxHaze, haze, 2);
    }

    [Fact]
    public void The_same_cloud_with_no_sheet_near_gets_no_haze()
    {
        Assert.Equal(0f, SheetFade.Haze(LostAhead(0.95f, 1.5f, 2f, 4f)));
    }

    [Fact]
    public void ViewShare_is_full_for_a_sheet_across_the_view_axis()
    {
        Assert.Equal(1f, SheetFade.ViewShare(1f, PoisonSize, 0.3f));
        Assert.Equal(1f, SheetFade.ViewShare(3.5f, PoisonSize, 0.3f));
    }

    [Fact]
    public void ViewShare_is_zero_for_a_sheet_beside_the_head_that_never_reaches_the_view()
    {
        // Looking along a cloud from outside it: 6 m to the side at 0.4 m ahead, its near edge is 2.5 m off the axis.
        Assert.Equal(0f, SheetFade.ViewShare(6f, PoisonSize, 0.4f));
    }

    [Fact]
    public void ViewShare_falls_from_the_axis_to_the_edge_of_the_view()
    {
        float atAxis = SheetFade.ViewShare(3.5f, PoisonSize, 1f);
        float halfway = SheetFade.ViewShare(3.5f + (SheetFade.ViewTangent / 2f), PoisonSize, 1f);
        float atEdge = SheetFade.ViewShare(3.5f + SheetFade.ViewTangent, PoisonSize, 1f);

        Assert.Equal(1f, atAxis, 4);
        Assert.Equal(0.5f, halfway, 4);
        Assert.Equal(0f, atEdge, 4);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.5f)]
    public void ViewShare_is_zero_on_and_behind_the_eye_plane_off_the_axis(float z)
    {
        Assert.Equal(0f, SheetFade.ViewShare(6f, PoisonSize, z));
    }

    [Fact]
    public void A_sheet_out_of_view_lifts_no_haze_though_it_faded()
    {
        float z = 0.4f;
        float removed = Removed(1f, SheetFade.Alpha(z, Start, End));

        Assert.Equal(1f, removed, 4);
        Assert.Equal(
            0f,
            SheetFade.Haze(SheetFade.Lost(0f, removed * SheetFade.ViewShare(6f, PoisonSize, z)))
        );
    }

    [Fact]
    public void Alpha_at_a_quarter_of_the_way_follows_smoothstep()
    {
        Assert.Equal(0.156f, SheetFade.Alpha(0.75f, Start, End), 3);
    }

    [Fact]
    public void Haze_at_half_lost_is_half_the_cap()
    {
        Assert.Equal(0.35f, SheetFade.Haze(0.5f), 3);
    }

    [Fact]
    public void Haze_at_a_quarter_lost_stays_under_a_straight_line_to_the_cap()
    {
        // At half lost the curve and the line both read 0.35, so only an off-centre value tells them apart.
        Assert.Equal(0.109f, SheetFade.Haze(0.25f), 3);
    }

    [Fact]
    public void ViewShare_uses_the_measured_tangent_of_1_43()
    {
        // 0.715 m past the sheet's edge at 1 m ahead: half of 1.43, written out so the constant cannot drift unseen.
        Assert.Equal(0.5f, SheetFade.ViewShare(3.5f + 0.715f, PoisonSize, 1f), 3);
    }

    [Fact]
    public void DrawnSize_is_the_renderers_cap_at_this_depth_when_that_is_smaller()
    {
        // Spray: _MaxParticleSize 0.5 of the view's height, which is 2 x 0.4 x 1.43 m at 0.4 m ahead.
        Assert.Equal(0.572f, SheetFade.DrawnSize(10f, 0.5f, 0.4f), 3);
    }

    [Fact]
    public void DrawnSize_is_the_sheet_when_the_cap_does_not_reach_it()
    {
        Assert.Equal(7f, SheetFade.DrawnSize(7f, 10f, 0.4f), 3);
        Assert.Equal(7f, SheetFade.DrawnSize(7f, 0.5f, 20f), 3);
    }

    [Fact]
    public void A_capped_sheet_beside_the_view_lifts_no_haze_though_its_full_size_would_reach_it()
    {
        const float z = 0.4f;

        Assert.Equal(1f, SheetFade.ViewShare(2f, 10f, z));
        Assert.Equal(0f, SheetFade.ViewShare(2f, SheetFade.DrawnSize(10f, 0.5f, z), z));
    }
}
