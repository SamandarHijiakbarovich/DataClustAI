using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExcelAiCategorizer.Models;
using Microsoft.Extensions.Options;

namespace ExcelAiCategorizer.Services;

/// <summary>
/// OpenAI-mos <c>/chat/completions</c> protokoli orqali ishlaydigan implementatsiya.
/// Bitta kod bilan bir nechta provayder qo'llab-quvvatlanadi — faqat
/// appsettings.json dagi BaseUrl va Model o'zgaradi:
///
///   Google Gemini : https://generativelanguage.googleapis.com/v1beta/openai/
///   Groq          : https://api.groq.com/openai/v1/
///   OpenRouter    : https://openrouter.ai/api/v1/
///   Ollama (lokal): http://localhost:11434/v1/
///   Mistral       : https://api.mistral.ai/v1/
/// </summary>
public sealed class OpenAiCompatibleCategorizationService : IAiCategorizationService
{
    public const string HttpClientName = "ai";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiSettings _settings;
    private readonly RequestRateLimiter _rateLimiter;
    private readonly ILogger<OpenAiCompatibleCategorizationService> _logger;

    public OpenAiCompatibleCategorizationService(
        IHttpClientFactory httpClientFactory,
        IOptions<AiSettings> settings,
        ILogger<OpenAiCompatibleCategorizationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
        _rateLimiter = new RequestRateLimiter(_settings.RequestsPerMinute);
    }

    public async Task<BatchResult> CategorizeBatchAsync(
        IReadOnlyList<ExcelRowItem> batch,
        CategorizationOptions options,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _rateLimiter.WaitTurnAsync(cancellationToken);
                return await SendAsync(batch, options, cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt <= _settings.MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2);   // 4s, 8s, 16s
                _logger.LogWarning(ex,
                    "AI so'rovi muvaffaqiyatsiz ({Attempt}/{Max}). {Delay}s dan keyin qayta urinamiz.",
                    attempt, _settings.MaxRetries, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public async Task<SampleAnalysisResult> AnalyzeSampleAsync(
        IReadOnlyList<ExcelRowItem> sample,
        CategorizationOptions options,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _rateLimiter.WaitTurnAsync(cancellationToken);
                return await SendAnalysisAsync(sample, options, cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt <= _settings.MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2);
                _logger.LogWarning(ex,
                    "Boshlang'ich tahlil so'rovi muvaffaqiyatsiz ({Attempt}/{Max}). {Delay}s dan keyin qayta.",
                    attempt, _settings.MaxRetries, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task<SampleAnalysisResult> SendAnalysisAsync(
        IReadOnlyList<ExcelRowItem> sample,
        CategorizationOptions options,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(NormalizeBaseUrl(_settings.BaseUrl));

        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "chat/completions", BuildAnalysisBody(sample, options), JsonOptions, timeout.Token);

        await EnsureSuccessAsync(response, timeout.Token);

        var completion = await response.Content
            .ReadFromJsonAsync<ChatCompletion>(JsonOptions, timeout.Token);

        var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
            throw new AiTransientException("Boshlang'ich tahlilda modeldan bo'sh javob keldi.");

        SampleAnalysisResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SampleAnalysisResponse>(
                CategorizationPrompt.ExtractJson(content), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new AiTransientException(
                $"Tahlil javobini JSON sifatida o'qib bo'lmadi: {Preview(content)}", ex);
        }

        return new SampleAnalysisResult(
            parsed?.Overview?.Trim() ?? string.Empty,
            CategorizationPrompt.CleanTaxonomy(parsed?.Categories ?? []),
            completion?.Usage?.PromptTokens ?? 0,
            completion?.Usage?.CompletionTokens ?? 0);
    }

    private Dictionary<string, object?> BuildAnalysisBody(
        IReadOnlyList<ExcelRowItem> sample, CategorizationOptions options)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = _settings.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = CategorizationPrompt.BuildAnalysisSystem(options) },
                new { role = "user",   content = CategorizationPrompt.BuildAnalysisUser(sample) }
            },
            ["temperature"] = 0,
            ["max_tokens"] = _settings.MaxTokens
        };

