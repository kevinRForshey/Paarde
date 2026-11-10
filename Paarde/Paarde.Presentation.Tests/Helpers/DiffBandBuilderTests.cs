using Paarde.Core.Models;
using Paarde.Presentation.Helpers;

namespace Paarde.Presentation.Tests.Helpers;

public class DiffBandBuilderTests
{
    [Fact]
    public void Build_AllUnchanged_ReturnsNoBands()
    {
        var diffLines = new[]
        {
            new DiffLine(DiffLineKind.Unchanged, "a"),
            new DiffLine(DiffLineKind.Unchanged, "b"),
        };

        var bands = DiffBandBuilder.Build(diffLines);

        Assert.Empty(bands);
    }

    [Fact]
    public void Build_SingleAddedLine_ReturnsOneBandAtItsProposedLineNumber()
    {
        var diffLines = new[]
        {
            new DiffLine(DiffLineKind.Unchanged, "a"),
            new DiffLine(DiffLineKind.Added, "b"),
            new DiffLine(DiffLineKind.Unchanged, "c"),
        };

        var bands = DiffBandBuilder.Build(diffLines);

        var band = Assert.Single(bands);
        Assert.Equal(new DiffBand(StartLine: 2, LineCount: 1, IsAddition: true), band);
    }

    [Fact]
    public void Build_ConsecutiveAddedLines_CoalesceIntoOneBand()
    {
        var diffLines = new[]
        {
            new DiffLine(DiffLineKind.Unchanged, "a"),
            new DiffLine(DiffLineKind.Added, "b"),
            new DiffLine(DiffLineKind.Added, "c"),
            new DiffLine(DiffLineKind.Added, "d"),
            new DiffLine(DiffLineKind.Unchanged, "e"),
        };

        var bands = DiffBandBuilder.Build(diffLines);

        var band = Assert.Single(bands);
        Assert.Equal(new DiffBand(StartLine: 2, LineCount: 3, IsAddition: true), band);
    }

    [Fact]
    public void Build_TrailingAddedRun_ClosesAtEndOfFile()
    {
        var diffLines = new[]
        {
            new DiffLine(DiffLineKind.Unchanged, "a"),
            new DiffLine(DiffLineKind.Added, "b"),
            new DiffLine(DiffLineKind.Added, "c"),
        };

        var bands = DiffBandBuilder.Build(diffLines);

        var band = Assert.Single(bands);
        Assert.Equal(new DiffBand(StartLine: 2, LineCount: 2, IsAddition: true), band);
    }

    [Fact]
    public void Build_PureDeletion_ReturnsOneZeroLengthMarkerAtTheBoundary()
    {
        var diffLines = new[]
        {
            new DiffLine(DiffLineKind.Unchanged, "a"),
            new DiffLine(DiffLineKind.Removed, "b"),
            new DiffLine(DiffLineKind.Removed, "c"),
            new DiffLine(DiffLineKind.Unchanged, "d"),
        };

        var bands = DiffBandBuilder.Build(diffLines);

        var band = Assert.Single(bands);
        Assert.Equal(new DiffBand(StartLine: 2, LineCount: 0, IsAddition: false), band);
    }

    [Fact]
    public void Build_DeletionAtEndOfFile_MarkerAnchorsPastLastLine()
    {
        var diffLines = new[]
        {
            new DiffLine(DiffLineKind.Unchanged, "a"),
            new DiffLine(DiffLineKind.Removed, "b"),
        };

        var bands = DiffBandBuilder.Build(diffLines);

        var band = Assert.Single(bands);
        Assert.Equal(new DiffBand(StartLine: 2, LineCount: 0, IsAddition: false), band);
    }

    [Fact]
    public void Build_ReplacementRun_ReturnsAddedBandAndOneDeletionMarker()
    {
        var diffLines = new[]
        {
            new DiffLine(DiffLineKind.Unchanged, "a"),
            new DiffLine(DiffLineKind.Removed, "old1"),
            new DiffLine(DiffLineKind.Removed, "old2"),
            new DiffLine(DiffLineKind.Added, "new1"),
            new DiffLine(DiffLineKind.Unchanged, "b"),
        };

        var bands = DiffBandBuilder.Build(diffLines);

        Assert.Equal(2, bands.Count);
        Assert.Equal(new DiffBand(StartLine: 2, LineCount: 0, IsAddition: false), bands[0]);
        Assert.Equal(new DiffBand(StartLine: 2, LineCount: 1, IsAddition: true), bands[1]);
    }
}
