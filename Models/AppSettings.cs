namespace ExcelAiCategorizer.Models;

/// <summary>
/// appsettings.json dagi "Ai" bo'limi bilan bog'lanadi.
/// API kalitini appsettings.json ga yozmang — user-secrets yoki
/// ANTHROPIC_API_KEY muhit o'zgaruvchisidan foydalaning.
/// </summary>
public sealed class AiSettings
{
    public const string SectionName = "Ai";

    /// <summary>
    /// Qaysi implementatsiya ishlatiladi:
    ///   "OpenAiCompatible" — Gemini / Groq / OpenRouter / Ollama (bepul tariflar)
    ///   "Anthropic"        — rasmiy Claude SDK (pullik)
    /// </summary>
    public string Provider { get; set; } = "OpenAiCompatible";

    /// <summary>
    /// OpenAI-mos provayderning manzili. Anthropic uchun e'tiborga olinmaydi.
    /// </summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";

    /// <summary>
    /// API kaliti. Bo'sh bo'lsa:
    ///   Anthropic  → ANTHROPIC_API_KEY muhit o'zgaruvchisi ishlatiladi
    ///   Ollama     → kalit umuman kerak emas
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model identifikatori (provayderga bog'liq).</summary>
    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>
    /// JSON rejimi: "json_object" (ko'pchilik provayder), "json_schema"
    /// (qat'iy sxema qo'llab-quvvatlansa) yoki "none" (faqat prompt orqali).
    /// </summary>
    public string ResponseFormat { get; set; } = "json_object";

    /// <summary>
    /// Daqiqadagi maksimal so'rovlar soni. Bepul tariflarda limitga urilmaslik
    /// uchun so'rovlar orasiga avtomatik pauza qo'yiladi. 0 — cheklovsiz.
    /// </summary>
    public int RequestsPerMinute { get; set; } = 12;

    /// <summary>Fikrlash chuqurligi (faqat Anthropic): low | medium | high | xhigh | max.</summary>
    public string Effort { get; set; } = "medium";

    /// <summary>Bitta javobdagi maksimal token soni.</summary>
    public int MaxTokens { get; set; } = 8000;

    /// <summary>Bitta so'rovda AI ga yuboriladigan qatorlar soni (to'da hajmi).</summary>
    public int BatchSize { get; set; } = 25;

    /// <summary>Bir vaqtda parallel ketadigan to'dalar soni (rate-limit uchun cheklov).</summary>
    public int MaxParallelBatches { get; set; } = 3;

    /// <summary>Xatolikda qayta urinishlar soni (eksponensial kutish bilan).</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Bitta AI so'rovining maksimal davomiyligi.</summary>
    public int RequestTimeoutSeconds { get; set; } = 300;
}

/// <summary>appsettings.json dagi "Upload" bo'limi.</summary>
public sealed class UploadSettings
{
    public const string SectionName = "Upload";

    /// <summary>Yuklanadigan faylning maksimal hajmi (MB).</summary>
    public int MaxFileSizeMb { get; set; } = 25;

    /// <summary>Vaqtinchalik fayllar saqlanadigan papka (loyiha ildiziga nisbatan).</summary>
    public string StorageRoot { get; set; } = "App_Data";

    /// <summary>Natija fayllari necha soatdan keyin o'chiriladi.</summary>
    public int ResultRetentionHours { get; set; } = 6;

    public long MaxFileSizeBytes => (long)MaxFileSizeMb * 1024 * 1024;
}
