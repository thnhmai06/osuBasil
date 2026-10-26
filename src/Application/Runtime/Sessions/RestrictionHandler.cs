using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Users;
using Basil.Domain.Users;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Runtime.Sessions;

/// <summary>Stops a restricted user from spectating when the restriction suspends spectating.</summary>
internal sealed class RestrictionHandler(
	UserRegistry registry,
	TimeProvider time,
	ILogger<RestrictionHandler> logger) : INotificationHandler<UserRestricted>
{
	private const string Subject = "restricted user";

	/// <inheritdoc />
	public ValueTask Handle(UserRestricted restricted, CancellationToken cancellationToken)
	{
		var user = registry.Online.GetValueOrDefault(restricted.User);
		if (user is null) return ValueTask.CompletedTask;

		if (user.EffectivePermissions(time.GetUtcNow()).Allows(UserPermissions.SpectatingWatch))
			return ValueTask.CompletedTask;

		if (user.Bancho is { } bancho && registry.FindSpectating(bancho) is { } host)
			HandlerStep.Run(logger, "stop spectating", Subject, user.Record.Id,
				() => host.ScopeTo(bancho).StopSpectating(), cancellationToken);

		foreach (var connection in user.Tourneys)
			if (registry.FindSpectating(connection) is { } spectated)
				HandlerStep.Run(logger, "stop spectating", Subject, user.Record.Id,
					() => spectated.ScopeTo(connection).StopSpectating(), cancellationToken);

		return ValueTask.CompletedTask;
	}
}