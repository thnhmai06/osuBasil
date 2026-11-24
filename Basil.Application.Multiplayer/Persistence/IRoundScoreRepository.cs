using Basil.Domain.Multiplayer.Report.Rounds;

namespace Basil.Application.Multiplayer.Persistence;

public interface IRoundScoreRepository
{
	Task<RoundScore> RecordAsync(RoundScore roundScore);

	/// <summary>Lists the scores of all of <paramref name="rounds"/>, which belong to one match.</summary>
	Task<IReadOnlyList<RoundScore>> ListAsync(IReadOnlyCollection<Round> rounds, CancellationToken ct = default);

	Task<IReadOnlyList<RoundScore>> ListAsync(Round round, CancellationToken ct = default);
}