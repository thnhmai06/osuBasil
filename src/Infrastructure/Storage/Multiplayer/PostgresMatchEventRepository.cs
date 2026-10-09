using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Storage.Memory;
using Basil.Infrastructure.Storage.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores what happened in matches.</summary>
internal sealed class PostgresMatchEventRepository(
	Database database,
	DatabaseWriter writer,
	IMatchRepository matches,
	IUserRepository users)
	: IMatchEventRepository
{
	private readonly OwnedLists<int, Match, MatchEvent> _byMatch = new();
	private readonly IdSequence _ids = new(database, "match_events");

	/// <inheritdoc />
	public async Task CreateAsync(MatchEvent matchEvent, CancellationToken cancellationToken = default)
	{
		var id = await _ids.NextAsync(cancellationToken);
		var parameters = new
		{
			Id = id,
			MatchId = matchEvent.Match.Id,
			Type = (int)matchEvent.Type,
			Timestamp = matchEvent.Timestamp.ToUniversalTime(),
			ActorId = matchEvent.Actor?.Id,
			TargetId = matchEvent.Target?.Id,
			matchEvent.Detail
		};
		var appended = writer.AppendAsync(Root.Match(matchEvent.Match.Id), new WriteCommand(
			"""
			insert into match_events (id, match_id, type, timestamp, actor_id, target_id, detail)
			values (@Id, @MatchId, @Type, @Timestamp, @ActorId, @TargetId, @Detail)
			on conflict (id) do nothing
			""",
			parameters));
		_byMatch.Change(matchEvent.Match.Id, matchEvent.Match, list => Insert(list, matchEvent), appended);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<MatchEvent>> ListAsync(Match match, CancellationToken cancellationToken = default)
	{
		var liveMatch = await matches.GetAsync(match.Id, cancellationToken) ?? match;
		return await _byMatch.GetOrLoadAsync(liveMatch.Id, liveMatch, async () =>
		{
			var rows = await database.ReadAsync(connection => connection.QueryAsync<MatchEventRow>(
				"""
				select type, timestamp, actor_id, target_id, detail
				from match_events
				where match_id = @MatchId
				order by timestamp, id
				""",
				new { MatchId = liveMatch.Id }), cancellationToken);

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
		if (events.Contains(matchEvent))
			return events;

		var index = events.FindIndex(item => item.Timestamp > matchEvent.Timestamp);
		return index < 0 ? events.Add(matchEvent) : events.Insert(index, matchEvent);
	}

	/// <summary>A stored row of the <c>match_events</c> table.</summary>
	private sealed class MatchEventRow
	{
		public int Type { get; set; }
		public DateTimeOffset Timestamp { get; set; }
		public int? ActorId { get; set; }
		public int? TargetId { get; set; }
		public string? Detail { get; set; }
	}
}