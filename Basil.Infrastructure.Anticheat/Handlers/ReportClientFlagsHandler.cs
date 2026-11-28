using Basil.Application.Anticheat.Messages;
using Basil.Application.Anticheat.Services;
using Basil.Domain.Client;
using Mediator;

namespace Basil.Infrastructure.Anticheat.Handlers;

internal sealed class ReportClientFlagsHandler(IAnticheatReporter reporter) : IRequestHandler<ReportClientFlags, bool>
{
	public ValueTask<bool> Handle(ReportClientFlags request, CancellationToken ct)
	{
		return ValueTask.FromResult(reporter.Report(request.Player, request.Flags) != ClientFlags.Clean);
	}
}
