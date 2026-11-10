namespace Paarde.Presentation.Contracts.Services;

// View models marshal background-thread callbacks (LLM
// streaming deltas, PTY output, working-directory change notifications) back onto the UI thread
// through this instead of a framework-specific dispatcher - Avalonia's Dispatcher.UIThread today,
// WinUI 3's DispatcherQueue later. Tests supply a synchronous fake.
public interface IUiDispatcher
{
    // Queues the action to run on the UI thread; never runs it inline on the caller's stack in
    // the real implementations.
    void Post(Action action);

    // A repeating timer whose ticks are raised on the UI thread. Created stopped.
    IUiTimer CreateTimer(TimeSpan interval, Action tick);
}

public interface IUiTimer
{
    void Start();

    void Stop();
}
