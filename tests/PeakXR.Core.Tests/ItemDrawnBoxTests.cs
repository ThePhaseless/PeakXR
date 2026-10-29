using System.Globalization;
using System.IO;
using System.Linq;

namespace PeakXR.Core.Tests;

public class ItemDrawnBoxTests
{
    /// <summary>docs/research/item-grab-shapes.csv's rows as (prefab, drawn low corner, drawn high corner).</summary>
    internal static (string Name, Vector3 Lo, Vector3 Hi)[] Rows()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PeakXR.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var lines = File.ReadAllLines(
            Path.Combine(dir.FullName, "docs", "research", "item-grab-shapes.csv")
        );
        var head = lines[0].Split(',');
        int lo = System.Array.IndexOf(head, "vis_lo");
        int hi = System.Array.IndexOf(head, "vis_hi");
        return lines
            .Skip(1)
            .Select(l => l.Split(','))
            .Select(c => (c[0], V(c[lo]), V(c[hi])))
            .ToArray();
    }

    static Vector3 V(string s)
    {
        var p = s.Split(' ').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return new Vector3(p[0], p[1], p[2]);
    }

    [Fact]
    public void EveryDatabaseItemHasItsMeshBoxFromTheResearchData()
    {
        var rows = Rows();
        Assert.Equal(194, rows.Length);
        foreach (var (name, lo, hi) in rows)
        {
            Assert.True(ItemDrawnBox.TryGet(name, out var gotLo, out var gotHi), name);
            Assert.Equal(lo, gotLo);
            Assert.Equal(hi, gotHi);
        }
    }

    [Fact]
    public void ARopeSpoolRestsOnItsMeshNotOnItsPartsBoxes()
    {
        // Its parts' boxes put its lowest corner 19 cm under the mesh.
        Assert.True(ItemDrawnBox.TryGet("RopeSpool", out var lo, out var hi));
        Assert.Equal(new Vector3(-0.124f, -0.262f, -0.202f), lo);
        Assert.Equal(new Vector3(0.125f, 0.259f, 0.392f), hi);
    }

    [Fact]
    public void AnItemAGameUpdateAddsIsNotKnown() =>
        Assert.False(ItemDrawnBox.TryGet("SomethingNew", out _, out _));
}
