using Basil.Domain.Multiplayer;
using Microsoft.Extensions.Logging;

namespace Basil.Server.Features.Multiplayer.Handlers.Slots;

/// <summary>Reassigns, re-teams, and locks match slots in one atomic pass.</summary>
public sealed class SetSlotsHandler(ILogger<SetSlotsHandler> logger)
{
	public enum SetSlotsResult : byte
	{
		Ok,
		PlayerCountMismatch,
		UnknownUserId,
		SlotOccupiedAndLocked,
		DuplicateUserId
	}

	/// <summary>Represents one entry in a <c>PUT /matches/{matchId}/slots</c> request.</summary>
	/// <remarks>Each entry is keyed by a slot index (0-based) in the request dictionary.</remarks>
	/// <param name="UserId">
	///     The id of the userSession to move into the slot, or <see langword="null" /> to leave occupancy
	///     alone.
	/// </param>
	/// <param name="Team">
	///     The literal <c>"Red"</c> or <c>"Blue"</c> team to assign, or <see langword="null" /> to keep the
	///     current team.
	/// </param>
	/// <param name="Locked">
	///     <see langword="true" /> to lock an empty slot, <see langword="false" /> to open it, or
	///     <see langword="null" /> to leave its status alone.
	/// </param>
	public sealed record SlotPatchEntry(int? UserId, string? Team, bool? Locked);

	/// <summary>Reassigns, re-teams, and locks slots in one atomic pass, then republishes the slot views.</summary>
	/// <remarks>
	///     Every <see cref="SlotPatchEntry.UserId" /> referenced anywhere in <paramref name="entries" />
	///     must already occupy some slot in this match (<see cref="SetSlotsResult.UnknownUserId" />
	///     otherwise). This never seats a new userSession; it only rearranges existing occupants.
	///     <paramref name="isFullReplace" /> (PUT) also requires the referenced user ids to exactly
	///     match the match's current full occupant set (<see cref="SetSlotsResult.PlayerCountMismatch" />
	///     otherwise); PATCH only touches the slots actually given. A <see cref="SlotPatchEntry.Team" />
	///     value other than the literal strings <c>"Red"</c> or <c>"Blue"</c> is a no-op: the
	///     destination slot's existing team is preserved, never reset to neutral, and never inherited
	///     from the moving userSession's previous slot.
	/// </remarks>
	/// <param name="match">The match whose slots to rearrange.</param>
	/// <param name="entries">The slot patches keyed by a 0-based slot index.</param>
	/// <param name="isFullReplace">
	///     <see langword="true" /> to require the entries to cover every occupied slot; otherwise,
	///     <see langword="false" />.
	/// </param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	/// <returns>
	///     <see cref="SetSlotsResult.Ok" /> on success, or <see cref="SetSlotsResult.SlotOccupiedAndLocked" />,
	///     <see cref="SetSlotsResult.UnknownUserId" />, <see cref="SetSlotsResult.DuplicateUserId" />,
	///     or <see cref="SetSlotsResult.PlayerCountMismatch" /> on validation failure.
	/// </returns>
	public Task<SetSlotsResult> SetSlotsAsync(MatchSession match,
		IReadOnlyDictionary<int, SlotPatchEntry> entries,
		bool isFullReplace, MatchMutationScope mutation, CancellationToken cancellationToken = default)
	{
		foreach (var entry in entries.Values)
			if (entry.UserId is not null && entry.Locked == true)
				return Task.FromResult(SetSlotsResult.SlotOccupiedAndLocked);

		var currentOccupantIds = match.Slots
			.Where(s => s.PlayerId is not null)
			.Select(s => s.PlayerId!.Value)
			.ToHashSet();

		var referencedUserIds = entries.Values
			.Where(e => e.UserId is not null)
			.Select(e => e.UserId!.Value)
			.ToList();

		if (referencedUserIds.Any(uid => !currentOccupantIds.Contains(uid)))
			return Task.FromResult(SetSlotsResult.UnknownUserId);

		// PUT already rejects this indirectly (a duplicate collapses the referenced set below its
		// full-occupant count, tripping PlayerCountMismatch), but PATCH has no equivalent guard --
		// without this, the same userId assigned to two destination slots would leave both slots
		// claiming that occupant instead of rejecting the payload outright.
		if (referencedUserIds.Count != referencedUserIds.Distinct().Count())
			return Task.FromResult(SetSlotsResult.DuplicateUserId);

		if (isFullReplace)
		{
			var referencedSet = referencedUserIds.ToHashSet();
			if (referencedSet.Count != currentOccupantIds.Count || !referencedSet.SetEquals(currentOccupantIds))
				return Task.FromResult(SetSlotsResult.PlayerCountMismatch);
		}

		// Snapshot every slot's pre-mutation state so a swap (A<->B) can look up each userSession's
		// origin slot without being affected by the other entry's own mutation.
		var original = match.Slots.Select(s => (s.PlayerId, s.Status, s.Team, s.Mods)).ToArray();
		var destinationSlots = entries.Where(kv => kv.Value.UserId is not null).Select(kv => kv.Key).ToHashSet();

		// Vacate the previous slot of every moved userSession, unless that slot is itself a destination
		// in this same payload (a direct swap doesn't need clearing; it gets overwritten below).
		foreach (var (slotIndex, entry) in entries)
		{
			if (entry.UserId is not { } uid) continue;

			var oldIndex = Array.FindIndex(original, o => o.PlayerId == uid);
			if (oldIndex >= 0 && oldIndex != slotIndex && !destinationSlots.Contains(oldIndex))
				match.Slots[oldIndex].Reset();
		}

		foreach (var (slotIndex, entry) in entries)
		{
			var slot = match.Slots[slotIndex];

			if (entry.UserId is { } uid)
			{
				var oldIndex = Array.FindIndex(original, o => o.PlayerId == uid);
				var source = original[oldIndex];
				slot.PlayerId = uid;
				slot.Status = source.Status;
				slot.Mods = source.Mods;
			}

			if (entry.Team is "Red" or "Blue")
				slot.Team = entry.Team == "Red" ? MatchTeam.Red : MatchTeam.Blue;

			if (entry.Locked is { } locked && slot.PlayerId is null)
				slot.Status = locked ? SlotStatus.Locked : SlotStatus.Open;
		}

		logger.LogDebug("Room settings changed: MatchId={MatchId} SlotsChanged={SlotsChanged}", match.DbId,
			entries.Count);
		// Routes through the same publish every packet-driven slot mutation uses, so `slot` and
		// `slots` (and main/settings) always fire together for this HTTP-driven path too.
		mutation.PublishState();
		return Task.FromResult(SetSlotsResult.Ok);
	}
}
