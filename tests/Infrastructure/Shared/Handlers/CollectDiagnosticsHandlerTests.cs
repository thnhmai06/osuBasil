using Basil.Application.Shared.Diagnostics;
using Basil.Application.Shared.Events;
using Basil.Infrastructure.Shared.Handlers;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Infrastructure.Shared.Tests.Handlers;

/// <summary>The database and events categories report real values, and say when the engine cannot measure the pool.</summary>
public sealed class CollectDiagnosticsHandlerTests
{
	private static async Task<Collector> CollectAsync(ConnectionStats? pool)
	{
		var source = Substitute.For<IDatabaseSource>();
		source.Connections.Returns(pool);
		var provider = Substitute.For<IDatabaseProvider>();
		provider.CreateSource(Arg.Any<string>(), Arg.Any<int>()).Returns(source);
		await using var database = new Database(new DatabaseProviders(provider, provider),
			Options.Create(new DatabaseOptions { WriteConnectionString = "unused" }));
		var queue = new EventQueue(NullLogger<EventQueue>.Instance);
		queue.Enqueue(new Noop());
		var collector = new Collector();

		await new CollectDiagnosticsHandler(database, queue).Handle(new CollectDiagnostics(collector),
			TestContext.Current.CancellationToken);
		return collector;
	}

	[Fact]
	public async Task Events_ReportPublishedPendingAndFailures()
	{
		var collector = await CollectAsync(null);

		Assert.Equal(1, collector["events", "publishedTotal"]);
		Assert.Equal(1, collector["events", "pending"]);
		Assert.Equal(0, collector["events", "handlerFailuresTotal"]);
	}

	[Fact]
	public async Task EngineWithoutPoolStatistics_SaysNotMeasured()
	{
		var collector = await CollectAsync(null);

		Assert.Equal(0, collector["database", "connectionsMeasured"]);
		Assert.False(collector.Has("database", "connectionsInUse"));
		Assert.Equal(0, collector["database", "commandsTotal"]);
	}

	[Fact]
	public async Task EngineWithPoolStatistics_ReportsInUseAndIdle()
	{
		var collector = await CollectAsync(new ConnectionStats(2, 3));

		Assert.Equal(1, collector["database", "connectionsMeasured"]);
		Assert.Equal(4, collector["database", "connectionsInUse"]);
		Assert.Equal(6, collector["database", "connectionsIdle"]);
	}

	private sealed class Noop : IEventDelivery
	{
		public ValueTask DeliverAsync(CancellationToken ct)
		{
			return ValueTask.CompletedTask;
		}
	}

	private sealed class Collector : IDiagnosticsCollector
	{
		private readonly Dictionary<(string, string), long> _values = [];

		public long this[string category, string name] => _values[(category, name)];

		public void Add(DiagnosticsValue value)
		{
			_values[(value.Category, value.Name)] = value.Value;
		}

		public bool Has(string category, string name)
		{
			return _values.ContainsKey((category, name));
		}
	}
}
