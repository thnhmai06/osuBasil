using Basil.Domain.Auth;
using Basil.Domain.Client;
using Basil.Domain.Social;
using Basil.Domain.Utilities;

using Basil.Application.Common.Events;
using Basil.Application.Multiplayer;
using Basil.Application.Multiplayer.Events;
namespace Basil.Application.Sessions;

/// <summary>
///     A real osu! client's session: gameplay presence, spectating, and current room membership, in
///     addition to the chat state every <see cref="UserSession" /> has.
/// </summary>
/// <remarks>
///     Thread-safe with respect to spectator bookkeeping: another session starting or stopping
///     spectating this one does not corrupt <see cref="Spectators" />.
/// </remarks>
public sealed class GameSession : UserSession
{
	private readonly ConcurrentSet<GameSession> _spectators = [];

	/// <summary>Gets the login that created this session.</summary>
	public required Login Login { get; init; }

	/// <inheritdoc />
	public override Domain.Users.User User => Login.User;

	/// <inheritdoc />
	public override DateTimeOffset LoginTime => Login.OccurredAt;

	/// <summary>Gets the osu! client version reported at login.</summary>
	public ClientVersion ClientVersion => Login.Version;

	/// <summary>Gets the hardware and client fingerprint captured at login.</summary>
	public ClientFingerprint ClientFingerprint => Login.Fingerprint;

	/// <summary>Gets the IP address this session connected from.</summary>
	public System.Net.IPAddress Ip => Login.Ip;

	/// <summary>Gets the client's UTC offset reported at login.</summary>
	public int UtcOffset { get; init; }

	/// <summary>Gets or sets the presence-list filter the client requested.</summary>
	public PresenceVisibility PresenceVisibility { get; set; } = PresenceVisibility.Nil;

	/// <summary>Gets or sets the client's currently reported presence status.</summary>
	/// <remarks>Changing the value records a <see cref="StatusChanged" /> event only when it actually changes.</remarks>
	public PlayerStatus Status
	{
		get;
		set
		{
			if (Equals(field, value)) return;
			field = value;
			Record(new StatusChanged(this, value));
		}
	} = PlayerStatus.Idle;

	/// <summary>Gets the session this client is currently spectating, or <see langword="null" /> if none.</summary>
	public GameSession? Spectating { get; private set; }

	/// <summary>Gets the sessions currently spectating this client.</summary>
	public IReadOnlySet<GameSession> Spectators => _spectators;

	/// <summary>Gets or sets the slot this session currently occupies, or <see langword="null" /> if none.</summary>
	/// <remarks>Only <see cref="Multiplayer.RoomSlot" /> sets this value.</remarks>
	public RoomSlot? Slot { get; internal set; }

	/// <summary>Gets the room this session currently sits in, or <see langword="null" /> if none.</summary>
	public Room? Room => Slot?.Slots.Room;

	/// <summary>
	///     Starts spectating another client, keeping both sides of the relationship consistent.
	/// </summary>
	/// <param name="host">The session to spectate.</param>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="host" /> is this same session, or this session is already spectating someone.
	/// </exception>
	public void Spectate(GameSession host)
	{
		if (host.User.Equals(User))
			throw new InvalidOperationException("A session cannot spectate itself.");
		if (Spectating is not null)
			throw new InvalidOperationException("This session is already spectating someone.");

		Spectating = host;
		host._spectators.Add(this);
		host.Record(new SpectatorAdded(host, this));
	}

	/// <summary>
	///     Stops spectating, keeping both sides of the relationship consistent.
	/// </summary>
	/// <exception cref="InvalidOperationException">This session is not spectating anyone.</exception>
	public void StopSpectating()
	{
		var host = Spectating
		           ?? throw new InvalidOperationException("This session is not spectating anyone.");

		Spectating = null;
		host._spectators.Remove(this);
		host.Record(new SpectatorRemoved(host, this));
	}

	/// <summary>Joins this session to a room, seating it in the first available slot.</summary>
	/// <param name="room">The room to join.</param>
	/// <returns>The assigned slot, or <see langword="null" /> when the room is full.</returns>
	/// <exception cref="InvalidOperationException">
	///     The user is banned, or the session is already seated in a different room.
	/// </exception>
	public RoomSlot? JoinRoom(Room room)
	{
		return room.Slots.Seat(this);
	}

	/// <summary>Leaves the room this session is currently seated in, if any.</summary>
	public void LeaveRoom()
	{
		if (Room is { } room && room.Slots.Vacate(this) is { } slot)
			room.Emit(new PlayerLeft(room, this, slot));
	}
}