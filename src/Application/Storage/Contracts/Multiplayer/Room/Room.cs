using Basil.Domain.Multiplayer.Match;

namespace Basil.Application.Storage.Contracts.Multiplayer.Room;

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
	internal Room(int id, Domain.Multiplayer.Match.Match match, MatchSettings settings, bool isTournament)
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
	}

	/// <summary>The match this room is a live projection of.</summary>
	public Domain.Multiplayer.Match.Match Match { get; }

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

	private sealed class Scope(SemaphoreSlim held) : IAsyncDisposable
	{
		private int _released;

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0) held.Release();
			return ValueTask.CompletedTask;
		}
	}

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
}