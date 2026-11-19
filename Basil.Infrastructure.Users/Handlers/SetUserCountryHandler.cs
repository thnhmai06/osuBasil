using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class SetUserCountryHandler(UserRegistry registry)
	: IRequestHandler<SetUserCountry, UserActionResult>
{
	public async ValueTask<UserActionResult> Handle(SetUserCountry request, CancellationToken ct)
	{
		return await registry.ScopeTo(request.Actor, request.Target).Profile
			.SetCountryAsync(request.Country);
	}
}