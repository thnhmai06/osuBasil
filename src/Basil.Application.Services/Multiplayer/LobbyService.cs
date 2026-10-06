using System.Threading.Channels;
using Basil.Application.Contracts.Chat;
using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Services.Users;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Content;
using Basil.Domain.Content;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer;

/// <summary>Opens and closes rooms and tracks who watches the multiplayer lobby.</summary>
internal sealed class LobbyService(
	ILobby lobby,
	IMatchRepository matches,
	IChannelService channels,
	ISettingsRepository serverSettings,
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
	public async Task<RoomResult> CloseAsync(Room room, Connection by, CancellationToken cancellationToken = default)
	{
		await using var scope = await room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.Ok;
		if (!RoomRules.CanManage(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;

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

	/// <inheritdoc />
	public async Task<(Room? Room, RoomResult Result)> OpenAsync(
		Connection by,
		string name,
		string password,
		bool isTournament,
		bool isPrivate,
		MatchSettings? settings = null,
		CancellationToken cancellationToken = default)
	{
		if (!isTournament && by is not BanchoConnection)
			throw new ArgumentException("A room opened in game needs the creator's osu! client.", nameof(by));

		var now = time.GetUtcNow();
		var required = isTournament ? Permissions.PlayerCreateRoom : Permissions.PlayerCreateRoom | Permissions.PlayerJoinRoom;
		switch (PermissionRules.Check(by, required, now))
		{
			case Access.NotGranted: return (null, RoomResult.NotAuthorized);
			case Access.Suspended: return (null, RoomResult.Silenced);
		}

		if ((await serverSettings.GetAsync(cancellationToken)).LockedCreation.HasFlag(CreationLocks.Rooms) &&
		    !PermissionRules.Allows(by, Permissions.TournamentManageAnyRoom, now))
			return (null, RoomResult.NotAuthorized);

		var creator = by.User;
		var limited = isTournament && !PermissionRules.Allows(by, Permissions.TournamentUnlimitedRooms, now);
		if (limited && TooManyRooms(creator)) return (null, RoomResult.TooManyRooms);

		// A room opened in game seats its creator; a tournament room seats the creator's osu! client when it may join.
		var seat = isTournament
			? by.Session.Bancho is { IsOpen: true } bancho && PermissionRules.Allows(bancho, Permissions.PlayerJoinRoom, now)
				? bancho
				: null
			: (BanchoConnection)by;
		if (!isTournament && lobby.RoomOf(seat!) is not null) return (null, RoomResult.AlreadyInRoom);

		if (lobby.IsFull) return (null, RoomResult.NoRoomId);

		var match = await matches.CreateAsync(new MatchData
		{
			Name = name,
			StartedAt = now,
			EndedAt = null,
			Creator = creator,
			IsPrivate = isPrivate
		}, cancellationToken);

		using var scope = lobby.Enter();
		if (lobby.IsFull) return (null, RoomResult.NoRoomId);
		if (limited && TooManyRooms(creator)) return (null, RoomResult.TooManyRooms);

		var room = lobby.Add(id =>
		{
			var opened = new Room(id, match, settings ?? new MatchSettings(), isTournament);
			if (!string.IsNullOrEmpty(password)) opened.Settings.Password = password;
			if (seat is not null && lobby.RoomOf(seat) is null)
				SeatCreator(opened, seat);
			return opened;
		});
		if (room is null) return (null, RoomResult.NoRoomId);

		DateTimeOffset? closesAt = room.IsTournament && !room.Slots.Any(slot => slot.Player is not null)
			? ScheduleClosing(room)
			: null;
		Emit(new LobbyRoomOpened(room, room.Authority.Host, closesAt));
		return (room, RoomResult.Ok);
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

		Emit(new LobbyRoomClosingAnnounced(room, ScheduleClosing(room)));
	}

	/// <summary>Stops the countdown that would close an empty tournament room.</summary>
	/// <param name="room">The room that has a player again.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal static void RoomOccupied(Room room)
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

		RoundMechanics.StopCountdown(room);
		var aborted = RoundMechanics.EndCurrentRound(room, time.GetUtcNow(), true);

		var evicted = room.Slots.Where(s => s.Player is not null).Select(s => s.Player!).ToList();
		foreach (var player in evicted) RoomSlotsMechanics.Vacate(room, player);

		room.Match.Value.EndedAt = time.GetUtcNow();
		room.Members.ClearObservers();
		room.IsClosed = true;
		channels.Close(room.Channel);
		lobby.Remove(room);
		Emit(new LobbyRoomClosed(room, evicted, aborted));
	}

	private DateTimeOffset ScheduleClosing(Room room)
	{
		var closesAt = time.GetUtcNow() + EmptyTournamentRoomTimeout;
		Schedule(room, closesAt - EmptyRoomWarningBefore, () => WarnIfStillEmptyAsync(room, closesAt));
		return closesAt;
	}

	private void Schedule(Room room, DateTimeOffset at, Func<Task> action)
	{
		var delay = at - time.GetUtcNow();
		var timer = time.CreateTimer(_ => action(), null, delay > TimeSpan.Zero ? delay : TimeSpan.Zero,
			Timeout.InfiniteTimeSpan);
		var previous = room.ClosingTimer;
		room.ClosingTimer = timer;
		previous?.Dispose();
	}

	private async Task WarnIfStillEmptyAsync(Room room, DateTimeOffset closesAt)
	{
		await using var scope = await room.EnterAsync();
		if (scope is null || room.Slots.Any(slot => slot.Player is not null)) return;

		Emit(new LobbyRoomClosingAnnounced(room, closesAt));
		Schedule(room, closesAt, () => CloseIfStillEmptyAsync(room));
	}

	private async Task CloseIfStillEmptyAsync(Room room)
	{
		await using var scope = await room.EnterAsync();
		if (scope is not null && !room.Slots.Any(slot => slot.Player is not null)) Close(room);
	}

	private bool TooManyRooms(User creator)
	{
		return lobby.Rooms.Count(room => room.IsTournament && creator.Equals(room.Authority.Creator)) >= MaxRoomsPerCreator;
	}

	private void SeatCreator(Room room, BanchoConnection creator)
	{
		if (RoomSlotsMechanics.Seat(room, creator) is null) return;
		room.Authority.Host = creator;
		channels.Join(room.Channel, creator);
	}

	private void Emit(LobbyEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}
}