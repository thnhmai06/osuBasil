using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class GetUserHandler(IUserRepository users) : IRequestHandler<GetUser, UserRecord?>
{
	public async ValueTask<UserRecord?> Handle(GetUser request, CancellationToken ct)
	{
		var record = await users.GetAsync(request.Id, ct);
		return record != null && record.Value.DeletedAt is null ? record : null;
	}
}
