using System.Collections.Concurrent;
using System.Diagnostics;
using ExcelAiCategorizer.Models;
using Microsoft.Extensions.Options;

namespace ExcelAiCategorizer.Services;

/// <summary>
/// Navbatdagi vazifalarni ketma-ket oladi va har birini oxirigacha bajaradi:
/// Excel o'qish → to'dalarga bo'lish → AI ga parallel yuborish → natijani yozish.
/// </summary>
public sealed class CategorizationWorker : BackgroundService
{
    private readonly IJobQueue _queue;
    private readonly IJobStore _store;
    private readonly IExcelService _excel;
    private readonly IAiCategorizationService _ai;
    private readonly IFileStorage _storage;
    private readonly AiSettings _settings;
    private readonly ILogger<CategorizationWorker> _logger;

    public CategorizationWorker(
        IJobQueue queue,
        IJobStore store,
        IExcelService excel,
        IAiCategorizationService ai,
        IFileStorage storage,
        IOptions<AiSettings> settings,
        ILogger<CategorizationWorker> logger)
    {
        _queue = queue;
        _store = store;
        _excel = excel;
        _ai = ai;
        _storage = storage;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            var job = _store.Get(jobId);
            if (job is null)
            {
                _logger.LogWarning("Navbatdagi {JobId} vazifasi registrda topilmadi.", jobId);
                continue;
            }

            try
            {
                await ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = "Server to'xtatilgani sababli tahlil bekor qilindi.";
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{JobId} vazifasi xatolik bilan tugadi.", jobId);
                job.Status = JobStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
            finally
            {
                // Manba fayl endi kerak emas — natija alohida saqlanadi.
                _storage.DeleteQuietly(job.SourcePath);
            }
        }
    }

    private async Task ProcessAsync(CategorizationJob job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        job.Status = JobStatus.Running;

        // --- 1. Excel'ni o'qish ---
        ExcelTable table;
        await using (var stream = File.OpenRead(job.SourcePath))
        {
            table = _excel.Read(stream, job.RequestedColumn);
        }

        var options = job.Options with { ColumnName = table.TextColumnName };
        job.TotalRows = table.Rows.Count;

        // --- 2. To'dalarga bo'lish ---
        var batches = table.Rows
            .Chunk(Math.Max(1, _settings.BatchSize))
            .ToList();

        job.TotalBatches = batches.Count;

        _logger.LogInformation(
            "{JobId}: {Rows} qator, {Batches} to'da, '{Column}' ustuni.",
            job.Id, table.Rows.Count, batches.Count, table.TextColumnName);

        // --- 3. Cheklangan parallellik bilan AI ga yuborish ---
        var results = new ConcurrentDictionary<int, CategoryAssignment>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _settings.MaxParallelBatches),
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(batches, parallelOptions, async (batch, token) =>
        {
            try
            {
                var result = await _ai.CategorizeBatchAsync(batch, options, token);

                foreach (var assignment in result.Assignments)
                    results[assignment.RowNumber] = assignment;

                job.AddUsage(result.InputTokens, result.OutputTokens);
                job.AddProcessed(result.Assignments.Count);

                // Model ba'zi qatorlarni tashlab ketgan bo'lsa ham hisobga olamiz
                var missing = batch.Length - result.Assignments.Count;
                if (missing > 0)
                {
                    job.AddFailed(missing);
                    job.AddProcessed(missing);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bitta to'da qulasa — butun vazifa to'xtamaydi.
                _logger.LogError(ex, "{JobId}: to'da qayta ishlanmadi ({Count} qator).",
                    job.Id, batch.Length);

                job.AddFailed(batch.Length);
                job.AddProcessed(batch.Length);
            }
        });

        // --- 4. Natija faylini yozish ---
        var bytes = _excel.Write(table, results, out var summary);
        job.ResultPath = await _storage.SaveResultAsync(job.Id, bytes, ct);
        job.Summary = summary;

        job.Status = JobStatus.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "{JobId} tugadi: {Ok}/{Total} qator, {Seconds:F1}s, {In}+{Out} token.",
            job.Id, results.Count, job.TotalRows, stopwatch.Elapsed.TotalSeconds,
            job.InputTokens, job.OutputTokens);
    }
}
