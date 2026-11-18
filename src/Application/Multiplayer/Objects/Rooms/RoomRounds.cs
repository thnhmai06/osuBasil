using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed class RoomRounds
{
	public Round? LastRound { get; internal set; }

	public Round? CurrentRound => LastRound is { EndedAt: null } round ? round : null;

	public bool InProgress => CurrentRound is not null;

	internal bool AllLoadedAnnounced { get; set; }

	internal bool AllSkippedAnnounced { get; set; }

	internal RoundSettlement? Settlement { get; set; }
}