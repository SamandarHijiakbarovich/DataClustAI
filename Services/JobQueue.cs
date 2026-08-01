using System.Threading.Channels;

namespace ExcelAiCategorizer.Services;

public interface IJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken ct = default);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct);
}

/// <summary>
/// Controller va fon ishchisi o'rtasidagi ko'prik.
/// Channel — thread-safe, bloklamaydigan navbat.
/// </summary>
public sealed class ChannelJobQueue : IJobQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(jobId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
