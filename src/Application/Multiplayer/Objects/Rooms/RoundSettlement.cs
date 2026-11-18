using Basil.Domain.Game.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

internal sealed class RoundSettlement
{
	private readonly HashSet<UserRecord> _completed = [];
	private readonly Dictionary<UserRecord, GameTeam?> _participants;
	private readonly HashSet<UserRecord> _struckOff = [];

	internal RoundSettlement(Round round, IEnumerable<(UserRecord Player, GameTeam? Team)> participants)
	{
		Round = round;
		_participants = participants.ToDictionary(p => p.Player, p => p.Team);
	}

	internal Round Round { get; }

	internal List<RoundScore> Scores { get; } = [];

	internal bool Closed { get; private set; }

	internal ITimer? Timer { get; set; }

	internal bool HasParticipants => _participants.Count > 0;

	internal bool HasCompletions => _completed.Count > 0;

	internal void Complete(UserRecord player)
	{
		if (_participants.ContainsKey(player)) _completed.Add(player);
	}

	internal void Leave(UserRecord player)
	{
		if (_participants.ContainsKey(player) && !_completed.Contains(player)) _struckOff.Add(player);
	}

	internal bool Awaits(UserRecord player, out GameTeam? team)
	{
		team = null;
		return !Closed && _participants.TryGetValue(player, out team) && !_struckOff.Contains(player) &&
		       Scores.All(s => s.Score.Value.UserId != player.Id);
	}

	internal IReadOnlyList<UserRecord> Missing()
	{
		return [.. _participants.Keys.Where(player => Awaits(player, out _))];
	}

	internal void Close()
	{
		Closed = true;
		Timer?.Dispose();
		Timer = null;
	}
}