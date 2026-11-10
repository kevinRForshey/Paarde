using Avalonia.Controls;

using Paarde.Avalonia.Helpers;
using Paarde.Presentation.Tests.Fakes;

namespace Paarde.Avalonia.Tests.Helpers;

public class ResizablePaneHelperTests
{
    private readonly FakeLocalSettingsService _settingsService = new();
    private readonly ColumnDefinition _column = new();

    private ResizablePaneHelper CreateSut(double defaultWidth = 280) =>
        new(_column, _settingsService, "Layout.Test.Width", defaultWidth);

    [Fact]
    public async Task RestoreAsync_NothingPersisted_UsesTheDefaultWidth()
    {
        var sut = CreateSut(defaultWidth: 280);

        await sut.RestoreAsync();

        Assert.Equal(280, _column.Width.Value);
    }

    [Fact]
    public async Task RestoreAsync_WidthPreviouslyPersisted_RestoresIt()
    {
        await _settingsService.SaveSettingAsync("Layout.Test.Width", 412.5);
        var sut = CreateSut(defaultWidth: 280);

        await sut.RestoreAsync();

        Assert.Equal(412.5, _column.Width.Value);
    }

    [Fact]
    public async Task RestoreAsync_PersistedWidthIsNegative_ClampsToZero()
    {
        // Shouldn't happen from dragging a GridSplitter, but guards against a corrupted
        // settings file producing a Grid with a nonsensical negative column width.
        await _settingsService.SaveSettingAsync("Layout.Test.Width", -50d);
        var sut = CreateSut();

        await sut.RestoreAsync();

        Assert.Equal(0, _column.Width.Value);
    }

    [Fact]
    public async Task OnSplitterDragCompleted_PersistsTheColumnsCurrentWidth()
    {
        var sut = CreateSut();
        _column.Width = new GridLength(123);

        sut.OnSplitterDragCompleted();
        // OnSplitterDragCompleted is async void (a UI event handler signature) - give its one
        // await (a synchronous FakeLocalSettingsService completion) a chance to run.
        await Task.Yield();

        Assert.Equal(123, await _settingsService.ReadSettingAsync<double?>("Layout.Test.Width"));
    }

    [Fact]
    public async Task OnSplitterDragCompleted_ColumnDraggedToZero_PersistsZero()
    {
        var sut = CreateSut();
        _column.Width = new GridLength(0);

        sut.OnSplitterDragCompleted();
        await Task.Yield();

        Assert.Equal(0, await _settingsService.ReadSettingAsync<double?>("Layout.Test.Width"));
    }

    [Fact]
    public void Collapse_WhenOpen_SetsWidthToZeroAndMarksCollapsed()
    {
        var sut = CreateSut();
        _column.Width = new GridLength(250);

        sut.Collapse();

        Assert.Equal(0, _column.Width.Value);
        Assert.True(sut.IsCollapsed);
    }

    [Fact]
    public void Collapse_AlreadyCollapsed_IsANoOp()
    {
        var sut = CreateSut();
        _column.Width = new GridLength(0);

        sut.Collapse();

        Assert.Equal(0, _column.Width.Value);
        Assert.True(sut.IsCollapsed);
    }

    [Fact]
    public void Restore_AfterCollapse_RestoresTheWidthItHadBeforeCollapsing()
    {
        var sut = CreateSut();
        _column.Width = new GridLength(250);
        sut.Collapse();

        sut.Restore();

        Assert.Equal(250, _column.Width.Value);
        Assert.False(sut.IsCollapsed);
    }

    [Fact]
    public void Restore_WithoutHavingCollapsedFirst_UsesTheDefaultWidth()
    {
        var sut = CreateSut(defaultWidth: 280);

        sut.Restore();

        Assert.Equal(280, _column.Width.Value);
    }

    [Fact]
    public void ToggleCollapse_WhenOpen_Collapses()
    {
        var sut = CreateSut();
        _column.Width = new GridLength(250);

        sut.ToggleCollapse();

        Assert.True(sut.IsCollapsed);
    }

    [Fact]
    public void ToggleCollapse_WhenCollapsed_Restores()
    {
        var sut = CreateSut();
        _column.Width = new GridLength(250);
        sut.ToggleCollapse();

        sut.ToggleCollapse();

        Assert.Equal(250, _column.Width.Value);
        Assert.False(sut.IsCollapsed);
    }

    // Zen mode collapses two independent panes as a pair - this
    // just confirms two ResizablePaneHelper instances don't interfere with each other, since each
    // one owns its own ColumnDefinition/settings key.
    [Fact]
    public void Collapse_TwoIndependentHelpers_EachTracksItsOwnPreCollapseWidth()
    {
        var secondColumn = new ColumnDefinition();
        var sut1 = CreateSut();
        var sut2 = new ResizablePaneHelper(secondColumn, _settingsService, "Layout.Test.Width2", defaultWidth: 280);

        _column.Width = new GridLength(220);
        secondColumn.Width = new GridLength(360);

        sut1.Collapse();
        sut2.Collapse();
        sut1.Restore();

        Assert.Equal(220, _column.Width.Value);
        Assert.True(sut2.IsCollapsed);

        sut2.Restore();

        Assert.Equal(360, secondColumn.Width.Value);
    }
}
