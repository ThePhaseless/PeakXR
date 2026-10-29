using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeakXR.Core;

/// <summary>
/// The box around what each item draws lying on the ground, in its own frame (root scale kept), from its real mesh
/// vertices (docs/research/item-grab-shapes.csv, <c>vis_lo</c> and <c>vis_hi</c>: preview hands and a backpack's worn
/// mesh left out, as the game hides them). The game's meshes are not readable at runtime (6 of 346 have Read/Write),
/// and a box of each part's bounds overstates a rotated part: the rope spool's lowest corner lands 19 cm under its
/// mesh, so it would hover that far over a table.
/// </summary>
public static class ItemDrawnBox
{
    /// <summary>Its drawn box's low and high corners, false for an item the data does not have (a game update).</summary>
    public static bool TryGet(string prefab, out Vector3 lo, out Vector3 hi)
    {
        bool known = Boxes.TryGetValue(prefab ?? "", out var box);
        lo = box.Lo;
        hi = box.Hi;
        return known;
    }

    /// <summary>By prefab name, generated from docs/research/item-grab-shapes.csv.</summary>
    private static readonly Dictionary<string, (Vector3 Lo, Vector3 Hi)> Boxes = new(
        StringComparer.Ordinal
    )
    {
        ["Airplane Food"] = (new(-0.386f, -0.181f, -0.046f), new(0.386f, 0.308f, 0.362f)),
        ["AloeVera"] = (new(-0.382f, -0.077f, -0.404f), new(0.386f, 0.564f, 0.471f)),
        ["Amulet_Clone"] = (new(-0.315f, -0.073f, -0.028f), new(0.314f, 0.304f, 0.408f)),
        ["Amulet_Healing"] = (new(-0.228f, -0.131f, -0.052f), new(0.220f, 0.289f, 0.414f)),
        ["Amulet_InfiniteStamina"] = (new(-0.291f, -0.032f, 0.019f), new(0.261f, 0.332f, 0.417f)),
        ["Amulet_SuperJump"] = (new(-0.207f, -0.049f, 0.028f), new(0.166f, 0.319f, 0.410f)),
        ["AncientIdol"] = (new(-0.433f, -0.820f, -0.153f), new(0.493f, 0.463f, 0.564f)),
        ["Anti-Rope Spool"] = (new(-0.124f, -0.262f, -0.202f), new(0.125f, 0.259f, 0.392f)),
        ["AntiZooka"] = (new(-0.277f, -0.209f, -1.310f), new(0.217f, 0.569f, 1.145f)),
        ["Antidote"] = (new(-0.163f, -0.164f, 0.074f), new(0.163f, 0.464f, 0.404f)),
        ["Apple Berry Green"] = (new(-0.212f, -0.376f, -0.247f), new(0.209f, 0.031f, 0.212f)),
        ["Apple Berry Red"] = (new(-0.212f, -0.376f, -0.247f), new(0.209f, 0.031f, 0.212f)),
        ["Apple Berry Yellow"] = (new(-0.212f, -0.376f, -0.247f), new(0.209f, 0.031f, 0.212f)),
        ["Backpack"] = (new(-0.460f, -0.152f, -0.452f), new(0.460f, 0.744f, 0.160f)),
        ["Balloon"] = (new(-0.273f, -0.435f, -0.273f), new(0.273f, 0.273f, 0.273f)),
        ["BalloonBunch"] = (new(-0.374f, -0.323f, -0.358f), new(0.374f, 0.204f, 0.347f)),
        ["Bandages"] = (new(-0.143f, -0.205f, 0.009f), new(0.136f, 0.211f, 0.339f)),
        ["Basketball"] = (new(-0.324f, -0.330f, -0.329f), new(0.324f, 0.330f, 0.329f)),
        ["Beehive"] = (new(-0.340f, -0.194f, -0.327f), new(0.341f, 0.590f, 0.340f)),
        ["Berrynana Blue"] = (new(-0.197f, -0.515f, -0.175f), new(0.268f, 0.028f, 0.221f)),
        ["Berrynana Brown"] = (new(-0.197f, -0.515f, -0.175f), new(0.268f, 0.028f, 0.221f)),
        ["Berrynana Peel Blue Variant"] = (
            new(-0.331f, -0.362f, -0.205f),
            new(0.296f, 0.134f, 0.329f)
        ),
        ["Berrynana Peel Brown Variant"] = (
            new(-0.331f, -0.362f, -0.205f),
            new(0.296f, 0.134f, 0.329f)
        ),
        ["Berrynana Peel Pink Variant"] = (
            new(-0.331f, -0.362f, -0.205f),
            new(0.296f, 0.134f, 0.329f)
        ),
        ["Berrynana Peel Yellow"] = (new(-0.331f, -0.362f, -0.205f), new(0.296f, 0.134f, 0.329f)),
        ["Berrynana Pink"] = (new(-0.197f, -0.515f, -0.175f), new(0.268f, 0.028f, 0.221f)),
        ["Berrynana Yellow"] = (new(-0.197f, -0.515f, -0.175f), new(0.268f, 0.028f, 0.221f)),
        ["BingBong"] = (new(-0.673f, -0.758f, -0.082f), new(0.521f, 0.430f, 0.381f)),
        ["BingBong_Prop Variant"] = (new(-0.517f, -0.734f, -0.129f), new(0.454f, 0.229f, 0.231f)),
        ["Binoculars"] = (new(-0.314f, -0.137f, -0.268f), new(0.314f, 0.159f, 0.269f)),
        ["Binoculars_Prop"] = (new(-0.314f, -0.140f, -0.268f), new(0.314f, 0.163f, 0.269f)),
        ["BookOfBones"] = (new(-0.259f, -0.261f, -0.022f), new(0.209f, 0.220f, 0.262f)),
        ["BounceShroom"] = (new(-0.299f, -0.090f, -0.292f), new(0.299f, 0.230f, 0.307f)),
        ["Bugfix"] = (new(-0.102f, -0.065f, -0.099f), new(0.102f, 0.272f, 0.049f)),
        ["Bugle"] = (new(-0.200f, -0.452f, -0.031f), new(0.131f, -0.028f, 0.743f)),
        ["Bugle_Magic"] = (new(-0.250f, -0.451f, -0.023f), new(0.185f, 0.003f, 0.748f)),
        ["Bugle_Prop Variant"] = (new(-0.200f, -0.452f, -0.031f), new(0.131f, -0.028f, 0.743f)),
        ["Bugle_Scoutmaster Variant"] = (
            new(-0.212f, -0.454f, -0.052f),
            new(0.139f, -0.004f, 0.767f)
        ),
        ["C_Bishop B"] = (new(-0.275f, -0.631f, -0.204f), new(0.260f, 0.701f, 0.187f)),
        ["C_Bishop W"] = (new(-0.275f, -0.631f, -0.204f), new(0.260f, 0.701f, 0.187f)),
        ["C_Bishop_f"] = (new(-0.275f, -0.654f, -0.258f), new(0.260f, 0.577f, 0.186f)),
        ["C_Bishop_f Variant"] = (new(-0.275f, -0.631f, -0.204f), new(0.260f, 0.701f, 0.187f)),
        ["C_Bishop_m"] = (new(-0.275f, -0.650f, -0.256f), new(0.260f, 0.582f, 0.187f)),
        ["C_Bishop_m Variant"] = (new(-0.275f, -0.650f, -0.256f), new(0.260f, 0.582f, 0.187f)),
        ["C_King"] = (new(-0.275f, -0.648f, -0.272f), new(0.260f, 0.625f, 0.171f)),
        ["C_King B"] = (new(-0.346f, -0.632f, -0.265f), new(0.331f, 0.907f, 0.230f)),
        ["C_King Variant"] = (new(-0.346f, -0.632f, -0.265f), new(0.331f, 0.907f, 0.230f)),
        ["C_King W"] = (new(-0.346f, -0.632f, -0.265f), new(0.331f, 0.907f, 0.230f)),
        ["C_Knight"] = (new(-0.179f, -0.519f, -0.371f), new(0.249f, 0.453f, 0.235f)),
        ["C_Knight B"] = (new(-0.223f, -0.634f, -0.380f), new(0.259f, 0.563f, 0.302f)),
        ["C_Knight Variant"] = (new(-0.223f, -0.634f, -0.380f), new(0.259f, 0.563f, 0.302f)),
        ["C_Knight W"] = (new(-0.223f, -0.634f, -0.380f), new(0.259f, 0.563f, 0.302f)),
        ["C_Pawn B"] = (new(-0.191f, -0.612f, -0.178f), new(0.194f, 0.218f, 0.207f)),
        ["C_Pawn W"] = (new(-0.191f, -0.612f, -0.178f), new(0.194f, 0.218f, 0.207f)),
        ["C_Pawn_f"] = (new(-0.274f, -0.642f, -0.275f), new(0.260f, 0.374f, 0.168f)),
        ["C_Pawn_f Variant"] = (new(-0.274f, -0.642f, -0.275f), new(0.260f, 0.374f, 0.168f)),
        ["C_Pawn_m"] = (new(-0.275f, -0.644f, -0.274f), new(0.260f, 0.372f, 0.169f)),
        ["C_Pawn_m Variant"] = (new(-0.191f, -0.612f, -0.178f), new(0.194f, 0.218f, 0.207f)),
        ["C_Queen"] = (new(-0.275f, -0.650f, -0.262f), new(0.259f, 0.505f, 0.186f)),
        ["C_Queen B"] = (new(-0.315f, -0.630f, -0.336f), new(0.300f, 0.730f, 0.243f)),
        ["C_Queen Variant"] = (new(-0.315f, -0.630f, -0.336f), new(0.300f, 0.730f, 0.243f)),
        ["C_Queen W"] = (new(-0.315f, -0.630f, -0.336f), new(0.300f, 0.730f, 0.243f)),
        ["C_Rook B"] = (new(-0.260f, -0.621f, -0.256f), new(0.245f, 0.443f, 0.165f)),
        ["C_Rook W"] = (new(-0.260f, -0.621f, -0.256f), new(0.245f, 0.443f, 0.165f)),
        ["C_Rook_f"] = (new(-0.275f, -0.644f, -0.273f), new(0.260f, 0.510f, 0.170f)),
        ["C_Rook_f Variant"] = (new(-0.275f, -0.644f, -0.273f), new(0.260f, 0.510f, 0.170f)),
        ["C_Rook_m"] = (new(-0.275f, -0.643f, -0.272f), new(0.260f, 0.510f, 0.171f)),
        ["C_Rook_m Variant"] = (new(-0.260f, -0.621f, -0.256f), new(0.245f, 0.443f, 0.165f)),
        ["CactusBall"] = (new(-0.562f, -0.513f, -0.430f), new(0.352f, 0.390f, 0.484f)),
        ["Candle"] = (new(-0.163f, -0.144f, -0.165f), new(0.185f, 0.331f, 0.165f)),
        ["ChainShooter"] = (new(-0.147f, 0.011f, -0.070f), new(0.154f, 0.309f, 0.804f)),
        ["Cheat Compass"] = (new(-0.190f, -0.156f, -0.149f), new(0.190f, 0.336f, 0.122f)),
        ["Cheat Compass 1"] = (new(-0.190f, -0.156f, -0.149f), new(0.190f, 0.336f, 0.122f)),
        ["ClimbingChalk"] = (new(-0.140f, -0.140f, -0.140f), new(0.140f, 0.140f, 0.140f)),
        ["ClimbingSpike"] = (new(-0.282f, -0.308f, 0.080f), new(0.284f, 0.434f, 0.701f)),
        ["CloudFungus"] = (new(-0.347f, -0.310f, -0.349f), new(0.334f, 0.040f, 0.275f)),
        ["Clusterberry Black"] = (new(-0.209f, -0.383f, -0.174f), new(0.233f, 0.052f, 0.244f)),
        ["Clusterberry Red"] = (new(-0.209f, -0.383f, -0.174f), new(0.233f, 0.053f, 0.244f)),
        ["Clusterberry Yellow"] = (new(-0.209f, -0.383f, -0.174f), new(0.233f, 0.053f, 0.244f)),
        ["Clusterberry_UNUSED"] = (new(-0.209f, -0.472f, -0.229f), new(0.233f, 0.036f, 0.188f)),
        ["Compass"] = (new(-0.190f, -0.028f, -0.102f), new(0.190f, 0.486f, 0.106f)),
        ["Cure-All"] = (new(-0.216f, -0.346f, -0.036f), new(0.216f, 0.281f, 0.396f)),
        ["Cursed Skull"] = (new(-0.299f, -0.272f, -0.270f), new(0.293f, 0.325f, 0.349f)),
        ["Dynamite"] = (new(-0.233f, -0.218f, -0.079f), new(0.200f, 0.673f, 0.309f)),
        ["EarlyWorm"] = (new(-0.383f, -0.036f, -0.299f), new(-0.309f, 0.168f, 0.412f)),
        ["Egg"] = (new(-0.650f, -0.079f, -0.642f), new(0.562f, 0.242f, 0.612f)),
        ["EggRaven"] = (new(-0.295f, 0.003f, -0.360f), new(0.312f, 0.164f, 0.267f)),
        ["EggTurkey"] = (new(-0.440f, -0.513f, -0.518f), new(0.428f, 0.408f, 0.311f)),
        ["Energy Drink"] = (new(-0.081f, -0.093f, -0.058f), new(0.075f, 0.304f, 0.087f)),
        ["Fannypack"] = (new(-0.368f, 0.042f, -0.452f), new(0.381f, 0.336f, 0.087f)),
        ["FireWood"] = (new(-0.199f, -0.393f, -0.141f), new(0.255f, 0.450f, 0.073f)),
        ["FirstAidKit"] = (new(-0.282f, -0.083f, 0.009f), new(0.282f, 0.442f, 0.331f)),
        ["Flag_Plantable_Checkpoint"] = (
            new(-0.222f, -0.267f, -0.070f),
            new(1.148f, 0.727f, 0.220f)
        ),
        ["Flare"] = (new(-0.045f, -0.000f, 0.090f), new(0.045f, 0.693f, 0.180f)),
        ["FortifiedMilk"] = (new(-0.162f, -0.129f, -0.136f), new(0.162f, 0.405f, 0.188f)),
        ["Frisbee"] = (new(-0.313f, -0.022f, -0.313f), new(0.313f, 0.052f, 0.313f)),
        ["Frog"] = (new(-0.863f, -0.073f, -0.660f), new(0.844f, 1.017f, 0.631f)),
        ["FrogLegs"] = (new(-0.327f, -0.476f, -0.095f), new(0.335f, 0.198f, 0.242f)),
        ["Glider"] = (new(-1.478f, 0.299f, -1.825f), new(1.619f, 0.923f, 0.389f)),
        ["Glizzy"] = (new(-0.101f, -0.208f, -0.078f), new(0.510f, 0.841f, 0.101f)),
        ["Glizzy_CattailVariant"] = (new(-0.101f, -0.208f, -0.035f), new(0.539f, 1.201f, 0.138f)),
        ["Granola Bar"] = (new(-0.253f, -0.219f, -0.040f), new(0.261f, 0.308f, 0.121f)),
        ["Guidebook"] = (new(-0.230f, -0.247f, -0.028f), new(0.209f, 0.268f, 0.027f)),
        ["GuidebookPage"] = (new(-0.187f, -0.244f, -0.010f), new(0.199f, 0.244f, -0.010f)),
        ["GuidebookPageScroll Variant"] = (
            new(-0.075f, 0.029f, -0.080f),
            new(0.068f, 0.503f, 0.064f)
        ),
        ["GuidebookPage_0_Intro"] = (new(-0.187f, -0.244f, -0.010f), new(0.199f, 0.244f, -0.010f)),
        ["GuidebookPage_10_Sleepy"] = (
            new(-0.187f, -0.244f, -0.010f),
            new(0.199f, 0.244f, -0.010f)
        ),
        ["GuidebookPage_11_Awake"] = (new(-0.187f, -0.244f, -0.010f), new(0.199f, 0.244f, -0.010f)),
        ["GuidebookPage_12_Crashout"] = (
            new(-0.187f, -0.244f, -0.010f),
            new(0.199f, 0.244f, -0.010f)
        ),
        ["GuidebookPage_13_FirstTeams"] = (
            new(-0.187f, -0.244f, -0.010f),
            new(0.199f, 0.244f, -0.010f)
        ),
        ["GuidebookPage_1_Mushrooms"] = (
            new(-0.187f, -0.244f, -0.010f),
            new(0.199f, 0.244f, -0.010f)
        ),
        ["GuidebookPage_2_Campfire"] = (
            new(-0.187f, -0.244f, -0.010f),
            new(0.199f, 0.244f, -0.010f)
        ),
        ["GuidebookPage_3_Revival"] = (
            new(-0.187f, -0.244f, -0.010f),
            new(0.199f, 0.244f, -0.010f)
        ),
        ["GuidebookPage_4_Poison"] = (new(-0.187f, -0.244f, -0.010f), new(0.199f, 0.244f, -0.010f)),
        ["GuidebookPage_5_Zombies"] = (
            new(-0.187f, -0.244f, -0.010f),
            new(0.199f, 0.244f, -0.010f)
        ),
        ["GuidebookPage_6_BodyHeat"] = (
            new(-0.187f, -0.244f, -0.010f),
            new(0.199f, 0.244f, -0.010f)
        ),
        ["GuidebookPage_7_BurningSun"] = (
            new(-0.187f, -0.244f, -0.010f),
            new(0.199f, 0.244f, -0.010f)
        ),
        ["GuidebookPage_8_Magma"] = (new(-0.187f, -0.244f, -0.010f), new(0.199f, 0.244f, -0.010f)),
        ["GuidebookPage_9_Gloom"] = (new(-0.187f, -0.244f, -0.010f), new(0.199f, 0.244f, -0.010f)),
        ["HealingDart Variant"] = (new(-0.062f, -0.062f, -0.268f), new(0.062f, 0.062f, 0.741f)),
        ["HealingPuffShroom"] = (new(-0.288f, -0.324f, -0.274f), new(0.314f, 0.248f, 0.333f)),
        ["Heat Pack"] = (new(-0.226f, -0.119f, 0.096f), new(0.226f, 0.275f, 0.278f)),
        ["Item_Coconut"] = (new(-0.342f, -0.402f, -0.261f), new(0.299f, 0.435f, 0.396f)),
        ["Item_Coconut_half"] = (new(-0.330f, -0.448f, -0.094f), new(0.353f, 0.239f, 0.441f)),
        ["Item_Honeycomb"] = (new(-0.341f, -0.410f, -0.339f), new(0.352f, 0.111f, 0.345f)),
        ["Jetpack"] = (new(-0.460f, -0.199f, -0.488f), new(0.460f, 0.673f, 0.206f)),
        ["Kingberry Green"] = (new(-0.253f, -0.527f, -0.253f), new(0.253f, 0.129f, 0.253f)),
        ["Kingberry Purple"] = (new(-0.253f, -0.527f, -0.253f), new(0.253f, 0.129f, 0.253f)),
        ["Kingberry Yellow"] = (new(-0.253f, -0.527f, -0.253f), new(0.253f, 0.129f, 0.253f)),
        ["Lantern"] = (new(-0.212f, -0.445f, -0.184f), new(0.212f, 0.282f, 0.184f)),
        ["Lantern_Faerie"] = (new(-0.212f, -0.183f, -0.212f), new(0.212f, 0.513f, 0.212f)),
        ["Lollipop"] = (new(-0.401f, -0.974f, 0.045f), new(0.407f, 0.486f, 0.216f)),
        ["Lollipop_Prop"] = (new(-0.259f, -0.716f, 0.074f), new(0.263f, 0.227f, 0.184f)),
        ["MagicBean"] = (new(-0.099f, -0.092f, -0.113f), new(0.141f, 0.260f, 0.095f)),
        ["Mandrake"] = (new(-0.656f, -0.490f, -0.616f), new(0.686f, 0.999f, 0.582f)),
        ["Mandrake_Hidden"] = (new(-0.646f, -0.755f, -0.589f), new(0.666f, 0.734f, 0.638f)),
        ["Marshmallow"] = (new(-0.101f, -0.208f, -0.098f), new(0.363f, 0.735f, 0.134f)),
        ["MedicinalRoot"] = (new(-0.646f, -0.755f, -0.589f), new(0.666f, 0.734f, 0.638f)),
        ["Megaphone"] = (new(-0.214f, -0.328f, -0.197f), new(0.216f, 0.215f, 0.291f)),
        ["Mushroom Chubby"] = (new(-0.193f, -0.107f, -0.193f), new(0.193f, 0.464f, 0.193f)),
        ["Mushroom Cluster"] = (new(-0.168f, -0.070f, -0.201f), new(0.141f, 0.482f, 0.167f)),
        ["Mushroom Cluster Poison"] = (new(-0.168f, -0.070f, -0.201f), new(0.141f, 0.482f, 0.167f)),
        ["Mushroom Glow"] = (new(-0.168f, -0.183f, -0.202f), new(0.146f, 0.258f, 0.173f)),
        ["Mushroom Lace"] = (new(-0.279f, -0.044f, -0.199f), new(0.220f, 0.445f, 0.297f)),
        ["Mushroom Lace Poison"] = (new(-0.279f, -0.044f, -0.198f), new(0.220f, 0.445f, 0.298f)),
        ["Mushroom Normie"] = (new(-0.253f, -0.059f, -0.247f), new(0.253f, 0.457f, 0.260f)),
        ["Mushroom Normie Poison"] = (new(-0.253f, -0.059f, -0.247f), new(0.253f, 0.457f, 0.260f)),
        ["Napberry"] = (new(-0.276f, -0.367f, -0.206f), new(0.316f, 0.371f, 0.150f)),
        ["NestEgg"] = (new(-0.330f, -0.298f, -0.330f), new(0.330f, 0.483f, 0.330f)),
        ["NestEgg_Raven"] = (new(-0.248f, -0.201f, -0.307f), new(0.248f, 0.385f, 0.189f)),
        ["PandorasBox"] = (new(-0.236f, -0.202f, 0.125f), new(0.236f, 0.217f, 0.335f)),
        ["Parachute"] = (new(-0.256f, -0.187f, -0.033f), new(0.256f, 0.293f, 0.330f)),
        ["Parasol"] = (new(-0.300f, -0.253f, -0.430f), new(0.201f, 1.513f, 0.143f)),
        ["Parasol_Roots Variant"] = (new(-0.300f, -0.253f, -0.430f), new(0.201f, 1.513f, 0.143f)),
        ["Passport"] = (new(-0.141f, -0.193f, -0.018f), new(0.141f, 0.193f, 0.018f)),
        ["Pepper Berry"] = (new(-0.209f, -0.567f, -0.209f), new(0.266f, 0.076f, 0.139f)),
        ["Pirate Compass"] = (new(-0.238f, -0.028f, -0.102f), new(0.238f, 0.519f, 0.167f)),
        ["PortableStovetopItem"] = (new(-0.331f, -0.385f, 0.012f), new(0.331f, -0.049f, 0.674f)),
        ["Prickleberry_Gold"] = (new(-0.370f, -0.215f, -0.425f), new(0.377f, 0.493f, 0.281f)),
        ["Prickleberry_Red"] = (new(-0.370f, -0.215f, -0.373f), new(0.377f, 0.477f, 0.337f)),
        ["RescueHook"] = (new(-0.273f, -0.349f, -0.236f), new(0.328f, 0.385f, 1.019f)),
        ["RescueHook_Infinite"] = (new(-0.273f, -0.349f, -0.236f), new(0.328f, 0.385f, 1.019f)),
        ["RitualDagger"] = (new(-0.144f, -0.346f, -0.261f), new(0.087f, 1.093f, 0.082f)),
        ["Rocketpack"] = (new(-0.334f, -0.096f, -0.544f), new(0.346f, 0.751f, 0.164f)),
        ["RopeShooter"] = (new(-0.200f, -0.050f, -0.153f), new(0.181f, 0.332f, 1.145f)),
        ["RopeShooterAnti"] = (new(-0.200f, -0.050f, -0.153f), new(0.181f, 0.332f, 1.145f)),
        ["RopeSpool"] = (new(-0.124f, -0.262f, -0.202f), new(0.125f, 0.259f, 0.392f)),
        ["Scorpion"] = (new(-0.761f, -0.031f, -0.319f), new(0.761f, 0.459f, 0.180f)),
        ["ScoutCannonItem"] = (new(-0.400f, -0.176f, -0.438f), new(0.400f, 0.635f, 0.507f)),
        ["ScoutCookies"] = (new(-0.251f, -0.441f, -0.067f), new(0.251f, 0.337f, 0.303f)),
        ["ScoutCookies_Vanilla"] = (new(-0.251f, -0.441f, -0.067f), new(0.251f, 0.337f, 0.303f)),
        ["ScoutEffigy"] = (new(-0.126f, -0.354f, -0.119f), new(0.126f, 0.380f, 0.137f)),
        ["ScoutmasterSoul"] = (new(-0.170f, -0.170f, -0.170f), new(0.170f, 0.170f, 0.170f)),
        ["ScoutsHonor"] = (new(-0.220f, -0.337f, -0.088f), new(0.229f, 0.132f, 0.386f)),
        ["ShelfShroom"] = (new(-0.312f, -0.179f, -0.344f), new(0.387f, 0.124f, 0.305f)),
        ["Shell Big"] = (new(-0.346f, -0.283f, -0.227f), new(0.539f, 0.524f, 0.493f)),
        ["Shroomberry_Blue"] = (new(-0.263f, -0.025f, -0.270f), new(0.267f, 0.564f, 0.250f)),
        ["Shroomberry_Green"] = (new(-0.263f, -0.025f, -0.270f), new(0.267f, 0.564f, 0.250f)),
        ["Shroomberry_Purple"] = (new(-0.263f, -0.025f, -0.270f), new(0.267f, 0.564f, 0.250f)),
        ["Shroomberry_Red"] = (new(-0.263f, -0.025f, -0.270f), new(0.267f, 0.564f, 0.250f)),
        ["Shroomberry_Yellow"] = (new(-0.263f, -0.025f, -0.270f), new(0.267f, 0.564f, 0.250f)),
        ["Skull"] = (new(-0.299f, -0.272f, -0.270f), new(0.293f, 0.325f, 0.349f)),
        ["Snowball"] = (new(-0.210f, -0.210f, -0.214f), new(0.214f, 0.214f, 0.210f)),
        ["Sports Drink"] = (new(-0.100f, -0.240f, -0.100f), new(0.100f, 0.287f, 0.100f)),
        ["Stone"] = (new(-0.226f, -0.195f, -0.262f), new(0.225f, 0.211f, 0.175f)),
        ["Strange Gem"] = (new(-0.220f, -0.337f, -0.088f), new(0.229f, 0.132f, 0.386f)),
        ["Sunscreen"] = (new(-0.083f, -0.210f, -0.131f), new(0.082f, 0.352f, 0.149f)),
        ["Torch"] = (new(-0.067f, -0.126f, -0.059f), new(0.060f, 0.522f, 0.077f)),
        ["TrailMix"] = (new(-0.156f, 0.025f, 0.049f), new(0.153f, 0.350f, 0.204f)),
        ["Warp Compass"] = (new(-0.190f, -0.156f, -0.149f), new(0.190f, 0.336f, 0.122f)),
        ["WarpFungus"] = (new(-0.229f, -0.204f, -0.205f), new(0.206f, 0.189f, 0.231f)),
        ["Warpsketball"] = (new(-0.324f, -0.328f, -0.326f), new(0.324f, 0.328f, 0.326f)),
        ["Winterberry Orange"] = (new(-0.232f, -0.340f, -0.223f), new(0.236f, 0.052f, 0.240f)),
        ["Winterberry Yellow"] = (new(-0.245f, -0.345f, -0.228f), new(0.223f, 0.047f, 0.235f)),
    };
}
