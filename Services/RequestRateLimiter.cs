namespace ExcelAiCategorizer.Services;

/// <summary>
/// So'rovlar orasida minimal interval saqlaydi.
/// Bepul tariflarda daqiqadagi so'rovlar soni qattiq cheklangani uchun zarur
/// (masalan 15 so'rov/daqiqa → har 4 soniyada bittadan).
/// </summary>
public sealed class RequestRateLimiter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public RequestRateLimiter(int requestsPerMinute)
    {
        _minInterval = requestsPerMinute <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(60.0 / requestsPerMinute);
    }

    public async Task WaitTurnAsync(CancellationToken cancellationToken)
    {
        if (_minInterval == TimeSpan.Zero) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequest;
            var remaining = _minInterval - elapsed;

            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, cancellationToken);

            _lastRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}
