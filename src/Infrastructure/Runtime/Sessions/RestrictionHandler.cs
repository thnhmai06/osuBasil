using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Users;
using Basil.Infrastructure.Runtime.Events;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Sessions;

/// <summary>Applies the room and spectating consequences of a user's restrictions.</summary>
internal sealed class RestrictionHandler(
	IUserRegistry registry,
	IRoomService rooms,
	ISessionService sessions,
	TimeProvider time,
	ILogger<RestrictionHandler> logger) : IEventHandler<UserEvent>
{
	/// <inheritdoc />
	public async ValueTask HandleAsync(UserEvent @event, CancellationToken cancellationToken)
	{
		if (@event is not UserRestricted restricted) return;

		var session = registry.Sessions.GetValueOrDefault(restricted.User);
		if (session is null) return;

		await RunAsync("remove it from rooms", session.User.Id,
				() => rooms.Members.RemoveSuspendedAsync(session, cancellationToken), cancellationToken)
			;

		if (session.Check(Permissions.SpectatingWatch, time.GetUtcNow()) is Access.Allowed) return;

		if (session.Bancho is { } bancho)
			Run("stop spectating", session.User.Id,
				() => sessions.Connections.Bancho.Spectators.StopSpectating(bancho), cancellationToken);

		foreach (var connection in session.Tourneys)
			Run("stop spectating", session.User.Id,
				() => sessions.Connections.Bancho.Spectators.StopSpectating(connection), cancellationToken);
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