using System.Diagnostics;
using ExcelAiCategorizer.Models;
using ExcelAiCategorizer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ExcelAiCategorizer.Controllers;

public sealed class HomeController : Controller
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IJobStore _store;
    private readonly IJobQueue _queue;
    private readonly IFileStorage _storage;
    private readonly UploadSettings _uploadSettings;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IJobStore store,
        IJobQueue queue,
        IFileStorage storage,
        IOptions<UploadSettings> uploadSettings,
        ILogger<HomeController> logger)
    {
        _store = store;
        _queue = queue;
        _storage = storage;
        _uploadSettings = uploadSettings.Value;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index() => View(new UploadViewModel());

    // ------------------------------------------------------------- yuklash

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(52_428_800)]   // 50 MB — haqiqiy chegara sozlamadan tekshiriladi
    public async Task<IActionResult> Start(UploadViewModel model, CancellationToken ct)
    {
        Validate(model);

        if (!ModelState.IsValid)
            return View(nameof(Index), model);

        var jobId = Guid.NewGuid();

        await using var stream = model.File!.OpenReadStream();
        var sourcePath = await _storage.SaveUploadAsync(jobId, stream, ct);

        var job = new CategorizationJob
        {
            Id = jobId,
            FileName = Path.GetFileName(model.File.FileName),
            SourcePath = sourcePath,
            RequestedColumn = string.IsNullOrWhiteSpace(model.ColumnName)
                ? null
                : model.ColumnName.Trim(),
            Options = new CategorizationOptions
            {
                Categories = model.ParseCategories(),
                AllowNewCategories = model.AllowNewCategories,
                Context = model.Context?.Trim() ?? string.Empty
            }
        };

        _store.Add(job);
        await _queue.EnqueueAsync(jobId, ct);

        _logger.LogInformation("Yangi vazifa navbatga qo'yildi: {JobId} ({File})",
            jobId, job.FileName);

        return RedirectToAction(nameof(Progress), new { id = jobId });
    }

    private void Validate(UploadViewModel model)
    {
        if (model.File is null || model.File.Length == 0)
        {
            ModelState.AddModelError(nameof(model.File), "Fayl tanlanmagan yoki bo'sh.");
            return;
        }

        var extension = Path.GetExtension(model.File.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(model.File),
                "Faqat .xlsx formatidagi fayllar qo'llab-quvvatlanadi (.xls emas).");

        if (model.File.Length > _uploadSettings.MaxFileSizeBytes)
            ModelState.AddModelError(nameof(model.File),
                $"Fayl hajmi {_uploadSettings.MaxFileSizeMb} MB dan oshmasligi kerak.");

        if (!model.AllowNewCategories && model.ParseCategories().Count == 1)
            ModelState.AddModelError(nameof(model.CategoriesRaw),
                "Kamida ikkita kategoriya kiriting yoki AI ga yangi kategoriya yaratishga ruxsat bering.");
    }

    // ------------------------------------------------------------- kuzatish

    [HttpGet]
    public IActionResult Progress(Guid id)
    {
        var job = _store.Get(id);
        if (job is null) return NotFound("Bunday vazifa topilmadi yoki muddati o'tgan.");

        return View(job);
    }

    /// <summary>Brauzer har 2 soniyada shu endpointni so'raydi.</summary>
    [HttpGet]
    public IActionResult Status(Guid id)
    {
        var job = _store.Get(id);
        if (job is null) return NotFound();

        return Json(new JobStatusDto(
            Id: job.Id.ToString(),
            Status: job.Status.ToString(),
            Progress: job.ProgressPercent,
            ProcessedRows: job.ProcessedRows,
            TotalRows: job.TotalRows,
            FailedRows: job.FailedRows,
            InputTokens: job.InputTokens,
            OutputTokens: job.OutputTokens,
            Error: job.ErrorMessage,
            IsDownloadReady: job.Status == JobStatus.Completed && job.ResultPath is not null,
            Summary: job.Summary,
            Overview: job.Overview));
    }

    // ------------------------------------------------------------- yuklab olish

    [HttpGet]
    public IActionResult Download(Guid id)
    {
        var job = _store.Get(id);

        if (job?.ResultPath is null || !System.IO.File.Exists(job.ResultPath))
            return NotFound("Natija fayli hali tayyor emas yoki o'chirilgan.");

        return PhysicalFile(job.ResultPath, XlsxContentType, job.ResultFileName);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
}
