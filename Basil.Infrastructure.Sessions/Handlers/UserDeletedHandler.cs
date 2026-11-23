using Basil.Application.Sessions.Registry;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Sessions;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class UserDeletedHandler(SessionRegistry registry) : INotificationHandler<UserDeleted>
{
	public ValueTask Handle(UserDeleted notification, CancellationToken ct)
	{
		registry.CloseAll(notification.User, SessionEndReason.Deleted);
		return ValueTask.CompletedTask;
	}
}