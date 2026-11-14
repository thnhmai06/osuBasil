using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Checks a claimed password hash for <see cref="VerifyUserPassword" />.</summary>
internal sealed class VerifyUserPasswordHandler(ICredentialRepository credentials)
	: IRequestHandler<VerifyUserPassword, bool>
{
	/// <inheritdoc />
	public async ValueTask<bool> Handle(VerifyUserPassword request, CancellationToken ct)
	{
		return await credentials.IsPasswordAsync(request.Account.Record, request.PasswordHash, ct);
	}
}