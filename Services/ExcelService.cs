using System.Text;
using ClosedXML.Excel;
using ExcelAiCategorizer.Models;

namespace ExcelAiCategorizer.Services;

/// <summary>
/// ClosedXML asosidagi Excel o'qish/yozish servisi.
/// Hech qanday tashqi holatga bog'liq emas — shuning uchun singleton.
/// </summary>
public sealed class ExcelService : IExcelService
{
    private const int MaxRows = 20_000;
    private const int MaxTextLength = 2_000;
    private const int HeaderSearchDepth = 25;

    /// <summary>Barcha ustunlarni tanlash uchun qabul qilinadigan kalit so'zlar.</summary>
    private static readonly string[] AllColumnsKeywords =
        ["*", "barcha", "barchasi", "hammasi", "hamma", "all", "hamma ustun", "barcha ustun"];

    public ExcelTable Read(Stream stream, string? columnSpec)
    {
        using var workbook = new XLWorkbook(stream);

        var sheet = workbook.Worksheets.FirstOrDefault()
                    ?? throw new InvalidOperationException("Faylda birorta ham varaq topilmadi.");

        var used = sheet.RangeUsed()
                   ?? throw new InvalidOperationException($"'{sheet.Name}' varag'i bo'sh.");

        if (used.RowCount() < 2)
            throw new InvalidOperationException("Faylda sarlavhadan tashqari ma'lumot qatori yo'q.");

        // Sarlavha har doim 1-qatorda bo'lavermaydi: eksport fayllarida
        // yuqorida banner, sana, parametrlar bloki bo'lishi mumkin.
        var headerRow = FindHeaderRow(used);

        var headers = used.Row(headerRow)
            .Cells()
            .Select((c, i) =>
            {
                var text = c.GetFormattedString().Trim();
                return string.IsNullOrEmpty(text) ? $"Ustun {i + 1}" : text;
            })
            .ToList();

        var rawRows = new List<string[]>();
        for (var r = headerRow + 1; r <= used.RowCount() && rawRows.Count < MaxRows; r++)
        {
            var rangeRow = used.Row(r);
            var cells = new string[headers.Count];
            var hasValue = false;

            for (var c = 0; c < headers.Count; c++)
            {
                var text = rangeRow.Cell(c + 1).GetFormattedString().Trim();
                cells[c] = text;
                if (text.Length > 0) hasValue = true;
            }

            if (hasValue) rawRows.Add(cells);   // butunlay bo'sh qatorlarni tashlab ketamiz
        }

        if (rawRows.Count == 0)
            throw new InvalidOperationException(
                $"Sarlavha {headerRow}-qatorda topildi, lekin undan keyin ma'lumot qatori yo'q.");

        var columnIndexes = ResolveColumns(headers, rawRows, columnSpec);

        var rows = rawRows
            .Select((cells, i) => new ExcelRowItem
            {
                RowNumber = i + 1,
                Text = ComposeText(cells, headers, columnIndexes),
                Cells = cells
            })
            .Where(r => r.Text.Length > 0)   // tahlil qilinadigan matni bo'lmagan qatorlar chiqadi
            .ToList();

        if (rows.Count == 0)
        {
            var names = string.Join(", ", columnIndexes.Select(i => headers[i]));
            throw new InvalidOperationException(
                $"'{names}' ustun(lar)ida tahlil qilish uchun matn topilmadi.");
        }

        return new ExcelTable
        {
            SheetName = sheet.Name,
            Headers = headers,
            HeaderRowNumber = used.FirstRow().RowNumber() + headerRow - 1,
            TextColumnIndexes = columnIndexes,
            Rows = rows
        };
    }

    // ------------------------------------------------------- sarlavha qatorini topish

    /// <summary>
    /// Sarlavha qatorini topadi (diapazonga nisbatan 1-based).
    /// Uch mezon: (1) deyarli to'liq to'ldirilgan, (2) kataklari asosan matnli
    /// — ma'lumot qatorlarida raqam ko'p bo'ladi, (3) keyingi qatorda ma'lumot bor.
    /// Shu uch shartga mos keluvchi ENG BIRINCHI qator tanlanadi.
    /// </summary>
    private static int FindHeaderRow(IXLRange used)
    {
        var columnCount = used.ColumnCount();
        var limit = Math.Min(HeaderSearchDepth, used.RowCount() - 1);

        var filled = new int[limit + 2];
        var textual = new int[limit + 2];

        for (var r = 1; r <= limit + 1; r++)
            (filled[r], textual[r]) = Inspect(used.Row(r), columnCount);

        var maxFilled = filled.Max();
        if (maxFilled < 2) return 1;

        // Sarlavhada bitta-ikkita katak bo'sh bo'lishi mumkin — kichik chegirma beramiz.
        var threshold = maxFilled - Math.Max(1, maxFilled / 10);

        for (var r = 1; r <= limit; r++)
        {
            if (filled[r] < 2 || filled[r] < threshold) continue;   // banner/parametr bloki emas
            if (filled[r + 1] < 1) continue;                        // keyingi qator bo'sh
            if (textual[r] < filled[r] * 0.7) continue;             // raqamli qator — ma'lumot

            return r;
        }

        return 1;
    }

