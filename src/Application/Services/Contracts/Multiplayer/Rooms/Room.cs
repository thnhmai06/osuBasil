using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Matches;
using Basil.Domain.Multiplayer.Rounds;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>
///     Holds an osu! multiplayer match's live runtime state, grouped like the room service: settings,
///     authority, members, slots and rounds.
/// </summary>
public sealed class Room : IEquatable<Room>
{
	/// <summary>Opens a room for a match.</summary>
	/// <param name="id">The room id, carried by the client protocol.</param>
	/// <param name="match">The match the room plays.</param>
	/// <param name="settings">The room's initial settings.</param>
	/// <param name="isTournament">Whether the room is a tournament room.</param>
	/// <param name="context">The runtime context for emitting events and checking permissions.</param>
	internal Room(int id, MatchRecord match, MatchSettings settings, bool isTournament, RoomContext context)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(id);
		Id = id;
		Match = match;
		Settings = new RoomSettings(match, settings);
		Authority = new RoomAuthority(match);
		Members = new RoomMembers();
		Rounds = new RoomRounds();
		IsTournament = isTournament;
		Slots = new RoomSlots(this);
		Channel = new RoomChannelSession(this);
		Context = context;
	}

	/// <summary>The runtime context for emitting events and checking permissions.</summary>
	internal RoomContext Context { get; }

	/// <summary>The match this room is a live projection of.</summary>
	public MatchRecord Match { get; }

	/// <summary>Gets the runtime identifier assigned to this room.</summary>
	public int Id { get; }

	/// <summary>Gets a value that indicates whether the room is a tournament room, which stays open for a while when empty.</summary>
	public bool IsTournament { get; }

	/// <summary>Gets the room's chat channel.</summary>
	public RoomChannelSession Channel { get; }

	/// <summary>The match's 16 slots, in order.</summary>
	public RoomSlots Slots { get; }

	/// <summary>Gets the settings the room plays with.</summary>
	public RoomSettings Settings { get; }

	/// <summary>Gets who has authority over the room.</summary>
	public RoomAuthority Authority { get; }

	/// <summary>Gets who belongs to the room besides its seated players.</summary>
	public RoomMembers Members { get; }

	/// <summary>Gets the room's rounds and its countdown.</summary>
	public RoomRounds Rounds { get; }

	/// <summary>Gets a value that indicates whether the room has closed.</summary>
	public bool IsClosed { get; internal set; }

	/// <summary>Admits one state transition of the room at a time.</summary>
	internal SemaphoreSlim Gate { get; } = new(1, 1);

	/// <summary>The timer that closes the room while it is empty.</summary>
	internal ITimer? ClosingTimer { get; set; }

	/// <summary>Gets the join URL of this room, in <c>osump://{id}/{password}</c> form.</summary>
	public string Url => $"osump://{Id}/{Settings.Password}";

	/// <summary>
	///     Gets an osu! chat embed for this room, formatted as a clickable name linked to <see cref="Url" />.
	/// </summary>
	public string UrlEmbed => $"({Settings.Name})[{Url}]";

	/// <inheritdoc />
	public bool Equals(Room? other)
	{
		return other is not null && Match.Equals(other.Match);
	}

	/// <summary>Creates a scoped room for a connection, capturing roles and permissions.</summary>
	/// <param name="by">The connection acting on the room.</param>
	/// <returns>A scoped room with permissions snapshot.</returns>
	public ScopedRoom ScopeTo(Connection by)
	{
		return new ScopedRoom(this, by);
	}

	/// <summary>Enters the room's exclusive scope, in which one state transition of the room runs one at a time.</summary>
	/// <param name="cancellationToken">A token that cancels the wait.</param>
	/// <returns>The scope, to dispose when done; or <see langword="null" /> when the room has closed.</returns>
	public async Task<IAsyncDisposable?> EnterAsync(CancellationToken cancellationToken = default)
	{
		await Gate.WaitAsync(cancellationToken);
		if (!IsClosed) return new Scope(Gate);
		Gate.Release();
		return null;
	}

	/// <summary>Seats a player in the lowest-index empty, unlocked slot.</summary>
	/// <param name="player">The connection to seat.</param>
	/// <returns>The assigned slot, or <see langword="null" /> when no slot is available.</returns>
	/// <remarks>Seating a player who already has a slot returns that slot. The caller reports the seating.</remarks>
	/// <exception cref="InvalidOperationException">The user is banned from the room.</exception>
	internal RoomSlot? Seat(BanchoConnection player)
	{
		if (Members.Banned.Contains(player.User))
			throw new InvalidOperationException("The user is banned from this room.");

		if (Slots.Find(player) is { } existing) return existing;

		var slot = Slots.FirstOrDefault(s => s is { Locked: false, Player: null });
		if (slot is null) return null;

		slot.Occupy(player);

		if (Settings.MatchSettings.TeamType.IsTeamMode())
		{
			var redCount = Slots.Count(s => s.Team == GameTeam.Red);
			var blueCount = Slots.Count(s => s.Team == GameTeam.Blue);
			slot.Team = redCount <= blueCount ? GameTeam.Red : GameTeam.Blue;
		}

		return slot;
	}

	/// <summary>Seats a player who passed the room's checks, replacing a closed connection of the same user if present.</summary>
	/// <param name="player">The connection to seat.</param>
	/// <param name="stale">The closed connection whose seat the player may take.</param>
	/// <returns>The result of seating the player.</returns>
	internal RoomResult TakeSeat(BanchoConnection player, BanchoConnection? stale)
	{
		if (stale is not null) return ReplaceSeat(player, stale);

		if (Seat(player) is not { } slot) return RoomResult.Full;

		JoinChannel(player);
		MarkOccupied();
		Context.Events.Emit(new RoomPlayerJoined(this, player, slot.Index, null, null));
		return RoomResult.Ok;
	}

	/// <summary>Removes a seated player and updates the room's round, channel and empty-room state.</summary>
	/// <param name="by">The player's game connection.</param>
	/// <returns>Ok when the player left, otherwise NotInRoom.</returns>
	internal RoomResult Leave(BanchoConnection by)
	{
		if (Vacate(by) is not { } slot) return RoomResult.NotInRoom;

		var progress = AdvanceRound();
		Context.Events.Emit(new RoomPlayerLeft(this, by, slot.Index, Authority.Host, progress));
		LeaveChannel(by);
		ReportIfEmpty();
		return RoomResult.Ok;
	}

	/// <summary>Adds a tournament connection as an observer.</summary>
	/// <param name="by">The tournament connection observing.</param>
	/// <returns>Ok, or IsPlayer when the user is seated in the room.</returns>
	internal RoomResult ObserverJoin(TourneyConnection by)
	{
		if (Slots.Find(by.User) is not null) return RoomResult.IsPlayer;
		if (!Members.MutableObservers.Add(by)) return RoomResult.Ok;

		Context.Events.Emit(new RoomObserverJoined(this, by));
		JoinChannel(by);
		return RoomResult.Ok;
	}

	/// <summary>Removes a tournament connection from the room's observers.</summary>
	/// <param name="by">The tournament connection leaving.</param>
	/// <returns>Ok when the observer left, otherwise NotObserver.</returns>
	internal RoomResult ObserverLeave(TourneyConnection by)
	{
		if (!Members.MutableObservers.Remove(by)) return RoomResult.NotObserver;

		Context.Events.Emit(new RoomObserverLeft(this, by));
		LeaveChannel(by);
		return RoomResult.Ok;
	}

	/// <summary>Clears the slot occupied by a player, passing host on when the player was host.</summary>
	/// <param name="player">The connection to remove.</param>
	/// <returns>The slot that was vacated, or <see langword="null" /> when the connection was not seated.</returns>
	/// <remarks>When the host leaves, host passes to the next seated player by slot order.</remarks>
	internal RoomSlot? Vacate(BanchoConnection player)
	{
		var slot = Slots.Find(player);
		if (slot is null) return null;

		PassHostFrom(player);

		slot.Clear();
		return slot;
	}

	/// <summary>Finds the room a player is seated in.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <returns>The room containing the player, or <see langword="null" />.</returns>
	internal Room? RoomOf(BanchoConnection player)
	{
		return Context.Lobby.RoomOf(player);
	}

	/// <summary>Adds a connection to this room's channel.</summary>
	/// <param name="connection">The connection that joins.</param>
	internal void JoinChannel(Connection connection)
	{
		Context.Channel.Join(this, connection);
	}

	/// <summary>Removes a connection from this room's channel unless its user manages the room.</summary>
	/// <param name="connection">The connection that leaves.</param>
	internal void LeaveChannel(Connection connection)
	{
		if (!Authority.IsManagedBy(connection.User)) Context.Channel.Part(this, connection);
	}

	/// <summary>Tells the lobby that this room has a player again.</summary>
	internal void MarkOccupied()
	{
		Context.Lobby.Occupied(this);
	}

	/// <summary>Tells the lobby when this room has no seated players.</summary>
	internal void ReportIfEmpty()
	{
		if (!Slots.Any(slot => slot.Player is not null)) Context.Lobby.Emptied(this);
	}

	/// <summary>Starts a countdown in this room, replacing any countdown already running.</summary>
	/// <param name="by">The user who started the countdown.</param>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the countdown starts a round when it elapses.</param>
	/// <returns>The countdown that started.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal RoomCountdown StartCountdown(UserRecord by, TimeSpan length, bool startsRound)
	{
		CancelCountdown(announce: false);

		var countdown = new RoomCountdown(by, length, startsRound, Context.Clock.GetUtcNow());
		Rounds.Countdown = countdown;
		Rounds.CountdownTimers =
		[
			.. RoomCountdown.MarksFor(length, startsRound).Select(mark =>
				Context.Clock.CreateTimer(_ => Fire(() => TickCountdownAsync(countdown, mark)), null, length - mark,
					Timeout.InfiniteTimeSpan)),
			Context.Clock.CreateTimer(_ => Fire(() => ElapseCountdownAsync(countdown)), null, length,
				Timeout.InfiniteTimeSpan)
		];
		Context.Events.Emit(new RoomCountdownStarted(this, countdown));
		return countdown;
	}

	/// <summary>Cancels this room's countdown.</summary>
	/// <param name="announce">Whether to emit a cancellation event.</param>
	/// <returns><see langword="true" /> when a countdown was cancelled.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal bool CancelCountdown(bool announce)
	{
		var countdown = Rounds.Countdown;
		foreach (var timer in Rounds.CountdownTimers) timer.Dispose();
		Rounds.CountdownTimers = [];
		Rounds.Countdown = null;

		if (countdown is null) return false;
		if (announce) Context.Events.Emit(new RoomCountdownCancelled(this, countdown));
		return true;
	}

	private async Task TickCountdownAsync(RoomCountdown countdown, TimeSpan remaining)
	{
		await using var scope = await EnterAsync();
		if (scope is null || !ReferenceEquals(Rounds.Countdown, countdown)) return;
		Context.Events.Emit(new RoomCountdownTicked(this, countdown, remaining));
	}

	private async Task ElapseCountdownAsync(RoomCountdown countdown)
	{
		await using var scope = await EnterAsync();
		if (scope is null) return;
		ElapseCountdown(countdown);
	}

	private void ElapseCountdown(RoomCountdown countdown)
	{
		if (!ReferenceEquals(Rounds.Countdown, countdown)) return;

		CancelCountdown(announce: false);
		(Round Round, IReadOnlyList<BanchoConnection> Players)? started = null;
		var result = RoomResult.Ok;
		if (countdown.StartsRound)
		{
			if (Rounds.InProgress)
				result = RoomResult.InProgress;
			else if (Settings.Beatmap is null)
				result = RoomResult.NoBeatmap;
			else
				started = BeginRound();
		}

		Context.Events.Emit(new RoomCountdownElapsed(this, countdown, result));
		if (started is { } round) Context.Events.Emit(new RoomRoundStarted(this, round.Round, round.Players));
	}

	private static void Fire(Func<Task> action)
	{
		_ = action().ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
	}

	/// <summary>Ends a room's round in progress and sets its players back to not ready.</summary>
	/// <param name="aborted">Whether the round ends as aborted rather than played out.</param>
	/// <returns>The round that ended, or <see langword="null" /> when no round was in progress.</returns>
	internal Round? EndCurrentRound(bool aborted)
	{
		if (Rounds.CurrentRound is not { } round) return null;

		round.EndedAt = Context.Clock.GetUtcNow();
		round.Aborted = aborted;
		Slots.ResetPlayers();
		return round;
	}

	/// <summary>Begins a round and marks the seated players who have the beatmap as playing.</summary>
	/// <returns>The new round and the players taking part.</returns>
	internal (Round Round, IReadOnlyList<BanchoConnection> Players) BeginRound()
	{
		var round = new Round
		{
			Index = (Rounds.LastRound?.Index ?? 0) + 1,
			Match = Match,
			BeatmapHash = Settings.Beatmap!.Hash,
			Settings = Settings.MatchSettings.Clone(),
			StartedAt = Context.Clock.GetUtcNow(),
			EndedAt = null
		};
		Rounds.LastRound = round;
		Rounds.AllLoadedAnnounced = false;
		Rounds.AllSkippedAnnounced = false;

		var players = new List<BanchoConnection>();
		foreach (var slot in Slots.Where(s => s.Player is not null && s.Status is not RoomSlotStatus.NoMap))
		{
			slot.SetStatus(RoomSlotStatus.Playing);
			players.Add(slot.Player!);
		}

		if (players.Count == 0) EndCurrentRound(false);
		return (round, players);
	}

	/// <summary>Works out what the players who are still playing have all done, after a player stopped playing.</summary>
	/// <returns>
	///     What changed for the round in progress, or <see langword="null" /> when no round is in progress or nothing
	///     changed.
	/// </returns>
	/// <remarks>
	///     Ends the round when nobody is playing any more; otherwise reports that every remaining player has loaded or
	///     skipped, once per round.
	/// </remarks>
	internal RoomRoundProgress? AdvanceRound()
	{
		if (Rounds.CurrentRound is not { } round) return null;

		var playing = Slots.Where(s => s.Status is RoomSlotStatus.Playing).ToList();
		if (playing.Count == 0)
		{
			EndCurrentRound(false);
			return new RoomRoundProgress(round, false, false, true);
		}

		var allLoaded = !Rounds.AllLoadedAnnounced && playing.All(s => s.Loaded is true);
		if (allLoaded) Rounds.AllLoadedAnnounced = true;

		var allSkipped = !Rounds.AllSkippedAnnounced && playing.All(s => s.IntroSkipped is true);
		if (allSkipped) Rounds.AllSkippedAnnounced = true;

		return allLoaded || allSkipped ? new RoomRoundProgress(round, allLoaded, allSkipped, false) : null;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is Room other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Match.GetHashCode();
	}

	private void PassHostFrom(BanchoConnection leaving)
	{
		if (!ReferenceEquals(Authority.Host, leaving)) return;
		Authority.Host = Slots
			.FirstOrDefault(slot => slot.Player is not null && !ReferenceEquals(slot.Player, leaving))
			?.Player;
	}

	/// <summary>Gives the seat of a closed connection to a new connection of the same user.</summary>
	/// <param name="player">The connection taking the seat.</param>
	/// <param name="stale">The closed connection whose seat is replaced.</param>
	/// <returns>Ok after replacing the seated connection.</returns>
	private RoomResult ReplaceSeat(BanchoConnection player, BanchoConnection stale)
	{
		var slot = Slots.Find(stale)!;
		var team = slot.Team;
		var wasHost = ReferenceEquals(Authority.Host, stale);

		slot.Clear();
		slot.Occupy(player);
		slot.Team = team;
		if (wasHost) Authority.Host = player;

		LeaveChannel(stale);
		var progress = AdvanceRound();
		JoinChannel(player);
		MarkOccupied();
		Context.Events.Emit(new RoomPlayerJoined(this, player, slot.Index, stale, progress));
		return RoomResult.Ok;
	}

	private sealed class Scope(SemaphoreSlim held) : IAsyncDisposable
	{
		private int _released;

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0) held.Release();
			return ValueTask.CompletedTask;
		}
	}
}