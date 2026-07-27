namespace FordConnectToAbrpSync.Sync;

/// <summary>
/// Bridges external Wake/Sleep Signals into the sequential Sync loop. A signal
/// latches: one arriving while a Sync Cycle is running is consumed by the very
/// next wait, so no nudge is ever lost. A Wake Signal also opens a Boost
/// Window, during which the loop keeps the normal poll interval even while the
/// vehicle still reports Idle — cover for the lag between sitting down in the
/// car and Ford's cloud noticing. Thread-safe: signals arrive on HTTP threads
/// while the loop waits.
/// </summary>
internal sealed class SyncPacer
{
    private readonly Lock _gate = new();
    private TaskCompletionSource _pulse = NewPulse();
    private DateTimeOffset _boostUntil = DateTimeOffset.MinValue;

    /// <summary>Wake Signal: open a Boost Window and interrupt the current wait.</summary>
    public void Wake(DateTimeOffset now, TimeSpan boostWindow)
    {
        lock (_gate)
        {
            _boostUntil = now + boostWindow;
            _pulse.TrySetResult();
        }
    }

    /// <summary>Sleep Signal: close any Boost Window and interrupt the current wait.</summary>
    public void Sleep()
    {
        lock (_gate)
        {
            _boostUntil = DateTimeOffset.MinValue;
            _pulse.TrySetResult();
        }
    }

    public bool IsBoosted(DateTimeOffset now)
    {
        lock (_gate)
        {
            return now < _boostUntil;
        }
    }

    /// <summary>
    /// Waits until <paramref name="delay"/> elapses or a signal arrives,
    /// whichever comes first. Returns true when a signal cut the wait short.
    /// Single consumer: only the Sync loop may call this.
    /// </summary>
    public async Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        TaskCompletionSource pulse;
        lock (_gate)
        {
            pulse = _pulse;
        }

        // Linked cts stops the Task.Delay timer once a pulse wins, so an
        // interrupted 30-minute Idle wait doesn't leave a timer ticking.
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var winner = await Task.WhenAny(pulse.Task, Task.Delay(delay, delayCts.Token));

        if (winner == pulse.Task)
        {
            await delayCts.CancelAsync();
            lock (_gate)
            {
                if (ReferenceEquals(_pulse, pulse))
                {
                    _pulse = NewPulse();
                }
            }

            return true;
        }

        // Delay finished — either the interval elapsed or shutdown was requested.
        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    private static TaskCompletionSource NewPulse() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
