using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Tests;

/// <summary>What the server does before it serves anyone: it recovers the runtime.</summary>
public sealed class RuntimeStartupTests
{
	[Fact]
	public async Task StartupRecovers_BotOnline()
	{
		using var app = new TestApplication();
		var bot = await app.SeedBotAsync();

		await new RuntimeStartup(app.Server, NullLogger<RuntimeStartup>.Instance)
			.StartAsync(TestContext.Current.CancellationToken);

		Assert.Contains(bot, app.Users.Online.Keys);
	}
}