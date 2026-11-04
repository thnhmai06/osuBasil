using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="StopSpectating" /> for the spectating connection.</summary>
internal sealed class StopSpectatingHandler : IRequestHandler<StopSpectating, bool>
{
	/// <inheritdoc />
	public ValueTask<bool> Handle(StopSpectating request, CancellationToken cancellationToken)
	{
		return ValueTask.FromResult(request.Host.ScopeTo(request.Actor).StopSpectating());
	}
}
