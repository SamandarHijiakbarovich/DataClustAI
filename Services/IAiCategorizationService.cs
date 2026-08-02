using ExcelAiCategorizer.Models;

namespace ExcelAiCategorizer.Services;

/// <summary>Bitta to'da (batch) natijasi va sarflangan tokenlar.</summary>
public sealed record BatchResult(
    IReadOnlyList<CategoryAssignment> Assignments,
    long InputTokens,
    long OutputTokens);

/// <summary>
/// Tahlil boshidagi bitta chaqiruv natijasi: ma'lumot xulosasi (chatbot uslubida),
/// aniqlangan umumiy kategoriyalar va sarflangan tokenlar.
/// </summary>
public sealed record SampleAnalysisResult(
    string Overview,
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
    /// Tahlil boshida namuna qatorlarni bitta chaqiruvda tahlil qiladi va qaytaradi:
    /// (1) ma'lumot nima haqidaligi haqida qisqa xulosa (foydalanuvchiga chatbot uslubida),
    /// (2) butun ma'lumotni qamrab oladigan ixcham va izchil kategoriyalar ro'yxati.
    /// Kategoriyalar keyin barcha to'dalarga qat'iy qo'llaniladi — natija bir xil bo'ladi.
    /// </summary>
    Task<SampleAnalysisResult> AnalyzeSampleAsync(
        IReadOnlyList<ExcelRowItem> sample,
        CategorizationOptions options,
        CancellationToken cancellationToken);
}
