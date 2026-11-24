using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

internal sealed class EfMatchEventRepository(DatabaseSession database, IUserRepository users)
	: IMatchEventRepository
{
	public Task RecordAsync(MatchEvent matchEvent)
	{
		var row = new MatchEventRow
		{
			MatchId = matchEvent.Match.Id,
			Type = (int)matchEvent.Type,
			Timestamp = matchEvent.Timestamp.ToUniversalTime(),
			ActorId = matchEvent.Actor?.Id,
			TargetId = matchEvent.Target?.Id,
			Detail = matchEvent.Detail
		};
		return database.WriteAsync(async (MultiplayerDbContext context) =>
		{
			row.Id = await IdCounters.NextAsync(context, "match_event");
			context.MatchEvents.Add(row);
		});
	}

	public async Task<IReadOnlyList<MatchEvent>> ListAsync(MatchRecord match,
		CancellationToken ct = default)
	{
		var rows = await database.ReadAsync((MultiplayerDbContext context) => context.MatchEvents
			.Where(row => row.MatchId == match.Id)
			.OrderBy(row => row.Timestamp)
			.ThenBy(row => row.Id)
			.Take(ListLimits.Ceiling)
			.ToListAsync(ct), ct);

		var ids = rows.SelectMany(row => new[] { row.ActorId, row.TargetId }).OfType<int>().Distinct().ToArray();
		var found = (await users.GetManyAsync(ids, ct)).ToDictionary(user => user.Id);
		return
		[
			.. rows.Select(row => new MatchEvent(match, (MatchEventType)row.Type, row.Timestamp,
				row.ActorId is { } actorId ? found.GetValueOrDefault(actorId) : null,
				row.TargetId is { } targetId ? found.GetValueOrDefault(targetId) : null, row.Detail))
		];
	}
}