using Basil.Application.Shared;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Stores what happened in matches.</summary>
internal sealed class EfMatchEventRepository(DatabaseSession database, IUserRepository users)
	: IMatchEventRepository
{
	/// <inheritdoc />
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

	/// <inheritdoc />
	public async Task<IReadOnlyList<MatchEvent>> ListAsync(MatchRecord match,
		CancellationToken ct = default)
	{
		var rows = await database.ReadAsync((MultiplayerDbContext context) => context.MatchEvents
			.Where(row => row.MatchId == match.Id)
			.OrderBy(row => row.Timestamp)
			.ThenBy(row => row.Id)
			.Take(ListLimits.Ceiling)
			.ToListAsync(ct), ct);

		var events = new List<MatchEvent>(rows.Count);
		foreach (var row in rows)
		{
			var actor = row.ActorId is { } actorId ? await users.GetAsync(actorId, ct) : null;
			var target = row.TargetId is { } targetId ? await users.GetAsync(targetId, ct) : null;
			events.Add(new MatchEvent(match, (MatchEventType)row.Type, row.Timestamp, actor, target, row.Detail));
		}

		return events;
	}
}