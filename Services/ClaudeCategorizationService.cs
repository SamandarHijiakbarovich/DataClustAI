using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using ExcelAiCategorizer.Models;
using Microsoft.Extensions.Options;

namespace ExcelAiCategorizer.Services;

/// <summary>
/// Anthropic Claude API orqali kategoriyalash (rasmiy .NET SDK, pullik).
/// Bepul variant uchun <see cref="OpenAiCompatibleCategorizationService"/> ga qarang.
/// </summary>
public sealed class ClaudeCategorizationService : IAiCategorizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AnthropicClient _client;
    private readonly AiSettings _settings;
    private readonly ILogger<ClaudeCategorizationService> _logger;

    public ClaudeCategorizationService(
        AnthropicClient client,
        IOptions<AiSettings> settings,
        ILogger<ClaudeCategorizationService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<BatchResult> CategorizeBatchAsync(
        IReadOnlyList<ExcelRowItem> batch,
        CategorizationOptions options,
        CancellationToken cancellationToken)
    {
        var parameters = BuildRequest(batch, options);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendAsync(parameters, batch, options, cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt <= _settings.MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));   // 2s, 4s, 8s
                _logger.LogWarning(ex,
                    "AI so'rovi muvaffaqiyatsiz ({Attempt}/{Max}). {Delay}s dan keyin qayta urinamiz.",
                    attempt, _settings.MaxRetries, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private MessageCreateParams BuildRequest(
        IReadOnlyList<ExcelRowItem> batch, CategorizationOptions options)
    {
        return new MessageCreateParams
        {
            Model = _settings.Model,
            MaxTokens = _settings.MaxTokens,

            // O'zgarmas ko'rsatmalar — cache_control tufayli keyingi
            // to'dalarda qayta hisoblanmaydi (arzonroq va tezroq).
            System = new List<TextBlockParam>
            {
                new()
                {
                    Text = CategorizationPrompt.BuildSystem(options),
                    CacheControl = new CacheControlEphemeral()
                }
            },

            OutputConfig = new OutputConfig
            {
                Effort = ParseEffort(_settings.Effort),
                Format = new JsonOutputFormat { Schema = CategorizationPrompt.BuildSchema(options) }
            },

            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = CategorizationPrompt.BuildUser(batch)
                }
            ]
        };
    }

    private async Task<BatchResult> SendAsync(
        MessageCreateParams parameters,
        IReadOnlyList<ExcelRowItem> batch,
        CategorizationOptions options,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));

        var response = await _client.Messages.Create(parameters, cancellationToken: timeout.Token);

        if (response.StopReason == "refusal")
            throw new InvalidOperationException(
                "Model so'rovni bajarishdan bosh tortdi. Ma'lumot mazmunini tekshiring.");

        if (response.StopReason == "max_tokens")
            throw new AiTransientException(
                "Javob token chegarasiga yetdi. Ai:BatchSize ni kamaytiring yoki Ai:MaxTokens ni oshiring.");

        var text = response.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(b => b.Text)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(text))
            throw new AiTransientException("Modeldan bo'sh javob keldi.");

        var parsed = JsonSerializer.Deserialize<BatchCategorizationResponse>(
                         CategorizationPrompt.ExtractJson(text), JsonOptions)
                     ?? throw new AiTransientException("Javobni JSON sifatida o'qib bo'lmadi.");

        return new BatchResult(
            CategorizationPrompt.Normalize(parsed.Items, batch, options),
            response.Usage.InputTokens,
            response.Usage.OutputTokens);
    }

    private static Effort ParseEffort(string value) =>
        Enum.TryParse<Effort>(value, ignoreCase: true, out var effort) ? effort : Effort.Medium;

    /// <summary>Qayta urinish mantiqiy bo'lgan xatolarni ajratadi.</summary>
    private static bool IsTransient(Exception ex) => ex switch
    {
        AnthropicRateLimitException => true,      // 429
        Anthropic5xxException => true,            // 500 / 529
        HttpRequestException => true,             // tarmoq uzilishi
        TaskCanceledException => true,            // timeout
        JsonException => true,                    // noto'g'ri JSON
        AiTransientException => true,             // bo'sh javob / max_tokens
        _ => false
    };
}
