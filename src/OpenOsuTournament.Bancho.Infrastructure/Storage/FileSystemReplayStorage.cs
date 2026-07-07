using Microsoft.Extensions.Options;
using OpenOsuTournament.Bancho.Application.Abstractions.Scores;
using OpenOsuTournament.Bancho.Application.Configuration;

namespace OpenOsuTournament.Bancho.Infrastructure.Storage;

/// <inheritdoc cref="IReplayStorage" />
public sealed class FileSystemReplayStorage(IOptions<StorageOptions> options) : IReplayStorage
{
    public async Task WriteAsync(long scoreId, byte[] data, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.Value.ReplaysPath);
        await File.WriteAllBytesAsync(PathFor(scoreId), data, cancellationToken);
    }

    public async Task<byte[]?> ReadAsync(long scoreId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(scoreId);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    private string PathFor(long scoreId)
    {
        return Path.Combine(options.Value.ReplaysPath, $"{scoreId}.osr");
    }
}