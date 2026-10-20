using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Permissions;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Sessions;

/// <summary>Applies the room and spectating consequences of a user's restrictions.</summary>
internal sealed class RestrictionHandler(
	IUserRegistry registry,
	IRoomService rooms,
	TimeProvider time,
	ILogger<RestrictionHandler> logger) : IEventHandler<UserEvent>
{
	/// <inheritdoc />
	public async ValueTask HandleAsync(UserEvent @event, CancellationToken cancellationToken)
	{
		if (@event is not UserRestricted restricted) return;

		var user = registry.Online.GetValueOrDefault(restricted.User);
		if (user is null)
		{
			// Offline: no connection to remove, but a referee must not stay on the lists of open rooms.
			if ((restricted.Restriction.Value.Permissions & Permissions.RoomJoin) != 0 &&
			    !restricted.User.Value.Permissions.Allows(Permissions.RoomManageAny))
				await RunAsync("remove it from referee lists", restricted.User.Id,
					() => rooms.Members.RemoveRefereeAsync(restricted.User, cancellationToken), cancellationToken);
			return;
		}

		await RunAsync("remove it from rooms", user.Record.Id,
			() => rooms.Members.RemoveSuspendedAsync(user, cancellationToken), cancellationToken);

		if (user.Check(Permissions.SpectatingWatch, time.GetUtcNow()) is Access.Allowed) return;

		if (user.Bancho is { } bancho && registry.FindSpectating(bancho) is { } host)
			Run("stop spectating", user.Record.Id,
				() => host.ScopeTo(bancho).StopSpectating(), cancellationToken);

		foreach (var connection in user.Tourneys)
			if (registry.FindSpectating(connection) is { } spectated)
				Run("stop spectating", user.Record.Id,
					() => spectated.ScopeTo(connection).StopSpectating(), cancellationToken);
	}

	/// <summary>Runs one restriction consequence, logging a failure so the remaining consequences still run.</summary>
	private async Task RunAsync(string step, int userId, Func<Task> action, CancellationToken cancellationToken)
	{
		try
		{
			await action();
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !cancellationToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not {Step} for restricted user {UserId}", step, userId);
		}
	}

	/// <summary>Runs one restriction consequence, logging a failure so the remaining consequences still run.</summary>
	private void Run(string step, int userId, Action action, CancellationToken cancellationToken)
	{
		try
		{
			action();
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !cancellationToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not {Step} for restricted user {UserId}", step, userId);
		}
	}
}