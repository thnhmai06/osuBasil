using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SilenceUser" /> on the account it names.</summary>
internal sealed class SilenceUserHandler(UserRegistry registry) : IRequestHandler<SilenceUser, Result<UserOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<UserOutcome>> Handle(SilenceUser request, CancellationToken ct)
	{
		return await registry.Of(request.Target).ScopeTo(request.Actor).Silence
			.ImposeAsync(request.EndsAt, request.Reason);
	}
}
