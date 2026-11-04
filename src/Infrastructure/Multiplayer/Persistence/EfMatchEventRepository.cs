using Basil.Application.Common;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Stores what happened in matches.</summary>
internal sealed class EfMatchEventRepository(DatabaseSession database, IUserRepository users)
	: IMatchEventRepository
{
	/// <inheritdoc />
	public Task RecordAsync(MatchEvent matchEvent, CancellationToken cancellationToken = default)
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
		return database.WriteAsync((MultiplayerDbContext context, CancellationToken _) =>
		{
			context.MatchEvents.Add(row);
			return Task.CompletedTask;
		}, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<MatchEvent>> ListAsync(MatchRecord match,
		CancellationToken cancellationToken = default)
	{
		var rows = await database.ReadAsync((MultiplayerDbContext context) => context.MatchEvents
			.Where(row => row.MatchId == match.Id)
			.OrderBy(row => row.Timestamp)
			.ThenBy(row => row.Id)
			.Take(ListLimits.Ceiling)
			.ToListAsync(cancellationToken), cancellationToken);

		var events = new List<MatchEvent>(rows.Count);
		foreach (var row in rows)
		{
			var actor = row.ActorId is { } actorId ? await users.GetAsync(actorId, cancellationToken) : null;
			var target = row.TargetId is { } targetId ? await users.GetAsync(targetId, cancellationToken) : null;
			events.Add(new MatchEvent(match, (MatchEventType)row.Type, row.Timestamp, actor, target, row.Detail));
		}

		return events;
	}
}
