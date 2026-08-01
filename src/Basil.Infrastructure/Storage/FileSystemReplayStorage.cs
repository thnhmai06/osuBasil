using Basil.Application.Abstractions.Scores;
using Basil.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Storage;

/// <inheritdoc cref="IReplayStorage" />
/// <remarks>
///     Stores each replay as a single <c>.osr</c> file named <c>{scoreId}.osr</c> in
///     <see cref="StorageOptions.ReplaysPath" />. A read of a score with no stored replay returns
///     null rather than throwing.
/// </remarks>
public sealed class FileSystemReplayStorage(IOptions<StorageOptions> options) : IReplayStorage
{
	/// <inheritdoc />
	/// <remarks>Creates the replays folder when it does not yet exist, then writes the file.</remarks>
	public async Task WriteAsync(long scoreId, byte[] data, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(options.Value.ReplaysPath);
		await File.WriteAllBytesAsync(PathFor(scoreId), data, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<byte[]?> ReadAsync(long scoreId, CancellationToken cancellationToken = default)
	{
		var path = PathFor(scoreId);
		return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
	}

	/// <summary>Builds the absolute path of a score's replay file.</summary>
	/// <param name="scoreId">The id of the score.</param>
	/// <returns>The absolute path of the <c>.osr</c> file for the score.</returns>
	private string PathFor(long scoreId)
	{
		return Path.Combine(options.Value.ReplaysPath, $"{scoreId}.osr");
	}
}