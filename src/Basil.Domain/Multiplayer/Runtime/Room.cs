using System.Collections.Immutable;
using Basil.Domain.Chat;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Domain.Multiplayer.Runtime;

/// <summary>
///     Holds an osu! multiplayer match's room settings, live slot state and lifecycle flags — the
///     part of a live match that is business state rather than live-projection machinery.
/// </summary>
public sealed class Room
{
	/// <summary>Gets the registry slot identifier assigned to this room.</summary>
	public required int Id
	{
		get;
		init => field = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Room Id must be positive.");
	}

	public Room()
	{
		Channel = new RoomChannel(this);
	}

	#region Settings

	/// <summary>The match this room refers to.</summary>
	public required Match Match { get; init; }

	/// <summary>
	///     Sets the room's password, which a client must supply to join.
	/// </summary>
	/// <remarks>
	///     The getter is private; consumers write the password, and <see cref="Url" /> incorporates
	///     it into the join URL.
	/// </remarks>
	public string Password { private get; set; } = string.Empty;

	/// <summary>Gets a value indicating whether the room is protected by a non-empty, non-whitespace password.</summary>
	public bool HasPassword => !string.IsNullOrWhiteSpace(Password);

	/// <summary>Gets a value that indicates whether a round is currently being played.</summary>
	public bool InProgress { get; private set; } = false;

	/// <summary>Gets the room's settings, including the selected beatmap, mode, and mods.</summary>
	public RoomSettings Settings { get; init; } = new();

	public readonly RoomChannel Channel;

	/// <summary>
	///     Gets or sets a value that indicates whether the room is locked against player-initiated
	///     slot and team changes.
	/// </summary>
	/// <remarks>
	///     Corresponds to osu!'s <c>!mp lock</c>. It does not affect joining the room, and referees
	///     are not restricted by it; enforcing the restriction for player-initiated actions is the
	///     caller's responsibility.
	/// </remarks>
	public bool IsLocked { get; set; }

	/// <summary>Gets the join URL of this room, in <c>osump://{id}/{password}</c> form.</summary>
	public string Url => $"osump://{Id}/{Password}";

	/// <summary>
	///     Gets an osu! chat embed for this room, formatted as a clickable name linked to <see cref="Url" />.
	/// </summary>
	public string Embed => $"({Match.Name})[{Url}]";

	#endregion

	#region Users

	/// <summary>
	///     Gets or sets the player who created this match, or <see langword="null" /> when the room was
	///     created via the HTTP API with no session behind it. Set once, right after the room is
	///     created.
	/// </summary>
	public required User? Creator { get; init; }

	/// <summary>Gets the current room host.</summary>
	public User? Host { get; private set; }

	/// <summary>The players banned from this match.</summary>
	public readonly ConcurrentSet<User> BannedUsers = [];

	/// <summary>The players a referee has invited via <c>!mp invite</c>.</summary>
	public readonly ConcurrentSet<User> InvitedUsers = [];

	/// <summary>The players whose connections are tourney clients attached to this match.</summary>
	public readonly ConcurrentSet<User> TourneyUsers = [];

	/// <summary>The players granted referee authority for this match.</summary>
	public readonly ConcurrentSet<User> Referees = [];

	/// <summary>The match's 16 slots, in order.</summary>
	public readonly ImmutableList<RoomSlot> Slots = [.. Enumerable.Range(0, 16).Select(_ => new RoomSlot())];

	/// <summary>Gets a value indicating whether every slot is occupied.</summary>
	public bool IsFull => Slots.All(s => s.Availability != RoomSlotAvailability.Open);

	/// <summary>Gets a value that indicates whether <paramref name="player" /> created this match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns><see langword="true" /> if the player created this match; otherwise, <see langword="false" />.</returns>
	public bool IsCreator(User player)
	{
		return Creator is not null && Creator.Equals(player);
	}

	/// <summary>Gets a value that indicates whether <paramref name="player" /> may issue <c>!mp</c> commands on this match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns>
	///     <see langword="true" /> if the player is a referee or the creator of this match;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool HasRefereePermission(User player)
	{
		return Referees.Contains(player) || IsCreator(player);
	}

