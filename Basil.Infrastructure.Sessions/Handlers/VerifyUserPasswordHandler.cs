using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Persistence;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class VerifyUserPasswordHandler(ICredentialRepository credentials)
	: IRequestHandler<VerifyUserPassword, bool>
{
	public async ValueTask<bool> Handle(VerifyUserPassword request, CancellationToken ct)
	{
		return await credentials.IsPasswordAsync(request.Account.Record, request.PasswordHash, ct);
	}
}