using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

// Queues posted work instead of running it - matching what Dispatcher.UIThread.Post did under
// these tests before IUiDispatcher existed (no running UI loop, so posts never ran). Tests that
// want the posted work to happen call RunPending(); timers only fire on FakeUiTimer.Tick().
public class FakeUiDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _pending = new();

    public List<FakeUiTimer> CreatedTimers { get; } = [];

    public int PendingCount => _pending.Count;

    public void Post(Action action) => _pending.Enqueue(action);

    public IUiTimer CreateTimer(TimeSpan interval, Action tick)
    {
        var timer = new FakeUiTimer(interval, tick);
        CreatedTimers.Add(timer);
        return timer;
    }

    // Runs everything queued so far, including work queued by the work being run.
    public void RunPending()
    {
        while (_pending.TryDequeue(out var action))
        {
            action();
        }
    }

    public class FakeUiTimer(TimeSpan interval, Action tick) : IUiTimer
    {
        public TimeSpan Interval { get; } = interval;

        public bool IsRunning { get; private set; }

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;

        public void Tick() => tick();
    }
}