	/// <summary>
	///     Gets a value indicating whether <paramref name="player" /> may join this room.
	/// </summary>
	/// <remarks>Banned players can never join; every other player may. A password, if any, is checked separately.</remarks>
	/// <param name="player">The player to check.</param>
	/// <returns>
	///     <see langword="true" /> if the player may join; otherwise, <see langword="false" />.
	/// </returns>
	public bool HasJoinPermission(User player)
	{
		return !BannedUsers.Contains(player);
	}

	/// <summary>
	///     Transfers host authority to another player.
	/// </summary>
	/// <param name="player">The player to make host, or <see langword="null" /> to clear the host.</param>
	/// <exception cref="InvalidOperationException"><paramref name="player" /> has no slot in this room.</exception>
	public void TransferHost(User? player)
	{
		if (player is not null && GetSlot(player) is null)
			throw new InvalidOperationException("The player does not have a slot in this room.");

		Host = player;
	}

	/// <summary>Gets the slot occupied by <paramref name="player" />, or <see langword="null" /> when they have none.</summary>
	/// <param name="player">The player to look up.</param>
	/// <returns>The player's slot, or <see langword="null" />.</returns>
	public RoomSlot? GetSlot(User player)
	{
		return Slots.FirstOrDefault(s => player.Equals(s.User));
	}

	/// <summary>
	///     Assigns <paramref name="player" /> to the first open slot.
	/// </summary>
	/// <param name="player">The player joining the room.</param>
	/// <returns>The slot the player was assigned to, or <see langword="null" /> when the room is full.</returns>
	/// <exception cref="InvalidOperationException"><paramref name="player" /> already has a slot in this room.</exception>
	public RoomSlot? Join(User player)
	{
		if (GetSlot(player) is not null)
			throw new InvalidOperationException("The player already has a slot in this room.");

		var slot = Slots.FirstOrDefault(s => s.Availability == RoomSlotAvailability.Open);
		if (slot is null) return null;

		slot.Assign(player);
		if (Settings.TeamType is GameTeamType.TeamVs or GameTeamType.TagTeamVs)
			slot.SetTeam(GameTeam.Red);

		return slot;
	}

