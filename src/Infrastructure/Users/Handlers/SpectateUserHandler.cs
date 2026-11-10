using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="SpectateUser" /> with the rights of the spectating connection.</summary>
internal sealed class SpectateUserHandler : IRequestHandler<SpectateUser, Result<ConnectionOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ConnectionOutcome>> Handle(SpectateUser request, CancellationToken ct)
	{
		return await request.Host.ScopeTo(request.Actor).SpectateAsync();
	}
}