using ExcelAiCategorizer.Models;
using Microsoft.Extensions.Options;

namespace ExcelAiCategorizer.Services;

/// <summary>
/// Muddati o'tgan natija fayllarini va job yozuvlarini har soatda tozalaydi.
/// </summary>
public sealed class CleanupService : BackgroundService
{
    private readonly IJobStore _store;
    private readonly IFileStorage _storage;
    private readonly UploadSettings _settings;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(
        IJobStore store,
        IFileStorage storage,
        IOptions<UploadSettings> settings,
        ILogger<CleanupService> logger)
    {
        _store = store;
        _storage = storage;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-_settings.ResultRetentionHours);
            var removed = 0;

            foreach (var job in _store.Snapshot())
            {
                if (job.CreatedAt > cutoff) continue;

                _storage.DeleteQuietly(job.ResultPath);
                _storage.DeleteQuietly(job.SourcePath);
                _store.Remove(job.Id);
                removed++;
            }

            if (removed > 0)
                _logger.LogInformation("{Count} ta eski vazifa tozalandi.", removed);
        }
    }
}
