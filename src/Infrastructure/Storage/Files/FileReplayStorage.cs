using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Scores;

namespace Basil.Infrastructure.Storage.Files;

/// <summary>Stores the raw <c>.osr</c> bytes of scores' replays, one file per score.</summary>
internal sealed class FileReplayStorage(DataPaths paths) : IReplayStorage
{
	/// <inheritdoc />
	public Task SaveAsync(Score score, Stream content, CancellationToken cancellationToken = default)
	{
		return FileStorage.SaveAsync(Path.Combine(paths.Replays, $"{score.Id}.osr"), content, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(Score score, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(FileStorage.Open(Path.Combine(paths.Replays, $"{score.Id}.osr")));
	}
}
