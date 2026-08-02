using Anthropic;
using ExcelAiCategorizer.Models;
using ExcelAiCategorizer.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Render/konteyner muhitida PORT muhit o'zgaruvchisi orqali berilgan portni tinglaymiz.
// Lokalda PORT bo'lmaydi — launchSettings o'z portini ishlatadi.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Render TLS'ni proxy'da tugatib, ilovaga HTTP forward qiladi.
// Original sxemani (https) tanish uchun forwarded header'larni qabul qilamiz —
// aks holda UseHttpsRedirection cheksiz redirect qilib qo'yadi.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---------------------------------------------------------------- sozlamalar

builder.Services.Configure<AiSettings>(
    builder.Configuration.GetSection(AiSettings.SectionName));

builder.Services.Configure<UploadSettings>(
    builder.Configuration.GetSection(UploadSettings.SectionName));

// Multipart yuklash chegarasi (controller'dagi RequestSizeLimit bilan mos)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 52_428_800; // 50 MB
});

// ---------------------------------------------------------------- AI provayderi

var aiSettings = builder.Configuration
    .GetSection(AiSettings.SectionName)
    .Get<AiSettings>() ?? new AiSettings();

var useAnthropic = string.Equals(
    aiSettings.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase);

if (useAnthropic)
{
    // AnthropicClient thread-safe va ichida HttpClient saqlaydi — singleton bo'lishi shart.
    // ApiKey bo'sh bo'lsa, SDK o'zi ANTHROPIC_API_KEY muhit o'zgaruvchisini o'qiydi.
    builder.Services.AddSingleton(serviceProvider =>
    {
        var settings = serviceProvider.GetRequiredService<IOptions<AiSettings>>().Value;

        return string.IsNullOrWhiteSpace(settings.ApiKey)
            ? new AnthropicClient()
            : new AnthropicClient { ApiKey = settings.ApiKey };
    });

    builder.Services.AddSingleton<IAiCategorizationService, ClaudeCategorizationService>();
}
else
{
    // Gemini / Groq / OpenRouter / Ollama — hammasi OpenAI-mos protokol orqali.
    builder.Services.AddHttpClient(OpenAiCompatibleCategorizationService.HttpClientName);
    builder.Services.AddSingleton<IAiCategorizationService, OpenAiCompatibleCategorizationService>();
}

// ---------------------------------------------------------------- ilova servislari

builder.Services.AddSingleton<IExcelService, ExcelService>();
builder.Services.AddSingleton<IJobStore, InMemoryJobStore>();
builder.Services.AddSingleton<IJobQueue, ChannelJobQueue>();
builder.Services.AddSingleton<IFileStorage, FileStorage>();

// Fon xizmatlari
builder.Services.AddHostedService<CategorizationWorker>();
builder.Services.AddHostedService<CleanupService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// ---------------------------------------------------------------- pipeline

// Proxy header'larini boshqa middleware'lardan oldin qo'llash shart.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
