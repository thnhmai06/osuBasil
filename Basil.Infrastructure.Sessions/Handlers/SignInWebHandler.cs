using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class SignInWebHandler(SessionRegistry registry) : IRequestHandler<SignInWeb, LoginResult>
{
	public async ValueTask<LoginResult> Handle(SignInWeb request, CancellationToken ct)
	{
		var name = UserData.TryNormalize(request.Name);
		// shortcut: scans the online users; the client must be online anyway, add an index if the online count grows large.
		var online = name is null
			? null
			: registry.Online.Values.FirstOrDefault(user => user.Record.Value.NormalizedName == name);
		return online is null
			? LoginResult.Fail(LoginFailure.UnknownUser)
			: await online.SignInWebAsync(request.PasswordMd5, ct);
	}
}
