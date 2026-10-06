using Basil.Domain.Users;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>Referees and the host.</summary>
public interface IRoomAuthorityService
{
	/// <summary>Makes a user a referee.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The creator or a user with <see cref="Permissions.TournamentManageAnyRoom" />.</param>
	/// <param name="user">The user to make referee.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, IsCreator, AlreadyReferee, TooManyReferees or RoomClosed when the room has closed.</returns>
	Task<RoomResult> AddRefereeAsync(Room room, Connection by, User user,
		CancellationToken cancellationToken = default);

	/// <summary>Removes a user from the referees.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The creator or a user with <see cref="Permissions.TournamentManageAnyRoom" />.</param>
	/// <param name="user">The referee to remove.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotReferee or RoomClosed when the room has closed.</returns>
	Task<RoomResult> RemoveRefereeAsync(Room room, Connection by, User user,
		CancellationToken cancellationToken = default);

	/// <summary>Gives host to a seated player, or clears it.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The host or a manager.</param>
	/// <param name="host">The seated player to make host, or <see langword="null" /> to clear the host.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>Giving host to the current host does nothing.</remarks>
	Task<RoomResult> SetHostAsync(Room room, Connection by, BanchoConnection? host,
		CancellationToken cancellationToken = default);
}
