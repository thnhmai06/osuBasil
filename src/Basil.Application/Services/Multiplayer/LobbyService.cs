using System.Threading.Channels;
using Basil.Application.Contracts.Chat;
using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Multiplayer;
using Basil.Application.Multiplayer.Events;
using Basil.Application.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer;

/// <summary>Opens and closes rooms and tracks who watches the multiplayer lobby.</summary>
internal sealed class LobbyService(
	Lobby lobby,
	IMatchRepository matches,
	UserRegistry users,
	IChannelService channels,
	TimeProvider time) : ILobbyService
{
	/// <summary>The most tournament rooms one creator can have open.</summary>
	internal const int MaxRoomsPerCreator = 4;

	/// <summary>How long an empty tournament room stays open.</summary>
	internal static readonly TimeSpan EmptyTournamentRoomTimeout = TimeSpan.FromMinutes(15);

	/// <summary>How long before an empty tournament room closes the lobby announces its closing a second time.</summary>
	internal static readonly TimeSpan EmptyRoomWarningBefore = TimeSpan.FromMinutes(5);

	private readonly Channel<LobbyEvent> _events = Channel.CreateUnbounded<LobbyEvent>();

	/// <inheritdoc />
	public ChannelReader<LobbyEvent> Events => _events.Reader;

	/// <inheritdoc />
	public async Task<(Room? Room, RoomResult Result)> OpenAsync(
		User? creator,
		BanchoConnection? creatorConnection,
		string name,
		string password,
		bool isTournament,
		bool isPrivate,
		MatchSettings? settings = null,
		CancellationToken cancellationToken = default)
	{
		if (!isTournament && creatorConnection is null)
			throw new ArgumentNullException(nameof(creatorConnection),
				"A room opened in game needs the creator's game client.");

		if (creator is not null)
		{
			if (creator.Value.SilenceEndsAt > time.GetUtcNow()) return (null, RoomResult.Silenced);
			if (!creator.Value.Privilege.Has(ClientPrivileges.Player)) return (null, RoomResult.NotAuthorized);
			if (isTournament && TooManyRooms(creator)) return (null, RoomResult.TooManyRooms);
		}

		// A room opened in game seats its creator; one who already plays in a room cannot open another.
		if (!isTournament && lobby.RoomOf(creatorConnection!) is not null) return (null, RoomResult.AlreadyInRoom);

		if (lobby.IsFull) return (null, RoomResult.NoRoomId);

		var match = await matches.CreateAsync(new MatchData
		{
			Name = name,
			StartedAt = time.GetUtcNow(),
			EndedAt = null,
			Creator = creator,
			IsPrivate = isPrivate
		}, cancellationToken);

		using var scope = lobby.Enter();
		if (lobby.IsFull) return (null, RoomResult.NoRoomId);
		if (creator is not null && isTournament && TooManyRooms(creator)) return (null, RoomResult.TooManyRooms);

		var room = lobby.Add(id =>
		{
			var opened = new Room(id, match, settings ?? new MatchSettings(), isTournament);
			if (!string.IsNullOrEmpty(password)) opened.Password = password;
			return opened;
		});
		if (room is null) return (null, RoomResult.NoRoomId);

		if (creatorConnection is not null && lobby.RoomOf(creatorConnection) is null)
			SeatCreator(room, creatorConnection);

		if (users.Sessions.Select(session => session.Bot).FirstOrDefault(b => b is not null) is { } bot)
			channels.Join(room.Channel, bot);

		Emit(new LobbyRoomOpened(room, room.Host));

		// A tournament room opened without a seated player starts its empty-room countdown now.
		if (!room.Slots.Any(slot => slot.Player is not null)) RoomEmptied(room);
		return (room, RoomResult.Ok);
	}

	/// <inheritdoc />
	public async Task<RoomResult> CloseAsync(Room room, Connection by, CancellationToken cancellationToken = default)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;

		await using var scope = await lobby.EnterAsync(room, cancellationToken);
		if (scope is null) return RoomResult.Ok;

		Close(room);
		return RoomResult.Ok;
	}

	/// <inheritdoc />
	public void Watch(BanchoConnection by)
	{
		if (lobby.AddWatcher(by))
			Emit(new LobbyWatcherJoined(by));
	}

	/// <inheritdoc />
	public void Unwatch(BanchoConnection by)
	{
		if (lobby.RemoveWatcher(by))
			Emit(new LobbyWatcherLeft(by));
	}

	/// <summary>
	///     Closes an empty room now; a tournament room is announced as closing at once and again
	///     <see cref="EmptyRoomWarningBefore" /> before it closes, and closes after
	///     <see cref="EmptyTournamentRoomTimeout" /> if still empty.
	/// </summary>
	/// <param name="room">The room that has no player left.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal void RoomEmptied(Room room)
	{
		if (!room.IsTournament)
		{
			Close(room);
			return;
		}

		var closesAt = time.GetUtcNow() + EmptyTournamentRoomTimeout;
		Emit(new LobbyRoomClosingAnnounced(room, closesAt));
		Schedule(room, closesAt - EmptyRoomWarningBefore, () => WarnIfStillEmptyAsync(room, closesAt));
	}

	/// <summary>Stops the countdown that would close an empty tournament room.</summary>
	/// <param name="room">The room that has a player again.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal void RoomOccupied(Room room)
	{
		var timer = room.ClosingTimer;
		room.ClosingTimer = null;
		timer?.Dispose();
	}

	/// <summary>Closes a room, ending its match and vacating every slot.</summary>
	/// <param name="room">The room to close.</param>
	/// <remarks>
	///     The caller holds the room's scope. A round in progress ends as aborted. Closing a closed room does nothing.
	///     The room's whole consequence is one <see cref="LobbyRoomClosed" /> event.
	/// </remarks>
	internal void Close(Room room)
	{
		RoomOccupied(room);
		if (room.IsClosed) return;

		room.CountdownTimer?.Dispose();
		room.CountdownTimer = null;
		room.CountdownEndsAt = null;

		var aborted = room.CurrentRound;
		if (aborted is not null)
		{
			aborted.EndedAt = time.GetUtcNow();
			aborted.Aborted = true;
			RoomSlotsMechanics.ResetPlayers(room);
		}

		var evicted = room.Slots.Where(s => s.Player is not null).Select(s => s.Player!).ToList();
		foreach (var player in evicted) RoomSlotsMechanics.Vacate(room, player);

		room.Match.Value.EndedAt = time.GetUtcNow();
		room.ClearObservers();
		room.IsClosed = true;
		channels.Close(room.Channel);
		lobby.Remove(room);
		Emit(new LobbyRoomClosed(room, evicted, aborted));
	}

	private void Schedule(Room room, DateTimeOffset at, Func<Task> action)
	{
		var delay = at - time.GetUtcNow();
		var timer = time.CreateTimer(_ => _ = action(), null, delay > TimeSpan.Zero ? delay : TimeSpan.Zero,
			Timeout.InfiniteTimeSpan);
		var previous = room.ClosingTimer;
		room.ClosingTimer = timer;
		previous?.Dispose();
	}

	private async Task WarnIfStillEmptyAsync(Room room, DateTimeOffset closesAt)
	{
		await using var scope = await lobby.EnterAsync(room);
		if (scope is null || room.Slots.Any(slot => slot.Player is not null)) return;

		Emit(new LobbyRoomClosingAnnounced(room, closesAt));
		Schedule(room, closesAt, () => CloseIfStillEmptyAsync(room));
	}

	private async Task CloseIfStillEmptyAsync(Room room)
	{
		await using var scope = await lobby.EnterAsync(room);
		if (scope is not null && !room.Slots.Any(slot => slot.Player is not null)) Close(room);
	}

	private bool TooManyRooms(User creator)
	{
		return lobby.Rooms.Count(room => room.IsTournament && creator.Equals(room.Creator)) >= MaxRoomsPerCreator;
	}

	private void SeatCreator(Room room, BanchoConnection creator)
	{
		if (RoomSlotsMechanics.Seat(room, creator) is null) return;
		room.Host = creator;
		channels.Join(room.Channel, creator);
	}

	private void Emit(LobbyEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}
}