using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Matches;
using Basil.Domain.Multiplayer.Rounds;

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