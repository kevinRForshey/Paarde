using Avalonia.Threading;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.Avalonia.Services;

// specs/presentation-extraction R2.2: the Avalonia side of IUiDispatcher.
public class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public IUiTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => tick();
        return new AvaloniaUiTimer(timer);
    }

    private sealed class AvaloniaUiTimer(DispatcherTimer timer) : IUiTimer
    {
        public void Start() => timer.Start();

        public void Stop() => timer.Stop();
    }
}
