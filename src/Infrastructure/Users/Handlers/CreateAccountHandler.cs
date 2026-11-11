using Basil.Application.Shared;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="CreateAccount" /> with the privileges of the asking connection.</summary>
internal sealed class CreateAccountHandler(UserRegistry registry)
	: IRequestHandler<CreateAccount, (UserRecord? User, UserOutcome Result)>
{
	/// <inheritdoc />
	public async ValueTask<(UserRecord? User, UserOutcome Result)> Handle(CreateAccount request,
		CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor).CreateAccountAsync(request.Data, request.PasswordHash,
			request.Privileges);
	}
}