    /// <summary>Qatordagi to'ldirilgan va matnli (raqam bo'lmagan) kataklar soni.</summary>
    private static (int Filled, int Textual) Inspect(IXLRangeRow row, int columnCount)
    {
        var filled = 0;
        var textual = 0;

        for (var c = 1; c <= columnCount; c++)
        {
            var text = row.Cell(c).GetFormattedString().Trim();
            if (text.Length == 0) continue;

            filled++;
            if (!double.TryParse(text, out _)) textual++;
        }

        return (filled, textual);
    }

    // ------------------------------------------------------- ustunlarni tanlash

    /// <summary>
    /// Foydalanuvchi kiritgan qiymatni ustun indekslariga aylantiradi:
    ///   bo'sh          -> matnga eng boy bitta ustun avtomatik tanlanadi
    ///   "*" / "barcha" -> to'ldirilgan barcha ustunlar
    ///   "A, B, C"      -> nomlar bo'yicha bir nechta ustun
    ///   "A"            -> bitta ustun
    /// </summary>
    private static IReadOnlyList<int> ResolveColumns(
        List<string> headers, List<string[]> rows, string? columnSpec)
    {
        var spec = columnSpec?.Trim();

        if (string.IsNullOrEmpty(spec))
            return [AutoDetectColumn(headers, rows)];

        if (AllColumnsKeywords.Contains(spec, StringComparer.OrdinalIgnoreCase))
        {
            var filled = Enumerable.Range(0, headers.Count)
                .Where(c => rows.Any(r => r[c].Length > 0))
                .ToList();

            return filled.Count > 0 ? filled : [AutoDetectColumn(headers, rows)];
        }

        var requested = spec
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var indexes = new List<int>();
        var missing = new List<string>();

        foreach (var name in requested)
        {
            var idx = headers.FindIndex(h =>
                string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0) indexes.Add(idx);
            else missing.Add(name);
        }

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Bu ustun(lar) topilmadi: {string.Join(", ", missing)}. " +
                $"Mavjud ustunlar: {string.Join(", ", headers)}. " +
                $"Barcha ustunlarni tahlil qilish uchun \"*\" yoki \"barcha\" deb yozing.");

