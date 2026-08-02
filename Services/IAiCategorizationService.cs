using ExcelAiCategorizer.Models;

namespace ExcelAiCategorizer.Services;

/// <summary>Bitta to'da (batch) natijasi va sarflangan tokenlar.</summary>
public sealed record BatchResult(
    IReadOnlyList<CategoryAssignment> Assignments,
    long InputTokens,
    long OutputTokens);

/// <summary>Aniqlangan umumiy kategoriyalar ro'yxati va sarflangan tokenlar.</summary>
public sealed record TaxonomyResult(
    IReadOnlyList<string> Categories,
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

    /// <summary>
    /// Kategoriyalar oldindan berilmaganida ishlatiladi: namuna qatorlarni tahlil qilib,
    /// butun ma'lumotni qamrab oladigan ixcham va izchil kategoriyalar ro'yxatini aniqlaydi.
    /// So'ng bu ro'yxat barcha to'dalarga qat'iy qo'llaniladi — natija bir xil bo'ladi.
    /// </summary>
    Task<TaxonomyResult> DiscoverCategoriesAsync(
        IReadOnlyList<ExcelRowItem> sample,
        CategorizationOptions options,
        CancellationToken cancellationToken);
}
