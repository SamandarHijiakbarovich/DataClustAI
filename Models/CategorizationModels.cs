using System.Text.Json.Serialization;

namespace ExcelAiCategorizer.Models;

/// <summary>
/// Foydalanuvchi bergan tahlil shartlari — AI ga uzatiladi.
/// </summary>
public sealed record CategorizationOptions
{
    /// <summary>Oldindan belgilangan kategoriyalar. Bo'sh bo'lsa — AI o'zi aniqlaydi.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Ro'yxatdan tashqari yangi kategoriya yaratishga ruxsat.</summary>
    public bool AllowNewCategories { get; init; }

    /// <summary>Ma'lumotlar konteksti (masalan: "mijoz shikoyatlari", "mahsulot nomlari").</summary>
    public string Context { get; init; } = string.Empty;

    /// <summary>Tahlil qilinayotgan ustun nomi — promptga kontekst beradi.</summary>
    public string ColumnName { get; init; } = string.Empty;

    public bool HasFixedCategories => Categories.Count > 0 && !AllowNewCategories;
}

/// <summary>
/// AI qaytargan bitta qator natijasi. JSON nomlari model javobiga mos.
/// </summary>
public sealed class CategoryAssignment
{
    [JsonPropertyName("row")]
    public int RowNumber { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "Aniqlanmadi";

    /// <summary>0.0 – 1.0 oralig'idagi ishonch darajasi.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>AI javobining ildiz obyekti (JSON schema shu shaklga majburlaydi).</summary>
public sealed class BatchCategorizationResponse
{
    [JsonPropertyName("items")]
    public List<CategoryAssignment> Items { get; set; } = [];
}

/// <summary>
/// Taksonomiya aniqlash bosqichida AI qaytaradigan umumiy kategoriyalar ro'yxati.
/// </summary>
public sealed class CategoryTaxonomyResponse
{
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];
}
