using System;

namespace Paarde.Avalonia.Helpers;

// Pure debounce state machine behind Epic 15's ghost-text completion trigger (T15.1.2/T15.3.1) -
// kept separate from CodeEditor so the timing decision itself is unit-testable against manufactured
// timestamps, without a real DispatcherTimer. CodeEditor owns the actual timer: it calls NotifyInput
// on every keystroke and ShouldFire on every tick, firing a completion request only when the latter
// returns true.
public sealed class GhostTextDebouncer(TimeSpan delay)
{
    private DateTimeOffset _lastInputAt;
    private bool _pending;

    public void NotifyInput(DateTimeOffset now)
    {
        _lastInputAt = now;
        _pending = true;
    }

    // Cancels a pending fire without requiring the delay to elapse first - used when the editor
    // stops being eligible for a suggestion (e.g. read-only, or a suggestion is already showing)
    // between NotifyInput and the delay elapsing.
    public void Cancel() => _pending = false;

    public bool ShouldFire(DateTimeOffset now)
    {
        if (!_pending || now - _lastInputAt < delay)
        {
            return false;
        }

        _pending = false;
        return true;
    }
}
