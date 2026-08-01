using System.ComponentModel.DataAnnotations;

namespace ExcelAiCategorizer.Models;

public sealed class UploadViewModel
{
    [Display(Name = "Excel fayl (.xlsx)")]
    [Required(ErrorMessage = "Iltimos, Excel faylni tanlang.")]
    public IFormFile? File { get; set; }

    [Display(Name = "Tahlil qilinadigan ustun(lar)")]
    [StringLength(500)]
    public string? ColumnName { get; set; }

    [Display(Name = "Kategoriyalar (har biri yangi qatorda yoki vergul bilan)")]
    [StringLength(4000)]
    public string? CategoriesRaw { get; set; }

    [Display(Name = "AI yangi kategoriya o'ylab topishi mumkin")]
    public bool AllowNewCategories { get; set; }

    [Display(Name = "Ma'lumot konteksti")]
    [StringLength(500)]
    public string? Context { get; set; }

    /// <summary>Matnli ro'yxatni toza kategoriyalar massiviga aylantiradi.</summary>
    public IReadOnlyList<string> ParseCategories()
    {
        if (string.IsNullOrWhiteSpace(CategoriesRaw))
            return [];

        return CategoriesRaw
            .Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
    }
}
