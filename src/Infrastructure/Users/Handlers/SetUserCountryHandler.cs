using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetUserCountryHandler(UserRegistry registry)
	: IRequestHandler<SetUserCountry, UserOutcome>
{
	public async ValueTask<UserOutcome> Handle(SetUserCountry request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile
			.SetCountryAsync(request.Country);
	}
}