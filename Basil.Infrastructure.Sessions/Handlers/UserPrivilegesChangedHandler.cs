using Basil.Application.Sessions.Registry;
using Basil.Application.Users.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class UserPrivilegesChangedHandler(SessionRegistry registry)
	: INotificationHandler<UserPrivilegesChanged>
{
	public ValueTask Handle(UserPrivilegesChanged notification, CancellationToken ct)
	{
		registry.CloseDisallowed(notification.User);
		return ValueTask.CompletedTask;
	}
}