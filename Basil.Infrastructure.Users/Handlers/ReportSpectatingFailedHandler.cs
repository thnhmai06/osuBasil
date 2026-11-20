using Basil.Application.Users.Messages;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class ReportSpectatingFailedHandler : IRequestHandler<ReportSpectatingFailed, bool>
{
	public ValueTask<bool> Handle(ReportSpectatingFailed request, CancellationToken ct)
	{
		return ValueTask.FromResult(request.Host.ScopeTo(request.Actor).ReportSpectatingFailed());
	}
}