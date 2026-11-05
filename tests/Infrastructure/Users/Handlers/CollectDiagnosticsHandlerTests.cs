using Basil.Application.Common.Diagnostics;
using Basil.Domain.Authentication;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>Users reports the users online and the open connections of every type, including types with none.</summary>
public sealed class CollectDiagnosticsHandlerTests
{
	private static async Task<Dictionary<(string Category, string Name), long>> CollectAsync(TestApplication app)
	{
		var collector = new Collector();
		await new CollectDiagnosticsHandler(app.Users).Handle(new CollectDiagnostics(collector),
			TestContext.Current.CancellationToken);
		return collector.Counts;
	}

	[Fact]
	public async Task Handle_WithNobodyOnline_ReportsEveryConnectionTypeAsZero()
	{
		using var app = new TestApplication();

		var counts = await CollectAsync(app);

		Assert.Equal(0, counts[("application", "usersOnline")]);
		foreach (var type in new[] { "stable", "stabletourney", "irc", "api" })
			Assert.Equal(0, counts[("sessions", type)]);
		Assert.Equal(0, counts[("stable", "osu")]);
		Assert.Equal(0, counts[("stable", "tourney")]);
		Assert.Equal(0, counts[("irc", "connections")]);
	}

	[Fact]
	public async Task Handle_CountsEachConnectionUnderItsType()
	{
		using var app = new TestApplication();
		await app.LoginAsync(await app.CreateUserAsync("Osu", "password"), SessionType.Stable);
		await app.LoginAsync(await app.CreateUserAsync("Chatter", "password"), SessionType.Irc);
		await app.LoginAsync(await app.CreateUserAsync("Api", "password"));

		var counts = await CollectAsync(app);

		Assert.Equal(3, counts[("application", "usersOnline")]);
		Assert.Equal(1, counts[("sessions", "stable")]);
		Assert.Equal(1, counts[("sessions", "irc")]);
		Assert.Equal(1, counts[("sessions", "api")]);
		Assert.Equal(0, counts[("sessions", "stabletourney")]);
		Assert.Equal(1, counts[("stable", "osu")]);
		Assert.Equal(1, counts[("irc", "connections")]);
	}

	[Fact]
	public async Task Handle_DoesNotCountTheBotsOwnConnectionsAsClients()
	{
		using var app = new TestApplication();
		await app.Users.EnsureBotAsync(TestContext.Current.CancellationToken);
		await app.Users.ConnectBotAsync(TestContext.Current.CancellationToken);

		var counts = await CollectAsync(app);

		Assert.Equal(0, counts[("sessions", "stable")]);
		Assert.Equal(0, counts[("sessions", "irc")]);
	}

	private sealed class Collector : IDiagnosticsCollector
	{
		public Dictionary<(string Category, string Name), long> Counts { get; } = [];

		public void Add(DiagnosticsValue value)
		{
			Counts[(value.Category, value.Name)] = value.Value;
		}
	}
}