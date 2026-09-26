using Basil.Domain.Chat;
using Basil.Domain.Events;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Runtime;

/// <summary>
///     Holds an osu! multiplayer match's room settings, live slot state and lifecycle flags — the
///     part of a live match that is business state rather than live-projection machinery.
/// </summary>
public sealed class Room : IHasDomainEvents
{
	/// <summary>
	///     Gets the runtime identifier assigned to this room, used by the osu! client to reference
	///     it while joined. This id only exists for as long as the server is running and is lost on
	///     restart; use <see cref="Match" />.<see cref="Records.Match.Id" /> to look the match up later.
	/// </summary>
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
		Slots = new RoomSlots(this);
	}

	private readonly DomainEventLog _events = new();

	/// <inheritdoc />
	public IReadOnlyList<IDomainEvent> DomainEvents => _events.Events;

	/// <inheritdoc />
	public void ClearDomainEvents()
	{
		_events.Clear();
	}

	/// <summary>Records a domain event that has occurred to this room or one of its slots.</summary>
	/// <param name="domainEvent">The event to record.</param>
	internal void Record(IDomainEvent domainEvent)
	{
		_events.Record(domainEvent);
	}

	#region Settings

	/// <summary>The match this room refers to.</summary>
	public required Match Match { get; init; }

	/// <summary>Gets the room's password, which a client must supply to join.</summary>
	/// <remarks>
	///     The getter is private; consumers write the password via <see cref="ChangePassword" />, and
	///     <see cref="Url" /> incorporates it into the join URL.
	/// </remarks>
	public string Password { get; private set; } = string.Empty;

	/// <summary>Gets a value indicating whether the room is protected by a non-empty, non-whitespace password.</summary>
	public bool HasPassword => !string.IsNullOrWhiteSpace(Password);

	/// <summary>
	///     Changes the room's name.
	/// </summary>
	/// <param name="name">The new name of the match.</param>
	/// <exception cref="ArgumentException"><paramref name="name" /> is empty or whitespace.</exception>
	public void Rename(string name)
	{
		Match.Name = name;
		Record(new SettingsChanged(this));
	}

	/// <summary>
	///     Changes the room's password.
	/// </summary>
	/// <param name="password">The new password, or an empty string to remove password protection.</param>
	public void ChangePassword(string password)
	{
		Password = password;
		Record(new SettingsChanged(this));
	}

	/// <summary>
	///     Checks whether a supplied password satisfies the room's password protection.
	/// </summary>
	/// <param name="password">The password to check.</param>
	/// <returns>
	///     <see langword="true" /> if the room has no password, or <paramref name="password" />
	///     matches it; otherwise, <see langword="false" />.
	/// </returns>
	public bool CheckPassword(string password)
	{
		return !HasPassword || Password == password;
	}

	/// <summary>Gets a value that indicates whether a round is currently being played.</summary>
	public bool InProgress { get; private set; }

	/// <summary>Gets the record of the round currently being played, or <see langword="null" /> when none is tracked.</summary>
	/// <remarks>Cleared automatically when the round ends.</remarks>
	public Round? CurrentRound { get; private set; }

	/// <summary>Associates the record of the round that is currently being played with this room.</summary>
	/// <param name="round">The round record, belonging to this room's match.</param>
	/// <exception cref="InvalidOperationException">
	///     No round is in progress, or <paramref name="round" /> belongs to a different match.
	/// </exception>
	public void TrackRound(Round round)
	{
		if (!InProgress)
			throw new InvalidOperationException("No round is currently in progress.");
		if (!round.Match.Equals(Match))
			throw new InvalidOperationException("The round belongs to a different match.");

		CurrentRound = round;
	}

	/// <summary>Gets the room's settings, including the selected beatmap, mode, and mods.</summary>
	public RoomSettings Settings { get; init; } = new();

	public readonly RoomChannel Channel;

	/// <summary>
	///     Gets a value indicating whether the room is locked against player-initiated slot and team
	///     changes.
	/// </summary>
	/// <remarks>
	///     Corresponds to osu!'s <c>!mp lock</c>. It does not affect joining the room, and referees
	///     are not restricted by it; enforcing the restriction for player-initiated actions is the
	///     caller's responsibility.
	/// </remarks>
	public bool IsLocked { get; private set; }

	/// <summary>Locks the room against player-initiated slot and team changes.</summary>
	public void Lock()
	{
		IsLocked = true;
		Record(new RoomLockChanged(this, true));
	}

	/// <summary>Unlocks the room, allowing player-initiated slot and team changes again.</summary>
	public void Unlock()
	{
		IsLocked = false;
		Record(new RoomLockChanged(this, false));
	}

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

	private readonly HashSet<User> _bannedUsers = [];
	private readonly HashSet<User> _invitedUsers = [];
	private readonly HashSet<User> _referees = [];

	/// <summary>Gets the players granted referee authority for this match.</summary>
	public IReadOnlyCollection<User> Referees => _referees;

	/// <summary>The match's 16 slots, in order.</summary>
	public readonly RoomSlots Slots;

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
		return IsReferee(player) || IsCreator(player);
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
		return !IsBanned(player);
	}

	/// <summary>Bans a player from the match, removing them from the room if they are currently in it.</summary>
	/// <param name="player">The player to ban.</param>
	public void Ban(User player)
	{
		_bannedUsers.Add(player);
		if (Slots.Find(player) is not null)
			Leave(player);

		Record(new PlayerBanned(this, player));
	}

	/// <summary>Lifts a player's ban from the match, allowing them to join again.</summary>
	/// <param name="player">The player to unban.</param>
	/// <exception cref="InvalidOperationException"><paramref name="player" /> is not banned.</exception>
	public void Unban(User player)
	{
		if (!_bannedUsers.Remove(player))
			throw new InvalidOperationException("The player is not banned from this match.");

		Record(new PlayerUnbanned(this, player));
	}

	/// <summary>Gets a value that indicates whether <paramref name="player" /> is banned from the match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns><see langword="true" /> if the player is banned; otherwise, <see langword="false" />.</returns>
	public bool IsBanned(User player)
	{
		return _bannedUsers.Contains(player);
	}

	/// <summary>Invites a player to the match.</summary>
	/// <param name="player">The player to invite.</param>
	public void Invite(User player)
	{
		_invitedUsers.Add(player);
		Record(new PlayerInvited(this, player));
	}

	/// <summary>Gets a value that indicates whether <paramref name="player" /> has been invited to the match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns><see langword="true" /> if the player was invited; otherwise, <see langword="false" />.</returns>
	public bool IsInvited(User player)
	{
		return _invitedUsers.Contains(player);
	}

	/// <summary>Grants a player referee authority for this match.</summary>
	/// <param name="player">The player to grant referee authority to.</param>
	public void AddReferee(User player)
	{
		_referees.Add(player);
		Record(new RefereeAdded(this, player));
	}

	/// <summary>Revokes a player's referee authority for this match.</summary>
	/// <param name="player">The player to revoke referee authority from.</param>
	public void RemoveReferee(User player)
	{
		_referees.Remove(player);
		Record(new RefereeRemoved(this, player));
	}

	/// <summary>Gets a value that indicates whether <paramref name="player" /> is a referee for this match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns><see langword="true" /> if the player is a referee; otherwise, <see langword="false" />.</returns>
	public bool IsReferee(User player)
	{
		return _referees.Contains(player);
	}

	/// <summary>
	///     Transfers host authority to another player.
	/// </summary>
	/// <param name="player">The player to make host, or <see langword="null" /> to clear the host.</param>
	/// <exception cref="InvalidOperationException"><paramref name="player" /> has no slot in this room.</exception>
	public void TransferHost(User? player)
	{
		if (player is not null && Slots.Find(player) is null)
			throw new InvalidOperationException("The player does not have a slot in this room.");

		Host = player;
		Record(new HostChanged(this, player));
	}

	/// <summary>
	///     Assigns <paramref name="player" /> to the first open slot.
	/// </summary>
	/// <param name="player">The player joining the room.</param>
	/// <returns>The slot the player was assigned to, or <see langword="null" /> when the room is full.</returns>
	/// <exception cref="InvalidOperationException"><paramref name="player" /> already has a slot in this room.</exception>
	public RoomSlot? Join(User player)
	{
		if (Slots.Find(player) is not null)
			throw new InvalidOperationException("The player already has a slot in this room.");

		var slot = Slots.FindOpen();
		if (slot is null) return null;

		slot.Assign(player);
		if (Settings.TeamType is GameTeamType.TeamVs or GameTeamType.TagTeamVs)
			slot.SetTeam(GameTeam.Red);

		Record(new PlayerJoined(this, player, Slots.IndexOf(slot)));
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
		var slot = Slots.Find(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		slot.Clear();

		if (Host is not null && Host.Equals(player))
			TransferHost(Slots.FirstOrDefault(s => s.Availability == RoomSlotAvailability.Occupied)?.User);

		Record(new PlayerLeft(this, player));
		EndRoundIfFinished();
	}

	/// <summary>Removes a player from the room.</summary>
	/// <param name="player">The player to remove.</param>
	/// <exception cref="InvalidOperationException"><paramref name="player" /> has no slot in this room.</exception>
	public void Kick(User player)
	{
		Leave(player);
		Record(new PlayerKicked(this, player));
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
		var occupant = Slots[index].User;

		if (occupant is not null && Host is not null && Host.Equals(occupant))
			throw new InvalidOperationException("The host cannot lock their own slot.");

		var evicted = Slots.Lock(index);
		EndRoundIfFinished();
		return evicted;
	}

	/// <summary>
	///     Unlocks a slot, allowing players to join it again.
	/// </summary>
	/// <param name="index">The zero-based index of the slot to unlock.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is not a valid slot index.</exception>
	public void UnlockSlot(int index)
	{
		Slots.Unlock(index);
	}

	/// <summary>
	///     Resizes the room, locking slots beyond the new size and unlocking slots within it.
	/// </summary>
	/// <param name="size">The number of slots the room should have available, from 1 to 16.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="size" /> is not between 1 and 16.</exception>
	public void Resize(int size)
	{
		Slots.Resize(size);
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

		Slots.Move(player, targetIndex);
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

		var slot = Slots.Find(player) ??
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

		Record(new SettingsChanged(this));
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

		Record(new SettingsChanged(this));
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

		Record(new SettingsChanged(this));
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
		Record(new SettingsChanged(this));
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

		var slot = Slots.Find(player) ??
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
			var hostMods = Host is not null ? Slots.Find(Host)?.GameMods ?? GameMods.NoMod : GameMods.NoMod;
			Settings.Mods = (Settings.Mods & GameMods.SpeedChangingMods) | hostMods;

			foreach (var slot in Slots.Where(s => s.Availability == RoomSlotAvailability.Occupied))
				slot.SetMods(GameMods.NoMod);

			Settings.Freemods = false;
		}

		Record(new SettingsChanged(this));
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
		Record(new RoundStarted(this));
	}

	/// <summary>
	///     Marks a player as having finished loading the current beatmap.
	/// </summary>
	/// <param name="player">The player who finished loading.</param>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="player" /> has no slot in this room, or the player is not currently playing.
	/// </exception>
	public void MarkLoaded(User player)
	{
		var slot = Slots.Find(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		slot.MarkLoaded();
		Record(new PlayerLoaded(this, Slots.IndexOf(slot)));

		if (Slots.AllLoaded)
			Record(new AllPlayersLoaded(this));
	}

	/// <summary>
	///     Marks a player as having skipped the current beatmap's intro.
	/// </summary>
	/// <param name="player">The player who skipped the intro.</param>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="player" /> has no slot in this room, or the player is not currently playing.
	/// </exception>
	public void SkipIntro(User player)
	{
		var slot = Slots.Find(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		slot.SkipIntro();
		Record(new PlayerSkipped(this, Slots.IndexOf(slot)));

		if (Slots.AllSkipped)
			Record(new AllPlayersSkipped(this));
	}

	/// <summary>
	///     Marks a player as having failed the current round.
	/// </summary>
	/// <param name="player">The player who failed.</param>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="player" /> has no slot in this room, or the player is not currently playing.
	/// </exception>
	public void Fail(User player)
	{
		var slot = Slots.Find(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		if (slot.Status != RoomSlotStatus.Playing)
			throw new InvalidOperationException("The player is not currently playing.");

		Record(new PlayerFailed(this, Slots.IndexOf(slot)));
	}

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

		var slot = Slots.Find(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		if (slot.Status != RoomSlotStatus.Playing)
			throw new InvalidOperationException("The player is not currently playing.");

		slot.SetStatus(RoomSlotStatus.Complete);
		Record(new PlayerCompleted(this, Slots.IndexOf(slot)));
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

		EndRound(true);
	}

	private bool EndRoundIfFinished()
	{
		if (!InProgress || Slots.AnyPlaying) return false;
		EndRound(false);
		return true;
	}

	private void EndRound(bool aborted)
	{
		foreach (var slot in Slots.Where(s => s.Status is RoomSlotStatus.Playing or RoomSlotStatus.Complete))
			slot.SetStatus(RoomSlotStatus.NotReady);

		foreach (var slot in Slots.Where(s => s.Availability == RoomSlotAvailability.Occupied))
			slot.ResetRoundFlags();

		InProgress = false;
		var round = CurrentRound;
		CurrentRound = null;
		Record(new RoundEnded(this, aborted, round));
	}

	#endregion
}