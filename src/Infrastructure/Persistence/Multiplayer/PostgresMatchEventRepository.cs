using System.Collections.Immutable;
using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Persistence.Users;
using Basil.Domain.Multiplayer;
using Basil.Common.Infrastructure.Memory;
using Basil.Common.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Multiplayer;

/// <summary>Stores what happened in matches.</summary>
internal sealed class PostgresMatchEventRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IMatchRepository matches,
	IUserRepository users)
	: IMatchEventRepository
{
	private readonly OwnedLists<int, MatchRecord, MatchEvent> _byMatch = new();

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
		var appended = writer.AppendAsync(Root.Match(row.MatchId),
			new WriteCommand<MultiplayerDbContext>(async (context, token) =>
			{
				context.MatchEvents.Add(row);
				await context.SaveChangesAsync(token);
			}));
		_byMatch.Change(matchEvent.Match.Id, matchEvent.Match, list => Insert(list, matchEvent), appended);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<MatchEvent>> ListAsync(MatchRecord match,
		CancellationToken cancellationToken = default)
	{
		var liveMatch = await matches.GetAsync(match.Id, cancellationToken) ?? match;
		return await _byMatch.GetOrLoadAsync(liveMatch.Id, liveMatch, async () =>
		{
			var rows = await reader.ReadAsync((MultiplayerDbContext context) => context.MatchEvents
				.Where(row => row.MatchId == liveMatch.Id)
				.OrderBy(row => row.Timestamp)
				.ThenBy(row => row.Id)
				.ToListAsync(cancellationToken), cancellationToken);

			var events = ImmutableList.CreateBuilder<MatchEvent>();
			foreach (var row in rows)
			{
				var actor = row.ActorId is { } actorId ? await users.GetAsync(actorId, cancellationToken) : null;
				var target = row.TargetId is { } targetId ? await users.GetAsync(targetId, cancellationToken) : null;
				events.Add(new MatchEvent(
					liveMatch,
					(MatchEventType)row.Type,
					row.Timestamp,
					actor,
					target,
					row.Detail));
			}

			return events.ToImmutable();
		});
	}

	private static ImmutableList<MatchEvent> Insert(ImmutableList<MatchEvent> events, MatchEvent matchEvent)
	{
		// A load that already read the committed event must not get it twice.
		if (events.Any(item => SameEvent(item, matchEvent)))
			return events;

		var index = events.FindIndex(item => item.Timestamp > matchEvent.Timestamp);
		return index < 0 ? events.Add(matchEvent) : events.Insert(index, matchEvent);
	}

	/// <summary>Determines whether two events are the same, with the timestamp compared to the microsecond the database keeps.</summary>
	private static bool SameEvent(MatchEvent left, MatchEvent right)
	{
		return left.Type == right.Type && left.Timestamp.UtcTicks / 10 == right.Timestamp.UtcTicks / 10 &&
		       Equals(left.Actor, right.Actor) && Equals(left.Target, right.Target) && left.Detail == right.Detail;
	}
}