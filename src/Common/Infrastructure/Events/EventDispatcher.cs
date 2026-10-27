using Basil.Common.Application.Events;
using Microsoft.Extensions.Hosting;

namespace Basil.Common.Infrastructure.Events;

/// <summary>Runs the application's event queue for as long as the server lives.</summary>
internal sealed class EventDispatcher(EventQueue queue) : BackgroundService
{
	/// <summary>Delivers the queued events until the server stops.</summary>
	/// <param name="stoppingToken">A token that ends the delivery when the server stops.</param>
	protected override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		return queue.RunAsync(stoppingToken);
	}
}