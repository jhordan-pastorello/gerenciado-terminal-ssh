using StorageTerm.Layout;
using StorageTerm.Services;

namespace StorageTerm.Tests;

public class LayoutEngineTests
{
    private const int TotalCells = LayoutEngine.GridColumns * LayoutEngine.GridRows;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Compute_ReturnsOneTilePerPane(int count)
    {
        Assert.Equal(count, LayoutEngine.Compute(count).Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Compute_CoversEveryCellExactlyOnce(int count)
    {
        var tiles = LayoutEngine.Compute(count);
        var occupied = new bool[LayoutEngine.GridRows, LayoutEngine.GridColumns];

        foreach (var tile in tiles)
        {
            Assert.InRange(tile.Row, 0, LayoutEngine.GridRows - 1);
            Assert.InRange(tile.Column, 0, LayoutEngine.GridColumns - 1);
            Assert.InRange(tile.RowSpan, 1, LayoutEngine.GridRows);
            Assert.InRange(tile.ColumnSpan, 1, LayoutEngine.GridColumns);

            for (var row = tile.Row; row < tile.Row + tile.RowSpan; row++)
            {
                for (var column = tile.Column; column < tile.Column + tile.ColumnSpan; column++)
                {
                    Assert.False(occupied[row, column], $"Sobreposição em {row},{column} com {count} painéis.");
                    occupied[row, column] = true;
                }
            }
        }

        var filled = 0;
        foreach (var value in occupied)
        {
            if (value) filled++;
        }

        Assert.Equal(TotalCells, filled);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void Compute_DividesAreaEqually_WhenLayoutIsRegular(int count)
    {
        var areas = LayoutEngine.Compute(count).Select(t => t.RowSpan * t.ColumnSpan).Distinct().ToArray();
        Assert.Single(areas);
        Assert.Equal(TotalCells / count, areas[0]);
    }

    [Fact]
    public void ThreePanes_NewestIsLargeOnTheRight_AndFirstTwoAreStacked()
    {
        var tiles = LayoutEngine.Compute(3);

        Assert.Equal(new PaneTile(0, 0, 1, 3), tiles[0]);
        Assert.Equal(new PaneTile(1, 0, 1, 3), tiles[1]);
        Assert.Equal(new PaneTile(0, 3, 2, 3), tiles[2]);
    }

    [Fact]
    public void FivePanes_FirstFourAreTwoByTwo_AndNewestTakesExtraColumn()
    {
        var tiles = LayoutEngine.Compute(5);

        Assert.Equal(new PaneTile(0, 0, 1, 2), tiles[0]);
        Assert.Equal(new PaneTile(0, 2, 1, 2), tiles[1]);
        Assert.Equal(new PaneTile(1, 0, 1, 2), tiles[2]);
        Assert.Equal(new PaneTile(1, 2, 1, 2), tiles[3]);
        Assert.Equal(new PaneTile(0, 4, 2, 2), tiles[4]);
    }

    [Fact]
    public void SixPanes_AreThreeColumnsByTwoRows()
    {
        var tiles = LayoutEngine.Compute(6);

        Assert.Equal(6, tiles.Count);

        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(new PaneTile(0, index * 2, 1, 2), tiles[index]);
            Assert.Equal(new PaneTile(1, index * 2, 1, 2), tiles[index + 3]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Compute_ReturnsEmptyOutsideSupportedRange(int count)
    {
        Assert.Empty(LayoutEngine.Compute(count));
    }
}

public class SshCommandBuilderTests
{
    [Theory]
    [InlineData("storage-lan")]
    [InlineData("storage")]
    [InlineData("user@host")]
    [InlineData("host:2222")]
    [InlineData("a.b_c-d")]
    public void IsValidAlias_AcceptsSafeAliases(string alias)
    {
        Assert.True(SshCommandBuilder.IsValidAlias(alias));
    }

    [Theory]
    [InlineData("")]
    [InlineData("storage lan")]
    [InlineData("storage;rm -rf /")]
    [InlineData("$(whoami)")]
    [InlineData("storage&&calc")]
    [InlineData("storage|calc")]
    public void IsValidAlias_RejectsUnsafeAliases(string alias)
    {
        Assert.False(SshCommandBuilder.IsValidAlias(alias));
    }

    [Fact]
    public void Build_UsesSshConfigAliasAndKeepAliveOptions()
    {
        var command = SshCommandBuilder.Build("storage-lan");

        Assert.Contains("storage-lan", command);
        Assert.Contains("ConnectTimeout", command);
        Assert.Contains("ServerAliveInterval", command);
        Assert.Contains("TCPKeepAlive", command);
    }

    [Fact]
    public void Build_ThrowsForUnsafeAlias()
    {
        Assert.Throws<ArgumentException>(() => SshCommandBuilder.Build("storage;calc"));
    }
}

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("v2.0", "1.0.0", true)]
    [InlineData("v1.0.0.1", "1.0.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.9.9", "1.0.0", false)]
    [InlineData("v1.0.1-beta", "1.0.0", false)]
    [InlineData("abc", "1.0.0", false)]
    [InlineData("", "1.0.0", false)]
    public void IsNewer_ComparesReleaseTags(string tag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(tag, Version.Parse(current)));
    }
}
