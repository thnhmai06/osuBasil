using Basil.Application.Services.Contracts.Events;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Users;
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

		var session = registry.Sessions.GetValueOrDefault(restricted.User);
		if (session is null)
		{
			// Offline: no connection to remove, but a referee must not stay on the lists of open rooms.
			if ((restricted.Restriction.Value.Permissions & Permissions.RoomJoin) != 0 &&
			    !restricted.User.Value.Permissions.Allows(Permissions.RoomManageAny))
				await RunAsync("remove it from referee lists", restricted.User.Id,
					() => rooms.Members.RemoveRefereeAsync(restricted.User, cancellationToken), cancellationToken);
			return;
		}

		await RunAsync("remove it from rooms", session.User.Id,
				() => rooms.Members.RemoveSuspendedAsync(session, cancellationToken), cancellationToken);

		if (session.Check(Permissions.SpectatingWatch, time.GetUtcNow()) is Access.Allowed) return;

		if (session.Bancho is { } bancho && registry.FindSpectating(bancho) is { } host)
			Run("stop spectating", session.User.Id,
				() => host.ScopeTo(bancho).StopSpectating(), cancellationToken);

		foreach (var connection in session.Tourneys)
			if (registry.FindSpectating(connection) is { } spectated)
				Run("stop spectating", session.User.Id,
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
