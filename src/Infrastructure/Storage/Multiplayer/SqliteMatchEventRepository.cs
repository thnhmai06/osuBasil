using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Storage.Batching;
using Dapper;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores what happened in matches.</summary>
internal sealed class SqliteMatchEventRepository(DatabaseBatcher batcher, IUserRepository users)
	: IMatchEventRepository
{
	/// <inheritdoc />
	public Task CreateAsync(MatchEvent matchEvent, CancellationToken cancellationToken = default)
	{
		var parameters = new
		{
			MatchId = matchEvent.Match.Id,
			Type = (long)matchEvent.Type,
			Timestamp = matchEvent.Timestamp.ToUnixTimeMilliseconds(),
			ActorId = matchEvent.Actor?.Id,
			TargetId = matchEvent.Target?.Id,
			matchEvent.Detail
		};
		return batcher.AppendAsync((connection, transaction) => connection.ExecuteAsync(
			"""
			INSERT INTO MatchEvents (MatchId, Type, Timestamp, ActorId, TargetId, Detail)
			VALUES (@MatchId, @Type, @Timestamp, @ActorId, @TargetId, @Detail)
			""",
			parameters, transaction));
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<MatchEvent>> ListAsync(Match match, CancellationToken cancellationToken = default)
	{
		var rows = (await batcher.ReadAsync(connection => connection.QueryAsync<MatchEventRow>(
			"""
			SELECT Type, Timestamp, ActorId, TargetId, Detail
			FROM MatchEvents
			WHERE MatchId = @MatchId
			ORDER BY Timestamp, Id
			""",
			new { MatchId = match.Id }), cancellationToken)).ToList();

		var events = new List<MatchEvent>(rows.Count);
		foreach (var row in rows)
		{
			var actor = row.ActorId is { } actorId ? await users.GetAsync((int)actorId, cancellationToken) : null;
			var target = row.TargetId is { } targetId ? await users.GetAsync((int)targetId, cancellationToken) : null;
			events.Add(new MatchEvent(
				match,
				(MatchEventType)row.Type,
				DateTimeOffset.FromUnixTimeMilliseconds(row.Timestamp),
				actor,
				target,
				row.Detail));
		}

		return events;
	}

	/// <summary>A stored row of the MatchEvents table.</summary>
	private sealed class MatchEventRow
	{
		public long Type { get; set; }
		public long Timestamp { get; set; }
		public long? ActorId { get; set; }
		public long? TargetId { get; set; }
		public string? Detail { get; set; }
	}
}
