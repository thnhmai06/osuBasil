using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="GetUserSession" /> on the account it names.</summary>
internal sealed class GetUserSessionHandler(UserRegistry registry) : IRequestHandler<GetUserSession, (Result<UserOutcome> Result, SessionRecord? Session)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<UserOutcome> Result, SessionRecord? Session)> Handle(GetUserSession request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Sessions.GetAsync(request.SessionId, cancellationToken);
	}
}
