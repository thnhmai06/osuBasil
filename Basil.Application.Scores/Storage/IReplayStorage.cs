using Basil.Domain.Client;
using Basil.Domain.Scores;

namespace Basil.Application.Scores.Storage;

public interface IReplayStorage
{
	Task SaveAsync(ScoreRecord score, OsuVersion clientVersion, Stream content);

	Task<Stream?> OpenAsync(ScoreRecord score, CancellationToken ct = default);

	Task<Stream?> OpenFramesAsync(ScoreRecord score, CancellationToken ct = default);
}