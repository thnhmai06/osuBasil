using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Storage.Common;
using Basil.Application.Storage.Multiplayer;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Services.Multiplayer;

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
		var page = new PageRequest(0, 100);
		var allMatches = new List<Match>();

		while (true)
		{
			var paged = await matches.ListAsync(new MatchQuery(Ended: false, IncludePrivate: true), page, cancellationToken);
			allMatches.AddRange(paged.Items);
			if (paged.Items.Count < page.Limit)
				break;
			page = new PageRequest(page.Offset + page.Limit, page.Limit);
		}

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
			await events.CreateAsync(new MatchEvent(match, MatchEventType.Closed, now, Detail: "Server shutdown recovery"), cancellationToken);
		}

		return allMatches.Count;
	}
}