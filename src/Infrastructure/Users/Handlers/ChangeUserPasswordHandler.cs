using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="ChangeUserPassword" /> on the account it names.</summary>
internal sealed class ChangeUserPasswordHandler(UserRegistry registry)
	: IRequestHandler<ChangeUserPassword, UserOutcome>
{
	/// <inheritdoc />
	public async ValueTask<UserOutcome> Handle(ChangeUserPassword request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Credentials
			.ChangePasswordAsync(request.NewPasswordHash, request.CurrentPasswordHash, request.IncludeCurrent);
	}
}