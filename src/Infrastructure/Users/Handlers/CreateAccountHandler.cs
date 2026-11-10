using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="CreateAccount" /> with the rights of the asking connection.</summary>
internal sealed class CreateAccountHandler(UserRegistry registry)
	: IRequestHandler<CreateAccount, (UserRecord? User, Result<UserOutcome> Result)>
{
	/// <inheritdoc />
	public async ValueTask<(UserRecord? User, Result<UserOutcome> Result)> Handle(CreateAccount request,
		CancellationToken cancellationToken)
	{
		return await registry.ScopeTo(request.Actor).CreateAccountAsync(request.Data, request.PasswordHash,
			request.Privileges);
	}
}