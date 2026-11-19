using Basil.Application.Shared.Events;
using Microsoft.Extensions.Hosting;

namespace Basil.Infrastructure.Shared.Workers;

internal sealed class EventDispatcher(EventQueue queue) : BackgroundService
{
	protected override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		return queue.RunAsync(stoppingToken);
	}
}