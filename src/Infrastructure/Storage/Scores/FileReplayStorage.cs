using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Scores;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Options;

namespace Basil.Infrastructure.Storage.Scores;

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
		return Task.FromResult<Stream?>(FileStorage.Read(Path.Combine(paths.Replays, $"{score.Id}.osr")));
	}
}