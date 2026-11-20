using Basil.Domain.Game;
using Basil.Domain.Multiplayer.Report.Rounds;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>Collects the scores a round's players submit and tells who is still owed.</summary>
public sealed class RoundSettlement
{
	private readonly HashSet<UserRecord> _completed = [];
	private readonly Dictionary<UserRecord, GameTeam?> _participants;
	private readonly HashSet<UserRecord> _struckOff = [];

	/// <summary>Creates the settlement of a round for the players taking part.</summary>
	/// <param name="round">The round.</param>
	/// <param name="participants">The players taking part, each with their team at the start of the round.</param>
	internal RoundSettlement(Round round, IEnumerable<(UserRecord Player, GameTeam? Team)> participants)
	{
		Round = round;
		_participants = participants.ToDictionary(p => p.Player, p => p.Team);
	}

	/// <summary>Gets the round being settled.</summary>
	public Round Round { get; }

	private readonly List<RoundScore> _scores = [];

	/// <summary>Gets the scores received so far.</summary>
	public IReadOnlyList<RoundScore> Scores => _scores;

	/// <summary>Gets whether the settlement is closed and accepts no more scores.</summary>
	public bool Closed { get; private set; }

	/// <summary>Gets whether any player took part in the round.</summary>
	public bool HasParticipants => _participants.Count > 0;

	/// <summary>Gets whether any player finished playing the round.</summary>
	public bool HasCompletions => _completed.Count > 0;

	/// <summary>Records that a participant finished playing; a non-participant is ignored.</summary>
	/// <param name="player">The player.</param>
	internal void Complete(UserRecord player)
	{
		if (_participants.ContainsKey(player)) _completed.Add(player);
	}

	/// <summary>Strikes a participant who left before finishing, so no score is awaited from them.</summary>
	/// <param name="player">The player.</param>
	internal void Leave(UserRecord player)
	{
		if (_participants.ContainsKey(player) && !_completed.Contains(player)) _struckOff.Add(player);
	}

	/// <summary>Records a received score.</summary>
	/// <param name="score">The score.</param>
	internal void AddScore(RoundScore score)
	{
		_scores.Add(score);
	}

	/// <summary>Gets whether a score from a player is still awaited: the settlement is open, the player took part, did not leave before finishing and has not scored yet.</summary>
	/// <param name="player">The player.</param>
	/// <param name="team">The player's team at the start of the round; <see langword="null"/> in head-to-head or when the player took no part.</param>
	public bool Awaits(UserRecord player, out GameTeam? team)
	{
		team = null;
		return !Closed && _participants.TryGetValue(player, out team) && !_struckOff.Contains(player) &&
		       Scores.All(s => s.Score.Value.UserId != player.Id);
	}

	/// <summary>Gets the players whose score is still awaited.</summary>
	public IEnumerable<UserRecord> Missing => _participants.Keys.Where(player => Awaits(player, out _));

	/// <summary>Closes the settlement so it accepts no more scores.</summary>
	internal void Close()
	{
		Closed = true;
	}
}