        return indexes.Distinct().ToList();
    }

    /// <summary>Raqam bo'lmagan, o'rtacha matn uzunligi eng katta ustunni tanlaydi.</summary>
    private static int AutoDetectColumn(List<string> headers, List<string[]> rows)
    {
        var sample = rows.Take(200).ToList();
        var best = -1;
        var bestScore = -1.0;

        for (var c = 0; c < headers.Count; c++)
        {
            var values = sample.Select(r => r[c]).Where(v => v.Length > 0).ToList();
            if (values.Count == 0) continue;

            var numericRatio = values.Count(v => double.TryParse(v, out _)) / (double)values.Count;
            if (numericRatio > 0.5) continue;             // asosan raqamli ustun — mos emas

            var score = values.Average(v => v.Length) * (values.Count / (double)sample.Count);
            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        return best >= 0 ? best : 0;
    }

    /// <summary>
    /// Tanlangan ustunlardan AI ga yuboriladigan matnni yig'adi.
    /// Bitta ustun bo'lsa — faqat qiymat; bir nechta bo'lsa — "Ustun: qiymat" satrlari.
    /// </summary>
    private static string ComposeText(
        string[] cells, List<string> headers, IReadOnlyList<int> indexes)
    {
        if (indexes.Count == 1)
            return Truncate(cells[indexes[0]]);

        var builder = new StringBuilder();
        foreach (var i in indexes)
        {
            if (cells[i].Length == 0) continue;
            builder.Append(headers[i]).Append(": ").AppendLine(cells[i]);
        }

        return Truncate(builder.ToString().TrimEnd());
    }

    private static string Truncate(string value) =>
        value.Length <= MaxTextLength ? value : value[..MaxTextLength];

    // ------------------------------------------------------- yozish

    public byte[] Write(
        ExcelTable table,
        IReadOnlyDictionary<int, CategoryAssignment> results,
        out IReadOnlyList<CategorySummary> summary)
    {
        using var workbook = new XLWorkbook();

        var sheet = workbook.Worksheets.Add("Natija");
        WriteHeaders(sheet, table);
        WriteDataRows(sheet, table, results);
        FormatSheet(sheet, table);

        summary = BuildSummary(table, results);
        WriteSummarySheet(workbook, summary, table);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteHeaders(IXLWorksheet sheet, ExcelTable table)
    {
        for (var c = 0; c < table.Headers.Count; c++)
            sheet.Cell(1, c + 1).Value = table.Headers[c];

        var extra = table.Headers.Count;
        sheet.Cell(1, extra + 1).Value = "AI kategoriya";
        sheet.Cell(1, extra + 2).Value = "Ishonch";
        sheet.Cell(1, extra + 3).Value = "Izoh";
    }

    private static void WriteDataRows(
        IXLWorksheet sheet, ExcelTable table, IReadOnlyDictionary<int, CategoryAssignment> results)
    {
        var extra = table.Headers.Count;

        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var excelRow = i + 2;   // 1-qator sarlavha

            for (var c = 0; c < row.Cells.Count; c++)
                sheet.Cell(excelRow, c + 1).Value = row.Cells[c];

            if (results.TryGetValue(row.RowNumber, out var result))
            {
                sheet.Cell(excelRow, extra + 1).Value = result.Category;

                var confidenceCell = sheet.Cell(excelRow, extra + 2);
                confidenceCell.Value = result.Confidence;
                confidenceCell.Style.NumberFormat.Format = "0%";

                // Past ishonchni sariq bilan belgilaymiz — qo'lda tekshirish uchun
                if (result.Confidence < 0.6)
                    confidenceCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FDE68A");

                sheet.Cell(excelRow, extra + 3).Value = result.Reason;
            }
            else
            {
                var failCell = sheet.Cell(excelRow, extra + 1);
                failCell.Value = "Tahlil qilinmadi";
                failCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FECACA");
            }
        }
    }

    private static void FormatSheet(IXLWorksheet sheet, ExcelTable table)
    {
        var lastColumn = table.Headers.Count + 3;

        var header = sheet.Range(1, 1, 1, lastColumn);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F2937");
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.SetAutoFilter();
        sheet.Columns().AdjustToContents(1, 60, 12, 55);

        // Izoh ustuni uzun bo'ladi — matnni o'rash
        sheet.Column(lastColumn).Style.Alignment.WrapText = true;
        sheet.Column(lastColumn).Width = 55;
    }

    private static IReadOnlyList<CategorySummary> BuildSummary(
        ExcelTable table, IReadOnlyDictionary<int, CategoryAssignment> results)
    {
        var total = table.Rows.Count;
        if (total == 0) return [];

        var grouped = results.Values
            .GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategorySummary(
                g.Key,
                g.Count(),
                Math.Round(g.Count() * 100.0 / total, 2)))
            .OrderByDescending(s => s.Count)
            .ToList();

        var missing = total - results.Count;
        if (missing > 0)
            grouped.Add(new CategorySummary("Tahlil qilinmadi", missing,
                Math.Round(missing * 100.0 / total, 2)));

        return grouped;
    }

    private static void WriteSummarySheet(
        XLWorkbook workbook, IReadOnlyList<CategorySummary> summary, ExcelTable table)
    {
        var sheet = workbook.Worksheets.Add("Xulosa");

        sheet.Cell(1, 1).Value = "Tahlil xulosasi";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value = "Manba varaq:";
        sheet.Cell(2, 2).Value = table.SheetName;
        sheet.Cell(3, 1).Value = "Sarlavha qatori:";
        sheet.Cell(3, 2).Value = table.HeaderRowNumber;
        sheet.Cell(4, 1).Value = "Tahlil qilingan ustun(lar):";
        sheet.Cell(4, 2).Value = table.TextColumnName;
        sheet.Cell(5, 1).Value = "Jami qatorlar:";
        sheet.Cell(5, 2).Value = table.Rows.Count;
        sheet.Cell(6, 1).Value = "Sana:";
        sheet.Cell(6, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        sheet.Cell(8, 1).Value = "Kategoriya";
        sheet.Cell(8, 2).Value = "Soni";
        sheet.Cell(8, 3).Value = "Ulushi";

        var head = sheet.Range(8, 1, 8, 3);
        head.Style.Font.Bold = true;
        head.Style.Font.FontColor = XLColor.White;
        head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F2937");

        for (var i = 0; i < summary.Count; i++)
        {
            var r = 9 + i;
            sheet.Cell(r, 1).Value = summary[i].Category;
            sheet.Cell(r, 2).Value = summary[i].Count;
            sheet.Cell(r, 3).Value = summary[i].Percent / 100.0;
            sheet.Cell(r, 3).Style.NumberFormat.Format = "0.00%";
        }

        sheet.Columns().AdjustToContents(1, 3, 12, 60);
    }
}
