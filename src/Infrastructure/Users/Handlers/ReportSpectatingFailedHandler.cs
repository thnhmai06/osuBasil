using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Carries out <see cref="ReportSpectatingFailed" /> for the spectating connection.</summary>
internal sealed class ReportSpectatingFailedHandler : IRequestHandler<ReportSpectatingFailed, bool>
{
	/// <inheritdoc />
	public ValueTask<bool> Handle(ReportSpectatingFailed request, CancellationToken cancellationToken)
	{
		return ValueTask.FromResult(request.Host.ScopeTo(request.Actor).ReportSpectatingFailed());
	}
}