using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Answers <see cref="GetUser" /> from the stored accounts.</summary>
internal sealed class GetUserHandler(IUserRepository users) : IRequestHandler<GetUser, UserRecord?>
{
	/// <inheritdoc />
	public async ValueTask<UserRecord?> Handle(GetUser request, CancellationToken ct)
	{
		var record = request.Target.Id is { } id
			? await users.GetAsync(id, ct)
			: request.Target.Name is { } name
				? await users.GetByNameAsync(name, ct)
				: null;
		return record is { } found && found.Value.DeletedAt is null ? found : null;
	}
}