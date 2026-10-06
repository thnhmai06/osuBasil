using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Implementations.Common;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Services.Implementations.Multiplayer;

/// <summary>
///     Keeps the stored match history consistent.
/// </summary>
internal sealed class MatchService(
	IMatchRepository matches,
	IRoundRepository rounds,
	IMatchEventRepository events,
	TimeProvider time) : IMatchService
{
	/// <inheritdoc />
	public async Task<int> CloseUnfinishedAsync(CancellationToken cancellationToken = default)
	{
		var allMatches = await Paging.ListAllAsync(
			page => matches.ListAsync(new MatchQuery(false, true), page, cancellationToken),
			cancellationToken);

		var now = time.GetUtcNow();

		foreach (var match in allMatches)
		{
			foreach (var round in (await rounds.ListAsync(match, cancellationToken)).Where(r => r.EndedAt is null))
			{
				round.EndedAt = now;
				round.Aborted = true;
				await rounds.CreateOrUpdateAsync(round, cancellationToken);
			}

			match.Value.EndedAt = now;
			await matches.CreateOrUpdateAsync(match, cancellationToken);
			await events.CreateAsync(
				new MatchEvent(match, MatchEventType.Closed, now, Detail: "Server shutdown recovery"),
				cancellationToken);
		}

		return allMatches.Count;
	}
}