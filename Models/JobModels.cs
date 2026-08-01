namespace ExcelAiCategorizer.Models;

public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

/// <summary>
/// Bitta fayl tahlili. Fon xizmati (worker) tomonidan bajariladi,
/// brauzer esa /Home/Status orqali holatini so'rab turadi.
/// </summary>
public sealed class CategorizationJob
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string SourcePath { get; init; }
    public required string? RequestedColumn { get; init; }
    public required CategorizationOptions Options { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;
    public string? ErrorMessage { get; set; }

    /// <summary>Tayyor natija fayli joylashgan yo'l (tugagandan keyin to'ldiriladi).</summary>
    public string? ResultPath { get; set; }

    public string ResultFileName => $"{Path.GetFileNameWithoutExtension(FileName)}_kategoriyalangan.xlsx";

    // --- Progress hisoblagichlari (bir nechta oqimdan yoziladi) ---
    private int _processedRows;
    private int _failedRows;

    public int TotalRows { get; set; }
    public int TotalBatches { get; set; }

    public int ProcessedRows => Volatile.Read(ref _processedRows);
    public int FailedRows => Volatile.Read(ref _failedRows);

    public void AddProcessed(int count) => Interlocked.Add(ref _processedRows, count);
    public void AddFailed(int count) => Interlocked.Add(ref _failedRows, count);

    // --- Token sarfi statistikasi ---
    private long _inputTokens;
    private long _outputTokens;

    public long InputTokens => Interlocked.Read(ref _inputTokens);
    public long OutputTokens => Interlocked.Read(ref _outputTokens);

    public void AddUsage(long input, long output)
    {
        Interlocked.Add(ref _inputTokens, input);
        Interlocked.Add(ref _outputTokens, output);
    }

    public int ProgressPercent =>
        TotalRows == 0 ? 0 : (int)Math.Round(ProcessedRows * 100.0 / TotalRows);

    /// <summary>Kategoriyalar bo'yicha yakuniy taqsimot (tugaganda to'ldiriladi).</summary>
    public IReadOnlyList<CategorySummary> Summary { get; set; } = [];
}

public sealed record CategorySummary(string Category, int Count, double Percent);

/// <summary>Brauzerga JSON ko'rinishida qaytariladigan qisqa holat.</summary>
public sealed record JobStatusDto(
    string Id,
    string Status,
    int Progress,
    int ProcessedRows,
    int TotalRows,
    int FailedRows,
    long InputTokens,
    long OutputTokens,
    string? Error,
    bool IsDownloadReady,
    IReadOnlyList<CategorySummary> Summary);
