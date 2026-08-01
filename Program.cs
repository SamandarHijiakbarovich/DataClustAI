using Anthropic;
using ExcelAiCategorizer.Models;
using ExcelAiCategorizer.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

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
