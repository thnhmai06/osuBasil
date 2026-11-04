using Basil.Application.Common.Events;
using Basil.Gateway;
using Microsoft.Extensions.Hosting;

namespace Basil.Host.Rest.Http;

/// <summary>Keeps the gateway's load signal equal to how full the event queue is.</summary>
internal sealed class LoadSignalFeeder(EventQueue queue, LoadSignal signal, TimeProvider clock) : BackgroundService
{
	/// <summary>Reports the load once a second until the server stops.</summary>
	/// <param name="stoppingToken">A token that ends the reporting when the server stops.</param>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
		try
		{
			do
			{
				signal.Report(queue.Outstanding, queue.Ceiling);
			} while (await timer.WaitForNextTickAsync(stoppingToken));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
	}
}
