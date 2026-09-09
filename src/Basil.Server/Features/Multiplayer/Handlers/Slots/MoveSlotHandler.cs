using Basil.Domain.Multiplayer;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Multiplayer.Handlers.Slots;

/// <summary>Moves a userSession into an open destination slot and vacates their previous one.</summary>
public static class MoveSlotHandler
{
	public enum MoveResult : byte
	{
		Ok,
		DestinationNotOpen,
		TargetNotInMatch
	}

	/// <remarks><paramref name="destSlotIndex" /> is 0-based; callers convert from their own 1-based input.</remarks>
	/// <param name="match">The match whose slots to rearrange.</param>
	/// <param name="target">The userSession to move.</param>
	/// <param name="destSlotIndex">The 0-based index of the destination slot.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	/// <returns>
	///     <see cref="MoveResult.Ok" /> on success, <see cref="MoveResult.DestinationNotOpen" /> when the
	///     destination slot is not open, or <see cref="MoveResult.TargetNotInMatch" /> when the target
	///     occupies no slot in this match.
	/// </returns>
	public static Task<MoveResult> MoveSlotAsync(MatchSession match, UserSession target, int destSlotIndex,
		MatchMutationScope mutation, CancellationToken cancellationToken = default)
	{
		var destSlot = match.Slots[destSlotIndex];
		if (destSlot.Status != SlotStatus.Open) return Task.FromResult(MoveResult.DestinationNotOpen);

		var sourceSlot = match.GetSlot(target.Id);
		if (sourceSlot is null) return Task.FromResult(MoveResult.TargetNotInMatch);

		destSlot.CopyFrom(sourceSlot);
		sourceSlot.Reset();
		mutation.PublishState();
		return Task.FromResult(MoveResult.Ok);
	}
}
