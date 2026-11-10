using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SetUserCountry" /> on the account it names.</summary>
internal sealed class SetUserCountryHandler(UserRegistry registry)
	: IRequestHandler<SetUserCountry, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(SetUserCountry request, CancellationToken cancellationToken)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Profile
			.SetCountryAsync(request.Country);
	}
}