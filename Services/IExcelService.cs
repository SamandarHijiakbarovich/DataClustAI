using ExcelAiCategorizer.Models;

namespace ExcelAiCategorizer.Services;

public interface IExcelService
{
    /// <summary>
    /// Excel faylni o'qib, tahlilga tayyor jadvalga aylantiradi.
    /// Sarlavha qatori avtomatik aniqlanadi (banner/parametr bloklari o'tkazib yuboriladi).
    /// </summary>
    /// <param name="stream">.xlsx fayl oqimi.</param>
    /// <param name="columnSpec">
    /// Tahlil qilinadigan ustun(lar):
    /// <c>null</c> yoki bo'sh — matnga eng boy ustun avtomatik tanlanadi;
    /// <c>"*"</c> / <c>"barcha"</c> — to'ldirilgan barcha ustunlar;
    /// <c>"A, B"</c> — nomlar bo'yicha bir nechta ustun.
    /// </param>
    ExcelTable Read(Stream stream, string? columnSpec);

    /// <summary>
    /// Asl ma'lumot + AI natijalaridan yangi .xlsx fayl yasaydi.
    /// </summary>
    byte[] Write(ExcelTable table, IReadOnlyDictionary<int, CategoryAssignment> results,
                 out IReadOnlyList<CategorySummary> summary);
}
