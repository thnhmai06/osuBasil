using Basil.Application.Rulesets.Objects;
using Basil.Infrastructure.Rulesets.Storage;
using Microsoft.Extensions.Hosting;

namespace Basil.Infrastructure.Rulesets.Workers;

/// <summary>Tells the server at start-up when a newly staged version of the rulesets has become active.</summary>
internal sealed class RulesetsAnnouncer(RulesetStore store, RulesetLibrary library) : IHostedService
{
	public Task StartAsync(CancellationToken ct)
	{
		if (store.TakeUnannounced() is { } version) library.Updated(version);
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}
}