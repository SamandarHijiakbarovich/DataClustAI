using System.Text;
using System.Text.Json;
using ExcelAiCategorizer.Models;

namespace ExcelAiCategorizer.Services;

/// <summary>
/// Prompt va JSON sxemasini quruvchi umumiy mantiq.
/// Claude va OpenAI-mos implementatsiyalar bir xil promptdan foydalanadi —
/// shunda provayderni almashtirganda natija sifati taqqoslanadigan bo'ladi.
/// </summary>
public static class CategorizationPrompt
{
    /// <summary>O'zgarmas ko'rsatmalar (bitta vazifa davomida bir xil).</summary>
    public static string BuildSystem(CategorizationOptions options)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine(
            "Sen Excel jadvallaridagi matnli ma'lumotlarni kategoriyalarga ajratuvchi tahlilchisan.");
        prompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(options.Context))
            prompt.AppendLine($"Ma'lumot konteksti: {options.Context}");

        if (!string.IsNullOrWhiteSpace(options.ColumnName))
            prompt.AppendLine($"Tahlil qilinayotgan ustun nomi: {options.ColumnName}");

        prompt.AppendLine();

        if (options.Categories.Count > 0)
        {
            prompt.AppendLine("Ruxsat etilgan kategoriyalar:");
            foreach (var category in options.Categories)
                prompt.AppendLine($"  - {category}");

            prompt.AppendLine(options.AllowNewCategories
                ? "Agar matn ushbu kategoriyalarning birortasiga ham mos kelmasa, yangi qisqa kategoriya nomi o'ylab topishing mumkin."
                : "Faqat shu ro'yxatdagi kategoriyalardan foydalan. Yangi kategoriya yaratma.");
        }
        else
        {
            prompt.AppendLine(
                "Kategoriyalar ro'yxati berilmagan. Ma'lumotlarning mazmunidan kelib chiqib, " +
                "izchil va qisqa (1-3 so'zli) kategoriya nomlarini o'zing aniqla. " +
                "Bir xil mazmundagi qatorlar uchun aynan bir xil kategoriya nomini ishlat — " +
                "sinonimlar yoki turli yozilishlar bo'lmasin.");
        }

        prompt.AppendLine();
        prompt.AppendLine("Qat'iy qoidalar:");
        prompt.AppendLine("1. Kirishdagi HAR BIR qator uchun aynan bitta natija qaytar — birortasini ham tashlab ketma.");
        prompt.AppendLine("2. \"row\" maydoni kirishdagi qator raqami bilan bir xil bo'lishi shart.");
        prompt.AppendLine("3. \"confidence\" — 0.0 dan 1.0 gacha ishonch darajasi. Ikkilanayotgan bo'lsang, pastroq baho qo'y.");
        prompt.AppendLine("4. \"reason\" — o'zbek tilida, bitta qisqa jumla (20 so'zdan oshmasin).");
        prompt.AppendLine("5. Faqat matnda mavjud ma'lumotga tayan; taxmin qilma.");
        prompt.AppendLine();
        prompt.AppendLine("Javob FAQAT quyidagi shakldagi JSON bo'lsin, boshqa hech qanday matnsiz:");
        prompt.AppendLine("""{"items":[{"row":1,"category":"...","confidence":0.9,"reason":"..."}]}""");

        return prompt.ToString();
    }

    /// <summary>Har to'dada o'zgaradigan qism.</summary>
    public static string BuildUser(IReadOnlyList<ExcelRowItem> batch)
    {
        var payload = JsonSerializer.Serialize(
            batch.Select(r => new { row = r.RowNumber, text = r.Text }));

        return $"Quyidagi {batch.Count} ta qatorni kategoriyalarga ajrat:\n\n{payload}";
    }

    /// <summary>
    /// Javob shaklini majburlovchi JSON Schema.
    /// Kategoriyalar qat'iy bo'lsa — "enum" orqali model ro'yxatdan chiqa olmaydi.
    /// </summary>
    public static Dictionary<string, JsonElement> BuildSchema(CategorizationOptions options)
    {
        object categorySchema = options.HasFixedCategories
            ? new
            {
                type = "string",
                description = "Ruxsat etilgan kategoriyalardan biri.",
                @enum = options.Categories
            }
            : new
            {
                type = "string",
                description = "Kategoriya nomi (1-3 so'z)."
            };

        var itemSchema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["row"] = new { type = "integer", description = "Kirishdagi qator raqami." },
                ["category"] = categorySchema,
                ["confidence"] = new { type = "number", description = "0.0 dan 1.0 gacha ishonch." },
                ["reason"] = new { type = "string", description = "Qisqa izoh (o'zbekcha)." }
            },
            ["required"] = new[] { "row", "category", "confidence", "reason" },
            ["additionalProperties"] = false
        };

        var rootProperties = new Dictionary<string, object>
        {
            ["items"] = new Dictionary<string, object>
            {
                ["type"] = "array",
                ["description"] = "Har bir kirish qatori uchun bittadan natija.",
                ["items"] = itemSchema
            }
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(rootProperties),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "items" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
        };
    }

    /// <summary>
    /// Model javobini tozalaydi: notanish/takroriy qatorlarni tashlaydi,
    /// ishonchni 0..1 ga siqadi, qat'iy rejimda ro'yxatdan tashqari
    /// kategoriyalarni eng yaqiniga moslaydi.
    /// </summary>
    public static IReadOnlyList<CategoryAssignment> Normalize(
        IEnumerable<CategoryAssignment> items,
        IReadOnlyList<ExcelRowItem> batch,
        CategorizationOptions options)
    {
        var validRows = batch.Select(r => r.RowNumber).ToHashSet();
        var seen = new HashSet<int>();
        var result = new List<CategoryAssignment>(batch.Count);

        foreach (var item in items)
        {
            if (!validRows.Contains(item.RowNumber) || !seen.Add(item.RowNumber))
                continue;

            item.Confidence = Math.Clamp(item.Confidence, 0.0, 1.0);
            item.Category = string.IsNullOrWhiteSpace(item.Category)
                ? "Aniqlanmadi"
                : item.Category.Trim();

            // Bepul modellar "enum" ni har doim ham hurmat qilmaydi —
            // qat'iy rejimda ro'yxatga majburan moslaymiz.
            if (options.HasFixedCategories)
            {
                var match = options.Categories.FirstOrDefault(c =>
                    string.Equals(c, item.Category, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    item.Category = match;                  // yozilishini birxillashtiramiz
                }
                else
                {
                    item.Category = "Aniqlanmadi";
                    item.Confidence = 0;
                    item.Reason = "Model ro'yxatdan tashqari kategoriya qaytardi.";
                }
            }

            result.Add(item);
        }

        return result;
    }

    // ================================================================
    //  Taksonomiya aniqlash (1-bosqich) — kategoriyalar berilmaganda
    // ================================================================

    public const int TaxonomyMin = 4;
    public const int TaxonomyMax = 10;
    public const int TaxonomyIdeal = 7;

    /// <summary>Namunadan umumiy kategoriyalar ro'yxatini so'rovchi ko'rsatma.</summary>
    public static string BuildTaxonomySystem(CategorizationOptions options)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine(
            "Sen Excel ma'lumotlarini tahlil qiluvchi ekspertsan. Vazifang — quyidagi namuna " +
            "qatorlarni o'qib, BUTUN ma'lumotni qamrab oladigan ixcham kategoriyalar ro'yxatini tuzish.");
        prompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(options.Context))
            prompt.AppendLine($"Ma'lumot konteksti: {options.Context}");

        if (!string.IsNullOrWhiteSpace(options.ColumnName))
            prompt.AppendLine($"Tahlil qilinayotgan ustun: {options.ColumnName}");

        prompt.AppendLine();
        prompt.AppendLine("Talablar:");
        prompt.AppendLine($"1. Jami {TaxonomyMin}–{TaxonomyMax} ta kategoriya bo'lsin (ideal — {TaxonomyIdeal} ta). Ko'paytirma.");
        prompt.AppendLine("2. Har biri qisqa (1–3 so'z), umumiy va bir-biridan aniq farqli bo'lsin.");
        prompt.AppendLine("3. Sinonim yoki bir-birini takrorlaydigan nomlar BO'LMASIN. " +
            "Masalan \"Yetkazib berish narxi\" va \"Yetkazib berish xizmati\" — ikkisi o'rniga bitta \"Yetkazib berish\".");
        prompt.AppendLine("4. O'xshash mavzularni bitta umumiy guruhga birlashtir — mayda bo'laklarga bo'lma.");
        prompt.AppendLine("5. Kategoriyalar butun ma'lumotni qamrasin, lekin ortiqcha maxsuslashtirmasin.");
        prompt.AppendLine();
        prompt.AppendLine("Javob FAQAT quyidagi shakldagi JSON bo'lsin, boshqa hech qanday matnsiz:");
        prompt.AppendLine("""{"categories":["Kategoriya 1","Kategoriya 2","Kategoriya 3"]}""");

        return prompt.ToString();
    }

    /// <summary>Namuna qatorlar matni (raqamsiz — faqat mazmun kerak).</summary>
    public static string BuildTaxonomyUser(IReadOnlyList<ExcelRowItem> sample)
    {
        var payload = JsonSerializer.Serialize(sample.Select(r => r.Text));
        return $"Quyidagi {sample.Count} ta namuna qatorni tahlil qilib, " +
               $"umumiy kategoriyalar ro'yxatini aniqla:\n\n{payload}";
    }

    /// <summary>Taksonomiya javobini majburlovchi JSON Schema.</summary>
    public static Dictionary<string, JsonElement> BuildTaxonomySchema()
    {
        var rootProperties = new Dictionary<string, object>
        {
            ["categories"] = new Dictionary<string, object>
            {
                ["type"] = "array",
                ["description"] = "Umumiy kategoriyalar ro'yxati.",
                ["minItems"] = TaxonomyMin,
                ["maxItems"] = TaxonomyMax,
                ["items"] = new { type = "string", description = "Qisqa kategoriya nomi (1-3 so'z)." }
            }
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(rootProperties),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "categories" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
        };
    }

    /// <summary>
    /// Aniqlangan ro'yxatni tozalaydi: bo'shlarni olib tashlaydi, takrorlarni
    /// (registrga bog'liq bo'lmagan holda) birlashtiradi, sonini cheklaydi.
    /// </summary>
    public static IReadOnlyList<string> CleanTaxonomy(IEnumerable<string> categories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var raw in categories)
        {
            var name = raw?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name.Length > 40) name = name[..40].Trim();
            if (seen.Add(name)) result.Add(name);
            if (result.Count >= TaxonomyMax) break;
        }

        return result;
    }

    /// <summary>
    /// Modeldan kelgan matndan JSON obyektini ajratib oladi.
    /// Kichik modellar javobni ```json ... ``` ichiga o'rashi yoki
    /// oldidan izoh yozishi mumkin — shularni tozalaydi.
    /// </summary>
    public static string ExtractJson(string raw)
    {
        var text = raw.Trim();

        // Markdown kod bloklarini olib tashlash
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0) text = text[(firstNewline + 1)..];

            var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0) text = text[..fenceEnd];

            text = text.Trim();
        }

        // Birinchi '{' dan oxirgi '}' gacha
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}