        switch (_settings.ResponseFormat?.ToLowerInvariant())
        {
            case "json_schema":
                body["response_format"] = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "tahlil",
                        strict = true,
                        schema = CategorizationPrompt.BuildAnalysisSchema()
                    }
                };
                break;

            case "none":
                break;

            default:
                body["response_format"] = new { type = "json_object" };
                break;
        }

        return body;
    }

    private async Task<BatchResult> SendAsync(
        IReadOnlyList<ExcelRowItem> batch,
        CategorizationOptions options,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(NormalizeBaseUrl(_settings.BaseUrl));

        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "chat/completions", BuildBody(batch, options), JsonOptions, timeout.Token);

        await EnsureSuccessAsync(response, timeout.Token);

        var completion = await response.Content
            .ReadFromJsonAsync<ChatCompletion>(JsonOptions, timeout.Token);

        var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
            throw new AiTransientException("Modeldan bo'sh javob keldi.");

        BatchCategorizationResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<BatchCategorizationResponse>(
                CategorizationPrompt.ExtractJson(content), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new AiTransientException(
                $"Javobni JSON sifatida o'qib bo'lmadi: {Preview(content)}", ex);
        }

        if (parsed is null || parsed.Items.Count == 0)
            throw new AiTransientException($"Javobda natija yo'q: {Preview(content)}");

        return new BatchResult(
            CategorizationPrompt.Normalize(parsed.Items, batch, options),
            completion?.Usage?.PromptTokens ?? 0,
            completion?.Usage?.CompletionTokens ?? 0);
    }

    private Dictionary<string, object?> BuildBody(
        IReadOnlyList<ExcelRowItem> batch, CategorizationOptions options)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = _settings.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = CategorizationPrompt.BuildSystem(options) },
                new { role = "user",   content = CategorizationPrompt.BuildUser(batch) }
            },
            // Kategoriyalash — ijodkorlik talab qilmaydi; 0 barqarorroq natija beradi.
            ["temperature"] = 0,
            ["max_tokens"] = _settings.MaxTokens
        };

        // Provayderlar JSON rejimini turlicha qo'llab-quvvatlaydi — sozlamadan boshqariladi.
        switch (_settings.ResponseFormat?.ToLowerInvariant())
        {
            case "json_schema":
                body["response_format"] = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "kategoriyalash",
                        strict = true,
                        schema = CategorizationPrompt.BuildSchema(options)
                    }
                };
                break;

            case "none":
                break;   // faqat prompt orqali (eski Ollama versiyalari uchun)

            default:
                body["response_format"] = new { type = "json_object" };
                break;
        }

        return body;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = $"AI provayderi {(int)response.StatusCode} qaytardi: {Preview(body)}";

        // 429 (limit) va 5xx (server) — vaqtinchalik, qayta urinish mantiqiy.
        if (response.StatusCode == HttpStatusCode.TooManyRequests ||
            (int)response.StatusCode >= 500)
        {
            throw new AiTransientException(message);
        }

        // 401/403 — kalit muammosi, 400 — noto'g'ri model nomi va h.k. Qayta urinish yordam bermaydi.
        throw new InvalidOperationException(message);
    }

    private static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException(
                "Ai:BaseUrl sozlanmagan. appsettings.json ni tekshiring.");

        return url.EndsWith('/') ? url : url + "/";
    }

    private static string Preview(string text) =>
        text.Length <= 300 ? text : text[..300] + "...";

    private static bool IsTransient(Exception ex) => ex switch
    {
        AiTransientException => true,
        HttpRequestException => true,
        TaskCanceledException => true,
        _ => false
    };

    // ------------------------------------------------------- javob DTO'lari

    private sealed record ChatCompletion(
        [property: JsonPropertyName("choices")] List<Choice>? Choices,
        [property: JsonPropertyName("usage")] TokenUsage? Usage);

    private sealed record Choice(
        [property: JsonPropertyName("message")] ChatMessage? Message);

    private sealed record ChatMessage(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record TokenUsage(
        [property: JsonPropertyName("prompt_tokens")] long PromptTokens,
        [property: JsonPropertyName("completion_tokens")] long CompletionTokens);
}

/// <summary>Qayta urinish mantiqiy bo'lgan vaqtinchalik xato.</summary>
public sealed class AiTransientException : Exception
{
    public AiTransientException(string message) : base(message) { }
    public AiTransientException(string message, Exception inner) : base(message, inner) { }
}
