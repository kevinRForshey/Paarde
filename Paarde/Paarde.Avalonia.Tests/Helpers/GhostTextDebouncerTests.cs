using Paarde.Avalonia.Helpers;

namespace Paarde.Avalonia.Tests.Helpers;

public class GhostTextDebouncerTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldFire_NoInputYet_ReturnsFalse()
    {
        var debouncer = new GhostTextDebouncer(TimeSpan.FromMilliseconds(400));

        Assert.False(debouncer.ShouldFire(BaseTime));
    }

    [Fact]
    public void ShouldFire_BeforeDelayElapsed_ReturnsFalse()
    {
        var debouncer = new GhostTextDebouncer(TimeSpan.FromMilliseconds(400));

        debouncer.NotifyInput(BaseTime);

        Assert.False(debouncer.ShouldFire(BaseTime + TimeSpan.FromMilliseconds(399)));
    }

    [Fact]
    public void ShouldFire_AfterDelayElapsed_ReturnsTrue()
    {
        var debouncer = new GhostTextDebouncer(TimeSpan.FromMilliseconds(400));

        debouncer.NotifyInput(BaseTime);

        Assert.True(debouncer.ShouldFire(BaseTime + TimeSpan.FromMilliseconds(400)));
    }

    [Fact]
    public void ShouldFire_FiresOnlyOnceUntilNextInput()
    {
        var debouncer = new GhostTextDebouncer(TimeSpan.FromMilliseconds(400));
        var fireTime = BaseTime + TimeSpan.FromMilliseconds(400);

        debouncer.NotifyInput(BaseTime);

        Assert.True(debouncer.ShouldFire(fireTime));
        Assert.False(debouncer.ShouldFire(fireTime));
    }

    [Fact]
    public void NotifyInput_ResetsTheDelayWindow()
    {
        var debouncer = new GhostTextDebouncer(TimeSpan.FromMilliseconds(400));

        debouncer.NotifyInput(BaseTime);
        debouncer.NotifyInput(BaseTime + TimeSpan.FromMilliseconds(300));

        Assert.False(debouncer.ShouldFire(BaseTime + TimeSpan.FromMilliseconds(400)));
        Assert.True(debouncer.ShouldFire(BaseTime + TimeSpan.FromMilliseconds(700)));
    }

    [Fact]
    public void Cancel_SuppressesAPendingFire()
    {
        var debouncer = new GhostTextDebouncer(TimeSpan.FromMilliseconds(400));

        debouncer.NotifyInput(BaseTime);
        debouncer.Cancel();

        Assert.False(debouncer.ShouldFire(BaseTime + TimeSpan.FromMilliseconds(400)));
    }
}
