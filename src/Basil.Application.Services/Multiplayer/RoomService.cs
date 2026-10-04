using System.Threading.Channels;
using Basil.Application.Contracts.Chat;
using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer;

/// <summary>Changes rooms: membership, authority, settings, slots, rounds and countdowns.</summary>
internal sealed partial class RoomService(
	Lobby lobby,
	LobbyService lobbyService,
	IChannelService channels,
	TimeProvider time) : IRoomService
{
	/// <summary>The countdown length used when none is given.</summary>
	internal static readonly TimeSpan DefaultCountdownLength = TimeSpan.FromSeconds(30);

	/// <summary>The longest countdown allowed.</summary>
	internal static readonly TimeSpan MaxCountdownLength = TimeSpan.FromHours(1);

	private readonly Channel<RoomEvent> _events = Channel.CreateUnbounded<RoomEvent>();

	/// <inheritdoc />
	public ChannelReader<RoomEvent> Events => _events.Reader;

	/// <inheritdoc />
	public Task<RoomResult> JoinAsync(Room room, BanchoConnection by, string password,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Join(room, by, password), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> LeaveAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Leave(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public async Task<RoomResult> SeatAsync(Room room, Connection by, BanchoConnection player,
		CancellationToken cancellationToken = default)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (!player.IsOpen) return RoomResult.TargetOffline;
		if (player.User.Value.SilenceEndsAt > time.GetUtcNow()) return RoomResult.Silenced;
		if (!player.User.Value.Privilege.Has(ClientPrivileges.Player)) return RoomResult.NotAuthorized;
		if (room.Banned.Contains(player.User)) return RoomResult.Banned;

		if (lobby.RoomOf(player) is { } other && !ReferenceEquals(other, room))
		{
			if (!RoomRules.CanManage(other, by)) return RoomResult.InAnotherRoom;
			if (!room.Slots.Any(s => s is { Locked: false, Player: null })) return RoomResult.Full;
			if (room.Observers.Any(observer => observer.User.Equals(player.User))) return RoomResult.IsObserver;
			await LeaveAsync(other, player, cancellationToken);
		}

		return await InScopeAsync(room, () => Seat(room, by, player), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> InviteAsync(Room room, Connection by, UserSession target,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Invite(room, by, target), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetHostAsync(Room room, Connection by, BanchoConnection? host,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetHost(room, by, host), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ObserverJoinAsync(Room room, TourneyConnection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ObserverJoin(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ObserverLeaveAsync(Room room, TourneyConnection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ObserverLeave(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ConfigureAsync(Room room, Connection by, RoomSettingsChange change,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Configure(room, by, change), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ChangeSlotAsync(Room room, BanchoConnection by, int index,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ChangeSlot(room, by, index), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ArrangeSlotsAsync(Room room, Connection by, IReadOnlyList<SlotArrangement> arrangement,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ArrangeSlots(room, by, arrangement), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ToggleSlotLockAsync(Room room, Connection by, int index,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ToggleSlotLock(room, by, index), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetLockedAsync(Room room, Connection by, bool locked,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetLocked(room, by, locked), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetReadyAsync(Room room, BanchoConnection by, bool ready,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetReady(room, by, ready), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetHasMapAsync(Room room, BanchoConnection by, bool has,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetHasMap(room, by, has), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ToggleTeamAsync(Room room, BanchoConnection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ToggleTeam(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> StartAsync(Room room, Connection by, CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Start(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> AbortAsync(Room room, Connection by, CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Abort(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> MarkLoadedAsync(Room room, BanchoConnection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => MarkLoaded(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SkipAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Skip(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> FailAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Fail(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> CompleteAsync(Room room, BanchoConnection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Complete(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> StartCountdownAsync(Room room, Connection by, TimeSpan length, bool startsRound,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => StartCountdown(room, by, length, startsRound), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> CancelCountdownAsync(Room room, Connection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => CancelCountdown(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public async Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default)
	{
		if (connection is BanchoConnection player)
		{
			if (lobby.RoomOf(player) is { } room)
			{
				await using var scope = await lobby.EnterAsync(room, cancellationToken);
				if (scope is not null) Leave(room, player);
			}
		}
		else if (connection is TourneyConnection observer)
		{
			foreach (var room in lobby.Rooms.Where(room => room.Observers.Contains(observer)).ToArray())
			{
				await using var scope = await lobby.EnterAsync(room, cancellationToken);
				if (scope is not null) ObserverLeave(room, observer);
			}
		}

		// Managers stay in a room's channel after leaving their seat, and IRC referees join it directly.
		foreach (var room in lobby.Rooms)
			channels.Part(room.Channel, connection);
	}

	/// <inheritdoc />
	public Task<RoomResult> KickAsync(Room room, Connection by, User player,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Kick(room, by, player), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> BanAsync(Room room, Connection by, User player,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Ban(room, by, player), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> UnbanAsync(Room room, Connection by, User player,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Unban(room, by, player), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> AddRefereeAsync(Room room, Connection by, User user,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => AddReferee(room, by, user), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> RemoveRefereeAsync(Room room, Connection by, User user,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => RemoveReferee(room, by, user), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> MoveAsync(Room room, Connection by, User player, int index,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Move(room, by, player, index), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetTeamAsync(Room room, Connection by, User player, GameTeam team,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetTeam(room, by, player, team), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetPlayerModsAsync(Room room, BanchoConnection by, GameMods mods,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetPlayerMods(room, by, mods), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> RecordScoreAsync(Room room, User player, Score score,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => RecordScore(room, player, score), cancellationToken);
	}

	/// <summary>Runs one state transition of a room inside the room's exclusive scope.</summary>
	/// <param name="room">The room to change.</param>
	/// <param name="operation">The transition to run once the scope is held.</param>
	/// <param name="cancellationToken">A token that cancels the wait for the scope.</param>
	/// <returns>The result of <paramref name="operation" />, or RoomClosed when the room has closed.</returns>
	private async Task<RoomResult> InScopeAsync(Room room, Func<RoomResult> operation,
		CancellationToken cancellationToken)
	{
		await using var scope = await lobby.EnterAsync(room, cancellationToken);
		return scope is null ? RoomResult.RoomClosed : operation();
	}

	private void Emit(RoomEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}
}