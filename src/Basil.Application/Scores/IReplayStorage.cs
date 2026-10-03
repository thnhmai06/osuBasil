using Basil.Domain.Scores;

namespace Basil.Application.Scores;

/// <summary>Stores the replays of scores.</summary>
public interface IReplayStorage
{
	/// <summary>Stores the replay of a score, replacing any replay already stored for it.</summary>
	/// <param name="score">The score the replay belongs to.</param>
	/// <param name="content">The replay bytes.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SaveAsync(Score score, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Opens the stored replay of a score.</summary>
	/// <param name="score">The score the replay belongs to.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The replay bytes, or <see langword="null" /> when no replay is stored for the score.</returns>
	Task<Stream?> OpenAsync(Score score, CancellationToken cancellationToken = default);
}