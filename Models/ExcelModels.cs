namespace ExcelAiCategorizer.Models;

/// <summary>
/// Excel'dan o'qilgan bitta ma'lumot qatori.
/// </summary>
public sealed class ExcelRowItem
{
    /// <summary>Ma'lumot qatorining tartib raqami (1 dan boshlanadi, sarlavha hisobga olinmaydi).</summary>
    public required int RowNumber { get; init; }

    /// <summary>AI ga yuboriladigan matn (bir yoki bir nechta ustundan yig'ilgan).</summary>
    public required string Text { get; init; }

    /// <summary>Qatordagi barcha kataklarning matnli ko'rinishi — natija faylida qayta yoziladi.</summary>
    public required IReadOnlyList<string> Cells { get; init; }
}

/// <summary>
/// Excel varag'ining o'qilgan holati: sarlavhalar + qatorlar.
/// </summary>
public sealed class ExcelTable
{
    public required string SheetName { get; init; }

    /// <summary>Sarlavha qatoridagi ustun nomlari.</summary>
    public required IReadOnlyList<string> Headers { get; init; }

    /// <summary>
    /// Sarlavha topilgan qator raqami (varaqdagi haqiqiy raqam).
    /// Hisobot uslubidagi fayllarda 1 dan katta bo'lishi mumkin.
    /// </summary>
    public required int HeaderRowNumber { get; init; }

    /// <summary>Tahlil qilinadigan ustunlarning 0-dan boshlanuvchi indekslari.</summary>
    public required IReadOnlyList<int> TextColumnIndexes { get; init; }

    public required IReadOnlyList<ExcelRowItem> Rows { get; init; }

    /// <summary>Tahlil qilinayotgan ustun(lar) nomi — xulosa varag'i va prompt uchun.</summary>
    public string TextColumnName => TextColumnIndexes.Count switch
    {
        0 => "(aniqlanmadi)",
        1 => Headers[TextColumnIndexes[0]],
        _ => string.Join(", ", TextColumnIndexes.Select(i => Headers[i]))
    };
}
