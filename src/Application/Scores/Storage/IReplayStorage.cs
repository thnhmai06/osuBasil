using Basil.Domain.Game.Client;
using Basil.Domain.Game.Scores;

namespace Basil.Application.Scores.Storage;

public interface IReplayStorage
{
	Task SaveAsync(ScoreRecord score, OsuVersion clientVersion, Stream content);

	Task<Stream?> OpenAsync(ScoreRecord score, CancellationToken ct = default);

	Task<Stream?> OpenFramesAsync(ScoreRecord score, CancellationToken ct = default);
}