using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Users;
using Basil.Application.Services.Implementations.Multiplayer.Rooms;
using Basil.Application.Services.Implementations.Users;
using Basil.Application.Storage.Contracts.Content;
using Basil.Application.Storage.Contracts.Multiplayer.Matches;
using Basil.Domain.Content;
using Basil.Domain.Multiplayer.Matches;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Multiplayer;

/// <summary>Opens rooms for new matches and closes them again, empty or on request.</summary>
internal sealed class LobbyRoomsService(
	LobbyEventStream events,
	ILobby lobby,
	IMatchRepository matches,
	IChannelService channels,
	ISettingsRepository serverSettings,
	RoomEventStream roomEvents,
	RoomChannelService roomChannel,
	TimeProvider time,
	IUserService users) : ILobbyRoomsService, IRoomLobbyPort, IRoomUsersPort
{
	/// <summary>The most tournament rooms one creator can have open.</summary>
	internal const int MaxRoomsPerCreator = 4;

	/// <summary>How long an empty tournament room stays open.</summary>
	internal static readonly TimeSpan EmptyTournamentRoomTimeout = TimeSpan.FromMinutes(15);

	/// <summary>How long before an empty tournament room closes the lobby announces its closing a second time.</summary>
	internal static readonly TimeSpan EmptyRoomWarningBefore = TimeSpan.FromMinutes(5);

	/// <inheritdoc />
	void IRoomLobbyPort.Close(Room room, UserRecord? by) => Close(room, by);

	/// <inheritdoc />
	ValueTask<bool> IRoomUsersPort.BlocksAsync(UserRecord blocker, UserRecord who,
		CancellationToken cancellationToken) => users.Of(blocker).HasBlockedAsync(who, cancellationToken);

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
		var required = isTournament
			? Permissions.RoomCreate
			: Permissions.RoomCreate | Permissions.RoomJoin;
		switch (PermissionRules.Check(by, required, now))
		{
			case Access.NotGranted: return (null, RoomResult.NotAuthorized);
			case Access.Suspended: return (null, RoomResult.Silenced);
		}

		if ((await serverSettings.GetAsync(cancellationToken)).LockedCreation.HasFlag(CreationLocks.Rooms) &&
		    !PermissionRules.Allows(by, Permissions.RoomManageAny, now))
			return (null, RoomResult.NotAuthorized);

		var creator = by.User;
		var limited = isTournament && !PermissionRules.Allows(by, Permissions.RoomCreateUnlimited, now);
		if (limited && TooManyRooms(creator)) return (null, RoomResult.TooManyRooms);

		// A room opened in game seats its creator; a tournament room seats the creator's osu! client when it may join.
		var seat = isTournament
			? by.Session.Bancho is { IsOpen: true } bancho &&
			  PermissionRules.Allows(bancho, Permissions.RoomJoin, now)
				? bancho
				: null
			: (BanchoConnection)by;
		if (!isTournament && lobby.RoomOf(seat!) is not null) return (null, RoomResult.AlreadyInRoom);

		if (!MatchData.IsValidName(name)) return (null, RoomResult.InvalidSettings);

		// ponytail: two concurrent opens by one creator can both pass the room limit; count reservations per creator if that matters.
		if (lobby.Reserve() is not { } id) return (null, RoomResult.NoRoomId);

		MatchRecord match;
		try
		{
			match = await matches.CreateAsync(new MatchData
			{
				Name = name,
				StartedAt = now,
				EndedAt = null,
				Creator = creator,
				IsPrivate = isPrivate
			}, cancellationToken);
		}
		catch
		{
			lobby.Release(id);
			throw;
		}

		Room room;
		using (lobby.Enter())
		{
			var context = new RoomContext(time, roomEvents, this, roomChannel, this);
			room = new Room(id, match, settings ?? new MatchSettings(), isTournament, context);
			if (!string.IsNullOrEmpty(password)) room.Settings.Password = password;
			if (seat is not null && lobby.RoomOf(seat) is null) SeatCreator(room, seat);
			lobby.Add(room);
		}

		DateTimeOffset? closesAt = room.IsTournament && !room.Slots.Any(slot => slot.Player is not null)
			? ScheduleClosing(room)
			: null;
		events.Emit(new LobbyRoomOpened(room, room.Authority.Host, closesAt));
		return (room, RoomResult.Ok);
	}

	/// <summary>
	///     Closes an empty room now; a tournament room is announced as closing at once and again
	///     <see cref="EmptyRoomWarningBefore" /> before it closes, and closes after
	///     <see cref="EmptyTournamentRoomTimeout" /> if still empty.
	/// </summary>
	/// <param name="room">The room that has no player left.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	void IRoomLobbyPort.Emptied(Room room)
	{
		if (!room.IsTournament)
		{
			Close(room, null);
			return;
		}

		events.Emit(new LobbyRoomClosingAnnounced(room, ScheduleClosing(room)));
	}

	/// <summary>Stops the countdown that would close an empty tournament room.</summary>
	/// <param name="room">The room that has a player again.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	void IRoomLobbyPort.Occupied(Room room)
	{
		RoomOccupied(room);
	}

	Room? IRoomLobbyPort.RoomOf(BanchoConnection player)
	{
		return lobby.RoomOf(player);
	}

	private void RoomOccupied(Room room)
	{
		var timer = room.ClosingTimer;
		room.ClosingTimer = null;
		timer?.Dispose();
	}

	/// <summary>Closes a room, ending its match and vacating every slot.</summary>
	/// <param name="room">The room to close.</param>
	/// <param name="by">The user who closed the room, or <see langword="null" /> when the room closed itself.</param>
	/// <remarks>
	///     The caller holds the room's scope. A round in progress ends as aborted. Closing a closed room does nothing.
	///     The room's whole consequence is one <see cref="LobbyRoomClosed" /> event.
	/// </remarks>
	internal void Close(Room room, UserRecord? by)
	{
		RoomOccupied(room);
		if (room.IsClosed) return;

		var aborted = room.EndCurrentRound(true);
		room.CancelCountdown(announce: false);

		var evicted = room.Slots.Where(s => s.Player is not null).Select(s => s.Player!).ToList();
		foreach (var player in evicted) room.Vacate(player);

		room.Match.Value.EndedAt = time.GetUtcNow();
		room.Members.MutableObservers.Clear();
		room.IsClosed = true;
		channels.Close(room.Channel);
		lobby.Remove(room);
		events.Emit(new LobbyRoomClosed(room, by, evicted, aborted));
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

		events.Emit(new LobbyRoomClosingAnnounced(room, closesAt));
		Schedule(room, closesAt, () => CloseIfStillEmptyAsync(room));
	}

	private async Task CloseIfStillEmptyAsync(Room room)
	{
		await using var scope = await room.EnterAsync();
		if (scope is not null && !room.Slots.Any(slot => slot.Player is not null)) Close(room, null);
	}

	private bool TooManyRooms(UserRecord creator)
	{
		return lobby.Rooms.Values.Count(room => room.IsTournament && creator.Equals(room.Authority.Creator)) >=
		       MaxRoomsPerCreator;
	}

	private void SeatCreator(Room room, BanchoConnection creator)
	{
		if (room.Seat(creator) is null) return;
		room.Authority.Host = creator;
		room.JoinChannel(creator);
	}
}