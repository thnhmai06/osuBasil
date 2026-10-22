using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using MatchRecord = Basil.Domain.Multiplayer.MatchRecord;
using MatchSettings = Basil.Domain.Multiplayer.MatchSettings;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <summary>
///     Holds an osu! multiplayer match's live runtime state, grouped like the room service: settings,
///     authority, members, slots and rounds.
/// </summary>
public sealed partial class Room : IEquatable<Room>
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
		Channel = new RoomChannel(this, context.Chat);
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
	public RoomChannel Channel { get; }

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
	internal async Task<IAsyncDisposable?> EnterAsync(CancellationToken cancellationToken = default)
	{
		await Gate.WaitAsync(cancellationToken);
		if (!IsClosed) return new Scope(Gate);
		Gate.Release();
		return null;
	}

	/// <summary>Records a score for this room's latest round when the server itself calls it; it needs no rights.</summary>
	/// <param name="score">The relation between the stored score and its round.</param>
	/// <param name="player">The player who submitted the score.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, RoundMismatch or RoomClosed when the room has closed.</returns>
	public async Task<RoomOutcome> RecordScoreAsync(RoundScore score, UserRecord player,
		CancellationToken cancellationToken = default)
	{
		await using var scope = await EnterAsync(cancellationToken);
		if (scope is null) return RoomOutcome.RoomClosed;

		if (Rounds.LastRound is not { } round || !round.Equals(score.Round)) return RoomOutcome.RoundMismatch;

		Context.Events.Publish(new RoomRoundScoreSubmitted(this, round, player, score));
		return RoomOutcome.Ok;
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