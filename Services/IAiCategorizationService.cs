using ExcelAiCategorizer.Models;

namespace ExcelAiCategorizer.Services;

/// <summary>Bitta to'da (batch) natijasi va sarflangan tokenlar.</summary>
public sealed record BatchResult(
    IReadOnlyList<CategoryAssignment> Assignments,
    long InputTokens,
    long OutputTokens);

public interface IAiCategorizationService
{
    /// <summary>
    /// Qatorlar to'dasini AI ga yuborib, har biriga kategoriya biriktiradi.
    /// Xatolikda cheklangan sonda qayta urinadi; muvaffaqiyatsiz bo'lsa istisno tashlaydi.
    /// </summary>
    Task<BatchResult> CategorizeBatchAsync(
        IReadOnlyList<ExcelRowItem> batch,
        CategorizationOptions options,
        CancellationToken cancellationToken);
}
