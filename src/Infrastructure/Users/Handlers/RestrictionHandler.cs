using Basil.Application.Common.Events;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Handlers;

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
		if (user is null || user.EffectivePermissions(time.GetUtcNow()).Allows(UserPermissions.SpectatingWatch))
			return ValueTask.CompletedTask;

		if (user.Stable is { } stable && registry.FindSpectating(stable) is { } host)
			HandlerStep.Run(logger, "stop spectating", Subject, user.Record.Id,
				() => host.ScopeTo(stable).StopSpectating(), cancellationToken);

		foreach (var connection in user.Tourneys)
			if (registry.FindSpectating(connection) is { } spectated)
				HandlerStep.Run(logger, "stop spectating", Subject, user.Record.Id,
					() => spectated.ScopeTo(connection).StopSpectating(), cancellationToken);

		return ValueTask.CompletedTask;
	}
}