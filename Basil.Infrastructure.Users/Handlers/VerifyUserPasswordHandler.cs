using Basil.Application.Sessions.Persistence;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class VerifyUserPasswordHandler(ICredentialRepository credentials)
	: IRequestHandler<VerifyUserPassword, bool>
{
	public async ValueTask<bool> Handle(VerifyUserPassword request, CancellationToken ct)
	{
		return await credentials.IsPasswordAsync(request.Account.Record, request.PasswordHash, ct);
	}
}