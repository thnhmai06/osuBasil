using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Storage.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores what happened in matches.</summary>
internal sealed class PostgresMatchEventRepository(Database database, DatabaseWriter writer, IUserRepository users)
	: IMatchEventRepository
{
	/// <inheritdoc />
	public Task CreateAsync(MatchEvent matchEvent, CancellationToken cancellationToken = default)
	{
		var parameters = new
		{
			MatchId = matchEvent.Match.Id,
			Type = (int)matchEvent.Type,
			Timestamp = matchEvent.Timestamp.ToUniversalTime(),
			ActorId = matchEvent.Actor?.Id,
			TargetId = matchEvent.Target?.Id,
			matchEvent.Detail
		};
		_ = writer.AppendAsync(Root.Match(matchEvent.Match.Id), new WriteCommand(
			"""
			insert into match_events (match_id, type, timestamp, actor_id, target_id, detail)
			values (@MatchId, @Type, @Timestamp, @ActorId, @TargetId, @Detail)
			""",
			parameters));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<MatchEvent>> ListAsync(Match match, CancellationToken cancellationToken = default)
	{
		var rows = (await database.ReadAsync(connection => connection.QueryAsync<MatchEventRow>(
			"""
			select type, timestamp, actor_id, target_id, detail
			from match_events
			where match_id = @MatchId
			order by timestamp, id
			""",
			new { MatchId = match.Id }), cancellationToken)).ToList();

		var events = new List<MatchEvent>(rows.Count);
		foreach (var row in rows)
		{
			var actor = row.ActorId is { } actorId ? await users.GetAsync(actorId, cancellationToken) : null;
			var target = row.TargetId is { } targetId ? await users.GetAsync(targetId, cancellationToken) : null;
			events.Add(new MatchEvent(
				match,
				(MatchEventType)row.Type,
				row.Timestamp,
				actor,
				target,
				row.Detail));
		}

		return events;
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
