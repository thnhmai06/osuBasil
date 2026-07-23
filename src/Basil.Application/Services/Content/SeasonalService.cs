using Basil.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Content;

/// <summary>
///     Filesystem-backed seasonal background image storage — shared between the client-facing
///     `GET /web/osu-getseasonal.php`/`GET /seasonal/{fileName}` pair (osu! client protocol) and the
///     admin `/seasonal` HTTP routes, both of which operate on the same `StorageOptions.SeasonalsPath`
///     folder.
/// </summary>
public sealed class SeasonalService(IOptions<StorageOptions> storage)
{
    public enum CreateResult { Created, AlreadyExists }
    public enum ReplaceResult { Replaced, NotFound }

    public IReadOnlyList<string> ListFileNames()
    {
        Directory.CreateDirectory(storage.Value.SeasonalsPath);
        return Directory.EnumerateFiles(storage.Value.SeasonalsPath)
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? FindFilePath(string fileName)
    {
        var path = Path.Combine(storage.Value.SeasonalsPath, Path.GetFileName(fileName));
        return File.Exists(path) ? path : null;
    }

    public async Task<CreateResult> CreateAsync(string fileName, Stream content,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(storage.Value.SeasonalsPath);
        var path = Path.Combine(storage.Value.SeasonalsPath, Path.GetFileName(fileName));
        if (File.Exists(path)) return CreateResult.AlreadyExists;

        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, cancellationToken);
        return CreateResult.Created;
    }

    public async Task<ReplaceResult> ReplaceAsync(string fileName, Stream content,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(storage.Value.SeasonalsPath, Path.GetFileName(fileName));
        if (!File.Exists(path)) return ReplaceResult.NotFound;

        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, cancellationToken);
        return ReplaceResult.Replaced;
    }

    public bool Delete(string fileName)
    {
        var path = Path.Combine(storage.Value.SeasonalsPath, Path.GetFileName(fileName));
        if (!File.Exists(path)) return false;

        File.Delete(path);
        return true;
    }
}
