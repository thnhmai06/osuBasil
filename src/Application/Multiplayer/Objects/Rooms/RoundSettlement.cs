using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

/// <summary>
///     What a round waits for after it was played: the score of each player who took part, until the round is decided
///     or aborted.
/// </summary>
/// <remarks>Changed only under the room's lock.</remarks>
internal sealed class RoundSettlement
{
	private readonly HashSet<UserRecord> _completed = [];
	private readonly Dictionary<UserRecord, GameTeam?> _participants;
	private readonly HashSet<UserRecord> _struckOff = [];

	/// <summary>Starts the settlement of a round with the players who started it.</summary>
	/// <param name="round">The round.</param>
	/// <param name="participants">The players who took part, with the team each played in.</param>
	internal RoundSettlement(Round round, IEnumerable<(UserRecord Player, GameTeam? Team)> participants)
	{
		Round = round;
		_participants = participants.ToDictionary(p => p.Player, p => p.Team);
	}

	/// <summary>The round this settlement belongs to.</summary>
	internal Round Round { get; }

	/// <summary>The scores that came in, in the order they came.</summary>
	internal List<RoundScore> Scores { get; } = [];

	/// <summary>Whether the round has been decided or aborted, so no score is taken any more.</summary>
	internal bool Closed { get; private set; }

	/// <summary>The timer that decides the round when scores are missing.</summary>
	internal ITimer? Timer { get; set; }

	/// <summary>Whether any player took part.</summary>
	internal bool HasParticipants => _participants.Count > 0;

	/// <summary>Whether at least one player completed the round instead of leaving it.</summary>
	internal bool HasCompletions => _completed.Count > 0;

	/// <summary>Records that a player completed the round.</summary>
	/// <param name="player">The player.</param>
	internal void Complete(UserRecord player)
	{
		if (_participants.ContainsKey(player)) _completed.Add(player);
	}

	/// <summary>Drops a player who left before completing: no score is awaited from them.</summary>
	/// <param name="player">The player who left.</param>
	internal void Leave(UserRecord player)
	{
		if (_participants.ContainsKey(player) && !_completed.Contains(player)) _struckOff.Add(player);
	}

	/// <summary>Finds the team a player played in, when the player's score is still awaited.</summary>
	/// <param name="player">The player.</param>
	/// <param name="team">The team the player played in.</param>
	/// <returns><see langword="true" /> when a score from the player is awaited.</returns>
	internal bool Awaits(UserRecord player, out GameTeam? team)
	{
		team = null;
		return !Closed && _participants.TryGetValue(player, out team) && !_struckOff.Contains(player) &&
		       Scores.All(s => s.Score.Value.UserId != player.Id);
	}

	/// <summary>Lists the players whose score has not come in and is still awaited.</summary>
	internal IReadOnlyList<UserRecord> Missing()
	{
		return [.. _participants.Keys.Where(player => Awaits(player, out _))];
	}

	/// <summary>Stops the settlement: the round is decided or aborted, and the timer is gone.</summary>
	internal void Close()
	{
		Closed = true;
		Timer?.Dispose();
		Timer = null;
	}
}