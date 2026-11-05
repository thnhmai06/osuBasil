using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="ChangeUserPassword" /> on the account it names.</summary>
internal sealed class ChangeUserPasswordHandler(UserRegistry registry)
	: IRequestHandler<ChangeUserPassword, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(ChangeUserPassword request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Credentials
			.ChangePasswordAsync(request.NewPasswordHash, request.CurrentPasswordHash, request.IncludeCurrent,
				cancellationToken);
	}
}