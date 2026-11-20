using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class FindUserByNameHandler(IUserRepository users) : IRequestHandler<FindUserByName, UserRecord?>
{
	public async ValueTask<UserRecord?> Handle(FindUserByName request, CancellationToken ct)
	{
		var record = await users.GetByNameAsync(request.Name, ct);
		return record != null && record.Value.DeletedAt is null ? record : null;
	}
}
