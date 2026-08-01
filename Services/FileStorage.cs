using ExcelAiCategorizer.Models;
using Microsoft.Extensions.Options;

namespace ExcelAiCategorizer.Services;

public interface IFileStorage
{
    Task<string> SaveUploadAsync(Guid jobId, Stream content, CancellationToken ct);
    Task<string> SaveResultAsync(Guid jobId, byte[] content, CancellationToken ct);
    void DeleteQuietly(string? path);
}

/// <summary>
/// Fayllarni diskda saqlaydi (App_Data/uploads va App_Data/results).
/// Xotirada saqlash katta fayllarda serverni bo'g'ib qo'yadi.
/// </summary>
public sealed class FileStorage : IFileStorage
{
    private readonly string _uploadsPath;
    private readonly string _resultsPath;
    private readonly ILogger<FileStorage> _logger;

    public FileStorage(
        IOptions<UploadSettings> settings,
        IWebHostEnvironment environment,
        ILogger<FileStorage> logger)
    {
        _logger = logger;

        var root = Path.Combine(environment.ContentRootPath, settings.Value.StorageRoot);
        _uploadsPath = Path.Combine(root, "uploads");
        _resultsPath = Path.Combine(root, "results");

        Directory.CreateDirectory(_uploadsPath);
        Directory.CreateDirectory(_resultsPath);
    }

    public async Task<string> SaveUploadAsync(Guid jobId, Stream content, CancellationToken ct)
    {
        // Fayl nomi sifatida faqat GUID ishlatiladi — foydalanuvchi bergan nom
        // hech qachon yo'lga qo'shilmaydi (path traversal himoyasi).
        var path = Path.Combine(_uploadsPath, $"{jobId:N}.xlsx");

        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);

        return path;
    }

    public async Task<string> SaveResultAsync(Guid jobId, byte[] content, CancellationToken ct)
    {
        var path = Path.Combine(_resultsPath, $"{jobId:N}.xlsx");
        await File.WriteAllBytesAsync(path, content, ct);
        return path;
    }

    public void DeleteQuietly(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Faylni o'chirib bo'lmadi: {Path}", path);
        }
    }
}
