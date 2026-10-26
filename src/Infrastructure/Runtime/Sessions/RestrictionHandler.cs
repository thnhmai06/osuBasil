using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Sessions;
using Basil.Application.Runtime.Users;
using Basil.Domain.Users;
using Basil.Infrastructure.Runtime.Events;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Sessions;

/// <summary>Applies the room and spectating consequences of a user's restrictions.</summary>
internal sealed class RestrictionHandler(
	UserRegistry registry,
	Lobby lobby,
	TimeProvider time,
	ILogger<RestrictionHandler> logger) : IEventHandler<UserEvent>
{
	private const string Subject = "restricted user";

	/// <inheritdoc />
	public async ValueTask HandleAsync(UserEvent @event, CancellationToken cancellationToken)
	{
		if (@event is not UserRestricted restricted) return;

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

		if (user.EffectivePermissions(time.GetUtcNow()).Allows(UserPermissions.SpectatingWatch)) return;

		if (user.Bancho is { } bancho && registry.FindSpectating(bancho) is { } host)
			HandlerStep.Run(logger, "stop spectating", Subject, user.Record.Id,
				() => host.ScopeTo(bancho).StopSpectating(), cancellationToken);

		foreach (var connection in user.Tourneys)
			if (registry.FindSpectating(connection) is { } spectated)
				HandlerStep.Run(logger, "stop spectating", Subject, user.Record.Id,
					() => spectated.ScopeTo(connection).StopSpectating(), cancellationToken);
	}
}