	/// <summary>
	///     Removes <paramref name="player" /> from the room, clearing their slot.
	/// </summary>
	/// <remarks>
	///     When the player was the host, host authority passes to the occupant of the first occupied
	///     slot, or is cleared when no other player is left.
	/// </remarks>
	/// <param name="player">The player leaving the room.</param>
	/// <exception cref="InvalidOperationException"><paramref name="player" /> has no slot in this room.</exception>
	public void Leave(User player)
	{
		var slot = GetSlot(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		slot.Clear();

		if (Host is not null && Host.Equals(player))
			Host = Slots.FirstOrDefault(s => s.Availability == RoomSlotAvailability.Occupied)?.User;

		EndRoundIfFinished();
	}

	/// <summary>
	///     Locks a slot, evicting its occupant if any.
	/// </summary>
	/// <param name="index">The zero-based index of the slot to lock.</param>
	/// <returns>The player evicted from the slot, or <see langword="null" /> when it was not occupied.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is not a valid slot index.</exception>
	/// <exception cref="InvalidOperationException">The slot's occupant is the room's host.</exception>
	public User? LockSlot(int index)
	{
		var slot = GetSlotByIndex(index);
		var occupant = slot.User;

		if (occupant is not null && Host is not null && Host.Equals(occupant))
			throw new InvalidOperationException("The host cannot lock their own slot.");

		slot.Lock();
		EndRoundIfFinished();
		return occupant;
	}

	/// <summary>
	///     Unlocks a slot, allowing players to join it again.
	/// </summary>
	/// <param name="index">The zero-based index of the slot to unlock.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is not a valid slot index.</exception>
	public void UnlockSlot(int index)
	{
		GetSlotByIndex(index).Unlock();
	}

	/// <summary>
	///     Resizes the room, locking slots beyond the new size and unlocking slots within it.
	/// </summary>
	/// <remarks>Occupied slots beyond the new size are left occupied.</remarks>
	/// <param name="size">The number of slots the room should have available, from 1 to 16.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="size" /> is not between 1 and 16.</exception>
	public void Resize(int size)
	{
		if (size is < 1 or > 16)
			throw new ArgumentOutOfRangeException(nameof(size), size, "Room size must be between 1 and 16.");

		for (var i = 0; i < Slots.Count; i++)
		{
			var slot = Slots[i];
			if (i < size)
			{
				if (slot.Availability == RoomSlotAvailability.Locked)
					slot.Unlock();
			}
			else if (slot.Availability == RoomSlotAvailability.Open)
			{
				slot.Lock();
			}
		}
	}

	/// <summary>
	///     Moves a player's complete slot state to another slot.
	/// </summary>
	/// <param name="player">The player to move.</param>
	/// <param name="targetIndex">The zero-based index of the slot to move the player to.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="targetIndex" /> is not a valid slot index.</exception>
	/// <exception cref="InvalidOperationException">
	///     A round is in progress, <paramref name="player" /> has no slot in this room, or the target
	///     slot is not open.
	/// </exception>
	public void MoveSlot(User player, int targetIndex)
	{
		if (InProgress)
			throw new InvalidOperationException("A round is currently in progress.");

		var target = GetSlotByIndex(targetIndex);
		var slot = GetSlot(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		slot.MoveTo(target);
	}

	/// <summary>
	///     Changes the team assigned to a player.
	/// </summary>
	/// <param name="player">The player whose team to change.</param>
	/// <param name="team">The team to assign.</param>
	/// <exception cref="InvalidOperationException">
	///     The room's team type does not use teams, <paramref name="team" /> is
	///     <see cref="GameTeam.Neutral" />, or the player has no slot in this room.
	/// </exception>
	public void ChangeTeam(User player, GameTeam team)
	{
		if (Settings.TeamType is not (GameTeamType.TeamVs or GameTeamType.TagTeamVs))
			throw new InvalidOperationException("The room's team type does not use teams.");
		if (team == GameTeam.Neutral)
			throw new InvalidOperationException("A player cannot be assigned the neutral team.");

		var slot = GetSlot(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		slot.SetTeam(team);
	}

	/// <summary>
	///     Changes the room's team arrangement, re-assigning every occupied slot's team accordingly.
	/// </summary>
	/// <param name="teamType">The team arrangement to use.</param>
	public void ChangeTeamType(GameTeamType teamType)
	{
		Settings.TeamType = teamType;
		var team = teamType is GameTeamType.TeamVs or GameTeamType.TagTeamVs ? GameTeam.Red : GameTeam.Neutral;

		foreach (var slot in Slots.Where(s => s.Availability == RoomSlotAvailability.Occupied))
			slot.SetTeam(team);
	}

	/// <summary>
	///     Changes the room's selected beatmap, reverting ready players to not-ready.
	/// </summary>
	/// <param name="beatmapMd5">The MD5 hash of the beatmap to select, or <see langword="null" /> to clear the selection.</param>
	/// <exception cref="InvalidOperationException">A round is currently in progress.</exception>
	public void ChangeBeatmap(string? beatmapMd5)
	{
		if (InProgress)
			throw new InvalidOperationException("A round is currently in progress.");

		Settings.BeatmapMd5 = beatmapMd5;

		foreach (var slot in Slots.Where(s => s.Status == RoomSlotStatus.Ready))
			slot.SetStatus(RoomSlotStatus.NotReady);
	}

	/// <summary>
	///     Changes the room's game mode, re-filtering the room's and every player's mods for the new mode.
	/// </summary>
	/// <param name="mode">The game mode to use.</param>
	public void ChangeMode(GameMode mode)
	{
		Settings.Mode = mode;
		Settings.Mods = Settings.Mods;

		foreach (var slot in Slots.Where(s => s.Availability == RoomSlotAvailability.Occupied))
			slot.SetMods(slot.GameMods.RemoveInvalidMods(mode));
	}

	/// <summary>
	///     Changes the mods applied to the whole room.
	/// </summary>
	/// <remarks>
	///     When freemods is enabled, only the speed-changing mods of <paramref name="mods" /> apply
	///     room-wide, since the rest are chosen per player.
	/// </remarks>
	/// <param name="mods">The mods to apply.</param>
	public void ChangeMods(GameMods mods)
	{
		Settings.Mods = Settings.Freemods ? mods & GameMods.SpeedChangingMods : mods;
	}

	/// <summary>
	///     Changes the mods applied to a single player, while freemods is enabled.
	/// </summary>
	/// <param name="player">The player whose mods to change.</param>
	/// <param name="mods">The mods to assign to the player.</param>
	/// <exception cref="InvalidOperationException">
	///     Freemods is not enabled, or <paramref name="player" /> has no slot in this room.
	/// </exception>
	public void ChangePlayerMods(User player, GameMods mods)
	{
		if (!Settings.Freemods)
			throw new InvalidOperationException("Freemods is not enabled.");

		var slot = GetSlot(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		slot.SetMods(mods.RemoveInvalidMods(Settings.Mode));
	}

	/// <summary>
	///     Enables or disables freemods, redistributing mods between the room and its players.
	/// </summary>
	/// <param name="enabled">Whether freemods should be enabled.</param>
	public void ChangeFreemods(bool enabled)
	{
		if (enabled == Settings.Freemods) return;

		if (enabled)
		{
			foreach (var slot in Slots.Where(s => s.Availability == RoomSlotAvailability.Occupied))
				slot.SetMods(Settings.Mods & ~GameMods.SpeedChangingMods);

			Settings.Mods &= GameMods.SpeedChangingMods;
			Settings.Freemods = true;
		}
		else
		{
			var hostMods = Host is not null ? GetSlot(Host)?.GameMods ?? GameMods.NoMod : GameMods.NoMod;
			Settings.Mods = (Settings.Mods & GameMods.SpeedChangingMods) | hostMods;

			foreach (var slot in Slots.Where(s => s.Availability == RoomSlotAvailability.Occupied))
				slot.SetMods(GameMods.NoMod);

			Settings.Freemods = false;
		}
	}

	/// <summary>
	///     Starts a round, marking every ready occupied slot as playing.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	///     A round is already in progress, or no player has the beatmap.
	/// </exception>
	public void Start()
	{
		if (InProgress)
			throw new InvalidOperationException("A round is already in progress.");

		var players = Slots.Where(s =>
			s.Availability == RoomSlotAvailability.Occupied && s.Status != RoomSlotStatus.NoMap).ToList();
		if (players.Count == 0)
			throw new InvalidOperationException("No player has the beatmap.");

		foreach (var slot in players)
		{
			slot.ResetRoundFlags();
			slot.SetStatus(RoomSlotStatus.Playing);
		}

		InProgress = true;
	}

	/// <summary>Gets a value indicating whether every playing player has finished loading the beatmap.</summary>
	public bool AllPlayersLoaded => Slots.All(s => s.Status != RoomSlotStatus.Playing || s.BeatmapLoaded);

	/// <summary>Gets a value indicating whether every playing player has skipped the beatmap's intro.</summary>
	public bool AllPlayersSkipped => Slots.All(s => s.Status != RoomSlotStatus.Playing || s.IntroSkipped);

	/// <summary>
	///     Marks a player as having finished the current round.
	/// </summary>
	/// <param name="player">The player who finished.</param>
	/// <returns><see langword="true" /> when this was the last player playing, ending the round.</returns>
	/// <exception cref="InvalidOperationException">
	///     No round is in progress, <paramref name="player" /> has no slot in this room, or the player
	///     is not currently playing.
	/// </exception>
	public bool Complete(User player)
	{
		if (!InProgress)
			throw new InvalidOperationException("No round is currently in progress.");

		var slot = GetSlot(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		if (slot.Status != RoomSlotStatus.Playing)
			throw new InvalidOperationException("The player is not currently playing.");

		slot.SetStatus(RoomSlotStatus.Complete);
		return EndRoundIfFinished();
	}

	/// <summary>
	///     Aborts the current round, returning every player to not-ready.
	/// </summary>
	/// <exception cref="InvalidOperationException">No round is currently in progress.</exception>
	public void Abort()
	{
		if (!InProgress)
			throw new InvalidOperationException("No round is currently in progress.");

		EndRound();
	}

	private bool EndRoundIfFinished()
	{
		if (!InProgress || Slots.Any(s => s.Status == RoomSlotStatus.Playing)) return false;
		EndRound();
		return true;

	}

	private void EndRound()
	{
		foreach (var slot in Slots.Where(s => s.Status is RoomSlotStatus.Playing or RoomSlotStatus.Complete))
			slot.SetStatus(RoomSlotStatus.NotReady);

		foreach (var slot in Slots.Where(s => s.Availability == RoomSlotAvailability.Occupied))
			slot.ResetRoundFlags();

		InProgress = false;
	}

	private RoomSlot GetSlotByIndex(int index)
	{
		if (index < 0 || index >= Slots.Count)
			throw new ArgumentOutOfRangeException(nameof(index), index, "Slot index must be between 0 and 15.");

		return Slots[index];
	}

	#endregion
}