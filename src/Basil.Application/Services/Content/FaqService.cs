using Basil.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Content;

/// <summary>
///     Filesystem-backed FAQ entry storage, shared between `!faq` (<see cref="Bot.CommandDispatcher" />)
///     and the `/faq` HTTP routes — both read the same `StorageOptions.FaqsPath` folder of `.txt` files
///     through this one implementation instead of duplicating the file logic.
/// </summary>
public sealed class FaqService(IOptions<StorageOptions> storage)
{
    public enum CreateResult { Created, AlreadyExists, InvalidName }
    public enum ReplaceResult { Replaced, NotFound, InvalidName }

    public IReadOnlyList<string> ListEntries()
    {
        if (!Directory.Exists(storage.Value.FaqsPath)) return [];

        return Directory.EnumerateFiles(storage.Value.FaqsPath, "*.txt")
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Reads an entry's content, normalized to `\n`-joined lines with no trailing newline —
    /// independent of the file's own line-ending convention.</summary>
    public async Task<string?> ReadEntryAsync(string entry, CancellationToken cancellationToken = default)
    {
        if (!IsSafeEntry(entry)) return null;

        var path = Path.Combine(storage.Value.FaqsPath, $"{entry}.txt");
        if (!File.Exists(path)) return null;

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        return string.Join('\n', lines);
    }

    public async Task<CreateResult> CreateEntryAsync(string entry, Stream content,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeEntry(entry)) return CreateResult.InvalidName;

        Directory.CreateDirectory(storage.Value.FaqsPath);
        var path = Path.Combine(storage.Value.FaqsPath, $"{entry}.txt");
        if (File.Exists(path)) return CreateResult.AlreadyExists;

        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, cancellationToken);
        return CreateResult.Created;
    }

    public async Task<ReplaceResult> ReplaceEntryAsync(string entry, Stream content,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeEntry(entry)) return ReplaceResult.InvalidName;

        var path = Path.Combine(storage.Value.FaqsPath, $"{entry}.txt");
        if (!File.Exists(path)) return ReplaceResult.NotFound;

        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, cancellationToken);
        return ReplaceResult.Replaced;
    }

    public bool DeleteEntry(string entry)
    {
        if (!IsSafeEntry(entry)) return false;

        var path = Path.Combine(storage.Value.FaqsPath, $"{entry}.txt");
        if (!File.Exists(path)) return false;

        File.Delete(path);
        return true;
    }

    /// <summary>
    ///     Entry names behave like normal filenames — spaces and most punctuation are fine. `\` is
    ///     rejected since .NET only treats it as a path separator on Windows (a Linux deployment would
    ///     otherwise let `..\..\secret` through untouched), and a literal `..` is rejected outright as
    ///     defense in depth against path traversal.
    /// </summary>
    private static bool IsSafeEntry(string entry)
    {
        return entry.Length > 0 && !entry.Contains('\\') && !entry.Contains("..");
    }
}
