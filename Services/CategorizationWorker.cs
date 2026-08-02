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
    /// <summary>Taksonomiya aniqlash uchun olinadigan namuna qatorlar soni.</summary>
    private const int DiscoverySampleSize = 60;

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

        // --- 1.5. Boshlang'ich tahlil: ma'lumot xulosasi + (kerak bo'lsa) kategoriyalar ---
        // Bitta AI chaqiruvi ikkalasini qaytaradi:
        //   • "overview" — foydalanuvchiga chatbot uslubida ko'rsatiladigan qisqa xulosa;
        //   • "categories" — kategoriyalar berilmagan bo'lsa, butun faylga qo'llaniladigan
        //     yagona ro'yxat (aks holda har to'da o'zicha nom o'ylab topib, takror chiqadi).
        if (table.Rows.Count > 0)
        {
            try
            {
                var sample = SampleRows(table.Rows, DiscoverySampleSize);
                var analysis = await _ai.AnalyzeSampleAsync(sample, options, ct);
                job.AddUsage(analysis.InputTokens, analysis.OutputTokens);

                if (!string.IsNullOrWhiteSpace(analysis.Overview))
                    job.Overview = analysis.Overview;

                // Kategoriyalarni faqat foydalanuvchi bermagan bo'lsa qo'llaymiz.
                if (options.Categories.Count == 0 && analysis.Categories.Count > 0)
                {
                    var categories = analysis.Categories.ToList();

                    // "Boshqa" — hech qaysi guruhga tushmagan qatorlar uchun panoh.
                    if (!categories.Any(c => c.Equals("Boshqa", StringComparison.OrdinalIgnoreCase)))
                        categories.Add("Boshqa");

                    // Qat'iy rejim: barcha to'da bir xil ro'yxatdan foydalanadi.
                    options = options with { Categories = categories, AllowNewCategories = false };

                    _logger.LogInformation("{JobId}: AI {Count} ta kategoriya aniqladi: {Categories}",
                        job.Id, categories.Count, string.Join(", ", categories));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Tahlil bosqichi qulasa — xulosa bo'lmaydi, kategoriyalar bo'sh bo'lsa
                // har to'da o'zi aniqlaydi (eski xatti-harakat). Vazifa to'xtamaydi.
                _logger.LogWarning(ex,
                    "{JobId}: boshlang'ich tahlil bosqichi o'tkazib yuborildi.", job.Id);
            }
        }

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

    /// <summary>
    /// Butun ma'lumotni vakillik qiluvchi teng oraliqli namuna tanlaydi.
    /// Qatorlar namunadan kam bo'lsa — hammasi qaytariladi.
    /// </summary>
    private static IReadOnlyList<ExcelRowItem> SampleRows(
        IReadOnlyList<ExcelRowItem> rows, int max)
    {
        if (rows.Count <= max) return rows;

        var step = (double)rows.Count / max;
        var sample = new List<ExcelRowItem>(max);

        for (var i = 0; i < max; i++)
            sample.Add(rows[(int)(i * step)]);

        return sample;
    }
}
