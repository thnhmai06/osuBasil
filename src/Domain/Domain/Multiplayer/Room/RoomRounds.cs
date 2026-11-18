namespace Basil.Domain.Multiplayer;

public sealed class RoomRounds
{
	public Round? LastRound { get; internal set; }

	public Round? CurrentRound => LastRound is { EndedAt: null } round ? round : null;

	public bool InProgress => CurrentRound is not null;

	internal bool AllLoadedAnnounced { get; set; }

	internal bool AllSkippedAnnounced { get; set; }

	public RoundSettlement? Settlement { get; internal set; }

	internal bool SettlePending { get; set; }

	public bool TakeSettlePending()
	{
		var pending = SettlePending;
		SettlePending = false;
		return pending;
	}
}