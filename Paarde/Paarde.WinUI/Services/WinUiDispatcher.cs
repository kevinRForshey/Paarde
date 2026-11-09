using Microsoft.UI.Dispatching;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.WinUI.Services;

// specs/winui-frontend R3.1: IUiDispatcher over the UI thread's DispatcherQueue. Constructed by App
// on the UI thread and registered as an instance, since DI may first resolve it from a background
// thread, where DispatcherQueue.GetForCurrentThread() would return null.
public sealed class WinUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _dispatcherQueue;

    public WinUiDispatcher(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public void Post(Action action) => _dispatcherQueue.TryEnqueue(() => action());

    public IUiTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var timer = _dispatcherQueue.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = true;
        timer.Tick += (_, _) => tick();
        return new WinUiTimer(timer);
    }

    private sealed class WinUiTimer : IUiTimer
    {
        private readonly DispatcherQueueTimer _timer;

        public WinUiTimer(DispatcherQueueTimer timer)
        {
            _timer = timer;
        }

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();
    }
}
