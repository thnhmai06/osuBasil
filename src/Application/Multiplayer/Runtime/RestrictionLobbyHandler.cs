using Basil.Application.Common.Events;
using Basil.Application.Users.Users;
using Basil.Domain.Users;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Multiplayer.Runtime;

/// <summary>Applies the room consequences of a user's restrictions: a user who may no longer join rooms leaves them.</summary>
internal sealed class RestrictionLobbyHandler(
	UserRegistry registry,
	Lobby lobby,
	ILogger<RestrictionLobbyHandler> logger) : INotificationHandler<UserRestricted>
{
	private const string Subject = "restricted user";

	/// <inheritdoc />
	public async ValueTask Handle(UserRestricted restricted, CancellationToken cancellationToken)
	{
		var user = registry.Online.GetValueOrDefault(restricted.User);
		if (user is null)
		{
			// Offline: no connection to remove, but a referee must not stay on the lists of open rooms.
			if ((restricted.Restriction.Value.Permissions & UserPermissions.RoomJoin) != 0 &&
			    !restricted.User.Value.Permissions.Allows(UserPermissions.RoomManageAny))
				await HandlerStep.RunAsync(logger, "remove it from referee lists", Subject, restricted.User.Id,
					() => lobby.RemoveRefereeAsync(restricted.User, cancellationToken), cancellationToken);
			return;
		}

		await HandlerStep.RunAsync(logger, "remove it from rooms", Subject, user.Record.Id,
			() => lobby.RemoveSuspendedAsync(user, cancellationToken), cancellationToken);
	}
}