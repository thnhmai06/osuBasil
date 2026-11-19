using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Report.Rounds;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

public sealed class RoundSettlement
{
	private readonly HashSet<UserRecord> _completed = [];
	private readonly Dictionary<UserRecord, GameTeam?> _participants;
	private readonly HashSet<UserRecord> _struckOff = [];

	internal RoundSettlement(Round round, IEnumerable<(UserRecord Player, GameTeam? Team)> participants)
	{
		Round = round;
		_participants = participants.ToDictionary(p => p.Player, p => p.Team);
	}

	public Round Round { get; }

	public List<RoundScore> Scores { get; } = [];

	public bool Closed { get; private set; }

	public bool HasParticipants => _participants.Count > 0;

	public bool HasCompletions => _completed.Count > 0;

	public void Complete(UserRecord player)
	{
		if (_participants.ContainsKey(player)) _completed.Add(player);
	}

	public void Leave(UserRecord player)
	{
		if (_participants.ContainsKey(player) && !_completed.Contains(player)) _struckOff.Add(player);
	}

	public bool Awaits(UserRecord player, out GameTeam? team)
	{
		team = null;
		return !Closed && _participants.TryGetValue(player, out team) && !_struckOff.Contains(player) &&
		       Scores.All(s => s.Score.Value.UserId != player.Id);
	}

	public IReadOnlyList<UserRecord> Missing()
	{
		return [.. _participants.Keys.Where(player => Awaits(player, out _))];
	}

	public void Close()
	{
		Closed = true;
	}
}